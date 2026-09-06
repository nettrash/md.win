using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// Every branch of §4.5. The coordinator is the Mac's, so the tests are written as the Mac's rules:
/// first load immediate, everything else coalesced into one reload that keeps the reader's place, a
/// new document is a fresh page at the top, and a collapsed pane costs nothing until it is shown.
/// </summary>
public class PreviewCoordinatorTests
{
    /// <summary>Stands in for Md.Core's HTML writer: enough shape for ScreenHtml to find a head.</summary>
    sealed class StubHtml : IDocumentHtml
    {
        public int Calls { get; private set; }

        public string Document(string source, string title, bool dark)
        {
            Calls++;
            return $"<html><head><title>{title}</title></head><body data-md-dark=\"{(dark ? 1 : 0)}\">{source}</body></html>";
        }
    }

    sealed class Fixture
    {
        public FakePreviewSurface Surface { get; } = new();
        public FakeScheduler Scheduler { get; } = new();
        public StubHtml Html { get; } = new();
        public PreviewCoordinator Coordinator { get; }

        public Fixture() => Coordinator = new PreviewCoordinator(Surface, Scheduler, Html);

        /// <summary>What the page will report for <c>window.scrollY</c> on the next reload.</summary>
        public double PageScrollY { get; set; }

        public Fixture WithLivePage()
        {
            Surface.EvalHandler = script =>
                script == Scripts.ScrollY ? PageScrollY.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
            return this;
        }

        /// <summary>A first Update, its load reported complete: the state every later branch starts from.</summary>
        public Fixture Loaded(string text = "a", string? token = null)
        {
            Coordinator.Update(text, "Doc", dark: false, token);
            Surface.CompleteNavigation();
            return this;
        }

        public void Settle() => Scheduler.Advance(PreviewCoordinator.DebounceDelay);
    }

    // ───────────────────────────── first load ─────────────────────────────

    [Fact]
    public void TheFirstUpdateLoadsImmediately()
    {
        var f = new Fixture();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);

        Assert.Equal(new[] { AssetOrigin.IndexUrl }, f.Surface.Navigations);
        Assert.Equal(0, f.Surface.ReloadCount);
        Assert.Equal(0, f.Scheduler.PendingTimers);
    }

    [Fact]
    public void TheServedHtmlIsCoresPageWithTheWindowsFontStyle()
    {
        var f = new Fixture();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);

        Assert.Contains("<title>Doc</title>", f.Surface.Html, StringComparison.Ordinal);
        Assert.Contains(ScreenHtml.FontStyle, f.Surface.Html, StringComparison.Ordinal);
        Assert.Equal(1, f.Html.Calls);
    }

    [Fact]
    public void AnUpdateThatWouldRenderTheSamePageDoesNothing()
    {
        var f = new Fixture().Loaded();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);

        Assert.Single(f.Surface.HtmlHistory);
        Assert.Equal(1, f.Html.Calls);
        Assert.Equal(0, f.Scheduler.PendingTimers);
    }

    [Fact]
    public void TitleAndThemeArePartOfTheKey()
    {
        var f = new Fixture().WithLivePage().Loaded();

        f.Coordinator.Update("a", "Other", dark: false, token: null);
        f.Settle();
        Assert.Equal(1, f.Surface.ReloadCount);

        f.Surface.CompleteNavigation();
        f.Coordinator.Update("a", "Other", dark: true, token: null);
        f.Settle();
        Assert.Equal(2, f.Surface.ReloadCount);
    }

    // ───────────────────────────── coalescing ─────────────────────────────

    [Fact]
    public void TypingCoalescesIntoOneReloadAfter350Ms()
    {
        var f = new Fixture().WithLivePage().Loaded();

        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Scheduler.Advance(TimeSpan.FromMilliseconds(200));
        f.Coordinator.Update("abc", "Doc", dark: false, token: null);
        f.Scheduler.Advance(TimeSpan.FromMilliseconds(200));

        // The second keystroke restarted the debounce, so 400 ms in nothing has reloaded yet.
        Assert.Equal(0, f.Surface.ReloadCount);

        f.Scheduler.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Equal(1, f.Surface.ReloadCount);
        Assert.Contains("abc", f.Surface.Html, StringComparison.Ordinal);
        Assert.Single(f.Surface.Navigations);      // still the one first load
    }

    [Fact]
    public void TheDebounceIsExactly350Ms()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);

        f.Scheduler.Advance(TimeSpan.FromMilliseconds(349));
        Assert.Equal(0, f.Surface.ReloadCount);

        f.Scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, f.Surface.ReloadCount);
    }

    [Fact]
    public void EveryChangeStillRegeneratesTheHtmlImmediately()
    {
        // The debounce delays the reload, never the HTML: an export or a print taken mid-typing
        // must see what the editor holds.
        var f = new Fixture().WithLivePage().Loaded();
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);

        Assert.Contains("ab", f.Surface.Html, StringComparison.Ordinal);
        Assert.Equal(0, f.Surface.ReloadCount);
    }

    // ───────────────────────────── token change ─────────────────────────────

    [Fact]
    public void ANewDocumentLoadsImmediatelyAndForgetsTheScrollPosition()
    {
        var f = new Fixture().WithLivePage().Loaded("a", token: "one.md");
        f.PageScrollY = 900;
        f.Coordinator.Update("b", "Doc", dark: false, token: "one.md");
        f.Settle();                                   // a reload that saved 900
        f.Surface.CompleteNavigation();
        Assert.Equal(900, f.Coordinator.SavedScrollY);

        f.Coordinator.Update("c", "Doc", dark: false, token: "two.md");

        Assert.Equal(0, f.Coordinator.SavedScrollY);
        Assert.Equal(2, f.Surface.Navigations.Count);
        Assert.Equal(1, f.Surface.ReloadCount);
        Assert.Equal(0, f.Scheduler.PendingTimers);
    }

    [Fact]
    public void TheSameArticleWithTheSameTextIsNotANewDocument()
    {
        var f = new Fixture().Loaded("a", token: "one.md");
        f.Coordinator.Update("a", "Doc", dark: false, token: "one.md");

        Assert.Single(f.Surface.Navigations);
    }

    [Fact]
    public void ANewDocumentReloadsEvenWhenTheTextIsIdentical()
    {
        var f = new Fixture().Loaded("same", token: "one.md");
        f.Coordinator.Update("same", "Doc", dark: false, token: "two.md");

        Assert.Equal(2, f.Surface.Navigations.Count);
    }

    // ───────────────────────────── scroll restore ─────────────────────────────

    [Fact]
    public void AReloadSavesTheScrollPositionAndRestoresItWhenTheLoadFinishes()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.PageScrollY = 1234.5;

        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Settle();

        Assert.Contains(Scripts.ScrollY, f.Surface.Evals);
        Assert.Equal(1234.5, f.Coordinator.SavedScrollY);

        f.Surface.CompleteNavigation();
        Assert.Contains("window.__mdScrollTo?.(1234.5)", f.Surface.Evals);
    }

    [Fact]
    public void NothingIsRestoredFromTheTopOfThePage()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.PageScrollY = 0;

        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Settle();
        f.Surface.CompleteNavigation();

        Assert.DoesNotContain(f.Surface.Evals, e => e.StartsWith("window.__mdScrollTo", StringComparison.Ordinal));
    }

    [Fact]
    public void APageThatCannotAnswerReloadsFromTheTop()
    {
        var f = new Fixture().Loaded();                 // no EvalHandler: every script answers JSON null
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Settle();

        Assert.Equal(0, f.Coordinator.SavedScrollY);
        Assert.Equal(1, f.Surface.ReloadCount);
    }

    [Fact]
    public void AFailedLoadRestoresNothing()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.PageScrollY = 500;
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Settle();

        f.Surface.CompleteNavigation(isSuccess: false);

        Assert.DoesNotContain(f.Surface.Evals, e => e.StartsWith("window.__mdScrollTo", StringComparison.Ordinal));
    }

    // ───────────────────────────── stale while collapsed ─────────────────────────────

    [Fact]
    public void ACollapsedPaneRecordsTheChangeAndLoadsNothing()
    {
        var f = new Fixture();
        f.Surface.IsShown = false;

        f.Coordinator.Update("a", "Doc", dark: false, token: null);

        Assert.Empty(f.Surface.Navigations);
        Assert.Equal(0, f.Surface.ReloadCount);
        Assert.Equal(0, f.Scheduler.PendingTimers);
        Assert.True(f.Coordinator.IsStale);
        Assert.Contains("a", f.Surface.Html, StringComparison.Ordinal);   // the HTML is current all the same
    }

    [Fact]
    public void ShowingThePaneLoadsOnceHoweverManyChangesItMissed()
    {
        var f = new Fixture();
        f.Surface.IsShown = false;
        f.Coordinator.Update("a", "Doc", dark: false, token: null);
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Coordinator.Update("abc", "Doc", dark: false, token: null);

        f.Surface.IsShown = true;
        f.Coordinator.Show();

        Assert.Equal(new[] { AssetOrigin.IndexUrl }, f.Surface.Navigations);
        Assert.False(f.Coordinator.IsStale);
        Assert.Contains("abc", f.Surface.Html, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowingAPaneThatMissedNothingLoadsNothing()
    {
        var f = new Fixture().Loaded();
        f.Coordinator.Show();

        Assert.Single(f.Surface.Navigations);
    }

    [Fact]
    public void CollapsingDropsAWaitingReloadAndTheNextShowDoesIt()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        Assert.Equal(1, f.Scheduler.PendingTimers);

        f.Surface.IsShown = false;
        f.Coordinator.Hide();
        f.Settle();

        Assert.Equal(0, f.Surface.ReloadCount);         // nobody was looking
        Assert.True(f.Coordinator.IsStale);

        f.Surface.IsShown = true;
        f.Coordinator.Show();
        Assert.Equal(2, f.Surface.Navigations.Count);   // a fresh page, the Mac's look on return
    }

    [Fact]
    public void CollapsingWithNothingPendingLeavesThePageAlone()
    {
        var f = new Fixture().Loaded();
        f.Coordinator.Hide();

        Assert.False(f.Coordinator.IsStale);
        f.Coordinator.Show();
        Assert.Single(f.Surface.Navigations);
    }

    // ───────────────────────────── heading navigation ─────────────────────────────

    [Fact]
    public void AHeadingJumpEvaluatesTheScriptAndReportsBackOnTheNextTurn()
    {
        var f = new Fixture().Loaded();
        var handled = new List<Guid>();
        var jump = new PreviewNavigation(Guid.NewGuid(), "a-heading");

        f.Coordinator.Navigate(jump, handled.Add);

        Assert.Contains(Scripts.JumpToSlug("a-heading"), f.Surface.Evals);
        Assert.Empty(handled);                          // posted, not called
        f.Scheduler.RunPosted();
        Assert.Equal(new[] { jump.Id }, handled);
    }

    [Fact]
    public void TheSameRequestIsPerformedOnlyOnce()
    {
        var f = new Fixture().Loaded();
        var jump = new PreviewNavigation(Guid.NewGuid(), "a-heading");

        f.Coordinator.Navigate(jump);
        f.Coordinator.Navigate(jump);

        Assert.Single(f.Surface.Evals, e => e == Scripts.JumpToSlug("a-heading"));
    }

    [Fact]
    public void PickingTheSameHeadingTwiceWorksBecauseTheIdIsTheIdentity()
    {
        var f = new Fixture().Loaded();

        f.Coordinator.Navigate(new PreviewNavigation(Guid.NewGuid(), "a-heading"));
        f.Coordinator.Navigate(new PreviewNavigation(Guid.NewGuid(), "a-heading"));

        Assert.Equal(2, f.Surface.Evals.Count(e => e == Scripts.JumpToSlug("a-heading")));
    }

    [Fact]
    public void AJumpThatArrivesDuringALoadIsParkedAndDoneWhenTheLoadFinishes()
    {
        var f = new Fixture();
        var handled = new List<Guid>();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);   // loading
        var jump = new PreviewNavigation(Guid.NewGuid(), "a-heading");

        f.Coordinator.Navigate(jump, handled.Add);
        Assert.Empty(f.Surface.Evals);

        f.Surface.CompleteNavigation();
        Assert.Contains(Scripts.JumpToSlug("a-heading"), f.Surface.Evals);
        f.Scheduler.RunPosted();
        Assert.Equal(new[] { jump.Id }, handled);
    }

    [Fact]
    public void AParkedJumpIsPerformedExactlyOnce()
    {
        var f = new Fixture();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);
        f.Coordinator.Navigate(new PreviewNavigation(Guid.NewGuid(), "a-heading"));

        f.Surface.CompleteNavigation();
        f.Surface.CompleteNavigation();

        Assert.Single(f.Surface.Evals, e => e == Scripts.JumpToSlug("a-heading"));
    }

    [Fact]
    public void AJumpParkedByAReloadWaitsForThatReload()
    {
        var f = new Fixture().WithLivePage().Loaded();
        f.Coordinator.Update("ab", "Doc", dark: false, token: null);
        f.Settle();                                        // reload in flight

        f.Coordinator.Navigate(new PreviewNavigation(Guid.NewGuid(), "later"));
        Assert.DoesNotContain(Scripts.JumpToSlug("later"), f.Surface.Evals);

        f.Surface.CompleteNavigation();
        Assert.Contains(Scripts.JumpToSlug("later"), f.Surface.Evals);
    }

    [Fact]
    public void AJumpParkedBehindAFailedLoadIsNotThrownAway()
    {
        var f = new Fixture();
        f.Coordinator.Update("a", "Doc", dark: false, token: null);
        f.Coordinator.Navigate(new PreviewNavigation(Guid.NewGuid(), "a-heading"));

        f.Surface.CompleteNavigation(isSuccess: false);
        Assert.Empty(f.Surface.Evals);

        f.Coordinator.Update("b", "Doc", dark: false, token: "next.md");   // a fresh load
        f.Surface.CompleteNavigation();
        Assert.Contains(Scripts.JumpToSlug("a-heading"), f.Surface.Evals);
    }

    // ───────────────────────────── wiring ─────────────────────────────

    [Fact]
    public void TheTwoArgumentConstructorTakesTheProcessWideWriter()
    {
        var previous = DocumentHtml.Default;
        try
        {
            var html = new StubHtml();
            DocumentHtml.Default = html;
            var surface = new FakePreviewSurface();

            new PreviewCoordinator(surface, new FakeScheduler()).Update("a", "Doc", dark: false, token: null);

            Assert.Equal(1, html.Calls);
            Assert.Contains(ScreenHtml.FontStyle, surface.Html, StringComparison.Ordinal);
        }
        finally
        {
            DocumentHtml.Default = previous;
        }
    }

    [Fact]
    public void UntilItIsWiredTheDefaultWriterSaysSoRatherThanPreviewSomethingMadeUp()
    {
        // This test class is the only one that touches the process-wide default, and every test that
        // replaces it puts it back, so what is read here is what Md.App.Logic ships.
        var coordinator = new PreviewCoordinator(new FakePreviewSurface(), new FakeScheduler());

        var error = Assert.Throws<InvalidOperationException>(
            () => coordinator.Update("a", "Doc", dark: false, token: null));
        Assert.Contains("DocumentHtml.Default", error.Message, StringComparison.Ordinal);
    }
}
