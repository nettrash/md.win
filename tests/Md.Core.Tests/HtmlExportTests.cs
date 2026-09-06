using System.Globalization;
using System.Text;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

// The pure half of "Export as HTML": the document the app loads, and the finished file it writes
// once the offscreen WebView2 has handed the live DOM back. Nothing here renders anything — the
// browser's part (load, wait for `data-md-render-complete`, run the capture script) is simulated by
// `Capture`, which does to a string exactly what the script does to the DOM: drop every <script>
// and every stylesheet <link>, and hand back `documentElement.outerHTML` (no doctype).
//
// Ports mdTests.swift's `testEmbeddedKatexCSSInlinesEveryFaceAsWoff2`,
// `testHTMLExportKeepsPageBreaksVisible` and `testExportPageIsPlainWhiteAndAlwaysLight`, plus the
// string-level halves of RichRenderTests' three self-contained-export proofs
// (`testExportedHTMLStandsAloneWithNoEngines`, `…OfAPlotDocument…`, `…OfAPlainDocumentCarriesNoFontPayload`)
// and the two cases the Kotlin suite adds (a missing face, a missing stylesheet). The rest are this
// port's own: the two licence notices byte for byte, the two gates reading two different strings,
// the `$`-template hazard, and the four cultures.
//
// Every string assertion is ordinal: Assert.Contains(string, string) compares by the current
// culture, which is the class of bug this port exists to keep out.
public class HtmlExportTests
{
    private const char Dash = (char)0x2014;   // EM DASH, in both notices

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

    /// <summary>
    /// What the capture script does, done to a string: every &lt;script&gt;…&lt;/script&gt; and every
    /// stylesheet &lt;link&gt; removed, the doctype gone (outerHTML has none). The surrounding
    /// newlines are text nodes and stay, exactly as they would in the DOM. A real render would also
    /// have turned the diagram containers into &lt;svg&gt; and the formulas into markup; that is the
    /// engines' work, not this module's, and no assertion here depends on it.
    /// </summary>
    private static string Capture(string document)
    {
        var page = document;
        Assert.StartsWith(HtmlExport.Doctype, page, StringComparison.Ordinal);
        page = page[HtmlExport.Doctype.Length..];
        page = RemoveAll(page, "<script", "</script>");
        page = RemoveAll(page, "<link ", ">");
        return page;
    }

    private static string RemoveAll(string text, string open, string close)
    {
        while (true)
        {
            var start = text.IndexOf(open, StringComparison.Ordinal);
            if (start < 0) return text;
            var end = text.IndexOf(close, start, StringComparison.Ordinal);
            Assert.True(end >= 0, "unterminated " + open);
            text = text[..start] + text[(end + close.Length)..];
        }
    }

    // MARK: - the bundled assets, read off disk the way the package will read them

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "md.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);   // the suite always runs from inside the repo
        return dir!.FullName;
    }

    /// <summary>The app's asset reader, in its test spelling: <c>rich/…</c> under the packaged root.</summary>
    private static byte[]? ReadBundledAsset(string relativePath)
    {
        var path = Path.Combine(RepoRoot(), "src", "Md.App",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    private static Func<string, byte[]?> Assets(params (string Path, string Content)[] files) =>
        name =>
        {
            foreach (var (path, content) in files)
            {
                if (string.Equals(path, name, StringComparison.Ordinal)) return Encoding.UTF8.GetBytes(content);
            }
            return null;
        };

    // MARK: - the licence notices

    [Fact]
    public void TheKatexNoticeIsTheVerbatimMitAndOflText()
    {
        var expected = string.Join("\n",
        [
            "<!--",
            "  Mathematics rendered with KaTeX (https://katex.org) " + Dash + " MIT License,",
            "  Copyright (c) 2013-2020 Khan Academy and other contributors.",
            "  The embedded KaTeX_* fonts are licensed under the SIL Open Font",
            "  License 1.1 (https://scripts.sil.org/OFL); \"KaTeX\" is a Reserved Font",
            "  Name. The fonts are embedded unmodified.",
            "-->",
        ]);
        Assert.Equal(expected, HtmlExport.KatexNotice);
        // Legal text, so pin its shape too: seven lines, no trailing newline, no CR from a checkout.
        Assert.Equal(7, HtmlExport.KatexNotice.Split('\n').Length);
        Assert.EndsWith("-->", HtmlExport.KatexNotice, StringComparison.Ordinal);
        Lacks(HtmlExport.KatexNotice, "\r");
        Has(HtmlExport.KatexNotice, "SIL Open Font", "the OFL obligation the fonts travel under");
    }

    [Fact]
    public void TheMermaidNoticeIsTheVerbatimMitText()
    {
        var expected = string.Join("\n",
        [
            "<!--",
            "  Diagrams rendered with Mermaid (https://mermaid.js.org) " + Dash + " MIT License,",
            "  Copyright (c) 2014-2022 Knut Sveidqvist. The diagram SVG carries",
            "  Mermaid's own theme stylesheet.",
            "-->",
        ]);
        Assert.Equal(expected, HtmlExport.MermaidNotice);
        Assert.Equal(5, HtmlExport.MermaidNotice.Split('\n').Length);
        Lacks(HtmlExport.MermaidNotice, "\r");
        // A straight apostrophe, not U+2019: the three ports all spell it this way.
        Has(HtmlExport.MermaidNotice, "Mermaid's own theme stylesheet.");
    }

    [Fact]
    public void TheCaptureScriptIsTheDomReadTheAppMustRun()
    {
        var expected = string.Join("\n",
        [
            "(function () {",
            "  document.querySelectorAll('script, link[rel=\"stylesheet\"]').forEach(function (el) {",
            "    el.remove();",
            "  });",
            "  // A stale completion flag would be misleading in a file that has",
            "  // nothing left to complete.",
            "  document.documentElement.removeAttribute('data-md-render-complete');",
            "  return document.documentElement.outerHTML;",
            "})()",
        ]);
        Assert.Equal(expected, HtmlExport.CaptureScript);
        Lacks(HtmlExport.CaptureScript, "\r", "a CRLF checkout must not reach the DOM");
        Assert.Equal("<!DOCTYPE html>\n", HtmlExport.Doctype);
    }

    // MARK: - the document the app loads

    [Fact]
    public void TheExportDocumentSwapsThePageBreakRuleForTheOneAReaderCanSee()
    {
        var document = HtmlExport.ExportDocument("a\n\n\\newpage\n\nb", "T");
        // The marker survives; only the rule changes, so the break is visible in a scrolled file.
        Has(document, "<div class=\"md-pagebreak\"></div>");
        Has(document, HtmlExport.ScreenPageBreakRule);
        Lacks(document, "break-after: page", "invisible on screen");
        Lacks(document, HtmlExport.ExportPageBreakRule);
        // The light border colour is hard-coded: an export page is always light.
        Has(document, "rgba(43,38,32,0.16)");
        Lacks(document, "rgba(231,219,194,0.16)");
    }

    [Fact]
    public void ThePageBreakSwapTakesEveryOccurrenceUnlikeThePaddingRewrite()
    {
        // The rule has no <, > or & in it, so a code block quoting it reaches the markup verbatim.
        const string source = "See:\n\n```\n.md-pagebreak { height: 0; margin: 0; break-after: page; }\n```\n";
        var raw = MarkdownHtml.Document(source, "T", dark: false, export: true);
        Assert.Equal(2, Count(raw, HtmlExport.ExportPageBreakRule));

        var document = HtmlExport.ExportDocument(source, "T");
        Assert.Equal(0, Count(document, HtmlExport.ExportPageBreakRule));
        Assert.Equal(2, Count(document, HtmlExport.ScreenPageBreakRule));
        // Contrast: a document quoting the PDF margin rule the same way keeps its copy, because
        // that rewrite touches only the first occurrence. Both asymmetries are deliberate.
        var quotesTheMargin = MarkdownHtml.Document("```\nbody { padding: 48px 56px; }\n```\n", "T", dark: false, export: true);
        Assert.Equal(2, Count(quotesTheMargin, PdfExport.BodyPaddingRule));
        Assert.Equal(1, Count(PdfExport.StyledForExport(quotesTheMargin, PageSize.A5), PdfExport.BodyPaddingRule));
    }

    [Fact]
    public void TheExportDocumentIsAlwaysTheLightPaperPageAndCarriesA4Margins()
    {
        var document = HtmlExport.ExportDocument("hello", "T");
        Has(document, "html, body { background: #FFFFFF; }");
        Has(document, ":root { color-scheme: light; }");
        Has(document, "<body data-md-dark=\"0\">");
        Lacks(document, "#241E18", "the dark paper");
        // No window theme reaches it: `Document` collapses `dark && !export` before the palette.
        Assert.Equal(MarkdownHtml.Document("hello", "T", dark: true, export: true), MarkdownHtml.Document("hello", "T", dark: false, export: true));
        // And no trim size reaches it either: an HTML file is not paper.
        Has(document, PdfExport.BodyPaddingRule, "A4's margin, whatever md.pdfPageSize says");

        // WHICH SHEET: the export variant, byte for byte as md.vscode recorded it, with exactly one
        // rule swapped. Anchored to the golden file rather than to a second call of the same code.
        var sheet = Fixtures.Read(Path.Combine("golden", "stylesheet-export.css"))[..^1];
        Has(document, "<style>" + HtmlExport.VisiblePageBreaks(sheet) + "</style>", "the export sheet");
        Assert.Equal(1, Count(document, "<style>"));
    }

    // MARK: - the two gates

    [Fact]
    public void NeedsKatexAsksTheInputDocumentNotTheCapturedPage()
    {
        var math = HtmlExport.ExportDocument("Formula $a^2$ here.", "T");
        Assert.True(HtmlExport.NeedsKatex(math));
        Has(math, "<link rel=\"stylesheet\" href=\"rich/katex.min.css\">");

        // By capture time the link is gone from the DOM. Asking the page would gate every math
        // export off and no exported document would ever carry the stylesheet.
        var captured = Capture(math);
        Assert.False(HtmlExport.NeedsKatex(captured));
        Lacks(captured, "rich/");

        Assert.False(HtmlExport.NeedsKatex(HtmlExport.ExportDocument("no maths here", "T")));
        // Currency is not math, so it pulls nothing in (the writer's rule, pinned again here).
        Assert.False(HtmlExport.NeedsKatex(HtmlExport.ExportDocument("it costs $5 and $10 today", "T")));
        Assert.Equal("rich/katex.min.css", HtmlExport.KatexCssAsset);
    }

    [Fact]
    public void TheMermaidNoticeIsGatedOnTheCapturedPageNotTheInput()
    {
        var reader = Assets();
        var mermaid = HtmlExport.ExportDocument("```mermaid\ngraph TD\nA-->B\n```\n", "T");
        var page = HtmlExport.PreparePage(Capture(mermaid), mermaid, reader);
        Has(page, HtmlExport.MermaidNotice);
        Has(page, HtmlExport.MermaidNotice + "\n</head>", "inserted immediately before </head>");

        // Graphviz and PlantUML bake geometry, not Mermaid's own stylesheet: no notice for them.
        var dot = HtmlExport.ExportDocument("```dot\ndigraph { a -> b }\n```\n", "T");
        Lacks(HtmlExport.PreparePage(Capture(dot), dot, reader), "mermaid.js.org");
        var plain = HtmlExport.ExportDocument("hello", "T");
        Lacks(HtmlExport.PreparePage(Capture(plain), plain, reader), "mermaid.js.org");
        Assert.Equal("class=\"mermaid\"", HtmlExport.MermaidContainerNeedle);
    }

    // MARK: - the finished file

    [Fact]
    public void PreparePagePutsBackTheDoctypeThatOuterHtmlDrops()
    {
        // Without one every browser renders the file in quirks mode; the macOS test reads
        // `document.compatMode == "CSS1Compat"` back out of a real load.
        var page = HtmlExport.PreparePage("<html><head></head><body>x</body></html>", Assets(), hasMath: false);
        Assert.Equal("<!DOCTYPE html>\n<html><head></head><body>x</body></html>", page);
        Assert.StartsWith("<!DOCTYPE html>\n", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptyCaptureIsRejectedRatherThanWrittenToAFile()
    {
        // The app's "The rendered page could not be captured." case: a file with a doctype and
        // nothing else would look like a successful export.
        Assert.Throws<ArgumentException>(() => HtmlExport.PreparePage("", Assets(), hasMath: false));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.PreparePage(null!, Assets(), hasMath: false));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.PreparePage("<html></html>", null!, hasMath: false));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.EmbeddedKatexCss(null!));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.VisiblePageBreaks(null!));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.NeedsKatex(null!));
        Assert.Throws<ArgumentNullException>(() => HtmlExport.WithMermaidNotice(null!));
    }

    [Fact]
    public void APlainDocumentCarriesNoEngineAndNoFontPayload()
    {
        var document = HtmlExport.ExportDocument("# Title\n\nJust prose, no formulas.\n", "plain");
        var reads = new List<string>();
        var page = HtmlExport.PreparePage(Capture(document), document, name => { reads.Add(name); return ReadBundledAsset(name); });

        Lacks(page, "data:font/woff2", "no font payload without math");
        Lacks(page, "rich/", "no engine, no asset folder");
        Lacks(page, "<script", "nothing left to run");
        Lacks(page, "<link", "nothing left to fetch");
        Has(page, "Just prose");
        Assert.StartsWith("<!DOCTYPE html>\n", page, StringComparison.Ordinal);
        // The 300 KB stylesheet is not even read for a document that has no math.
        Assert.Empty(reads);
        // macOS bounds the plain export at 100 KB; ours is the same page minus the engines.
        Assert.True(Encoding.UTF8.GetByteCount(page) < 100_000, "plain export should stay small");
    }

    [Fact]
    public void APlotOnlyDocumentCarriesNoEngineAndNoFontPayload()
    {
        // A plot is finished SVG in the bytes the page is loaded from: no engine, no wait, no fonts.
        const string source = "# Two figures\n\n```plot\nx: -10..10\ny: -2..2\ntitle: Damped oscillation\nsin(x) * exp(-abs(x)/5)\n```\n\n```plot\nx: -4..4\ny: -1..3\n(x>0)*sqrt(x)\n```\n";
        var document = HtmlExport.ExportDocument(source, "plots");
        Has(document, "<div class=\"plot\"><svg");
        foreach (var engine in new[] { "katex.min.js", "mhchem.min.js", "mermaid.min.js", "viz-global.js", "viz-standalone", "highlight.min.js" })
        {
            Lacks(document, engine, "a plot needs no engine");
        }

        var page = HtmlExport.PreparePage(Capture(document), document, ReadBundledAsset);
        Assert.StartsWith("<!DOCTYPE html>\n", page, StringComparison.Ordinal);
        Lacks(page, "data:font/woff2");
        Lacks(page, "rich/");
        Lacks(page, "<script");
        Lacks(page, "<link");
        Assert.True(Count(page, "<svg") >= 2, "both figures survive");
        Assert.True(Count(page, "<polyline") >= 2, "both series survive");
    }

    // MARK: - the KaTeX stylesheet

    [Fact]
    public void EmbeddedKatexCssInlinesEveryFaceAsWoff2()
    {
        // The real bundled assets, read the way the package will read them.
        Assert.NotNull(ReadBundledAsset(HtmlExport.KatexCssAsset));
        var css = HtmlExport.EmbeddedKatexCss(ReadBundledAsset);
        Assert.NotNull(css);

        Lacks(css!, "url(fonts/", "no dead relative link survives");
        Has(css!, "url(data:font/woff2;base64,");
        Lacks(css!, "format(\"woff\")", "the alternates are dead weight off this machine");
        Lacks(css!, "format(\"truetype\")");
        // Every face, and only woff2: twenty rules, twenty data URIs.
        Assert.Equal(Count(css!, "@font-face"), Count(css!, "url(data:font/woff2;base64,"));
        Assert.Equal(20, Count(css!, "@font-face"));
        Has(css!, ".katex", "still a stylesheet, not just fonts");
        // Base64 with no line breaks: a wrapped payload would put newlines inside a url() value.
        var source = new UTF8Encoding(false, true).GetString(ReadBundledAsset(HtmlExport.KatexCssAsset)!);
        Assert.Equal(Count(source, "\n"), Count(css!, "\n"));
        Lacks(css!, "\r");
    }

    [Fact]
    public void TheWoff2RunSwallowsTheWoffAndTruetypeAlternates()
    {
        // The `[^;}]*` tail eats the sibling alternates, so the whole `src` run is replaced.
        const string sheet = "@font-face{font-family:KaTeX_Main;src:url(fonts/KaTeX_Main-Regular.woff2) format(\"woff2\"),url(fonts/KaTeX_Main-Regular.woff) format(\"woff\"),url(fonts/KaTeX_Main-Regular.ttf) format(\"truetype\")}.katex{font-size:1.21em}";
        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, sheet),
            ("rich/fonts/KaTeX_Main-Regular.woff2", "MZ")));

        Assert.Equal(
            "@font-face{font-family:KaTeX_Main;src:url(data:font/woff2;base64,TVo=) format(\"woff2\")}.katex{font-size:1.21em}",
            css);
    }

    [Fact]
    public void AMissingFaceLeavesItsRuleAloneRatherThanEmitABrokenDataUri()
    {
        // A relative path that is merely absent still degrades to a fallback face; a truncated
        // data: URI is a parse error that takes the surrounding rules with it.
        const string sheet = "@font-face{src:url(fonts/Present.woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/Absent.woff2) format(\"woff2\"),url(fonts/Absent.ttf) format(\"truetype\")}";
        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, sheet),
            ("rich/fonts/Present.woff2", "x")));

        Assert.Equal(
            "@font-face{src:url(data:font/woff2;base64,eA==) format(\"woff2\")}"
            + "@font-face{src:url(fonts/Absent.woff2) format(\"woff2\"),url(fonts/Absent.ttf) format(\"truetype\")}",
            css);
    }

    [Fact]
    public void AMissingStylesheetIsNullAndTheExportGoesOnWithoutIt()
    {
        // Unstyled formulas beat no file. (Kotlin pins the same two cases.)
        Assert.Null(HtmlExport.EmbeddedKatexCss(Assets()));
        // Not valid UTF-8: treated as missing, as Foundation's String(contentsOf:encoding:) does.
        Assert.Null(HtmlExport.EmbeddedKatexCss(name =>
            string.Equals(name, HtmlExport.KatexCssAsset, StringComparison.Ordinal) ? [0xFF, 0xFE, 0x41] : null));

        var document = HtmlExport.ExportDocument("Formula $a^2$ here.", "T");
        var page = HtmlExport.PreparePage(Capture(document), document, Assets());
        Assert.Equal("<!DOCTYPE html>\n" + Capture(document), page);
        Lacks(page, "SIL Open Font", "no fonts shipped, no font notice");
    }

    [Fact]
    public void AnEmptyFaceFileIsAFileNotAMissingOne()
    {
        // Parity with Foundation's Data(contentsOf:), which reads a zero-byte file happily. Only a
        // null read means "absent"; do not "fix" this into a length check.
        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, "@font-face{src:url(fonts/Empty.woff2) format(\"woff2\")}"),
            ("rich/fonts/Empty.woff2", "")));
        Assert.Equal("@font-face{src:url(data:font/woff2;base64,) format(\"woff2\")}", css);
    }

    [Fact]
    public void OnlyAsciiFaceNamesAreRewritten()
    {
        // The pattern spells its class out: [A-Za-z0-9_-]. A name outside it is not a KaTeX face and
        // must be left alone rather than sent through a lookup no culture agrees on.
        var sheet = "@font-face{src:url(fonts/KaTeX_" + (char)0x0130 + ".woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/" + (char)0xFF21 + "wide.woff2) format(\"woff2\")}"
            + "@font-face{src:url(fonts/Ok-1_2.woff2) format(\"woff2\")}";
        // The non-ASCII faces are READABLE. Without them the test could not tell "the pattern did
        // not match" from "it matched and the file was missing", and stayed green with the class
        // widened to .NET's Unicode-aware [\w-] — see PdfhtmlAdversarialTests for the wider pin.
        var css = HtmlExport.EmbeddedKatexCss(Assets(
            (HtmlExport.KatexCssAsset, sheet),
            ("rich/fonts/KaTeX_" + (char)0x0130 + ".woff2", "z"),
            ("rich/fonts/" + (char)0xFF21 + "wide.woff2", "z"),
            ("rich/fonts/Ok-1_2.woff2", "y")));

        Assert.Equal(1, Count(css!, "url(data:font/woff2;base64,"));
        Has(css!, "url(fonts/KaTeX_" + (char)0x0130 + ".woff2)");
        Has(css!, "url(fonts/" + (char)0xFF21 + "wide.woff2)");
    }

    [Fact]
    public void GeneratedTextIsSplicedNeverReadAsAReplacementTemplate()
    {
        // A stylesheet holding $&, $1 or $$ would be silently mangled by a regex replacement string.
        const string css = "a::after{content:\"$& $1 $$ $` $'\"}";
        var page = HtmlExport.WithEmbeddedKatex("<html><head></head><body></body></html>", css);
        Has(page, "<style>" + css + "</style>");
        Has(page, "<style>" + css + "</style>\n" + HtmlExport.KatexNotice + "\n</head>");
    }

    // MARK: - the whole pipeline

    [Fact]
    public void AMathExportCarriesTheStylesheetAndBothLicenceNotices()
    {
        const string source = "Formula $a^2 + b^2 = c^2$ and\n\n$$\\int_0^1 x\\,dx$$\n\n```mermaid\ngraph TD\nA-->B\n```\n";
        var document = HtmlExport.ExportDocument(source, "math");
        var page = HtmlExport.PreparePage(Capture(document), document, ReadBundledAsset);

        Assert.StartsWith("<!DOCTYPE html>\n", page, StringComparison.Ordinal);
        Has(page, "SIL Open Font", "the OFL notice travels with the faces");
        Has(page, "url(data:font/woff2;base64,");
        Lacks(page, "url(fonts/");
        Lacks(page, "rich/", "no engine, no stylesheet link");
        Lacks(page, "<script");
        Lacks(page, "<link");

        // With both, the head ends: …</style>\n(katex notice)\n(mermaid notice)\n</head>
        Has(page, "</style>\n" + HtmlExport.KatexNotice + "\n" + HtmlExport.MermaidNotice + "\n</head>");
        Assert.Equal(1, Count(page, "</head>"));
        Assert.Equal(1, Count(page, HtmlExport.KatexNotice));
        Assert.Equal(1, Count(page, HtmlExport.MermaidNotice));
    }

    [Fact]
    public void TheInsertionTouchesTheHeadAndNothingElse()
    {
        var document = HtmlExport.ExportDocument("Formula $a^2$ here.", "T");
        var captured = Capture(document);
        var page = HtmlExport.PreparePage(captured, document, ReadBundledAsset);

        // Everything before and after the insertion point is the captured page, byte for byte.
        var at = captured.IndexOf("</head>", StringComparison.Ordinal);
        Assert.True(at > 0);
        Assert.StartsWith("<!DOCTYPE html>\n" + captured[..at], page, StringComparison.Ordinal);
        Assert.EndsWith(captured[at..], page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOverloadReadsTheMathGateOffTheDocumentTheAppLoaded()
    {
        var document = HtmlExport.ExportDocument("Formula $a^2$ here.", "T");
        var captured = Capture(document);
        Assert.Equal(
            HtmlExport.PreparePage(captured, ReadBundledAsset, hasMath: true),
            HtmlExport.PreparePage(captured, document, ReadBundledAsset));

        var plain = HtmlExport.ExportDocument("no maths", "T");
        Assert.Equal(
            HtmlExport.PreparePage(Capture(plain), ReadBundledAsset, hasMath: false),
            HtmlExport.PreparePage(Capture(plain), plain, ReadBundledAsset));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    [InlineData("ar-SA")]
    [InlineData("fa-IR")]
    public void TheExportIsTheSameBytesUnderAnyCulture(string cultureName)
    {
        const string source = "# Title\n\nFormula $a^2$ here.\n\n```mermaid\ngraph TD\nA-->B\n```\n\n\\newpage\n\nAfter.";
        var reader = Assets(
            (HtmlExport.KatexCssAsset, "@font-face{src:url(fonts/KaTeX_Main-Regular.woff2) format(\"woff2\"),url(fonts/KaTeX_Main-Regular.ttf) format(\"truetype\")}.katex{font-size:1.21em}"),
            ("rich/fonts/KaTeX_Main-Regular.woff2", "MZ"));

        var document = HtmlExport.ExportDocument(source, "T");
        var expected = HtmlExport.PreparePage(Capture(document), document, reader);

        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);
            CultureInfo.CurrentUICulture = new CultureInfo(cultureName);

            var underCulture = HtmlExport.ExportDocument(source, "T");
            Assert.Equal(document, underCulture);
            Assert.Equal(expected, HtmlExport.PreparePage(Capture(underCulture), underCulture, reader));
            Assert.True(HtmlExport.NeedsKatex(underCulture));
            Has(expected, "url(data:font/woff2;base64,TVo=)");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
