using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The Windows typography graft (§4.3). The bytes are pinned because they are the only difference
/// between what a Windows reader sees and what every other port renders; the idempotence is pinned
/// because Print hands the same HTML through two pipelines.
/// </summary>
public class ScreenHtmlTests
{
    const string Page = "<!DOCTYPE html>\n<html><head><title>t</title></head>\n<body>x</body></html>";

    [Fact]
    public void FontStyleIsTheExactBytesTheDesignPins()
    {
        Assert.Equal(
            "<style id=\"md-win-fonts\">body{font-family:\"Lucida Sans Typewriter\",\"Courier New\",serif;}</style>",
            ScreenHtml.FontStyle);
    }

    [Fact]
    public void StyleGoesOnItsOwnLineBeforeTheFirstHeadEnd()
    {
        Assert.Equal(
            "<!DOCTYPE html>\n<html><head><title>t</title>\n"
            + "<style id=\"md-win-fonts\">body{font-family:\"Lucida Sans Typewriter\",\"Courier New\",serif;}</style>"
            + "</head>\n<body>x</body></html>",
            ScreenHtml.WithWindowsFonts(Page));
    }

    [Fact]
    public void OnlyTheFirstHeadEndIsUsed()
    {
        var twice = ScreenHtml.WithWindowsFonts("<head>a</head><head>b</head>");
        Assert.Equal("<head>a\n" + ScreenHtml.FontStyle + "</head><head>b</head>", twice);
    }

    [Fact]
    public void ASecondCallChangesNothing()
    {
        var once = ScreenHtml.WithWindowsFonts(Page);
        Assert.Equal(once, ScreenHtml.WithWindowsFonts(once));
    }

    [Fact]
    public void HtmlThatAlreadyCarriesTheIdIsLeftAlone()
    {
        // Whatever spelling of the style element the caller already has, one is enough.
        const string own = "<html><head><style id=\"md-win-fonts\">body{}</style></head></html>";
        Assert.Same(own, ScreenHtml.WithWindowsFonts(own));
    }

    [Fact]
    public void AFragmentWithNoHeadComesBackUntouched()
    {
        const string fragment = "<p>just a body</p>";
        Assert.Same(fragment, ScreenHtml.WithWindowsFonts(fragment));
    }

    [Fact]
    public void CodeStaysCourierNew()
    {
        // The style only touches body: the shared stylesheet's `code, pre` rule is more specific and
        // keeps monospace, and KaTeX / Mermaid / PlantUML carry their own faces.
        Assert.Contains("body{font-family:", ScreenHtml.FontStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("code", ScreenHtml.FontStyle, StringComparison.Ordinal);
        Assert.DoesNotContain("pre", ScreenHtml.FontStyle, StringComparison.Ordinal);
    }

    [Fact]
    public void LucidaSansTypewriterLeadsAndCourierNewIsTheFallback()
    {
        Assert.Contains("\"Lucida Sans Typewriter\",\"Courier New\",serif", ScreenHtml.FontStyle, StringComparison.Ordinal);

        // The face has Regular, Bold and Italic in the box on Windows 11, so nothing here is
        // synthesised; the Georgia the other ports fall back to is deliberately NOT named.
        Assert.DoesNotContain("Georgia", ScreenHtml.FontStyle, StringComparison.Ordinal);
    }

    [Fact]
    public void ADocumentThatMentionsTheMarkerInItsBodyLosesTheWindowsFont()
    {
        // KNOWN, and pinned so it is a decision rather than a surprise: idempotence is decided by
        // searching the *whole* page for the id (§4.3, "a second call finds the id and returns the
        // input"), so a reader who writes `id="md-win-fonts"` in a fenced code block gets a preview
        // in Courier New. Nothing else breaks and no port renders it differently, but if this is ever
        // to be fixed the search has to be scoped to the head.
        var page = "<html><head></head><body><pre>&lt;style " + ScreenHtml.IdMarker + "&gt;</pre></body></html>";

        Assert.Same(page, ScreenHtml.WithWindowsFonts(page));
        Assert.DoesNotContain(ScreenHtml.FontStyle, ScreenHtml.WithWindowsFonts(page), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkerIsTheIdOfTheStyleItInserts()
    {
        // The two constants have to agree or idempotence silently stops working and Print stacks a
        // second copy of the style on every pass.
        Assert.Contains(ScreenHtml.IdMarker, ScreenHtml.FontStyle, StringComparison.Ordinal);
        Assert.Equal("id=\"md-win-fonts\"", ScreenHtml.IdMarker);
    }
}
