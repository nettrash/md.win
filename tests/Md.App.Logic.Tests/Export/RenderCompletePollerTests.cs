using Md.App.Logic.Export;
using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The handshake every export waits on (§4.8). Two things are load-bearing and both are easy to get
/// wrong: the flag is compared against the <em>quoted</em> JSON string, and running out of attempts
/// is a success.
/// </summary>
public class RenderCompletePollerTests
{
    static (RenderCompletePoller Poller, FakeScheduler Scheduler, FakeRenderSurface Surface) Make(Func<string, string> answer)
    {
        var scheduler = new FakeScheduler();
        var surface = new FakeRenderSurface { EvalHandler = answer };
        return (new RenderCompletePoller(scheduler), scheduler, surface);
    }

    [Fact]
    public void ThePageThatIsAlreadyDoneIsNotWaitedFor()
    {
        using var deterministic = new NoSyncContext();
        var (poller, scheduler, surface) = Make(_ => Scripts.RenderCompleteResult);

        var task = poller.WaitAsync(surface);

        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal([Scripts.RenderComplete], surface.Evals);
        Assert.Equal(0, scheduler.PendingTimers);
    }

    [Fact]
    public void ItAsksEveryQuarterSecondAndStopsAsSoonAsTheFlagIsUp()
    {
        using var deterministic = new NoSyncContext();
        var asked = 0;
        var (poller, scheduler, surface) = Make(_ => ++asked >= 3 ? Scripts.RenderCompleteResult : "null");

        var task = poller.WaitAsync(surface);

        Assert.False(task.IsCompleted);
        Assert.Equal([RenderCompletePoller.Interval], scheduler.PendingDelays);
        scheduler.Advance(RenderCompletePoller.Interval);
        Assert.False(task.IsCompleted);
        scheduler.Advance(RenderCompletePoller.Interval);
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Equal(3, surface.Evals.Count);
    }

    [Fact]
    public void RunningOutOfAttemptsCountsAsSuccessBecauseTheDomIsStillWorthExporting()
    {
        using var deterministic = new NoSyncContext();
        var (poller, scheduler, surface) = Make(_ => "null");

        var task = poller.WaitAsync(surface);
        for (var i = 0; i < RenderCompletePoller.MaxAttempts; i++)
        {
            Assert.False(task.IsCompleted);
            scheduler.Advance(RenderCompletePoller.Interval);
        }

        Assert.True(task.IsCompletedSuccessfully);       // never faulted
        // The Mac's loop: one read, then 480 more after 480 waits.
        Assert.Equal(RenderCompletePoller.MaxAttempts + 1, surface.Evals.Count);
        Assert.Equal(0, scheduler.PendingTimers);
    }

    [Theory]
    [InlineData("1")]            // the number, not the attribute
    [InlineData("null")]         // a dead page or a failed script
    [InlineData("\"\"")]
    [InlineData("")]
    [InlineData("\"0\"")]
    public void NothingButTheQuotedOneCountsAsTheFlag(string answer)
    {
        using var deterministic = new NoSyncContext();
        var (poller, scheduler, surface) = Make(_ => answer);

        var task = poller.WaitAsync(surface);

        Assert.False(task.IsCompleted);
        Assert.Equal(1, scheduler.PendingTimers);
        _ = task;                                        // left waiting; the export would poll on
    }

    [Fact]
    public void ACancelledWaitStopsTheTimerRatherThanFiringItLater()
    {
        using var deterministic = new NoSyncContext();
        var (poller, scheduler, surface) = Make(_ => "null");
        using var cancellation = new CancellationTokenSource();

        var task = poller.WaitAsync(surface, cancellation.Token);
        Assert.Equal(1, scheduler.PendingTimers);

        cancellation.Cancel();

        Assert.True(task.IsCanceled);
        scheduler.Advance(RenderCompletePoller.Interval);
        Assert.Single(surface.Evals);                    // it never asked again
    }

    [Fact]
    public void TheNumbersAreTheFamilysTwoMinutes()
    {
        using var deterministic = new NoSyncContext();
        Assert.Equal(480, RenderCompletePoller.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(250), RenderCompletePoller.Interval);
        Assert.Equal("\"1\"", Scripts.RenderCompleteResult);
    }
}
