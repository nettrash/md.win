using System.Globalization;
using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The injected and evaluated scripts (§4.6, Appendix B). The scroll-sync script is shared with
/// md.macOS and is quoted here as the Mac has it, so the only licence this port takes — the
/// <c>postMessage</c> channel — stays the only one.
/// </summary>
public class ScriptsTests
{
    /// <summary>
    /// md.macOS's <c>WKUserScript</c> source, verbatim (understand/rich.md §5.2). Do not tidy: the
    /// point of the copy is that it is not tidied.
    /// </summary>
    const string MacScrollSync =
        """
        (function () {
            let lastProgrammatic = 0;
            function maxScroll() {
                return Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
            }
            window.__mdMarkProgrammatic = function () { lastProgrammatic = Date.now(); };
            window.__mdSyncScrollTo = function (fraction) {
                lastProgrammatic = Date.now();
                window.scrollTo(0, Math.max(0, Math.min(1, fraction)) * maxScroll());
            };
            window.__mdScrollTo = function (y) {
                lastProgrammatic = Date.now();
                window.scrollTo(0, y);
            };
            let pending = false;
            window.addEventListener('scroll', function () {
                if (pending) { return; }
                pending = true;
                requestAnimationFrame(function () {
                    pending = false;
                    const max = maxScroll();
                    window.webkit?.messageHandlers?.mdScroll?.postMessage({
                        fraction: max > 0 ? window.scrollY / max : 0,
                        echo: (Date.now() - lastProgrammatic) < 300
                    });
                });
            }, { passive: true });
        })();
        """;

    static string Lf(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal);

    [Fact]
    public void ScrollSyncIsTheMacScriptWithExactlyOneSubstitution()
    {
        var expected = Lf(MacScrollSync).Replace(
            Scripts.MacPostMessageCall, Scripts.WebView2PostMessageCall, StringComparison.Ordinal);

        Assert.Equal(expected, Scripts.ScrollSync);
    }

    [Fact]
    public void TheSubstitutionHappensExactlyOnce()
    {
        var mac = Lf(MacScrollSync);
        Assert.Equal(1, Occurrences(mac, Scripts.MacPostMessageCall));
        Assert.Equal(1, Occurrences(Scripts.ScrollSync, Scripts.WebView2PostMessageCall));
        Assert.DoesNotContain("webkit", Scripts.ScrollSync, StringComparison.Ordinal);
        // The message-handler name goes with it; __mdScrollTo, which merely reads the same way, stays.
        Assert.DoesNotContain("messageHandlers", Scripts.ScrollSync, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollSyncOnlyDefinesFunctionsAndListeners()
    {
        // It is injected at document *start* on Windows (the Mac injected at document end), so it
        // must not read or write the DOM at injection time.
        Assert.DoesNotContain("document.body", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.DoesNotContain("querySelector", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.Contains("window.addEventListener('scroll'", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.Contains("{ passive: true }", Scripts.ScrollSync, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollSyncKeepsTheThreeHostEntryPointsAndThe300MsEchoWindow()
    {
        Assert.Contains("window.__mdMarkProgrammatic = function ()", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.Contains("window.__mdSyncScrollTo = function (fraction)", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.Contains("window.__mdScrollTo = function (y)", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.Contains("(Date.now() - lastProgrammatic) < 300", Scripts.ScrollSync, StringComparison.Ordinal);
    }

    [Fact]
    public void ScriptsAreLfOnly()
    {
        // Both go through AddScriptToExecuteOnDocumentCreatedAsync as a single string; the bytes are
        // pinned against another port and must not follow the checkout's line endings.
        Assert.DoesNotContain("\r", Scripts.ScrollSync, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", Scripts.LinkGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkGuardCoversClickAndAuxclickInTheCapturePhase()
    {
        Assert.Contains("document.addEventListener('click', guard, true);", Scripts.LinkGuard, StringComparison.Ordinal);
        Assert.Contains("document.addEventListener('auxclick', guard, true);", Scripts.LinkGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkGuardLetsThroughOnlyFragmentsAndHttp()
    {
        Assert.Contains("if (href.charAt(0) === '#') return;", Scripts.LinkGuard, StringComparison.Ordinal);
        Assert.Contains("href.indexOf('http://') === 0 || href.indexOf('https://') === 0", Scripts.LinkGuard, StringComparison.Ordinal);
        Assert.Contains("e.preventDefault(); e.stopImmediatePropagation();", Scripts.LinkGuard, StringComparison.Ordinal);
        // Lower-cased before the comparisons, so `JavaScript:` is caught too.
        Assert.Contains(".trim().toLowerCase()", Scripts.LinkGuard, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRenderCompleteProbeAndItsAnswerAreTheFamilyContract()
    {
        Assert.Equal("document.documentElement.getAttribute('data-md-render-complete')", Scripts.RenderComplete);
        // Three characters: Android compares against "\"1\"" for the same reason.
        Assert.Equal("\"1\"", Scripts.RenderCompleteResult);
        Assert.Equal(3, Scripts.RenderCompleteResult.Length);
    }

    [Fact]
    public void TheMeasurementScriptsAreTheMacs()
    {
        Assert.Equal("window.scrollY", Scripts.ScrollY);
        Assert.Equal("document.documentElement.scrollHeight", Scripts.ScrollHeight);
    }

    [Fact]
    public void NumbersGoIntoScriptsInTheInvariantCulture()
    {
        var german = new CultureInfo("de-DE");
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = german;
            Assert.Equal("window.__mdScrollTo?.(1234.5)", Scripts.ScrollTo(1234.5));
            Assert.Equal("window.__mdSyncScrollTo(0.5)", Scripts.SyncScrollTo(0.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void TheHeadingJumpMarksItselfProgrammaticFirst()
    {
        Assert.Equal(
            "window.__mdMarkProgrammatic?.(); document.getElementById('a-heading')?.scrollIntoView(true)",
            Scripts.JumpToSlug("a-heading"));
    }

    static int Occurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
