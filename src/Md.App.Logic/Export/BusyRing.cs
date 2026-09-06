using Md.App.Logic.Seams;

namespace Md.App.Logic.Export;

/// <summary>
/// §7.1's footer progress ring, as policy: "exports on one window are serialised; a
/// <c>ProgressRing</c> appears in the footer after 500 ms". Two things stand between
/// <see cref="ExportPipeline.BusyChanged"/> and a control's <c>Visibility</c>, and neither belongs
/// in a window's code-behind, because neither can be tested there.
///
/// <para><b>The thread.</b> <c>BusyChanged</c> is raised from inside <c>ExportPipeline.Serialised</c>,
/// after <c>await _gate.WaitAsync().ConfigureAwait(false)</c> — so the false at the end, and often
/// the true at the start, arrive on a thread-pool thread. Touching a WinUI control from there is a
/// wrong-thread crash, so every notification is posted through <see cref="IUiThread"/>. Posting
/// <em>always</em>, rather than only when <c>IsCurrent</c> is false, is deliberate: it makes the
/// order the ring sees the order the pipeline raised, even when one export finishes on the pool
/// while the next begins on the UI thread.</para>
///
/// <para><b>The delay.</b> Showing the ring the instant an export starts would flicker one for every
/// LaTeX, TextBundle and Share ▸ Source…, all of which finish in a few milliseconds. So the ring is
/// armed on a timer and shown only if the export is <em>still</em> running when it fires; an export
/// that finishes first shows nothing at all, and — the case worth naming — is not followed by a
/// stray hide of a ring that was never shown.</para>
/// </summary>
public sealed class BusyRing
{
    readonly IScheduler _scheduler;
    readonly IUiThread _ui;
    readonly Action<bool> _show;
    readonly TimeSpan _delay;

    IDisposable? _armed;
    bool _busy;
    bool _shown;

    /// <summary>The ring §7.1 describes: <see cref="ExportPipeline.BusyRingDelay"/> after the export starts.</summary>
    /// <param name="show">Applied on the UI thread: true shows the ring, false hides it. Called only on a change.</param>
    public BusyRing(IScheduler scheduler, IUiThread ui, Action<bool> show)
        : this(scheduler, ui, show, ExportPipeline.BusyRingDelay) { }

    /// <param name="delay">For the tests that pin the boundary; the app always uses <see cref="ExportPipeline.BusyRingDelay"/>.</param>
    public BusyRing(IScheduler scheduler, IUiThread ui, Action<bool> show, TimeSpan delay)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(show);
        _scheduler = scheduler;
        _ui = ui;
        _show = show;
        _delay = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    /// <summary>Whether the ring is on screen right now — what the last <c>show</c> call said.</summary>
    public bool IsShown => _shown;

    /// <summary>
    /// Subscribe this straight to <see cref="ExportPipeline.BusyChanged"/>. Safe from any thread:
    /// the work is posted to the UI thread and nothing is read here.
    /// </summary>
    public void BusyChanged(bool busy) => _ui.Post(() => Apply(busy));

    /// <summary>
    /// The window is closing (§1.4 route 3): drop the armed timer and forget the ring, without
    /// touching the control — the footer is going with the window, and a hide applied to a dead
    /// XamlRoot is the crash this exists to avoid.
    /// </summary>
    public void Cancel()
    {
        _armed?.Dispose();
        _armed = null;
        _busy = false;
        _shown = false;
    }

    void Apply(bool busy)
    {
        // Idempotent: a second true does NOT restart the clock, or a pipeline that announced twice
        // would push the ring past the point where it is worth showing.
        if (busy == _busy) return;
        _busy = busy;

        if (busy)
        {
            _armed = _scheduler.After(_delay, Elapsed);
            return;
        }

        _armed?.Dispose();
        _armed = null;
        // Only when it was actually shown: a fast export must not blink a ring off that was never on.
        if (!_shown) return;
        _shown = false;
        _show(false);
    }

    void Elapsed()
    {
        _armed = null;
        if (!_busy || _shown) return;
        _shown = true;
        _show(true);
    }
}
