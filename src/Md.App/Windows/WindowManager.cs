// The live windows (shell-design.md §1.2, §1.3, §1.6). Everything decidable without Windows is in
// Md.App.Logic — ActivationRouter turns an activation into actions, WindowRegistry says who owns
// what, WindowPlacement does the cascade arithmetic, SessionStore is the file — so what is left
// here is creating windows, activating them and writing the session.
using Md.App.Book;
using Md.App.Export;
using Md.App.Logic;
using Md.App.Logic.Activation;
using Md.App.Logic.Commands;
using Md.App.Logic.Documents;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Logic.Text;
using Md.App.Logic.View;
using Md.App.Logic.Windows;
using Md.App.Menus;
using Md.App.Services;
using Md.Core.Document;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Launcher = Windows.System.Launcher;

namespace Md.App;

internal sealed class WindowManager
{
    readonly AppServices _services;
    readonly List<DocumentWindow> _windows = [];
    readonly UntitledNames _untitled = new();
    DocumentWindow? _lastActive;
    BookWindow? _book;
    bool _exiting;

    public WindowManager(AppServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        // Windows gives WinUI no reliable "will terminate" hook; this is the best-effort last flush
        // §6.3 asks for, and the 1 s autosave bounds what it can miss.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TerminateFlush();
    }

    /// <summary>Every open document window, in creation order.</summary>
    public IReadOnlyList<DocumentWindow> Windows => _windows;

    // ── activation (§1.2) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Perform one activation's actions in order and bring the result forward. The ownership half of
    /// §1.2 — "a path already open activates that window instead of opening a second" — is
    /// <see cref="ActivationRouter.Resolve"/>, applied here against the live registry.
    /// </summary>
    /// <param name="redirected">
    /// This activation came from a second instance. A process that is not the foreground process
    /// cannot bring its own window forward by <c>Activate()</c> alone — the classic "the file opened
    /// but the window stayed behind" — so the redirected case also asks Win32 (§1.1.1).
    /// </param>
    public void Perform(IReadOnlyList<ActivationAction> actions, bool redirected)
    {
        ArgumentNullException.ThrowIfNull(actions);

        DocumentWindow? target = null;
        foreach (var action in ActivationRouter.Resolve(actions, _services.Registry, FileIdentity.Instance))
        {
            target = action switch
            {
                ActivationAction.OpenPath open => OpenPath(open.Path) ?? target,
                ActivationAction.ImportTextPack pack => OpenPath(pack.Path) ?? target,
                ActivationAction.ImportTextBundleFolder bundle => OpenPath(bundle.Path) ?? target,
                ActivationAction.Focus focus => Find(focus.WindowId) ?? target,
                ActivationAction.OpenUntitled => OpenUntitled(),
                ActivationAction.RestoreSession => RestoreSession() ?? target,
                _ => target,
            };
        }

        // An activation must never leave the process running with no window: WinUI would pump for
        // ever behind nothing, and the user asked for md. The Book window counts — a session of
        // nothing but a book restores the book, not an untitled document nobody asked for.
        if (target is null && _windows.Count == 0 && _book is { } onlyBook)
        {
            onlyBook.Activate();
            if (redirected) Interop.NativeMethods.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(onlyBook));
            return;
        }
        target ??= _windows.Count > 0 ? _windows[^1] : OpenUntitled();

        target.Activate();
        if (redirected) Interop.NativeMethods.SetForegroundWindow(target.Handle);
    }

    /// <summary>
    /// A drag & drop onto a window root (§6.1). The actions are the same ones an activation
    /// produces and go through the same path — including "a path already open activates that window"
    /// — with one difference: a drop that classified to nothing (a folder that is not a
    /// <c>.textbundle</c>) opens no window. <see cref="Perform"/>'s "an activation must leave a
    /// window behind" fallback is right for an activation and wrong for a drop, which already
    /// happened on a window that is plainly there.
    /// </summary>
    public void PerformDrop(IReadOnlyList<ActivationAction> actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Count == 0) return;
        Perform(actions, redirected: false);
    }

    /// <summary>Activate the window with this id (the Window menu, and an activation of a file that is already open).</summary>
    public bool Activate(Guid id)
    {
        // The Book window is one of the rows the Window menu lists (§2.8), and the book pane's
        // "Show Window" hands back a document window's id — both arrive here.
        if (_book is { } book && book.Id == id)
        {
            book.Activate();
            return true;
        }
        if (Find(id) is not { } window) return false;
        window.Activate();
        return true;
    }

    // ── opening ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open a file, a <c>.textpack</c> or a <c>.textbundle</c> folder in its own window — or, when
    /// one already has it, activate that one (NSDocumentController's rule, §1.2).
    /// </summary>
    public DocumentWindow? OpenPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (_services.Registry.FindByPath(path, FileIdentity.Instance) is { } owner && Find(owner) is { } existing)
        {
            existing.Activate();
            return existing;
        }

        var window = Create();
        // A failed open leaves this window untitled with the reason on screen: the alert belongs to
        // the window that asked for the file, and a window closing under its own dialog is worse
        // than an empty one the writer can use.
        window.OpenDocument(path);
        window.Activate();
        return window;
    }

    /// <summary>File ▸ New, an Example, Duplicate, and a redirected plain launch.</summary>
    public DocumentWindow OpenUntitled(string text = "", string? title = null, bool dirty = false)
    {
        var window = Create();
        var ordinal = 0;
        if (title is null) title = _untitled.TakeName(out ordinal);
        window.OpenUntitled(text, title, dirty, ordinal);
        window.Activate();
        return window;
    }

    /// <summary>An untitled window's number is free again once it is saved or closed (§6.7).</summary>
    public void ReleaseUntitled(int ordinal) => _untitled.Release(ordinal);

    DocumentWindow Create()
    {
        var window = new DocumentWindow(_services, this);
        _windows.Add(window);
        window.Place(WindowPlacement.Cascade(_lastActive?.Frame, StoredDocumentSize(), window.WorkArea), maximized: false);
        return window;
    }

    WindowSize StoredDocumentSize() =>
        WindowPlacement.ParseSize(_services.Settings.GetString(SettingsKeys.DocumentWindowSize), WindowPlacement.DocumentDefault);

    DocumentWindow? Find(Guid id) => _windows.FirstOrDefault(w => w.Id == id);

    // ── the Book window (§8.1) ────────────────────────────────────────────────────────────────

    /// <summary>The Book window, or null while none is open. There is exactly one (§1.3).</summary>
    public BookWindow? Book => _book;

    /// <summary>
    /// Show the one app-wide Book window (§1.3): create it on demand, else activate the one that is
    /// already there. Every Book row of every window ends here.
    /// </summary>
    public BookWindow ShowBookWindow()
    {
        _book ??= CreateBookWindow();
        _book.Activate();
        return _book;
    }

    /// <summary>
    /// A Book row invoked from a document window (§2.6). The Book window owns the book, so the row
    /// is re-dispatched on <em>its</em> dispatcher — the same handler its own menu fires, enabled by
    /// its own snapshot, rather than a second copy of the flow living in the document window.
    /// </summary>
    public void RouteToBook(CommandId id, object? argument = null)
    {
        var window = ShowBookWindow();
        // A window that has just been created has not listed its book yet, and every whole-book
        // output reads that listing: running Print Book straight away would alert "No book is open"
        // about the book it is in the middle of opening. For a window that is already up the wait is
        // one already-completed task.
        _ = RunWhenListedAsync(window, id, argument);
    }

    async Task RunWhenListedAsync(BookWindow window, CommandId id, object? argument)
    {
        await window.Listed;
        // The window may have gone while the listing was in flight (a book on a drive that answers
        // slowly, and a reader who closed it meanwhile): a command dispatched into a closed window
        // has nowhere to put its pickers or its alerts.
        if (ReferenceEquals(_book, window)) window.Commands.Execute(id, argument);
    }

    /// <summary>
    /// Book ▸ Close Book from a document window. With a Book window open it is that window's own
    /// command (grant dropped, window closed, §2.6); without one the grant is dropped where it
    /// stands — opening a Book window for a second in order to close the book would be a flash of a
    /// window the writer never asked for. <paramref name="origin"/> only lends its dialogs; closing
    /// a book shows none.
    /// </summary>
    public void CloseBook(DocumentWindow origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (_book is { } book)
        {
            _ = book.CloseBookCommandAsync();
            return;
        }
        new BookLibraryHost(_services.Settings, origin.Pickers, origin.Alerts).CloseBook();
    }

    BookWindow CreateBookWindow()
    {
        // One queue-backed scheduler for the whole window, exactly as a document window has: it is
        // the IScheduler the autosave and the render-complete poll use and the IClock the command
        // dispatcher's double-fire guard measures against.
        var queue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var scheduler = new DispatcherScheduler(queue);

        // The alerts, the pickers and the export pipeline all need the window, and the window needs
        // them: the binding is handed over empty and bound below, before anything can call into it.
        var binding = new BookWindowBinding();
        var window = new BookWindow(new BookWindowServices(
            Settings: _services.Settings,
            Alerts: binding,
            Pickers: binding,
            Registry: _services.Registry,
            Scheduler: scheduler,
            Clock: scheduler,
            UiThread: new UiThread(queue),
            FileSystem: SystemIoFileSystem.Instance,
            Identity: FileIdentity.Instance,
            WordCounter: _services.WordCounter,
            Outputs: binding,
            OpenPath: path => OpenPath(path),
            ActivateWindow: id => Activate(id),
            Examples: _services.Examples.Examples,
            Recent: _services.Recent,
            // §6.1: the Book window's drop routes through the same resolution a document window's
            // does — one drop rule for the app, not one per window.
            PerformDrop: PerformDrop,
            Decorate: TintTitleBar));

        // §7: the same export half a document window has, over the Book window's own canvas and its
        // print-overlay slot — a book compiles and prints from the window that shows it (§8.7).
        var overlay = new Controls.PrintOverlay();
        window.OverlaySlot.Content = overlay;
        var exports = new DocumentExports(window, window.ExportRoot, overlay, scheduler, binding);
        // Hands over the export half AND subscribes §7.1's footer ring to its pipeline.
        window.AttachExports(exports);
        binding.Bind(window, exports, window.TrackOutput, ShowOverlay);

        RegisterShellCommands(window, binding);
        // The "Book" row of every window's Window menu (§2.8); removed again when the window goes.
        _services.Registry.Add(window.Id, Strings.Book);
        window.Closed += (_, _) => ForgetBook(window);
        return window;

        void ShowOverlay(bool shown) => window.OverlaySlot.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// §1.3's tinted <em>standard</em> title bar, for the Book window: applied once and again on
    /// every theme change, exactly as a document window re-tints from its own <c>ApplyTheme</c>.
    /// </summary>
    static void TintTitleBar(Window window)
    {
        if (window.Content is not FrameworkElement root) return;
        TitleBarTint.Apply(window.AppWindow, root.ActualTheme == ElementTheme.Dark);
        root.ActualThemeChanged += (element, _) => TitleBarTint.Apply(window.AppWindow, element.ActualTheme == ElementTheme.Dark);
    }

    /// <summary>
    /// The rows the Book window does not register itself: the File, Edit, Window and Help commands
    /// every window carries (§2.1), and §7's print, share and export acting on the article being
    /// written — which is what the Mac's book pane publishes as its active document (§8.5).
    /// </summary>
    void RegisterShellCommands(BookWindow window, BookWindowBinding binding)
    {
        var commands = window.Commands;
        var exports = window.Exports!;

        // File (§2.2). Everything that opens a document opens a document WINDOW: the Book window
        // edits articles of the book it is showing and nothing else.
        commands.Register(CommandId.New, () => OpenUntitled());
        commands.Register(CommandId.Open, () => _ = OpenWithPickerAsync(binding));
        // Open Recent belongs to EVERY window (§2.1), and the MRU behind it is the app's, not a
        // window's — the same rows a document window shows, opening into a document window.
        commands.Register(CommandId.OpenRecentEntry, argument => _ = OpenRecentAsync(window, argument as string, binding));
        commands.Register(CommandId.ClearRecent, () =>
        {
            _services.Recent.Clear();
            window.RefreshRecent();
        });
        commands.Register(CommandId.OpenTextBundleFolder, () => _ = OpenBundleFolderAsync(binding));
        commands.Register(CommandId.Example, argument => OpenExample(argument as string));
        commands.Register(CommandId.Close, () => _ = CloseBookWindowAsync(window));
        commands.Register(CommandId.Save, window.SaveArticle);
        commands.Register(CommandId.Duplicate, () =>
        {
            if (window.ActiveArticle is { } article) OpenUntitled(article.Text, Strings.DuplicateTitle(article.Title), dirty: true);
        });
        commands.Register(CommandId.Exit, () => _ = RequestExitAsync());

        // Print, Share and Export act on the article, never on the whole book — the Book menu's own
        // rows are the book's (book.md §14, §8.5). The article publishes no path, so Share ▸ Source…
        // offers a copy, which is the Mac's nil fileURL.
        commands.Register(CommandId.Print, () => Article(window, article => PrintArticleAsync(window, article)));
        commands.Register(CommandId.ShareSource, () => Article(window, article =>
        {
            window.SaveArticle();
            return exports.Pipeline.ShareSourceAsync(null, article.Text, article.Title);
        }));
        commands.Register(CommandId.ShareRenderedPdf, () => Article(window, a => exports.Pipeline.SharePdfAsync(a.Text, a.Title, PdfPageSize)));
        commands.Register(CommandId.ExportPdf, () => Article(window, a => exports.Pipeline.ExportPdfAsync(a.Text, a.Title, PdfPageSize)));
        commands.Register(CommandId.ExportHtml, () => Article(window, a => exports.Pipeline.ExportHtmlAsync(a.Text, a.Title)));
        commands.Register(CommandId.ExportEpub, () => Article(window, a => exports.Pipeline.ExportEpubAsync(a.Text, a.Title)));
        commands.Register(CommandId.ExportLaTeX, () => Article(window, a => exports.Pipeline.ExportLaTeXAsync(a.Text, a.Title)));
        // documentPath null: the article's images are the book's, and the Mac exports a bundle with
        // an empty assets/ from a document it publishes no URL for.
        commands.Register(CommandId.ExportTextBundle, () => Article(window, a => exports.Pipeline.ExportTextBundleAsync(a.Text, null, a.Title)));
        commands.Register(CommandId.ExportDiagramSvg, argument =>
        {
            if (argument is int ordinal) Article(window, a => exports.Pipeline.ExportDiagramSvgAsync(a.Text, a.Title, ordinal));
        });

        // Edit (§2.4) — the TextBox implements these; the rows only reach it.
        commands.Register(CommandId.Undo, () => window.Editor.Undo());
        commands.Register(CommandId.Redo, () => window.Editor.Redo());
        commands.Register(CommandId.Cut, () => window.Editor.CutSelectionToClipboard());
        commands.Register(CommandId.Copy, () => window.Editor.CopySelectionToClipboard());
        commands.Register(CommandId.Paste, () => window.Editor.PasteFromClipboard());
        commands.Register(CommandId.Delete, () => window.Editor.SelectedText = "");
        commands.Register(CommandId.SelectAll, () => window.Editor.SelectAll());

        // Window (§2.8)
        commands.Register(CommandId.Minimize, () => (window.AppWindow.Presenter as OverlappedPresenter)?.Minimize());
        commands.Register(CommandId.Zoom, () =>
        {
            if (window.AppWindow.Presenter is not OverlappedPresenter presenter) return;
            if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
            else presenter.Maximize();
        });
        commands.Register(CommandId.ActivateWindow, argument => { if (argument is Guid id) Activate(id); });

        // Help (§2.8)
        commands.Register(CommandId.Help, () => _ = Launcher.LaunchUriAsync(new Uri(Strings.Help.SupportUrl)));
        commands.Register(CommandId.PrivacyPolicy, () => _ = Launcher.LaunchUriAsync(new Uri(Strings.Help.PrivacyUrl)));
        commands.Register(CommandId.About, () => _ = Controls.AboutDialog.ShowAsync(window.Content.XamlRoot));
    }

    /// <summary>Run one flow over the article being written, and let the window's close wait for it (§1.4).</summary>
    static void Article(BookWindow window, Func<(string Text, string Title), Task> flow)
    {
        if (window.ActiveArticle is not { } article) return;
        window.TrackOutput(flow(article));
    }

    /// <summary>File ▸ Print… in the Book window: the overlay slot is shown for as long as the sheet is up (§7.2).</summary>
    static async Task PrintArticleAsync(BookWindow window, (string Text, string Title) article)
    {
        // A collapsed slot hides the WebView2 the preview is drawn inside, and Chromium draws no
        // preview at all for a hidden control (§7.2).
        window.OverlaySlot.Visibility = Visibility.Visible;
        try
        {
            await window.Exports!.PrintAsync(article.Text, article.Title);
        }
        finally
        {
            window.OverlaySlot.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// File ▸ Open Recent ▸ a row, from the Book window (§6.6). A token is a promise, not a path: a
    /// row whose file has been moved or deleted is forgotten and named, and the rows are re-read
    /// either way — exactly what a document window's own handler does, because it is the same MRU.
    /// </summary>
    async Task OpenRecentAsync(BookWindow window, string? token, IAlerts alerts)
    {
        if (token is null) return;
        var name = window.RecentRows.FirstOrDefault(e => string.Equals(e.Token, token, StringComparison.Ordinal))?.Name ?? token;
        if (await _services.Recent.ResolveAsync(token) is not { } file)
        {
            _services.Recent.Remove(token);
            window.RefreshRecent();
            await alerts.WarnAsync(Strings.Documents.FileNotFound(name), "");
            return;
        }
        OpenPath(file.Path);
        window.RefreshRecent();
    }

    async Task OpenWithPickerAsync(IPickers pickers)
    {
        foreach (var path in await pickers.OpenFilesAsync(DocumentLoader.OpenExtensions)) OpenPath(path);
    }

    async Task OpenBundleFolderAsync(IPickers pickers)
    {
        if (await pickers.PickFolderAsync(null) is { } folder) OpenPath(folder);
    }

    /// <summary>
    /// File ▸ Examples ▸ a row, from either window: an untitled, dirty window holding the bundled
    /// text (§2.3). One copy of the rule, because both windows carry the same nine rows.
    /// </summary>
    public void OpenExample(string? fileName)
    {
        if (fileName is null) return;
        var example = _services.Examples.Examples.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.Ordinal));
        if (example is null || _services.Examples.ReadText(example) is not { } text) return;
        // Untitled and dirty, as on the Mac: the row is a starting point, not a file to overwrite.
        OpenUntitled(text, dirty: true);
    }

    /// <summary>File ▸ Close in the Book window: §1.4 route 2 — the policy, then the window.</summary>
    static async Task CloseBookWindowAsync(BookWindow window)
    {
        if (await window.RequestCloseAsync()) window.CloseApproved();
    }

    /// <summary>The trim size the two PDF flows render at — <c>md.pdfPageSize</c>, shared by every window (§9).</summary>
    PageSize PdfPageSize => PageSize.Named(_services.Settings.GetString(SettingsKeys.PdfPageSize));

    void ForgetBook(BookWindow window)
    {
        if (!ReferenceEquals(_book, window)) return;
        _book = null;
        _services.Registry.Remove(window.Id);
        SaveSession();
    }

    // ── session (§1.6) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reopen what <c>session.json</c> lists, in order, with each window's mode, Zen and placement.
    /// Rows whose files are gone were already dropped by <see cref="SessionStore.Restorable"/>; if
    /// nothing is left, the launch gets its one untitled window instead.
    /// </summary>
    public DocumentWindow? RestoreSession()
    {
        var state = SessionStore.Restorable(SessionStore.Load(SystemIoFileSystem.Instance, _services.LocalFolder), SystemIoFileSystem.Instance);
        DocumentWindow? last = null;
        foreach (var row in state.Windows)
        {
            var window = Create();
            // A row whose file has become unreadable since Restorable checked it (a permission, a
            // decode failure) is treated exactly as an Open of it would be: the window stays,
            // untitled, with the reason on it. Skipping the Activate instead would leave a Window in
            // _windows that was never shown — one that still counts at Exit and could be handed a
            // dialog with no visible tree to put it in.
            var opened = window.OpenDocument(row.Path);
            window.Place(row.Placement, row.Maximized);
            if (opened) window.RestoreView(row.Mode, row.Zen, row.ZenReading);
            window.Activate();
            last = window;
        }

        // The Book window comes back with the documents, and re-lists whatever md.bookBookmark still
        // points at — a book that has since gone leaves the empty state, exactly as opening it by
        // hand would. Which window ends up in front is Perform's rule, not this one's: the last
        // document restored, or the Book window when the session held nothing else.
        if (state.Book is { Open: true } book)
        {
            // Placed before it is shown, as a document window is: activating first and moving
            // afterwards is a window that jumps across the desktop in front of the reader.
            var window = _book ??= CreateBookWindow();
            Place(window, book.Placement, book.Maximized);
            window.Activate();
        }
        return last ?? (_book is null ? OpenUntitled() : null);
    }

    /// <summary>Record the windows that are open now. Called when a window closes and once at Exit (§1.6).</summary>
    public void SaveSession()
    {
        if (_exiting) return;
        Write(_windows);
    }

    void Write(IEnumerable<DocumentWindow> windows)
    {
        var rows = new List<SessionWindow>();
        foreach (var window in windows)
        {
            if (window.SessionRow() is { } row) rows.Add(row);
        }
        // The Book window's row (§1.6): where it was, and that it was open at all. Absent — the key
        // is simply not written — decodes as "no book window", which is what a launch that never
        // opened one should restore.
        SessionStore.Save(SystemIoFileSystem.Instance, _services.LocalFolder, new SessionState(rows, BookRow()));
    }

    SessionBook? BookRow() =>
        _book is { } book ? new SessionBook(true, FrameOf(book), IsMaximized(book)) : null;

    // ── closing (§1.4) ────────────────────────────────────────────────────────────────────────

    /// <summary>The window has closed: forget it, and record what is left (§1.6).</summary>
    public void Forget(DocumentWindow window)
    {
        _windows.Remove(window);
        if (ReferenceEquals(_lastActive, window)) _lastActive = _windows.Count > 0 ? _windows[^1] : null;
        // §8.6: the book pane reclaims an article whose window has just closed.
        _book?.RecheckOwnership();
        SaveSession();
    }

    /// <summary>The window came forward; the next new window cascades from it (§1.3).</summary>
    public void NoteActivated(DocumentWindow window)
    {
        _lastActive = window;
        // The Mac's didBecomeKeyNotification, from the other side: a document window taking an
        // article makes the book pane step aside the moment that window is keyed (§8.6).
        _book?.RecheckOwnership();
    }

    /// <summary>
    /// File ▸ Exit: every window runs the §6.3 close policy in z-order, and the first Cancel aborts
    /// the exit with nothing closed. The session is written from the windows as they were, before
    /// any of them lets go of its document — a quit with three windows restores three.
    /// </summary>
    public async Task<bool> RequestExitAsync()
    {
        if (_exiting) return true;

        var open = _windows.ToList();
        Write(open);

        foreach (var window in open)
        {
            if (!await window.RequestCloseAsync()) return false;
        }
        // The Book window runs the same policy (§1.4): it waits for an output in flight and lets go
        // of the article it is writing.
        var book = _book;
        if (book is not null && !await book.RequestCloseAsync()) return false;

        // Only now: a Cancel above must leave the recorded session alone, and the closes below must
        // not rewrite it window by window down to nothing.
        _exiting = true;
        foreach (var window in open) window.CloseApproved();
        book?.CloseApproved();
        return true;
    }

    // ── the Book window's placement (§1.3) ────────────────────────────────────────────────────
    //
    // A document window does this arithmetic itself; the Book window sizes itself from
    // md.win.windowSize.book and needs the rest only when a session row is restored, so it lives
    // here rather than as a second copy of DocumentWindow.Place in another file.

    static void Place(BookWindow window, WindowRect frame, bool maximized)
    {
        var scale = ScaleOf(window);
        var wanted = new RectInt32(Px(frame.X, scale), Px(frame.Y, scale), Px(frame.Width, scale), Px(frame.Height, scale));
        var work = ToEpx(DisplayArea.GetFromRect(wanted, DisplayAreaFallback.Nearest).WorkArea, scale);
        var clamped = WindowPlacement.Clamp(frame, work);

        window.AppWindow.Move(new PointInt32(Px(clamped.X, scale), Px(clamped.Y, scale)));
        window.AppWindow.ResizeClient(new SizeInt32(Px(clamped.Width, scale), Px(clamped.Height, scale)));
        if (maximized && window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Maximize();
    }

    static WindowRect FrameOf(BookWindow window)
    {
        var scale = ScaleOf(window);
        var position = window.AppWindow.Position;
        var size = window.AppWindow.ClientSize;
        return new WindowRect(Epx(position.X, scale), Epx(position.Y, scale), Epx(size.Width, scale), Epx(size.Height, scale));
    }

    static bool IsMaximized(BookWindow window) =>
        window.AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    static double ScaleOf(Window window)
    {
        // GetDpiForWindow answers 0 for a window it does not know; 0/96 would collapse it to nothing.
        var dpi = Interop.NativeMethods.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window));
        return (dpi == 0 ? 96.0 : dpi) / 96.0;
    }

    static int Px(int epx, double scale) => (int)Math.Round(epx * scale, MidpointRounding.AwayFromZero);

    static int Epx(int px, double scale) => (int)Math.Round(px / scale, MidpointRounding.AwayFromZero);

    static WindowRect ToEpx(RectInt32 rect, double scale) =>
        new(Epx(rect.X, scale), Epx(rect.Y, scale), Epx(rect.Width, scale), Epx(rect.Height, scale));

    void TerminateFlush()
    {
        foreach (var window in _windows.ToList()) window.Session.TerminateFlush();
        // The article being written in the Book window is a file like any other; its 1 s autosave
        // bounds what this can miss, exactly as a document's does.
        _book?.SaveArticle();
    }
}

/// <summary>
/// The process-wide services §1.7 lists, in one object every window is handed. Lives here because
/// the window manager is their first and, today, only consumer — move it to its own file the moment
/// WP6 or WP7 needs one of them without a window, exactly as WP4 said of <c>PaneBrushes</c>.
/// </summary>
internal sealed class AppServices
{
    public AppServices(ISettingsStore settings, string localFolder)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrEmpty(localFolder);
        Settings = settings;
        LocalFolder = localFolder;
        ViewModeMemory = new SettingsViewModeStore(settings);
        WordCounter = WordCounters.Create();
    }

    /// <summary><c>ApplicationData.Current.LocalSettings</c> (§9).</summary>
    public ISettingsStore Settings { get; }

    /// <summary>Where <c>session.json</c> lives (§1.6).</summary>
    public string LocalFolder { get; }

    /// <summary>Who owns which file, and what the Window menu lists (§8.6).</summary>
    public WindowRegistry Registry { get; } = new();

    /// <summary>The only reader and writer of <c>md.viewModeMemory</c> (§5.2).</summary>
    public IViewModeStore ViewModeMemory { get; }

    /// <summary>ICU over the in-box <c>icu.dll</c>, with the simple counter as the fallback (§5.5).</summary>
    public IWordCounter WordCounter { get; }

    /// <summary>File ▸ Open Recent, and Windows' own Recent items and Jump List (§6.6).</summary>
    public RecentFiles Recent { get; } = new();

    /// <summary>The nine bundled File ▸ Examples rows (§2.3).</summary>
    public Services.ExampleLibrary Examples { get; } = new();
}
