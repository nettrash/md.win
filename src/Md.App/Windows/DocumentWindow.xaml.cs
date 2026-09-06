// One document window (shell-design.md §1). This is the file where the packages meet: WP2's
// TextFileSession, WP4's DocumentWindowState / ViewModeController / ZenController / panes, WP5's
// PreviewCoordinator and WP1's CommandTable are all driven from here, and every decision they make
// is theirs and already tested off Windows. What is left in this file is the part only Windows can
// run: the AppWindow, the three close routes, the presenter, the DPI arithmetic and the pickers.
using Md.App.Controls;
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
using Md.Core.Export;
using Md.Core.Markdown;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Launcher = Windows.System.Launcher;
using PreviewNav = Md.App.Logic.Preview.PreviewNavigation;

namespace Md.App;

internal sealed partial class DocumentWindow : Window
{
    /// <summary>
    /// The commands whose targets do not exist yet: exports and print are WP6's, books are WP7's.
    /// They stay enabled (§2.2 keeps Export ▸ live) and route here, to one clearly named no-op, so
    /// that the wiring is a single line to delete rather than a search. Nothing else in this file
    /// mentions them.
    /// </summary>
    static readonly CommandId[] NotYetWired =
    [
        // WP6 — exports, share and print (§7).
        CommandId.Print, CommandId.ShareSource, CommandId.ShareRenderedPdf,
        CommandId.ExportPdf, CommandId.ExportHtml, CommandId.ExportEpub, CommandId.ExportLaTeX,
        CommandId.ExportTextBundle, CommandId.ExportDiagramSvg,
        // WP7 — books (§8). ShowSidebar, Previous/Next Article are Book-window rows and are
        // disabled here by CommandEnablement; they are listed so no id is silently unhandled.
        CommandId.NewBook, CommandId.OpenBook, CommandId.ShowBook, CommandId.CloseBook,
        CommandId.ShareBookPdf, CommandId.PrintBook,
        CommandId.ExportBookPdf, CommandId.ExportBookEpub, CommandId.ExportBookLaTeX,
        CommandId.ExampleBook, CommandId.PreviousArticle, CommandId.NextArticle, CommandId.ShowSidebar,
    ];

    /// <summary>An export or print in flight is awaited before the window goes, but never for ever (§1.4).</summary>
    static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(10);

    readonly AppServices _services;
    readonly WindowManager _manager;
    readonly UiThread _ui;
    readonly DispatcherScheduler _scheduler;
    readonly FileSystemWatcherAdapter _watcher;
    readonly WinUiAlerts _alerts;
    readonly Pickers _pickers;

    readonly TextFileSession _session;
    readonly DocumentWindowState _state;
    readonly ViewModeController _modes;
    readonly ZenController _zen;
    readonly ScrollSyncGuard _scrollGuard;
    readonly DerivedTextScheduler _derived;
    readonly PreviewHost _previewHost;
    readonly Md.App.Logic.Preview.PreviewCoordinator _preview;
    readonly CommandDispatcher _dispatcher;
    readonly MenuBarBuilder _menu;

    IReadOnlyList<Md.App.Logic.Commands.DiagramRef> _diagrams = [];
    (bool Conflicted, string? Error)? _infoState;
    string _derivedText = "";
    bool? _zenApplied;
    IReadOnlyList<RecentEntry> _recent = [];
    ShellSnapshot _snapshot = ShellSnapshot.Empty;
    WindowRect _restoreFrame;
    int _untitledOrdinal;
    int _undoGeneration;
    bool _publishing;
    bool _closeApproved;
    bool _closePending;
    bool _closed;
    Task _output = Task.CompletedTask;
    (string Title, string Message)? _capturedAlert;
    bool _captureAlerts;

    public DocumentWindow(AppServices services, WindowManager manager)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(manager);
        _services = services;
        _manager = manager;
        InitializeComponent();

        _ui = new UiThread(DispatcherQueue);
        _scheduler = new DispatcherScheduler(DispatcherQueue);
        _watcher = new FileSystemWatcherAdapter(_ui);
        _alerts = new WinUiAlerts(this);
        _pickers = new Pickers(this, _alerts);

        _session = new TextFileSession(
            SystemIoFileSystem.Instance, _watcher, _scheduler, services.Registry, FileIdentity.Instance,
            SessionRole.Document, Id);
        _state = new DocumentWindowState();
        _modes = new ViewModeController(_state, services.ViewModeMemory);
        _zen = new ZenController(_state, _scheduler);
        _scrollGuard = new ScrollSyncGuard(_scheduler);
        _derived = new DerivedTextScheduler(_scheduler, _ui, services.WordCounter, DiagramsOf);
        _previewHost = new PreviewHost();
        _preview = new Md.App.Logic.Preview.PreviewCoordinator(_previewHost.Surface, _scheduler);
        _dispatcher = new CommandDispatcher(_scheduler, () => _snapshot);
        _menu = new MenuBarBuilder(_dispatcher, new MenuBarSources(services.Examples.Examples, NotePreview.Of));

        services.Registry.Add(Id, Strings.Untitled);
        BuildContent();
        RegisterCommands();
        SubscribeEvents();
        ApplyTheme();
        ApplyLayout();
        RefreshRecent();
        Publish();
    }

    /// <summary>This window's identity in <see cref="WindowRegistry"/> and in the Window menu.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The document, for the packages that need it (WP6's exports read <c>Text</c> and <c>Title</c>).</summary>
    public TextFileSession Session => _session;

    /// <summary>WP6's off-canvas export surface: a fresh WebView2 per export goes here at <c>Canvas.Left = -10000</c> (§7.1).</summary>
    public Canvas ExportSurface => ExportCanvas;

    /// <summary>WP6's print overlay host: a full-window <c>Grid</c>, collapsed until a print starts (§7.2).</summary>
    public Grid OverlayLayer => OverlayHost;

    /// <summary>The alerts and pickers this window owns, for WP6/WP7's pipelines.</summary>
    public IAlerts Alerts => _alerts;

    /// <summary>Ditto — every picker must be initialised with <em>this</em> window's handle (§6.1).</summary>
    public IPickers Pickers => _pickers;

    /// <summary>The UI-thread timer source, for a pipeline that needs one (WP6's render-complete poller).</summary>
    public IScheduler Scheduler => _scheduler;

    /// <summary>True while the window shows the dark palette; the preview HTML and the title bar follow it.</summary>
    public bool IsDark => Root.ActualTheme == ElementTheme.Dark;

    /// <summary>The window's handle, for the pickers, the share sheet and <c>SetForegroundWindow</c>.</summary>
    public nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(this);

    // ── placement ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Effective pixels per physical pixel; read from the window's DPI, not from XamlRoot, so it is valid before the content is loaded (§1.3).</summary>
    public double Scale
    {
        get
        {
            // GetDpiForWindow returns 0 for an invalid window; 0/96 would collapse every window to
            // nothing, so an unanswered call means "100 %".
            var dpi = Interop.NativeMethods.GetDpiForWindow(Handle);
            return (dpi == 0 ? 96.0 : dpi) / 96.0;
        }
    }

    /// <summary>The work area of the display this window is on, in effective pixels — what the manager cascades inside.</summary>
    public WindowRect WorkArea => ToEpx(DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea, Scale);

    /// <summary>The frame the window would restore to, in effective pixels: what <c>session.json</c> records.</summary>
    public WindowRect Frame => _restoreFrame;

    public bool IsMaximized => AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };

    /// <summary>
    /// Position and size the window in effective pixels, clamped into the work area of the display
    /// the frame lands on — which is what brings a placement saved on a monitor that is no longer
    /// attached back onto a screen the user can reach (§1.3).
    /// </summary>
    public void Place(WindowRect frame, bool maximized)
    {
        var scale = Scale;
        var wanted = new RectInt32(Px(frame.X, scale), Px(frame.Y, scale), Px(frame.Width, scale), Px(frame.Height, scale));
        var work = ToEpx(DisplayArea.GetFromRect(wanted, DisplayAreaFallback.Nearest).WorkArea, scale);
        var clamped = WindowPlacement.Clamp(frame, work);

        AppWindow.Move(new PointInt32(Px(clamped.X, scale), Px(clamped.Y, scale)));
        // ResizeClient, not Resize: the design's 900 × 640 is the CLIENT area, in physical pixels.
        AppWindow.ResizeClient(new SizeInt32(Px(clamped.Width, scale), Px(clamped.Height, scale)));
        _restoreFrame = clamped;

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = Px(WindowPlacement.Minimum.Width, scale);
            presenter.PreferredMinimumHeight = Px(WindowPlacement.Minimum.Height, scale);
            if (maximized) presenter.Maximize();
        }
    }

    /// <summary>The row <c>session.json</c> keeps for this window, or null while it is untitled (§1.6).</summary>
    public SessionWindow? SessionRow() =>
        _session.EditingPath is { } path
            ? new SessionWindow(path, _state.StoredMode, _state.ZenActive, _state.ZenReading, _restoreFrame, IsMaximized)
            : null;

    // ── opening ───────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Open a file, a <c>.textpack</c> or a <c>.textbundle</c> folder into this window — the session
    /// classifies which (§6.5). False when nothing could be read; the reason is already on screen as
    /// an alert, and the window stays (untitled) rather than vanishing under the dialog it owns.
    /// </summary>
    public bool OpenDocument(string path)
    {
        if (!_session.Open(path)) return false;
        ReleaseUntitledNumber();
        RefreshRecent();
        return true;
    }

    /// <summary>A new document with no file: File ▸ New, an Example (dirty), Duplicate, a restored untitled window.</summary>
    public void OpenUntitled(string text, string? title, bool dirty, int untitledOrdinal)
    {
        _untitledOrdinal = untitledOrdinal;
        _session.OpenUntitled(text, title, dirty);
    }

    /// <summary>Restore mode, Zen and Zen's reading side from a <c>session.json</c> row, after the document is open.</summary>
    public void RestoreView(ViewMode mode, bool zen, bool zenReading)
    {
        _modes.SetMode(mode);
        _state.ZenReading = zenReading;
        if (zen) _zen.SetActive(true);
    }

    // ── closing (§1.4) ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The §6.3 close policy, asked before the window goes. True means "the window may close now":
    /// an untitled, dirty document has been saved or abandoned on purpose, a saved one has been
    /// flushed (with a rescue copy and an alert if the flush failed), and any export or print in
    /// flight has finished or timed out. False is Cancel, and cancels an Exit with it.
    /// </summary>
    public async Task<bool> RequestCloseAsync()
    {
        if (_closeApproved) return true;

        await DrainOutputAsync();

        if (_session.Stage is Stage.Untitled && _session.IsDirty)
        {
            switch (await _alerts.AskSaveChangesAsync(Strings.Documents.SaveChangesQuestion(_session.Title)))
            {
                case CloseChoice.Save when !await SaveAsAsync():
                    return false;                    // the picker was cancelled: the close is off too
                case CloseChoice.Cancel:
                    return false;
                default:
                    break;
            }
        }

        // CloseFlush writes, rescues on failure and detaches. Its alert has to be awaited HERE: the
        // window is about to go, and a dialog put up after Close() has no tree to live in.
        _captureAlerts = true;
        _capturedAlert = null;
        try
        {
            _session.CloseFlush();
        }
        finally
        {
            _captureAlerts = false;
        }
        if (_capturedAlert is { } alert)
        {
            _capturedAlert = null;
            await _alerts.WarnAsync(alert.Title, alert.Message);
        }
        return true;
    }

    /// <summary>Close the window now, without re-asking: the policy has already said yes (§1.4 routes 2 and 3).</summary>
    public void CloseApproved()
    {
        _closeApproved = true;
        Close();
    }

    /// <summary>
    /// WP6 registers an export or print here so a close waits for it rather than tearing its
    /// WebView2 down mid-render (§1.4). Tracked outputs <em>accumulate</em>: Export ▸ PDF while an
    /// EPUB snapshot is still running must not make the close forget the EPUB, so the second call
    /// joins the first rather than replacing it.
    /// </summary>
    public void TrackOutput(Task output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = _output.IsCompleted ? output : Task.WhenAll(_output, output);
    }

    async Task DrainOutputAsync()
    {
        if (_output.IsCompleted) return;
        // Bounded: a wedged renderer must not make the window unclosable.
        await Task.WhenAny(_output, Task.Delay(OutputDrainTimeout));
    }

    // ── building ──────────────────────────────────────────────────────────────────────────────

    void BuildContent()
    {
        MenuSlot.Content = _menu.Build();
        AcceleratorInstaller.Install(Root, _dispatcher);
        // §6.1: drag & drop onto the window root. The root, not a pane — a document dropped anywhere
        // on the window opens, exactly as dropping on the Mac's window does.
        Root.AllowDrop = true;

        Panes.SetPreview(_previewHost);
        Panes.AttachScrollSync(_scrollGuard);
        Panes.ScrollSync.ScrollPreview = _previewHost.ApplyScrollFraction;
        _previewHost.PreviewDidScroll += fraction => Panes.ScrollSync.PreviewDidScroll(fraction);

        ZenCapsule.Attach(_zen, _state);
        // ArticlePanes' first arrange happens in its own constructor, before anything can subscribe,
        // so the preview's visibility is seeded here rather than waiting for a LayoutChanged.
        ShowPreview(Panes.Layout);

        Title = WindowTitle.For(Strings.Untitled, isDirty: false);
    }

    void SubscribeEvents()
    {
        _session.Changed += OnSessionChanged;
        _session.TextReplacedExternally += text => Panes.Editor.SetText(text);
        _session.IdentityChanged += OnIdentityChanged;
        _session.AlertRequested += OnSessionAlert;

        _state.Changed += OnStateChanged;
        _zen.FullScreenRequested += SetFullScreen;

        Panes.Editor.TextEdited += OnEditorTextEdited;
        Panes.Editor.Control.SelectionChanged += (_, _) => Publish();
        Panes.LayoutChanged += OnLayoutChanged;
        ContentHost.PointerMoved += (_, _) => ZenCapsule.Reveal();

        Find.SearchRequested += OnFindRequested;
        Find.Dismissed += HideFindBar;
        Find.QueryChanged += _ => Publish();

        _derived.Changed += OnDerivedChanged;
        _services.Registry.Changed += Publish;
        _services.Settings.Changed += OnSettingChanged;

        Root.DragOver += OnDragOver;
        Root.Drop += OnDrop;

        Root.ActualThemeChanged += (_, _) => ApplyTheme();
        Activated += OnActivated;
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
    }

    // ── events ────────────────────────────────────────────────────────────────────────────────

    void OnEditorTextEdited(string text)
    {
        // The Mac's sync() guard, from this side: our own SetText echo is not an edit.
        if (string.Equals(text, _session.Text, StringComparison.Ordinal)) return;
        _session.Edit(text);
    }

    void OnSessionChanged()
    {
        var title = WindowTitle.For(_session.Title, _session.IsDirty);
        if (!string.Equals(Title, title, StringComparison.Ordinal))
        {
            Title = title;
            _services.Registry.SetTitle(Id, title);
        }

        if (_undoGeneration != _session.UndoGeneration)
        {
            _undoGeneration = _session.UndoGeneration;
            // A fresh document, a revert or a reload: the Mac gives each one a fresh UndoManager.
            Panes.Editor.Control.ClearUndoRedoHistory();
        }

        Panes.Editor.SetText(_session.Text);
        if (!string.Equals(_derivedText, _session.Text, StringComparison.Ordinal))
        {
            // A save flips IsDirty without touching a character; restarting the 250 ms tick for that
            // would delay the footer behind every Ctrl+S.
            _derivedText = _session.Text;
            _derived.TextChanged(_session.Text);
        }
        UpdatePreview();
        UpdateInfoBar();
        Publish();
    }

    // The session's file changed: re-run the per-file view-mode rule for the new identity (§5.2)
    // and remember the file in Open Recent (§6.6). Both must happen for Open, Save As, Rename,
    // Move To and a rename under us — which is exactly the set that raises this.
    void OnIdentityChanged(string? path)
    {
        _modes.ApplyMemory(path, _session.Text);
        if (path is not null) _ = RememberRecentAsync(path);
    }

    async Task RememberRecentAsync(string path)
    {
        // The synchronization context Program.Main installs brings this back to the UI thread, so
        // the refresh needs no marshalling — but the window may be gone by then.
        await _services.Recent.AddAsync(path);
        if (_closed) return;
        RefreshRecent();
        Publish();
    }

    void OnSessionAlert(string title, string message)
    {
        if (_captureAlerts)
        {
            _capturedAlert = (title, message);
            return;
        }
        _ = _alerts.WarnAsync(title, message);
    }

    void OnStateChanged()
    {
        ApplyLayout();
        Footer.Update(_state.Derived);
        PerformJumps();
        Publish();
    }

    void OnDerivedChanged(DerivedText derived)
    {
        _diagrams = [.. derived.Diagrams.Select(d => new Md.App.Logic.Commands.DiagramRef(d.Ordinal, d.MenuTitle))];
        _state.Derived = derived;                    // raises Changed → footer, menus
    }

    void OnLayoutChanged(PaneLayout layout)
    {
        ShowPreview(layout);
        Publish();
    }

    /// <summary>
    /// The preview's own Visibility, not just its slot's. <c>IPreviewSurface.IsShown</c> reads the
    /// host's Visibility and the WebView2's, and <c>ArticlePanes</c> collapses the <em>slot</em> that
    /// holds the host — so without this the coordinator would think a collapsed pane was on screen
    /// and reload a page nobody is looking at, losing §4.5's stale-while-collapsed entirely.
    /// Set BEFORE Show/Hide, which is what decides whether the page has to catch up.
    /// </summary>
    void ShowPreview(PaneLayout layout)
    {
        var shown = SplitLayout.ShowsPreview(layout);
        _previewHost.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        if (shown) _preview.Show();
        else _preview.Hide();
    }

    void OnSettingChanged(string key)
    {
        // Another window picked a page size, or opened a book: this window's menu ticks follow.
        if (key is SettingsKeys.PdfPageSize or SettingsKeys.BookBookmark) _ui.Post(Publish);
    }

    void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated)
        {
            // §6.3: a flush on deactivation, so the bytes on disk are current the moment the writer
            // switches to another app — Share ▸ Source and every external tool see the same text.
            _session.FlushNow(explicitSave: false);
            Publish();
            return;
        }

        _manager.NoteActivated(this);
        // The Mac's didBecomeKeyNotification: the book pane reclaims an article whose window closed.
        _session.RecheckOwnership();
        RefreshRecent();
        Publish();
    }

    // ── drag & drop (§6.1) ────────────────────────────────────────────────────────────────────

    /// <summary>Storage items are the only payload md answers; text and bitmaps are not documents.</summary>
    void OnDragOver(object sender, DragEventArgs args)
    {
        args.AcceptedOperation = args.DataView.Contains(StandardDataFormats.StorageItems)
            ? DataPackageOperation.Copy
            : DataPackageOperation.None;
        args.Handled = true;
    }

    /// <summary>
    /// §6.1 / §6.5: files open as documents, a <c>.textpack</c> imports, a <c>.textbundle</c>
    /// <em>folder</em> imports and every other folder is ignored — the same
    /// <see cref="ActivationRouter.Classify"/> a double-click goes through, so a drop and an
    /// association cannot drift apart. A file already open activates its window rather than
    /// opening a second one, because the manager runs the same resolution.
    /// </summary>
    async void OnDrop(object sender, DragEventArgs args)
    {
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return;
        args.Handled = true;
        // GetStorageItemsAsync is awaited, so the source must be told when we are done with the
        // data package — without the deferral the view can be released under us.
        var deferral = args.GetDeferral();
        try
        {
            var actions = new List<ActivationAction>();
            foreach (var item in await args.DataView.GetStorageItemsAsync())
            {
                // The shell's own answer to "folder?", exactly as a File activation gets it.
                if (ActivationRouter.Classify(new ActivationItem(item.Path ?? "", item is StorageFolder)) is { } action)
                    actions.Add(action);
            }
            if (!_closed) _manager.PerformDrop(actions);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            // A drop whose source withdrew the data is not worth a dialog.
            System.Diagnostics.Debug.WriteLine($"md: drop failed: {e}");
        }
        finally
        {
            deferral.Complete();
        }
    }

    void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        // Route 1: a system affordance (the close button, Alt+F4, the taskbar). The event is
        // documented not to fire for Destroy, and this design never assumes Close() re-enters it —
        // hence the flag rather than a "we are already inside Closing" guess.
        if (_closeApproved) return;
        args.Cancel = true;
        _ = ApproveAndCloseAsync();
    }

    /// <summary>Routes 1 and 2 of §1.4 both land here: ask the policy, and close if it says yes. One attempt at a time — a second Ctrl+W over the open dialog must not stack another.</summary>
    async Task ApproveAndCloseAsync()
    {
        if (_closePending || _closeApproved) return;
        _closePending = true;
        try
        {
            if (await RequestCloseAsync()) CloseApproved();
        }
        finally
        {
            _closePending = false;
        }
    }

    void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange)
        {
            // Leaving full screen by ANY means — F11, Esc, Win+Down, the caption button — drops Zen;
            // our own toggle is filtered by the controller's own flag (§5.4).
            _zen.PresenterChanged(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen);
            ApplyLayout();
            Publish();
        }

        if (args.DidPositionChange || args.DidSizeChange) RememberFrame();
    }

    void OnClosed(object sender, WindowEventArgs args)
    {
        // Route 3: cleanup only. The decision was made before Close() was called, so Handled stays
        // false — nothing here may veto.
        if (_closed) return;
        _closed = true;

        _services.Settings.Changed -= OnSettingChanged;
        _services.Registry.Changed -= Publish;
        _derived.Cancel();
        _session.Dispose();
        _watcher.Dispose();

        // The browser process outlives the XAML tree unless it is told to go. WP5's PreviewHost does
        // not publish a Close() yet (integration note); its Content is the control, and closing that
        // is exactly what §1.4 asks for.
        if (_previewHost.Content is WebView2 web) web.Close();

        _services.Registry.Remove(Id);
        _services.Settings.SetString(SettingsKeys.DocumentWindowSize, WindowPlacement.FormatSize(_restoreFrame.Size));
        ReleaseUntitledNumber();
        _manager.Forget(this);
    }

    // ── commands ──────────────────────────────────────────────────────────────────────────────

    void RegisterCommands()
    {
        // File
        _dispatcher.Register(CommandId.New, () => _manager.OpenUntitled());
        _dispatcher.Register(CommandId.Open, () => _ = OpenWithPickerAsync());
        _dispatcher.Register(CommandId.OpenRecentEntry, argument => _ = OpenRecentAsync(argument as string));
        _dispatcher.Register(CommandId.ClearRecent, () => { _services.Recent.Clear(); RefreshRecent(); Publish(); });
        _dispatcher.Register(CommandId.OpenTextBundleFolder, () => _ = OpenBundleFolderAsync());
        _dispatcher.Register(CommandId.Example, argument => OpenExample(argument as string));
        _dispatcher.Register(CommandId.Close, () => _ = ApproveAndCloseAsync());
        _dispatcher.Register(CommandId.Save, () => _ = SaveAsync());
        _dispatcher.Register(CommandId.SaveAs, () => _ = SaveAsAsync());
        _dispatcher.Register(CommandId.Duplicate, Duplicate);
        _dispatcher.Register(CommandId.Rename, () => _ = RenameAsync());
        _dispatcher.Register(CommandId.MoveTo, () => _ = MoveToAsync());
        _dispatcher.Register(CommandId.RevertToSaved, () => _session.RevertToSaved());
        _dispatcher.Register(CommandId.PdfPageSize, argument => SetPdfPageSize(argument as string));
        _dispatcher.Register(CommandId.Exit, () => _ = _manager.RequestExitAsync());

        // Edit — the TextBox does these itself; the rows only reach it (§2.4).
        _dispatcher.Register(CommandId.Undo, () => Editor.Undo());
        _dispatcher.Register(CommandId.Redo, () => Editor.Redo());
        _dispatcher.Register(CommandId.Cut, () => Editor.CutSelectionToClipboard());
        _dispatcher.Register(CommandId.Copy, () => Editor.CopySelectionToClipboard());
        _dispatcher.Register(CommandId.Paste, () => Editor.PasteFromClipboard());
        _dispatcher.Register(CommandId.Delete, () => Editor.SelectedText = "");
        _dispatcher.Register(CommandId.SelectAll, () => Editor.SelectAll());
        _dispatcher.Register(CommandId.Find, ShowFindBar);
        _dispatcher.Register(CommandId.FindNext, () => Find.Search(forward: true));
        _dispatcher.Register(CommandId.FindPrevious, () => Find.Search(forward: false));
        _dispatcher.Register(CommandId.UseSelectionForFind, UseSelectionForFind);

        // View
        _dispatcher.Register(CommandId.ViewEdit, () => _modes.Select(ViewMode.Edit));
        _dispatcher.Register(CommandId.ViewSplit, () => _modes.Select(ViewMode.Split));
        _dispatcher.Register(CommandId.ViewPreview, () => _modes.Select(ViewMode.Preview));
        _dispatcher.Register(CommandId.ZenMode, () => _zen.Toggle());
        _dispatcher.Register(CommandId.FullScreen, ToggleFullScreen);
        _dispatcher.Register(CommandId.Escape, OnEscape);

        // Go
        _dispatcher.Register(CommandId.Contents, argument => { if (argument is OutlineEntry entry) _modes.JumpToHeading(entry); });
        _dispatcher.Register(CommandId.Notes, argument => { if (argument is NoteEntry note) _modes.JumpToNote(note); });

        // Window
        _dispatcher.Register(CommandId.Minimize, () => (AppWindow.Presenter as OverlappedPresenter)?.Minimize());
        _dispatcher.Register(CommandId.Zoom, ToggleZoom);
        _dispatcher.Register(CommandId.ActivateWindow, argument => { if (argument is Guid id) _manager.Activate(id); });

        // Help
        _dispatcher.Register(CommandId.Help, () => _ = Launcher.LaunchUriAsync(new Uri(Strings.Help.SupportUrl)));
        _dispatcher.Register(CommandId.PrivacyPolicy, () => _ = Launcher.LaunchUriAsync(new Uri(Strings.Help.PrivacyUrl)));
        _dispatcher.Register(CommandId.About, () => _ = AboutDialog.ShowAsync(Root.XamlRoot));

        foreach (var id in NotYetWired) _dispatcher.Register(id, () => NotWiredYet(id));
    }

    TextBox Editor => Panes.Editor.Control;

    /// <summary>The single "WP6/WP7 replace this" path (see <see cref="NotYetWired"/>).</summary>
    static void NotWiredYet(CommandId id) =>
        System.Diagnostics.Debug.WriteLine($"md: {id} has no handler yet (WP6 exports and print, WP7 books).");

    // ── file commands ─────────────────────────────────────────────────────────────────────────

    async Task OpenWithPickerAsync()
    {
        foreach (var path in await _pickers.OpenFilesAsync(DocumentLoader.OpenExtensions)) _manager.OpenPath(path);
    }

    async Task OpenRecentAsync(string? token)
    {
        if (token is null) return;
        var name = _recent.FirstOrDefault(e => string.Equals(e.Token, token, StringComparison.Ordinal))?.Name ?? token;
        if (await _services.Recent.ResolveAsync(token) is not { } file)
        {
            // A token is a promise, not a path: the file may have moved or gone (§6.6).
            _services.Recent.Remove(token);
            RefreshRecent();
            Publish();
            await _alerts.WarnAsync(Strings.Documents.FileNotFound(name), "");
            return;
        }
        _manager.OpenPath(file.Path);
    }

    async Task OpenBundleFolderAsync()
    {
        if (await _pickers.PickFolderAsync(null) is { } folder) _manager.OpenPath(folder);
    }

    void OpenExample(string? fileName)
    {
        if (fileName is null) return;
        var example = _services.Examples.Examples.FirstOrDefault(e => string.Equals(e.FileName, fileName, StringComparison.Ordinal));
        if (example is null || _services.Examples.ReadText(example) is not { } text) return;
        // Untitled and dirty, as on the Mac: the row is a starting point, not a file to overwrite.
        _manager.OpenUntitled(text, dirty: true);
    }

    async Task SaveAsync()
    {
        if (_session.Stage is Stage.Untitled)
        {
            await SaveAsAsync();
            return;
        }
        _session.FlushNow(explicitSave: true);
    }

    async Task<bool> SaveAsAsync()
    {
        var suggested = _session.Title.Length == 0 ? Strings.Untitled : _session.Title;
        var path = await _pickers.SaveFileAsync(suggested, DocumentLoader.SaveChoices, DocumentLoader.DefaultExtensionFor(_session.EditingPath));
        if (path is null) return false;

        if (!_session.SaveAs(path))
        {
            // PickSaveFileAsync returns a CREATED, empty file; a failed write must not leave it
            // behind pretending the save happened (§6.1).
            TryDelete(path);
            return false;
        }
        ReleaseUntitledNumber();
        return true;
    }

    void Duplicate() => _manager.OpenUntitled(_session.Text, Strings.DuplicateTitle(_session.Title), dirty: true);

    async Task RenameAsync()
    {
        if (_session.EditingPath is not { } path) return;
        var stem = await _alerts.PromptNameAsync(
            Strings.Documents.RenameTitle, Strings.Documents.RenameMessage, FileNames.StemOf(path), Strings.Documents.RenameTitle);
        if (stem is null) return;

        var name = FileNames.WithExtensionOf(FileNames.NameOf(path), stem);
        if (!FileNames.Validate(name))
        {
            await _alerts.WarnAsync(Strings.Documents.CouldNotRename, Strings.Documents.InvalidNameMessage);
            return;
        }
        // Write first: the file is about to change identity under the watcher.
        _session.FlushNow(explicitSave: false);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            await file.RenameAsync(name, NameCollisionOption.FailIfExists);
            _session.Retarget(FileNames.Combine(FileNames.DirectoryOf(path), name));
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            await _alerts.WarnAsync(Strings.Documents.CouldNotRename, e.Message);
        }
    }

    async Task MoveToAsync()
    {
        if (_session.EditingPath is not { } path) return;
        if (await _pickers.PickFolderAsync(null) is not { } folder) return;

        var name = FileNames.NameOf(path);
        _session.FlushNow(explicitSave: false);
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var destination = await StorageFolder.GetFolderFromPathAsync(folder);
            await file.MoveAsync(destination, name, NameCollisionOption.FailIfExists);
            _session.Retarget(FileNames.Combine(folder, name));
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            await _alerts.WarnAsync(Strings.Documents.CouldNotRename, e.Message);
        }
    }

    void SetPdfPageSize(string? id)
    {
        if (id is null) return;
        _services.Settings.SetString(SettingsKeys.PdfPageSize, PageSize.Named(id).Id);
        Publish();
    }

    // ── find, view, window commands ───────────────────────────────────────────────────────────

    void ShowFindBar()
    {
        // §5.4: Zen has no chrome at all, and the bar is one of the rows it collapses.
        if (_state.ZenActive) return;
        Find.Visibility = Visibility.Visible;
        if (Editor.SelectionLength > 0) Find.SetQuery(Editor.SelectedText);
        Find.FocusQuery();
        Publish();
    }

    void HideFindBar()
    {
        Find.Visibility = Visibility.Collapsed;
        Panes.Editor.FocusEditor();
        Publish();
    }

    void UseSelectionForFind()
    {
        if (_state.ZenActive || Editor.SelectionLength == 0) return;
        Find.Visibility = Visibility.Visible;
        Find.SetQuery(Editor.SelectedText);
        Publish();
    }

    void OnFindRequested(string query, bool forward)
    {
        // TextSearch indexes the TextBox's OWN string (with \r), because SelectionStart does too.
        var text = Editor.Text;
        var match = forward
            ? TextSearch.Next(text, query, Editor.SelectionStart + Editor.SelectionLength)
            : TextSearch.Previous(text, query, Editor.SelectionStart);
        if (match is { } hit) Panes.Editor.SelectRange(hit.Index, hit.Length);
    }

    void OnEscape()
    {
        if (Find.Visibility == Visibility.Visible)
        {
            HideFindBar();
            return;
        }
        _zen.SetActive(false);
    }

    void ToggleFullScreen() =>
        SetFullScreen(AppWindow.Presenter.Kind != AppWindowPresenterKind.FullScreen);

    void SetFullScreen(bool fullScreen) =>
        AppWindow.SetPresenter(fullScreen ? AppWindowPresenterKind.FullScreen : AppWindowPresenterKind.Overlapped);

    void ToggleZoom()
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore();
        else presenter.Maximize();
    }

    // ── layout, theme, preview ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Zen is a re-arrangement, not a second tree (§5.4): the chrome rows collapse and ContentHost
    /// grows the Mac's 1 : 4 : 1 by 4 : 92 : 4 grid around the single pane. Nothing is re-parented,
    /// so the live WebView2 keeps its process, its page and its scroll position across a toggle.
    /// </summary>
    void ApplyLayout()
    {
        var zen = _state.ZenActive;
        // Panes.Mode's own setter is a no-op when it does not move, but the grid tracks are not:
        // rebuilding them on every 250 ms derived tick would re-measure the whole window.
        Panes.Mode = zen ? (_state.ZenReading ? ViewMode.Preview : ViewMode.Edit) : _state.EffectiveMode;
        if (_zenApplied == zen) return;
        _zenApplied = zen;

        MenuSlot.Visibility = zen ? Visibility.Collapsed : Visibility.Visible;
        Footer.Visibility = zen ? Visibility.Collapsed : Visibility.Visible;
        ZenCapsule.Visibility = zen ? Visibility.Visible : Visibility.Collapsed;
        if (zen) Find.Visibility = Visibility.Collapsed;

        ContentHost.ColumnDefinitions.Clear();
        ContentHost.RowDefinitions.Clear();
        if (zen)
        {
            ContentHost.ColumnDefinitions.Add(Star(1));
            ContentHost.ColumnDefinitions.Add(Star(4));
            ContentHost.ColumnDefinitions.Add(Star(1));
            ContentHost.RowDefinitions.Add(StarRow(4));
            ContentHost.RowDefinitions.Add(StarRow(92));
            ContentHost.RowDefinitions.Add(StarRow(4));
        }
        Grid.SetColumn(Panes, zen ? 1 : 0);
        Grid.SetRow(Panes, zen ? 1 : 0);
    }

    void PerformJumps()
    {
        if (_state.EditorJump is { } jump) Panes.Editor.ApplyJump(jump, _state.EditorJumpHandled);
        // WP4's state carries its own placeholder record; WP5's coordinator takes its own. Two
        // fields, one meaning — the conversion is this line, and the day WP4's placeholders are
        // deleted it becomes a straight pass-through.
        if (_state.PreviewNavigation is { } navigation)
            _preview.Navigate(new PreviewNav(navigation.Id, navigation.Slug), _state.PreviewNavigationHandled);
    }

    void ApplyTheme()
    {
        var dark = IsDark;
        Root.Background = PaneBrushes.Get("PaperBackgroundBrush", Palette.For(dark).Paper);
        TitleBarTint.Apply(AppWindow, dark);
        // Background first, then the new page (WP5's note): the paper colour is painted before the
        // reload so a theme switch never flashes white.
        _previewHost.ApplyTheme(dark);
        UpdatePreview();
    }

    // A document window passes no token: its document never changes under it without the text
    // changing too, so the coordinator's scroll-preserving reload is always the right one.
    void UpdatePreview() => _preview.Update(_session.Text, _session.Title, IsDark, token: null);

    IReadOnlyList<Md.App.Logic.View.DiagramRef> DiagramsOf(string text) =>
        [.. DiagramSvg.Diagrams(text).Select(d => new Md.App.Logic.View.DiagramRef(d.Ordinal, d.Engine, d.Source, d.MenuTitle))];

    // ── the conflict and save-error bars (§6.3) ───────────────────────────────────────────────

    void UpdateInfoBar()
    {
        // Every keystroke reaches here; two Buttons per keystroke while a conflict is showing would
        // also throw away the focus the reader may have put on one of them.
        var state = (_session.Conflicted, _session.SaveErrorText);
        if (_infoState == state) return;
        _infoState = state;

        if (_session.Conflicted)
        {
            Info.Severity = InfoBarSeverity.Warning;
            Info.Title = Strings.Documents.FileChangedOnDisk;
            Info.Message = "";
            Info.ActionButton = null;
            // InfoBar has ONE ActionButton, and this bar needs two answers — so both live in Content.
            Info.Content = Buttons(
                (Strings.Buttons.ReloadFromDisk, () => _session.ResolveConflictReloading()),
                (Strings.Buttons.KeepMyVersion, () => _session.ResolveConflictKeepingMine()));
            Info.IsOpen = true;
            return;
        }

        if (_session.SaveErrorText is { } error)
        {
            Info.Severity = InfoBarSeverity.Error;
            Info.Title = Strings.Documents.CouldNotSave(error);
            Info.Message = "";
            Info.ActionButton = Button(Strings.Buttons.Retry, () => _session.FlushNow(explicitSave: true));
            Info.Content = Buttons((Strings.Buttons.SaveAs, () => _ = SaveAsAsync()));
            Info.IsOpen = true;
            return;
        }

        // A routine autosave shows nothing at all.
        Info.IsOpen = false;
        Info.ActionButton = null;
        Info.Content = null;
    }

    static Microsoft.UI.Xaml.Controls.Button Button(string label, Action action)
    {
        var button = new Microsoft.UI.Xaml.Controls.Button { Content = label };
        button.Click += (_, _) => action();
        return button;
    }

    static StackPanel Buttons(params (string Label, Action Action)[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        foreach (var (label, action) in buttons) panel.Children.Add(Button(label, action));
        return panel;
    }

    // ── the snapshot (§2.9) ───────────────────────────────────────────────────────────────────

    void Publish()
    {
        // SetTitle below re-enters through the registry's Changed; one pass is enough.
        if (_publishing) return;
        _publishing = true;
        try
        {
            _snapshot = Build();
            _menu.Refresh(_snapshot);
        }
        finally
        {
            _publishing = false;
        }
    }

    ShellSnapshot Build()
    {
        var derived = _state.Derived;
        var windows = new List<(Guid Id, string Title, bool IsThis)>();
        foreach (var (id, title) in _services.Registry.Windows) windows.Add((id, title, id == Id));

        return new ShellSnapshot(
            HasDocument: true,
            IsBookWindow: false,
            IsEditingArticle: false,
            IsSaved: _session.HasFileIdentity,
            IsDirty: _session.IsDirty,
            HasBook: _services.Settings.GetString(SettingsKeys.BookBookmark) is { Length: > 0 },
            DisplayedMode: _state.EffectiveMode,
            ZenActive: _state.ZenActive,
            ZenReading: _state.ZenReading,
            Outline: derived.Outline,
            Notes: derived.Notes,
            Diagrams: _diagrams,
            CanPrevious: false,
            CanNext: false,
            EditorVisible: SplitLayout.ShowsEditor(Panes.Layout),
            CanUndo: Editor.CanUndo,
            CanRedo: Editor.CanRedo,
            RecentEntries: _recent,
            WindowTitles: windows,
            HasSelection: Editor.SelectionLength > 0,
            HasFindQuery: Find.Query.Length > 0,
            PdfPageSizeId: PageSize.Named(_services.Settings.GetString(SettingsKeys.PdfPageSize)).Id,
            SidebarOpen: false,
            IsFullScreen: AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen,
            FindBarOpen: Find.Visibility == Visibility.Visible);
    }

    // The MRU is a WinRT call per row; it changes on an open, a save and an activation, never on a
    // keystroke, so it is cached rather than read while the snapshot is built.
    void RefreshRecent()
    {
        var rows = new List<RecentEntry>();
        foreach (var entry in _services.Recent.Entries)
        {
            var path = entry.Path;
            rows.Add(new RecentEntry(entry.Token, FileNames.NameOf(path), FileNames.DirectoryOf(path)));
        }
        _recent = rows;
    }

    // ── small helpers ─────────────────────────────────────────────────────────────────────────

    void RememberFrame()
    {
        if (AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;
        var scale = Scale;
        var position = AppWindow.Position;
        var size = AppWindow.ClientSize;
        _restoreFrame = new WindowRect(Epx(position.X, scale), Epx(position.Y, scale), Epx(size.Width, scale), Epx(size.Height, scale));
    }

    /// <summary>The window stopped being untitled (it opened or saved a file), or it closed: the number is free (§6.7).</summary>
    void ReleaseUntitledNumber()
    {
        if (_untitledOrdinal == 0) return;
        _manager.ReleaseUntitled(_untitledOrdinal);
        _untitledOrdinal = 0;
    }

    void TryDelete(string path)
    {
        try
        {
            SystemIoFileSystem.Instance.Delete(path);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            // The empty file stays; nothing else is lost.
        }
    }

    static bool IsFileFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Runtime.InteropServices.COMException;

    static ColumnDefinition Star(double weight) => new() { Width = new GridLength(weight, GridUnitType.Star) };

    static RowDefinition StarRow(double weight) => new() { Height = new GridLength(weight, GridUnitType.Star) };

    static int Px(int epx, double scale) => (int)Math.Round(epx * scale, MidpointRounding.AwayFromZero);

    static int Epx(int px, double scale) => (int)Math.Round(px / scale, MidpointRounding.AwayFromZero);

    static WindowRect ToEpx(RectInt32 rect, double scale) =>
        new(Epx(rect.X, scale), Epx(rect.Y, scale), Epx(rect.Width, scale), Epx(rect.Height, scale));
}
