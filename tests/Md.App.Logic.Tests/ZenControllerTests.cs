using Md.App.Logic.View;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-design.md §5.4 — the presenter/observer pair (our own toggle vs. the user leaving full
/// screen by any other means) and the 2.5 s capsule fade.
/// </summary>
public sealed class ZenControllerTests
{
    readonly DocumentWindowState _state = new();

    [Fact]
    public void TogglingAsksForFullScreenAndBack()
    {
        var requests = new List<bool>();
        var zen = new ZenController(_state);
        zen.FullScreenRequested += requests.Add;

        zen.Toggle();
        Assert.True(_state.ZenActive);
        zen.Toggle();
        Assert.False(_state.ZenActive);

        Assert.Equal([true, false], requests);
    }

    [Fact]
    public void OurOwnPresenterChangeDoesNotDropZen()
    {
        // AppWindow.Changed may arrive inside SetPresenter; the toggling flag is what makes that safe.
        var zen = new ZenController(_state);
        var togglingSeen = new List<bool>();
        zen.FullScreenRequested += full =>
        {
            togglingSeen.Add(zen.IsToggling);
            zen.PresenterChanged(full);
        };

        zen.Toggle();

        Assert.True(_state.ZenActive);
        Assert.Equal([true], togglingSeen);
        Assert.False(zen.IsToggling);
    }

    [Fact]
    public void APresenterReportOfNotFullScreenArrivingInsideOurOwnToggleIsIgnored()
    {
        // §5.4: "if Active && !toggling && !isFullScreen → Active = false". The !toggling clause is
        // what stops a presenter report we provoked from undoing the toggle that provoked it —
        // AppWindow.Changed can carry the OLD presenter, and SetPresenter can be refused outright.
        // Without the flag Zen would switch itself off in the same call that switched it on.
        var zen = new ZenController(_state);
        var togglingSeen = new List<bool>();
        zen.FullScreenRequested += _ =>
        {
            togglingSeen.Add(zen.IsToggling);
            zen.PresenterChanged(isFullScreen: false);
        };

        zen.Toggle();

        Assert.True(_state.ZenActive);
        Assert.Equal([true], togglingSeen);
        Assert.False(zen.IsToggling);
    }

    [Fact]
    public void TheSamePresenterReportOneTurnLATERStillDropsZen()
    {
        // The flag covers only our own call; a report that arrives after it returns is the user's.
        var zen = new ZenController(_state);
        zen.Toggle();
        Assert.False(zen.IsToggling);

        zen.PresenterChanged(isFullScreen: false);

        Assert.False(_state.ZenActive);
    }

    [Fact]
    public void LeavingFullScreenByAnyOtherMeansDropsZenAndAsksForNothing()
    {
        var requests = new List<bool>();
        var zen = new ZenController(_state);
        zen.Toggle();
        zen.FullScreenRequested += requests.Add;

        zen.PresenterChanged(isFullScreen: false);      // F11, Esc, Win+Down, the caption button

        Assert.False(_state.ZenActive);
        Assert.Empty(requests);                          // the window is already Overlapped
    }

    [Fact]
    public void FullScreenWithoutZenIsLeftAlone()
    {
        var zen = new ZenController(_state);

        zen.PresenterChanged(isFullScreen: true);
        Assert.False(_state.ZenActive);

        zen.PresenterChanged(isFullScreen: false);
        Assert.False(_state.ZenActive);
    }

    [Fact]
    public void ZenNeverTouchesTheStoredMode()
    {
        _state.StoredMode = ViewMode.Split;
        var zen = new ZenController(_state);

        zen.Toggle();
        zen.SetReading(true);
        zen.SetReading(false);
        zen.PresenterChanged(isFullScreen: false);

        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.False(_state.ZenReading);
    }

    // ── The fade ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCapsuleIsShownOnEnteringZenAndFadesAfterTwoAndAHalfSeconds()
    {
        var scheduler = new FakeScheduler();
        var shown = new List<bool>();
        var zen = new ZenController(_state, scheduler);
        zen.ControlsShownChanged += shown.Add;

        zen.Toggle();
        Assert.True(zen.ControlsShown);
        Assert.Equal(1, scheduler.PendingTimers);

        scheduler.Advance(TimeSpan.FromMilliseconds(2499));
        Assert.True(zen.ControlsShown);

        scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(zen.ControlsShown);
        Assert.Equal([false], shown);
    }

    [Fact]
    public void EveryRevealRestartsTheTimer()
    {
        var scheduler = new FakeScheduler();
        var zen = new ZenController(_state, scheduler);
        zen.Toggle();

        scheduler.Advance(TimeSpan.FromMilliseconds(2000));
        zen.Reveal();
        scheduler.Advance(TimeSpan.FromMilliseconds(2000));
        Assert.True(zen.ControlsShown);

        scheduler.Advance(TimeSpan.FromMilliseconds(500));
        Assert.False(zen.ControlsShown);

        zen.Reveal();
        Assert.True(zen.ControlsShown);
        Assert.Equal(1, scheduler.PendingTimers);
    }

    [Fact]
    public void LeavingZenCancelsTheHideAndLeavesTheCapsuleReadyForNextTime()
    {
        var scheduler = new FakeScheduler();
        var zen = new ZenController(_state, scheduler);
        zen.Toggle();
        scheduler.Advance(ZenController.ChromeHideDelay);
        Assert.False(zen.ControlsShown);

        zen.Toggle();

        Assert.True(zen.ControlsShown);
        Assert.Equal(0, scheduler.PendingTimers);
    }

    [Fact]
    public void RevealOutsideZenDoesNothing()
    {
        var scheduler = new FakeScheduler();
        var zen = new ZenController(_state, scheduler);

        zen.Reveal();

        Assert.Equal(0, scheduler.PendingTimers);
    }

    [Fact]
    public void WithoutASchedulerTheCapsuleSimplyStaysShown()
    {
        var zen = new ZenController(_state);

        zen.Toggle();
        zen.Reveal();

        Assert.True(zen.ControlsShown);
    }
}
