using Md.App.Logic.Export;
using Md.App.Logic.Tests.Fakes;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// §7.1: "exports on one window are serialised; a <c>ProgressRing</c> appears in the footer after
/// 500 ms". Every rule the WinUI half must not decide for itself is here — the delay, the two ways
/// an export can end before it, and the thread the notification arrives on.
///
/// The ring records what it was told rather than drawing anything: <see cref="Shown"/> is the exact
/// sequence of <c>show</c> calls, so "nothing happened" and "shown then hidden" are different
/// assertions and a spurious hide cannot hide inside a final <c>false</c>.
/// </summary>
public class BusyRingTests
{
    readonly FakeScheduler _scheduler = new();
    readonly FakeUiThread _ui = new();
    readonly List<bool> _shown = [];

    BusyRing Ring() => new(_scheduler, _ui, _shown.Add);

    static readonly TimeSpan Delay = ExportPipeline.BusyRingDelay;
    static readonly TimeSpan JustUnder = Delay - TimeSpan.FromMilliseconds(1);

    [Fact]
    public void TheDelayIsTheOneTheDesignPutsBesideTheEvent()
    {
        // The number lives in ExportPipeline, next to BusyChanged; the ring reads it rather than
        // keeping a second copy — which is how a design's one number becomes two.
        Assert.Equal(TimeSpan.FromMilliseconds(500), Delay);
    }

    [Fact]
    public void AFastExportNeverFlickersARing()
    {
        var ring = Ring();
        ring.BusyChanged(true);
        _scheduler.Advance(JustUnder);
        ring.BusyChanged(false);
        // Well past the delay: the armed timer must have been dropped, not merely outrun.
        _scheduler.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_shown);
        Assert.False(ring.IsShown);
        Assert.Equal(0, _scheduler.PendingTimers);
    }

    [Fact]
    public void ASlowExportShowsTheRingOnTheDelayAndHidesItWhenItEnds()
    {
        var ring = Ring();
        ring.BusyChanged(true);

        _scheduler.Advance(JustUnder);
        Assert.Empty(_shown);                       // 499 ms is still "not yet"

        _scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([true], _shown);
        Assert.True(ring.IsShown);

        ring.BusyChanged(false);
        Assert.Equal([true, false], _shown);
        Assert.False(ring.IsShown);
    }

    [Fact]
    public void ARingThatWasNeverShownIsNeverHidden()
    {
        // The case a naive `show(busy)` gets wrong: a fast export would end with show(false) on a
        // control that was already collapsed — harmless to look at, and a lie to anything reading
        // the sequence (an automation peer, a test, the next reader of this file).
        var ring = Ring();
        ring.BusyChanged(true);
        ring.BusyChanged(false);
        _scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(_shown);
    }

    [Fact]
    public void EachExportGetsItsOwnFiveHundredMilliseconds()
    {
        var ring = Ring();

        // One slow export: shown, then hidden.
        ring.BusyChanged(true);
        _scheduler.Advance(Delay);
        ring.BusyChanged(false);
        Assert.Equal([true, false], _shown);

        // The next one starts the clock again from zero rather than showing at once.
        ring.BusyChanged(true);
        _scheduler.Advance(JustUnder);
        Assert.Equal([true, false], _shown);
        _scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([true, false, true], _shown);
    }

    [Fact]
    public void ASecondBusyDoesNotRestartTheClock()
    {
        // ExportPipeline serialises, so true/false alternate — but a ring that restarted its timer on
        // a repeated true would push the ring past the moment it is worth showing, and that is the
        // kind of thing a second pipeline or a replayed event would silently cause.
        var ring = Ring();
        ring.BusyChanged(true);
        _scheduler.Advance(TimeSpan.FromMilliseconds(300));
        ring.BusyChanged(true);
        _scheduler.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Equal([true], _shown);
        // Both notifications were marshalled; the second simply had nothing to do.
        Assert.Equal(2, _ui.PostCount);
        Assert.Equal(0, _scheduler.PendingTimers);
    }

    [Fact]
    public void AnIdleFalseDoesNothingAtAll()
    {
        var ring = Ring();
        ring.BusyChanged(false);
        _scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(_shown);
        Assert.Equal(0, _scheduler.PendingTimers);
    }

    /// <summary>
    /// The reason this class exists at all: <c>ExportPipeline.Serialised</c> raises
    /// <c>BusyChanged</c> after <c>await _gate.WaitAsync().ConfigureAwait(false)</c>, so the call can
    /// arrive on a thread-pool thread. Every notification goes through <see cref="Seams.IUiThread"/>,
    /// and nothing reaches the <c>show</c> callback until that post runs.
    /// </summary>
    [Fact]
    public void EveryNotificationIsMarshalledThroughTheUiThread()
    {
        _ui.RunInline = false;
        var ring = Ring();

        ring.BusyChanged(true);
        Assert.Equal(1, _ui.PostCount);
        // Not even the timer is armed yet: the post is what arms it, on the UI thread.
        Assert.Equal(0, _scheduler.PendingTimers);
        _scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(_shown);

        _ui.RunPosted();
        Assert.Equal(1, _scheduler.PendingTimers);
        _scheduler.Advance(Delay);
        Assert.Equal([true], _shown);

        ring.BusyChanged(false);
        Assert.Equal([true], _shown);               // still queued
        _ui.RunPosted();
        Assert.Equal([true, false], _shown);
    }

    [Fact]
    public void PostsAreAppliedInTheOrderThePipelineRaisedThem()
    {
        // One export ending on the pool while the next begins on the UI thread: posting BOTH (rather
        // than applying inline when IsCurrent) is what keeps false-then-true from arriving reversed
        // and leaving a ring on screen with nothing running.
        _ui.RunInline = false;
        var ring = Ring();

        ring.BusyChanged(true);
        _ui.RunPosted();
        _scheduler.Advance(Delay);
        Assert.Equal([true], _shown);

        ring.BusyChanged(false);
        ring.BusyChanged(true);
        _ui.RunPosted();
        Assert.Equal([true, false], _shown);        // hidden, and the next 500 ms is running
        _scheduler.Advance(Delay);
        Assert.Equal([true, false, true], _shown);
    }

    [Fact]
    public void CancelDropsAnArmedRingWithoutTouchingTheControl()
    {
        // §1.4 route 3: the window is going. The footer goes with it, so the ring must neither fire
        // nor be told to hide — a hide applied to a dead XamlRoot is the crash this avoids.
        var ring = Ring();
        ring.BusyChanged(true);
        _scheduler.Advance(JustUnder);

        ring.Cancel();
        _scheduler.Advance(TimeSpan.FromSeconds(5));

        Assert.Empty(_shown);
        Assert.Equal(0, _scheduler.PendingTimers);
    }

    [Fact]
    public void CancelWhileTheRingIsShowingDoesNotHideIt()
    {
        var ring = Ring();
        ring.BusyChanged(true);
        _scheduler.Advance(Delay);
        Assert.Equal([true], _shown);

        ring.Cancel();
        Assert.Equal([true], _shown);
        Assert.False(ring.IsShown);
    }

    [Fact]
    public void ACancelledRingCanStartAgain()
    {
        // Not a case the app reaches — Cancel is the close — but a latch that could never re-arm
        // would be a trap for the next caller.
        var ring = Ring();
        ring.BusyChanged(true);
        ring.Cancel();

        ring.BusyChanged(true);
        _scheduler.Advance(Delay);
        Assert.Equal([true], _shown);
    }

    [Fact]
    public void ANegativeDelayShowsTheRingOnTheNextTick()
    {
        var ring = new BusyRing(_scheduler, _ui, _shown.Add, TimeSpan.FromMilliseconds(-5));
        ring.BusyChanged(true);
        Assert.Empty(_shown);                       // the timer still has to fire
        _scheduler.RunDue();
        Assert.Equal([true], _shown);
    }

    [Fact]
    public void TheThreeDependenciesAreRequired()
    {
        Assert.Throws<ArgumentNullException>(() => new BusyRing(null!, _ui, _shown.Add));
        Assert.Throws<ArgumentNullException>(() => new BusyRing(_scheduler, null!, _shown.Add));
        Assert.Throws<ArgumentNullException>(() => new BusyRing(_scheduler, _ui, null!));
    }
}
