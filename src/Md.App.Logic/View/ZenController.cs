using Md.App.Logic.Seams;

namespace Md.App.Logic.View;

/// <summary>
/// Zen mode (shell-design.md §5.4; macOS §7): the window goes full screen, the whole chrome
/// collapses to one centred column, and a floating capsule fades out after 2.5 s. Nothing here
/// touches <c>md.viewMode</c> or <c>md.viewModeMemory</c> — Zen's write/read switch is view state
/// and lives only in <see cref="DocumentWindowState"/> (and, for a saved document, session.json).
/// </summary>
/// <remarks>
/// The presenter is the App's: <see cref="FullScreenRequested"/> is <c>AppWindow.SetPresenter</c>
/// and <see cref="PresenterChanged"/> is <c>AppWindow.Changed</c> with <c>DidPresenterChange</c>.
/// The <c>toggling</c> flag is why the pair does not loop: our own <c>SetPresenter</c> raises
/// <c>Changed</c> (synchronously or a turn later — the flag survives both), and only a presenter
/// change we did not ask for drops Zen. Leaving full screen by any means — F11, Esc, Win+Down, the
/// caption button — therefore leaves Zen too, which is the Mac's behaviour.
/// </remarks>
public sealed class ZenController
{
    /// <summary>The Mac's 2.5 s: every reveal restarts it, and the capsule fades when it fires.</summary>
    public static readonly TimeSpan ChromeHideDelay = TimeSpan.FromMilliseconds(2500);

    readonly DocumentWindowState _state;
    readonly IScheduler? _scheduler;
    IDisposable? _hide;
    bool _toggling;
    bool _controlsShown = true;

    /// <param name="scheduler">
    /// The UI-thread timer behind the fade. Null (a window built without one, and every test that
    /// does not care) keeps the capsule permanently shown — no timer, no fade.
    /// </param>
    public ZenController(DocumentWindowState state, IScheduler? scheduler = null)
    {
        _state = state;
        _scheduler = scheduler;
    }

    /// <summary>True = <c>AppWindowPresenterKind.FullScreen</c>, false = <c>Overlapped</c>.</summary>
    public event Action<bool>? FullScreenRequested;

    /// <summary>The capsule's opacity target: shown = opacity 1 and hit-testable, hidden = 0 and not.</summary>
    public event Action<bool>? ControlsShownChanged;

    public bool IsActive => _state.ZenActive;

    /// <summary>False = writing (the editor is the centre pane), true = reading (the preview is).</summary>
    public bool IsReading => _state.ZenReading;

    public bool ControlsShown => _controlsShown;

    /// <summary>True only for the instant our own presenter change is in flight (§5.4).</summary>
    public bool IsToggling => _toggling;

    /// <summary>View ▸ Zen Mode (Ctrl+Shift+Enter), and the capsule's exit button.</summary>
    public void Toggle() => SetActive(!_state.ZenActive);

    public void SetActive(bool active)
    {
        if (_state.ZenActive == active) return;
        _state.ZenActive = active;
        _toggling = true;
        try
        {
            FullScreenRequested?.Invoke(active);
        }
        finally
        {
            _toggling = false;
        }
        if (active) Reveal();
        else CancelHide();
    }

    /// <summary>
    /// The presenter actually changed. Our own change is ignored; anything else that leaves full
    /// screen drops Zen — and drops it without asking for a presenter change back, because the
    /// window is already where it needs to be.
    /// </summary>
    public void PresenterChanged(bool isFullScreen)
    {
        if (_toggling || isFullScreen || !_state.ZenActive) return;
        _state.ZenActive = false;
        CancelHide();
    }

    /// <summary>
    /// The capsule's write / read switches, and Ctrl+1 / Ctrl+2 / Ctrl+3 through
    /// <see cref="ViewModeController.Select"/> while Zen is on. Stores nothing.
    /// </summary>
    public void SetReading(bool reading) => _state.ZenReading = reading;

    /// <summary>
    /// Pointer movement over the Zen column, and entering Zen. Shows the capsule and restarts the
    /// hide timer; a no-op outside Zen, where there is no capsule to reveal.
    /// </summary>
    public void Reveal()
    {
        if (!_state.ZenActive) return;
        ShowControls(true);
        _hide?.Dispose();
        _hide = _scheduler?.After(ChromeHideDelay, () => ShowControls(false));
    }

    void CancelHide()
    {
        _hide?.Dispose();
        _hide = null;
        // Leaving Zen leaves the capsule "shown", so re-entering does not start behind a fade-in.
        ShowControls(true);
    }

    void ShowControls(bool shown)
    {
        if (_controlsShown == shown) return;
        _controlsShown = shown;
        ControlsShownChanged?.Invoke(shown);
    }
}
