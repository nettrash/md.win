// The live windows (shell-design.md §1.2, §1.3, §1.6). Everything decidable without Windows is in
// Md.App.Logic — ActivationRouter turns an activation into actions, WindowRegistry says who owns
// what, WindowPlacement does the cascade arithmetic, SessionStore is the file — so what is left
// here is creating windows, activating them and writing the session.
using Md.App.Logic.Activation;
using Md.App.Logic.Documents;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Logic.Text;
using Md.App.Logic.View;
using Md.App.Logic.Windows;
using Md.App.Services;
using Md.Core.Document;

namespace Md.App;

internal sealed class WindowManager
{
    readonly AppServices _services;
    readonly List<DocumentWindow> _windows = [];
    readonly UntitledNames _untitled = new();
    DocumentWindow? _lastActive;
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
        // ever behind nothing, and the user asked for md.
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

    // ── the Book window (§8.1) — WP7's seam ───────────────────────────────────────────────────

    /// <summary>
    /// Show the one app-wide Book window (§1.3): create it on demand, else activate it. WP7 owns
    /// <c>Windows/BookWindow.xaml(.cs)</c> and replaces this body; until that type exists there is
    /// nothing to create, and the Book commands are routed to the document window's "not wired yet"
    /// path — so this is a named seam, not a stub that pretends a window appeared.
    /// </summary>
    public void ShowBookWindow() =>
        throw new NotImplementedException("WP7 owns BookWindow; WindowManager.ShowBookWindow is its integration point (§8.1).");

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
        return last ?? OpenUntitled();
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
        // The Book window's half is WP7's; until it exists the key is simply absent, which decodes
        // as "no book window was open".
        SessionStore.Save(SystemIoFileSystem.Instance, _services.LocalFolder, new SessionState(rows, null));
    }

    // ── closing (§1.4) ────────────────────────────────────────────────────────────────────────

    /// <summary>The window has closed: forget it, and record what is left (§1.6).</summary>
    public void Forget(DocumentWindow window)
    {
        _windows.Remove(window);
        if (ReferenceEquals(_lastActive, window)) _lastActive = _windows.Count > 0 ? _windows[^1] : null;
        SaveSession();
    }

    /// <summary>The window came forward; the next new window cascades from it (§1.3).</summary>
    public void NoteActivated(DocumentWindow window) => _lastActive = window;

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

        // Only now: a Cancel above must leave the recorded session alone, and the closes below must
        // not rewrite it window by window down to nothing.
        _exiting = true;
        foreach (var window in open) window.CloseApproved();
        return true;
    }

    void TerminateFlush()
    {
        foreach (var window in _windows.ToList()) window.Session.TerminateFlush();
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
