using Md.App.Logic.View;

namespace Md.App.Logic.Tests;

/// <summary>shell-design.md §3.4 — the pane link and the echo guard behind it.</summary>
public sealed class ScrollSyncTests
{
    [Fact]
    public void EachSideDrivesTheOther()
    {
        var sync = new ScrollSync();
        double? editor = null, preview = null;
        sync.ScrollEditor = f => editor = f;
        sync.ScrollPreview = f => preview = f;

        sync.EditorDidScroll(0.25);
        sync.PreviewDidScroll(0.75);

        Assert.Equal(0.25, preview);
        Assert.Equal(0.75, editor);
    }

    [Fact]
    public void AnUnregisteredSideIsSimplyNotDriven()
    {
        var sync = new ScrollSync();

        sync.EditorDidScroll(0.5);
        sync.PreviewDidScroll(0.5);
    }

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(0, 0)]
    [InlineData(1.5, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 1)]
    public void TheFractionIsClampedAndNeverNaN(double input, double expected)
    {
        var sync = new ScrollSync();
        double? seen = null;
        sync.ScrollPreview = f => seen = f;

        sync.EditorDidScroll(input);

        Assert.Equal(expected, seen);
    }
}

/// <summary>The flag, and the 50 ms timestamp window that is the contingency for an asynchronous <c>ViewChanged</c>.</summary>
public sealed class ScrollSyncGuardTests
{
    [Fact]
    public void TheFlagIsSetOnlyWhileTheProgrammaticScrollIsApplied()
    {
        var clock = new FakeClock();
        var guard = new ScrollSyncGuard(clock);

        Assert.False(guard.IsEcho);
        using (guard.Applying())
        {
            Assert.True(guard.IsApplying);
            Assert.True(guard.IsEcho);
        }
        Assert.False(guard.IsApplying);
        Assert.False(guard.IsEcho);
    }

    [Fact]
    public void TheWindowKeepsSwallowingEchoesAfterTheApplyReturns()
    {
        var clock = new FakeClock();
        var guard = new ScrollSyncGuard(clock, ScrollSyncGuard.DefaultWindow);
        using (guard.Applying()) { }

        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.True(guard.IsEcho);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(guard.IsEcho);
    }

    [Fact]
    public void TheWindowIsMeasuredFromTheEndOfTheApplyAndNotItsStart()
    {
        // ChangeView is not free, and the echo it provokes arrives after it returns — so the 50 ms
        // has to start when the apply finishes. Measured from the start it would already be half
        // spent by the time there is anything to swallow.
        var clock = new FakeClock();
        var guard = new ScrollSyncGuard(clock, ScrollSyncGuard.DefaultWindow);

        using (guard.Applying()) clock.Advance(TimeSpan.FromMilliseconds(40));

        clock.Advance(TimeSpan.FromMilliseconds(49));
        Assert.True(guard.IsEcho);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(guard.IsEcho);
    }

    [Fact]
    public void TheShippingDefaultIsThePlainFlagWithNoWindowAtAll()
    {
        // §3.4: the app ships with window = Zero (the Mac's synchronous ViewChanged) until a
        // Windows run proves otherwise; DefaultWindow is the contingency, not the default.
        var clock = new FakeClock();
        var guard = new ScrollSyncGuard(clock);

        using (guard.Applying()) { }

        Assert.False(guard.IsEcho);
        Assert.Equal(TimeSpan.FromMilliseconds(50), ScrollSyncGuard.DefaultWindow);
    }

    [Fact]
    public void NestedAppliesDoNotClearTheFlagEarly()
    {
        var guard = new ScrollSyncGuard(new FakeClock());

        var outer = guard.Applying();
        var inner = guard.Applying();
        inner.Dispose();
        Assert.True(guard.IsApplying);
        outer.Dispose();
        Assert.False(guard.IsApplying);
    }

    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var guard = new ScrollSyncGuard(new FakeClock());

        var scope = guard.Applying();
        scope.Dispose();
        scope.Dispose();

        Assert.False(guard.IsApplying);
    }
}
