using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The traps the porting reports name, aimed at this package: a script result that is a number no
/// script can be handed back, a link that spoofs our own host, a render-complete poll that must not
/// mistake a timeout for success, an HTML writer that throws, and a document swap behind a collapsed
/// pane that must not remember the old article's scroll position.
/// </summary>
public class PreviewAdversarialTests
{
    sealed class StubHtml : IDocumentHtml
    {
        public int Calls { get; private set; }
        public bool Throw { get; set; }

        public string Document(string source, string title, bool dark)
        {
            Calls++;
            if (Throw) throw new InvalidOperationException("no writer");
            return $"<html><head><title>{title}</title></head><body>{source}</body></html>";
        }
    }

    // ───────────────── a number that is not a number ─────────────────

    [Theory]
    [InlineData("1e999")]        // JSON has no infinity, but a double parse of this one has
    [InlineData("-1e999")]
    public void AnInfiniteScrollPositionIsNoAnswer(string json)
    {
        // JsonElement.TryGetDouble returns true and hands back double.PositiveInfinity for 1e999
        // (JSON has no infinity literal, but the double parse of that many zeroes overflows to one).
        // The finiteness guard is what stops it reaching a script: .NET spells infinity "Infinity" or
        // "∞" depending on the culture data, and neither is the reader's place — one scrolls to the
        // bottom, the other is a syntax error, and both fail in silence.
        Assert.Null(JsonScript.Number(json));

        var wouldHaveBeenEmitted = Scripts.ScrollTo(double.PositiveInfinity);
        Assert.True(
            wouldHaveBeenEmitted.Contains("Infinity", StringComparison.Ordinal) || wouldHaveBeenEmitted.Contains('∞'),
            "infinity is not a usable scroll target, however it is spelled: " + wouldHaveBeenEmitted);
    }

    [Fact]
    public void AFiniteButEnormousScrollPositionStillDecodes()
    {
        Assert.Equal(1e308, JsonScript.Number("1e308"));
    }

    // ───────────────── the render-complete flag (WP6's poller contract) ─────────────────

    [Theory]
    [InlineData("null")]      // the script failed, or the page went away mid-poll
    [InlineData("1")]         // the JSON number one, not the attribute
    [InlineData("\"0\"")]     // md-init.js has not finished
    [InlineData("\"\"")]
    [InlineData("undefined")] // not JSON at all
    public void OnlyTheQuotedOneIsRenderComplete(string result)
    {
        Assert.NotEqual(Scripts.RenderCompleteResult, result);
        Assert.NotEqual("1", JsonScript.String(result));
    }

    [Fact]
    public void TheRenderCompleteFlagDecodesToTheStringOne()
    {
        // A poller that compares the raw JSON must compare against RenderCompleteResult; one that
        // decodes must compare against "1". Both are pinned so a timeout stays distinguishable from
        // success — §4.8 makes a timeout succeed anyway, which is exactly why a false *positive*
        // (proceeding while a diagram is still drawing) would never be noticed.
        Assert.Equal("1", JsonScript.String(Scripts.RenderCompleteResult));
        Assert.Equal("\"1\"", Scripts.RenderCompleteResult);
    }

    // ───────────────── links that spoof our own origin ─────────────────

    [Theory]
    [InlineData("JAVASCRIPT:alert(1)")]              // Uri lower-cases the scheme; the guard also lower-cases the href
    [InlineData("jAvAsCrIpT:alert(document.cookie)")]
    [InlineData("vbscript:msgbox(1)")]
    public void AnUppercasedScriptSchemeIsStillCancelled(string uri) =>
        Assert.Equal(LinkDecision.Cancel, LinkPolicy.Decide(new Uri(uri), true, AssetOrigin.IndexUri));

    [Fact]
    public void UserinfoThatLooksLikeOurHostDoesNotBuyTrust()
    {
        // https://md.assets@evil.com/ has Host "evil.com" and UserInfo "md.assets". Treating it as
        // ours would let a rendered document navigate the preview to a stranger's page in-pane.
        var spoof = new Uri("https://md.assets@evil.com/");
        Assert.Equal("evil.com", spoof.Host);
        Assert.Equal(LinkDecision.OpenExternally, LinkPolicy.Decide(spoof, true, AssetOrigin.IndexUri));
    }

    [Theory]
    [InlineData("https://md.assets.evil.com/index.html")]   // suffix, not our host
    [InlineData("https://notmd.assets/index.html")]         // prefix, not our host
    public void ALookalikeHostGoesToTheBrowserAndNeverIntoThePane(string uri) =>
        Assert.Equal(LinkDecision.OpenExternally, LinkPolicy.Decide(new Uri(uri), true, AssetOrigin.IndexUri));

    [Fact]
    public void OurHostOnAnotherPortIsStillCancelledNotLaunched()
    {
        Assert.Equal(LinkDecision.Cancel,
            LinkPolicy.Decide(new Uri("https://md.assets:8443/other.md"), true, AssetOrigin.IndexUri));
    }

    [Fact]
    public void TheDefaultPortSpellingOfOurOwnDocumentIsStillOurs()
    {
        // WebView2 hands back canonical URIs, but :443 is the same document and must not become a 404.
        Assert.Equal(LinkDecision.Allow,
            LinkPolicy.Decide(new Uri("https://md.assets:443/index.html"), false, AssetOrigin.IndexUri));
    }

    // ───────────────── an HTML writer that throws ─────────────────

    [Fact]
    public void AWriterThatThrewDoesNotPoisonTheDedupeKey()
    {
        // The one Update that throws must not be remembered as rendered: WP3 wires
        // DocumentHtml.Default at start-up and Md.Core's writer can throw on its own, and either way
        // the identical retry has to produce a page rather than be deduped into silence.
        var surface = new FakePreviewSurface();
        var html = new StubHtml { Throw = true };
        var coordinator = new PreviewCoordinator(surface, new FakeScheduler(), html);

        Assert.Throws<InvalidOperationException>(() => coordinator.Update("a", "Doc", dark: false, token: null));
        Assert.Empty(surface.HtmlHistory);
        Assert.Empty(surface.Navigations);

        html.Throw = false;
        coordinator.Update("a", "Doc", dark: false, token: null);

        Assert.Contains("a", surface.Html, StringComparison.Ordinal);
        Assert.Equal(new[] { AssetOrigin.IndexUrl }, surface.Navigations);
    }

    [Fact]
    public void AWriterThatThrewDoesNotPoisonTheTokenEither()
    {
        var surface = new FakePreviewSurface();
        var html = new StubHtml { Throw = true };
        var coordinator = new PreviewCoordinator(surface, new FakeScheduler(), html);

        Assert.Throws<InvalidOperationException>(() => coordinator.Update("a", "Doc", dark: false, token: "one.md"));

        html.Throw = false;
        coordinator.Update("a", "Doc", dark: false, token: "one.md");
        surface.CompleteNavigation();

        // Still the first document as far as the coordinator is concerned: the next article is new.
        coordinator.Update("b", "Doc", dark: false, token: "two.md");
        Assert.Equal(2, surface.Navigations.Count);
    }

    // ───────────────── a document swap behind a collapsed pane ─────────────────

    [Fact]
    public void SwitchingArticleWhileCollapsedForgetsTheOldOnesScrollPosition()
    {
        var surface = new FakePreviewSurface();
        var scheduler = new FakeScheduler();
        var coordinator = new PreviewCoordinator(surface, scheduler, new StubHtml());
        surface.EvalHandler = script => script == Scripts.ScrollY ? "900" : "null";

        coordinator.Update("a", "One", dark: false, token: "one.md");
        surface.CompleteNavigation();
        coordinator.Update("ab", "One", dark: false, token: "one.md");
        scheduler.Advance(PreviewCoordinator.DebounceDelay);
        surface.CompleteNavigation();
        Assert.Equal(900, coordinator.SavedScrollY);

        // Edit mode, then the sidebar picks another article while nobody is looking.
        surface.IsShown = false;
        coordinator.Hide();
        coordinator.Update("b", "Two", dark: false, token: "two.md");
        Assert.True(coordinator.IsStale);

        surface.IsShown = true;
        var evalsBeforeShow = surface.Evals.Count;
        coordinator.Show();

        Assert.Equal(0, coordinator.SavedScrollY);            // the other article's place is not this one's
        Assert.Equal(2, surface.Navigations.Count);           // exactly one load on return
        Assert.False(coordinator.IsStale);

        surface.CompleteNavigation();
        Assert.DoesNotContain(
            surface.Evals.Skip(evalsBeforeShow),
            e => e.StartsWith("window.__mdScrollTo", StringComparison.Ordinal));
    }

    [Fact]
    public void ChangingThemeWhileCollapsedCostsOneLoadNotTwo()
    {
        // ActualThemeChanged calls Update with the other flag; if that happens in Edit mode the whole
        // point of "stale while collapsed" is that Mermaid and PlantUML do not redraw twice.
        var surface = new FakePreviewSurface { IsShown = false };
        var scheduler = new FakeScheduler();
        var coordinator = new PreviewCoordinator(surface, scheduler, new StubHtml());

        coordinator.Update("a", "Doc", dark: false, token: null);
        coordinator.Update("a", "Doc", dark: true, token: null);
        Assert.Empty(surface.Navigations);
        Assert.Equal(0, scheduler.PendingTimers);

        surface.IsShown = true;
        coordinator.Show();
        Assert.Single(surface.Navigations);
        Assert.Equal(0, surface.ReloadCount);
    }
}
