using System.Globalization;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

// The string half of a PDF export: the one declaration that scales with the chosen trim size.
// Port of the margin cases in mdTests.swift (`testA4MarginIsUnchangedAndSmallerPagesScaleTheMarginDown`
// is in PageSizeTests; these pin the rewrite that consumes it) plus the export-CSS cases
// `testHTMLPageBreakMarkerAndExportCSS` / `testExportPageIsPlainWhiteAndAlwaysLight` seen from the
// paper side. The macOS PDF tests that read a real MediaBox and count pages need a WebView and are
// the app's; nothing here renders anything.
//
// Every string assertion is ordinal: Assert.Contains(string, string) compares by the current
// culture, which is the class of bug this port exists to keep out.
public class PdfExportTests
{
    private static string Doc(string source, bool dark = false, bool export = true) =>
        MarkdownHtml.Document(source, "T", dark, export);

    private static void Has(string haystack, string needle, string? note = null) =>
        Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected to contain '" + needle + "'");

    private static void Lacks(string haystack, string needle, string? note = null) =>
        Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected NOT to contain '" + needle + "'");

    private static int Count(string haystack, string needle)
    {
        var total = 0;
        var from = 0;
        while (true)
        {
            var at = haystack.IndexOf(needle, from, StringComparison.Ordinal);
            if (at < 0) return total;
            total++;
            from = at + needle.Length;
        }
    }

    private static string ReplaceFirst(string text, string needle, string replacement)
    {
        var at = text.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(at >= 0, "expected '" + needle + "' to be present");
        return text[..at] + replacement + text[(at + needle.Length)..];
    }

    [Fact]
    public void A4IsTheHistoricalMarginSoAnA4ExportIsByteForByteWhatItAlwaysWas()
    {
        var html = Doc("# Title\n\nProse.");
        // The replacement text equals the original, so trim sizes cost the default nothing.
        Assert.Equal(html, PdfExport.StyledForExport(html, PageSize.A4));
        Assert.Equal("padding: 48px 56px;", PdfExport.BodyPaddingRule);
        Has(html, "    " + PdfExport.BodyPaddingRule, "the sheet's body rule");
    }

    [Fact]
    public void EveryTrimSizeRewritesTheBodyMarginToItsOwnScaledPadding()
    {
        var html = Doc("# Title\n\nProse.");
        var expected = new (PageSize Size, string Padding)[]
        {
            (PageSize.A4, "48px 56px"),
            (PageSize.A5, "34px 39px"),
            (PageSize.UsLetter, "45px 58px"),
            (PageSize.UsLegal, "57px 58px"),
            (PageSize.SixByNine, "37px 41px"),
            (PageSize.FiveByEight, "33px 34px"),
            (PageSize.Digest, "35px 37px"),
        };

        foreach (var (size, padding) in expected)
        {
            var styled = PdfExport.StyledForExport(html, size);
            Assert.Equal(padding, size.CssPadding);
            Has(styled, "padding: " + padding + ";", size.Id);
            Assert.Equal(1, Count(styled, "padding: " + padding + ";"));
            // Exactly one body rule, whichever size: the needle is gone unless it IS the answer.
            Assert.Equal(size.Id == "a4" ? 1 : 0, Count(styled, PdfExport.BodyPaddingRule));
        }
    }

    [Fact]
    public void OnlyTheFirstOccurrenceIsRewrittenSoAQuotedRuleInACodeBlockSurvives()
    {
        // The rule contains no <, > or & — a code block quoting it reaches the markup verbatim, so
        // this is a document a user could really write, not a contrived string.
        var html = Doc("Look:\n\n```\nbody { padding: 48px 56px; }\n```\n");
        Assert.Equal(2, Count(html, PdfExport.BodyPaddingRule));

        var styled = PdfExport.StyledForExport(html, PageSize.SixByNine);
        Has(styled, "padding: 37px 41px;", "the head's body rule");
        // The author's text is untouched: the sheet always precedes any user content.
        Assert.Equal(1, Count(styled, PdfExport.BodyPaddingRule));
        Has(styled, "body { padding: 48px 56px; }", "the code block");
        Assert.True(styled.IndexOf("padding: 37px 41px;", StringComparison.Ordinal)
            < styled.IndexOf(PdfExport.BodyPaddingRule, StringComparison.Ordinal));
    }

    [Fact]
    public void TheMarginIsTheOnlyThingThatScalesWithThePaper()
    {
        var html = Doc("# Title\n\nProse.\n\n```\ncode\n```\n\n\\newpage\n\nAfter.");
        foreach (var size in PageSize.All)
        {
            var styled = PdfExport.StyledForExport(html, size);
            // Put the A4 rule back and the two documents are the same bytes: nothing else moved.
            Assert.Equal(html, ReplaceFirst(styled, "padding: " + size.CssPadding + ";", PdfExport.BodyPaddingRule));

            // The rest of the paper styling is the `export: true` sheet, identical on every size.
            Has(styled, "font-size: 11pt;", size.Id);
            Has(styled, "pre { white-space: pre-wrap; overflow-wrap: anywhere; }", size.Id);
            Has(styled, "html, body { background: #FFFFFF; }", size.Id);
            Has(styled, ":root { color-scheme: light; }", size.Id);
            Has(styled, ".md-pagebreak { height: 0; margin: 0; break-after: page; }", size.Id);
            Has(styled, "* { -webkit-print-color-adjust: exact; print-color-adjust: exact; box-sizing: border-box; }", size.Id);
            Has(styled, "<body data-md-dark=\"0\">", size.Id);
            Has(styled, "<div class=\"md-pagebreak\"></div>", size.Id);

            // WHICH SHEET: the export variant, byte for byte as md.vscode recorded it, with exactly
            // one declaration rewritten. Anchored to the golden file, not to a second call.
            var sheet = Fixtures.Read(Path.Combine("golden", "stylesheet-export.css"))[..^1];
            Has(styled, "<style>" + ReplaceFirst(sheet, PdfExport.BodyPaddingRule, "padding: " + size.CssPadding + ";") + "</style>", size.Id);
            Assert.Equal(1, Count(styled, "<style>"));
        }
    }

    [Fact]
    public void AnHtmlWithoutTheBodyRuleComesBackUnchanged()
    {
        const string plain = "<p>no stylesheet here</p>";
        Assert.Same(plain, PdfExport.StyledForExport(plain, PageSize.Digest));
        Assert.Same(plain, PdfExport.StyledForExport(plain, PageSize.A4));
        Assert.Same(plain, PdfExport.StyledForExport(plain, PageSize.SixByNine));
    }

    [Fact]
    public void TheExportSheetIsThePaperVariantWhateverTheWindowTheme()
    {
        // `export: true` collapses `dark`, so the paper is white and the ink dark on both paths, and
        // Mermaid and PlantUML read `data-md-dark="0"` and render light too.
        foreach (var dark in new[] { false, true })
        {
            var styled = PdfExport.StyledForExport(Doc("hello", dark: dark), PageSize.FiveByEight);
            Has(styled, "html, body { background: #FFFFFF; }");
            Has(styled, ":root { color-scheme: light; }");
            Has(styled, "<body data-md-dark=\"0\">");
            Lacks(styled, "#241E18", "the dark paper");
            Lacks(styled, "rgba(231,219,194,0.16)", "the dark border");
            Lacks(styled, "font-size: 13pt;", "the screen body size");
        }
    }

    [Fact]
    public void ThePdfPageSizeReachesNeitherThePreviewNorTheHtmlExportNorTheSheet()
    {
        // The stylesheet itself knows nothing about trim sizes: it always writes A4's margin, and
        // only the two PDF paths rewrite it. `md.pdfPageSize` is a PDF-file setting.
        Has(MarkdownHtml.Css(dark: false, export: true), PdfExport.BodyPaddingRule, "the export sheet");
        Has(MarkdownHtml.Css(dark: false, export: false), PdfExport.BodyPaddingRule, "the light sheet");
        Has(MarkdownHtml.Css(dark: true, export: false), PdfExport.BodyPaddingRule, "the dark sheet");
        Has(Doc("hi", export: false), PdfExport.BodyPaddingRule, "the preview");
        Has(HtmlExport.ExportDocument("hi", "T"), PdfExport.BodyPaddingRule, "the HTML export");
    }

    [Fact]
    public void ThePageBoxNamesTheTrimSizeInPointsAndZeroesThePrinterMargin()
    {
        Assert.Equal(
            "<style>@page { size: 595.2pt 841.8pt; margin: 0; }\n"
            + "html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style>",
            PdfExport.PageBoxCss(PageSize.A4));
        // A whole number of points carries no decimal point, exactly as the TypeScript writes it.
        Has(PdfExport.PageBoxCss(PageSize.UsLetter), "size: 612pt 792pt;");
        Has(PdfExport.PageBoxCss(PageSize.SixByNine), "size: 432pt 648pt;");
        Has(PdfExport.PageBoxCss(PageSize.A5), "size: 419.5pt 595.3pt;");
        Has(PdfExport.PageBoxCss(PageSize.Digest), "size: 396pt 612pt;");
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void ThePaperStylingIsTheSameBytesUnderAnyCulture(string cultureName)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            // A German culture would write `595,2pt` and the browser would drop the declaration.
            Assert.Equal(
                "<style>@page { size: 595.2pt 841.8pt; margin: 0; }\n"
                + "html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style>",
                PdfExport.PageBoxCss(PageSize.A4));
            Has(PdfExport.PageBoxCss(PageSize.A5), "size: 419.5pt 595.3pt;");

            var styled = PdfExport.StyledForExport(Doc("hi"), PageSize.A5);
            Has(styled, "padding: 34px 39px;");
            Lacks(styled, PdfExport.BodyPaddingRule);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }

    [Fact]
    public void ThePageBoxIsAppendedAfterTheSheetAndNeverWovenIntoIt()
    {
        var html = Doc("hi");
        var withBox = PdfExport.WithPageBox(html, PageSize.SixByNine);

        // The sheet's own bytes survive: the family's stylesheet stays byte-identical everywhere.
        Has(withBox, "<style>" + MarkdownHtml.Css(dark: false, export: true) + "</style>\n<style>@page");
        Has(withBox, "size: 432pt 648pt;");
        // Appended, not inserted anywhere else: the head still closes right after it.
        Has(withBox, "print-color-adjust: exact; }</style>\n</head>");
        Assert.Equal(2, Count(withBox, "<style>"));

        // No `</style>` to anchor on: unchanged rather than mangled.
        Assert.Same("<p>plain</p>", PdfExport.WithPageBox("<p>plain</p>", PageSize.A4));
    }

    [Fact]
    public void NullArgumentsAreRejectedRatherThanSilentlyProducingHalfAPage()
    {
        Assert.Throws<ArgumentNullException>(() => PdfExport.StyledForExport(null!, PageSize.A4));
        Assert.Throws<ArgumentNullException>(() => PdfExport.StyledForExport("x", null!));
        Assert.Throws<ArgumentNullException>(() => PdfExport.PageBoxCss(null!));
        Assert.Throws<ArgumentNullException>(() => PdfExport.WithPageBox(null!, PageSize.A4));
        Assert.Throws<ArgumentNullException>(() => PdfExport.WithPageBox("x", null!));
    }
}
