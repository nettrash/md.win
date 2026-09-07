using Md.Core.Markdown;

namespace Md.Core.Tests;

// The HTML writer, case for case from mdTests.swift (the testHTML…, testFootnote…, testRaw…,
// testCSV…, testGraphvizInkRules… families and the HTML halves of the straddling front-matter
// tests), plus the pins the UTF-16 ports added — md.vscode's html.test.ts / inline.test.ts, which
// spell ICU's regex classes out, and the Kotlin suite's positive halves — and this port's own
// divergence pins (a word guard asked of the whole code point, the measured `\s` set, ICU's `.`).
//
// Every string assertion is ordinal: xUnit's two-argument Assert.Contains(string, string) is
// culture-sensitive and is never used here. Non-ASCII characters are built from their code points
// so that no invisible byte can hide in this file.
public class MarkdownHtmlTests
{
    private static string Doc(string source, bool dark = false, bool export = false) =>
        MarkdownHtml.Document(source, "t", dark, export);

    private static string Body(string source) => MarkdownHtml.Body(source, "t", false).Html;

    private static EngineNeeds Needs(string source) => MarkdownHtml.Body(source, "t", false).Needs;

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static void Has(string html, string needle) => Assert.Contains(needle, html, StringComparison.Ordinal);

    private static void Lacks(string html, string needle) => Assert.DoesNotContain(needle, html, StringComparison.Ordinal);

    private static string Cp(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static readonly string Acute = Cp(0x0301);        // COMBINING ACUTE ACCENT
    private static readonly string Zwj = Cp(0x200D);          // ZERO WIDTH JOINER
    private static readonly string Zwnj = Cp(0x200C);         // ZERO WIDTH NON-JOINER
    private static readonly string Bom = Cp(0xFEFF);          // ZERO WIDTH NO-BREAK SPACE
    private static readonly string Nbsp = Cp(0x00A0);         // NO-BREAK SPACE
    private static readonly string Zwsp = Cp(0x200B);         // ZERO WIDTH SPACE
    private static readonly string Nel = Cp(0x0085);          // NEXT LINE
    private static readonly string LineSeparator = Cp(0x2028);
    private static readonly string SuperTwo = Cp(0x00B2);     // SUPERSCRIPT TWO (No)
    private static readonly string Half = Cp(0x00BD);         // VULGAR FRACTION ONE HALF (No)
    private static readonly string RomanTen = Cp(0x2169);     // ROMAN NUMERAL TEN (Nl)
    private static readonly string ArabicThree = Cp(0x0663);  // ARABIC-INDIC DIGIT THREE (Nd)
    private static readonly string Ef = Cp(0x0444);           // CYRILLIC SMALL LETTER EF
    private static readonly string EAcute = Cp(0x00E9);
    private static readonly string MathBoldA = Cp(0x1D400);   // MATHEMATICAL BOLD CAPITAL A (astral, L)
    private static readonly string CircledA = Cp(0x24B6);     // CIRCLED LATIN CAPITAL LETTER A (So, Other_Alphabetic)
    private static readonly string TokenOpen = Cp(0xE000);
    private static readonly string TokenClose = Cp(0xE001);

    // MARK: - The document skeleton

    [Fact]
    public void HtmlWrapsDocument()
    {
        var html = MarkdownHtml.Document("# Title", "Doc", dark: false);
        Assert.StartsWith("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n", html, StringComparison.Ordinal);
        Has(html, "<title>Doc</title>");
        Has(html, "<h1 id=\"title\">Title</h1>");
        Has(html, "<body data-md-dark=\"0\">");
        Has(html, "<script type=\"module\" src=\"rich/md-init.js\"></script>\n</body>\n</html>");
        Assert.EndsWith("</html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlEscapesTheTitleAndNeverSniffsIt()
    {
        // Always the caller's string: nothing from an H1 or from front matter.
        Has(MarkdownHtml.Document("# Real Heading", "a & <b>", false), "<title>a &amp; &lt;b&gt;</title>");
        Lacks(MarkdownHtml.Document("---\ntitle: Front\n---\n\n# Real", "given", false), "<title>Front");
    }

    [Fact]
    public void HtmlGluesTheHeadIncludesOntoStyleAndEndsAtHtml()
    {
        // An empty head yields `</style>\n</head>`; a math document reads `…</style><link …>` on
        // one line; there is no trailing newline.
        var plain = Doc("hi");
        Has(plain, "</style>\n</head>");
        var maths = Doc("$a+b$");
        Has(maths, "</style><link rel=\"stylesheet\" href=\"rich/katex.min.css\">\n<script defer src=\"rich/katex.min.js\"></script>\n<script defer src=\"rich/mhchem.min.js\"></script>\n</head>");
        Assert.EndsWith("</html>", maths, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlLeavesABlankLineWhereAnEmptySourcesBodyWouldBe()
    {
        Has(Doc(""), "<body data-md-dark=\"0\">\n\n<script type=\"module\"");
        Assert.Equal("", Body(""));
    }

    [Fact]
    public void HtmlEscapesSpecialCharacters()
    {
        Has(Doc("a < b & c > d"), "a &lt; b &amp; c &gt; d");
        // No raw-HTML passthrough and no autolinks anywhere: both render as their own text.
        Assert.Equal("<p>&lt;b&gt;hi&lt;/b&gt;</p>", Body("<b>hi</b>"));
        Assert.Equal("<p>An autolink: &lt;https://nettrash.me&gt;</p>", Body("An autolink: <https://nettrash.me>"));
    }

    [Fact]
    public void EscapeIsExactlyFourCharactersAmpersandFirst()
    {
        Assert.Equal("&amp;&lt;&gt;&quot;", MarkdownHtml.Escape("&<>\""));
        Assert.Equal("a &amp; b", MarkdownHtml.Escape("a & b"));
        Assert.Equal("it's", MarkdownHtml.Escape("it's"));
        // Astral characters pass through whole.
        Assert.Equal(MathBoldA + " & " + Cp(0x1F600), MarkdownHtml.Escape(MathBoldA + " & " + Cp(0x1F600)).Replace("&amp;", "&", StringComparison.Ordinal));
        // The whole XSS story: the URL class stops at the first `)`, and `"` is escaped, so a URL
        // cannot break out of its href. The stray paren is left as text.
        Assert.Equal("<a href=\"&quot;onerror=alert(1\">a</a>)", MarkdownHtml.Inline("[a](\"onerror=alert(1))"));
    }

    [Fact]
    public void HtmlOmitsAuthorNotes()
    {
        var html = Doc("visible\n\n<!-- note: secret draft thought -->");
        Has(html, "visible");
        Lacks(html, "secret draft thought");
    }

    [Fact]
    public void HtmlPlainDocumentStaysLight()
    {
        var html = Doc("# Just text\n\nA paragraph.");
        Lacks(html, "katex.min.js");
        Lacks(html, "mermaid.min.js");
        Lacks(html, "viz-global.js");
        Lacks(html, "highlight.min.js");
        Has(html, "rich/md-init.js");
    }

    [Fact]
    public void HtmlThemeVariantsDiffer()
    {
        var light = Doc("hi");
        var dark = Doc("hi", dark: true);
        Assert.NotEqual(light, dark);
        Has(dark, "color-scheme: dark");
        // Backgrounds must be forced to print so the theme survives to PDF.
        Has(dark, "print-color-adjust: exact");
        Has(dark, "background: #241E18");
        Has(dark, "data-md-dark=\"1\"");
    }

    [Fact]
    public void ExportPageIsPlainWhiteAndAlwaysLight()
    {
        var export = Doc("hello", dark: true, export: true);
        Has(export, "background: #FFFFFF");
        Has(export, "color-scheme: light");
        Lacks(export, "#241E18");
        // The rich renderers (Mermaid's theme, PlantUML's flag) see the light mode too.
        Has(export, "data-md-dark=\"0\"");
        var preview = Doc("hello", dark: true);
        Has(preview, "background: #241E18");
        Has(preview, "data-md-dark=\"1\"");
    }

    [Fact]
    public void HtmlPageBreakMarkerAndExportCss()
    {
        var preview = Doc("a\n\n\\newpage\n\nb");
        Has(preview, "md-pagebreak");
        Lacks(preview, "break-after: page");
        Has(preview, ".md-pagebreak { border-top: 2px dashed rgba(43,38,32,0.16); margin: 1.6em 0; }");
        var export = Doc("a\n\n\\newpage\n\nb", export: true);
        Has(export, ".md-pagebreak { height: 0; margin: 0; break-after: page; }");
    }

    [Fact]
    public void HtmlExportKeepsPageBreaksVisible()
    {
        // The rule collapses; the marker stays, so the HTML file export can swap the rule back.
        var export = Doc("a\n\n\\newpage\n\nb", export: true);
        Has(export, "break-after: page");
        Has(export, "<div class=\"md-pagebreak\"></div>");
    }

    [Fact]
    public void YamlFrontMatterIsHiddenFromTheHtml()
    {
        // The HTML half of testYAMLFrontMatterIsParsedAndHidden: the opening `---` must not
        // survive as a rule and the metadata must not survive as prose.
        var html = Doc(Lines("---", "title: My Book", "author: nettrash", "date: 2026-07-24", "---", "", "# Chapter One", "", "Text."));
        Lacks(html, "My Book");
        Lacks(html, "<hr>");
        Has(html, "Chapter One");
    }

    [Fact]
    public void FrontMatterDoesNotStealTheFirstHeadingsAnchor()
    {
        // The HTML half of testFrontMatterDoesNotLeakIntoTheOutlineOrNotes: the closing `---`
        // underlines the last metadata line, and a scanner reading it as a setext heading would
        // push every real anchor out of step with the outline.
        Has(Doc("---\ntitle: My Post\n---\n\n# Hello\n"), "id=\"hello\"");
        Has(Doc("---\nHello: x\n---\n\n# Hello: x\n"), "id=\"hello-x\"");
    }

    [Fact]
    public void HtmlHeadingsCarryAnchorIds()
    {
        var html = Doc("# My Title\n\n# My Title");
        Has(html, "<h1 id=\"my-title\">");
        Has(html, "<h1 id=\"my-title-1\">");
        // Heading text goes through the inline pass; the slug is taken from the raw text.
        Assert.Equal("<h1 id=\"50-off\">50% <strong>off</strong></h1>", Body("# 50% **off**"));
        // A quoted heading gets no id and consumes no counter.
        Assert.Equal(Lines("<blockquote>", "<h2>Same</h2>", "</blockquote>", "<h2 id=\"same\">Same</h2>"), Body("> ## Same\n\n## Same"));
    }

    // MARK: - Inline spans

    [Fact]
    public void HtmlInlineEmphasis()
    {
        var html = Doc("**bold** and *italic* and ~~gone~~");
        Has(html, "<strong>bold</strong>");
        Has(html, "<em>italic</em>");
        Has(html, "<del>gone</del>");
        Assert.Equal("<strong>bold</strong> and <em>italic</em>", MarkdownHtml.Inline("__bold__ and _italic_"));
        // Bold before italic, so `***` nests; "no delimiter inside" content classes.
        Assert.Equal("<em><strong>x</strong></em>", MarkdownHtml.Inline("***x***"));
        Lacks(MarkdownHtml.Inline("**a*b**"), "<strong>");
        // Every pattern is a replace-all.
        Assert.Equal("<em>a</em> and <em>b</em> and <em>c</em>", MarkdownHtml.Inline("*a* and *b* and *c*"));
        Assert.Equal("<code>x</code> and <code>y</code>", MarkdownHtml.Inline("`x` and `y`"));
    }

    [Fact]
    public void HtmlCodeSpanIsEscapedAndNotReinterpreted()
    {
        var html = Doc("`a < *b* > c`");
        Has(html, "<code>a &lt; *b* &gt; c</code>");
        Lacks(html, "<em>b</em>");
    }

    [Fact]
    public void HtmlCodeSpanDollarIsNotMath()
    {
        Has(Doc("use `$x$` here"), "<code>$x$</code>");
        Assert.Equal("use <code>$x$</code> here", MarkdownHtml.Inline("use `$x$` here"));
    }

    [Fact]
    public void HtmlCodeSpanIsRestoredLiterallyDollarsAndAll()
    {
        // Restore is a literal replace; a regex replace would read `$$` as a template.
        Assert.Equal("<code>$$</code>", MarkdownHtml.Inline("`$$`"));
        Assert.Equal("<code>a$&amp;b</code>", MarkdownHtml.Inline("`a$&b`"));
        Assert.Equal("<code>$1 $2</code>", MarkdownHtml.Inline("`$1 $2`"));
    }

    [Fact]
    public void HtmlLink()
    {
        Has(Doc("[site](https://nettrash.me)"), "<a href=\"https://nettrash.me\">site</a>");
    }

    [Fact]
    public void HtmlLinkWithTitle()
    {
        Has(Doc("[site](https://nettrash.me \"Hover title\")"), "<a href=\"https://nettrash.me\" title=\"Hover title\">site</a>");
    }

    [Fact]
    public void HtmlImage()
    {
        // A void tag, not self-closed.
        Has(Doc("![Alt text](https://nettrash.me/favicon.ico)"), "<img src=\"https://nettrash.me/favicon.ico\" alt=\"Alt text\">");
    }

    [Fact]
    public void HtmlImageWithTitle()
    {
        // Attribute order is src, alt, title.
        Has(Doc("![Alt](https://nettrash.me/favicon.ico \"The favicon\")"),
            "<img src=\"https://nettrash.me/favicon.ico\" alt=\"Alt\" title=\"The favicon\">");
    }

    [Fact]
    public void HtmlLinkedImage()
    {
        // The image pass runs before the link pass, so the <img> nests inside the <a>.
        Has(Doc("[![badge](https://nettrash.me/favicon.ico)](https://nettrash.me)"),
            "<a href=\"https://nettrash.me\"><img src=\"https://nettrash.me/favicon.ico\" alt=\"badge\"></a>");
    }

    [Fact]
    public void HtmlTitledFormsRunBeforeUntitledAndAltMayBeEmpty()
    {
        // Otherwise the untitled pattern stops at the first `)` and the title leaks as text.
        Lacks(MarkdownHtml.Inline("[a](u \"t\")"), "&quot;t&quot;)");
        Assert.Equal("<img src=\"x.png\" alt=\"\">", MarkdownHtml.Inline("![](x.png)"));
        // A link label may not be empty.
        Assert.Equal("[](x)", MarkdownHtml.Inline("[](x)"));
    }

    [Fact]
    public void HtmlUnderscoreInWordIsNotItalic()
    {
        Lacks(Doc("call some_long_name now"), "<em>");
        Lacks(MarkdownHtml.Inline("a_b_c is one word"), "<em>");
    }

    [Fact]
    public void HtmlInlineMathIsNotMangledByEmphasis()
    {
        var html = Doc("total $a*b*c$ units");
        Has(html, "class=\"md-mathi\"");
        Has(html, "a*b*c");
        Lacks(html, "<em>");
        Has(html, "katex.min.js");
        Assert.Equal("total <span class=\"md-mathi\">a*b*c</span> units", MarkdownHtml.Inline("total $a*b*c$ units"));
    }

    [Fact]
    public void HtmlDisplayMathSpanPreserved()
    {
        var html = Doc("$$x^2 + y^2$$");
        Has(html, "class=\"md-mathd\"");
        Has(html, "x^2 + y^2");
        Assert.Equal("<span class=\"md-mathd\">x^2 + y^2</span>", MarkdownHtml.Inline("$$x^2 + y^2$$"));
        Assert.Equal("<span class=\"md-mathd\">x^2</span>", MarkdownHtml.Inline("\\[x^2\\]"));
        Assert.Equal("A <span class=\"md-mathi\">a_i</span> here.", MarkdownHtml.Inline("A \\(a_i\\) here."));
        // The LaTeX is escaped for HTML; KaTeX reads the decoded textContent.
        Assert.Equal("<span class=\"md-mathi\">a &lt; b</span>", MarkdownHtml.Inline("$a < b$"));
        // The bracket form keeps its inner spaces (golden math.html).
        Assert.Equal("<span class=\"md-mathd\"> \\sum_{k=1}^{n} k </span>", MarkdownHtml.Inline("\\[ \\sum_{k=1}^{n} k \\]"));
    }

    [Fact]
    public void HtmlCurrencyDollarsAreNotMath()
    {
        var html = Doc("it costs $5 and $10 today");
        Has(html, "$5 and $10");
        Lacks(html, "class=\"md-mathi\"");
        Lacks(html, "katex.min.js");
        Assert.Equal("it costs $5 and $10 today", MarkdownHtml.Inline("it costs $5 and $10 today"));
        // A dollar glued to a word character on either side, or a newline inside, is prose.
        foreach (var prose in new[] { "a$x$b", "5$x$", "$x$b", Acute + "$x$", "$x\ny$", "a $$ b" })
        {
            Lacks(MarkdownHtml.Inline(prose), "md-math");
        }
        Assert.Equal("total <span class=\"md-mathi\">a+b</span> units", MarkdownHtml.Inline("total $a+b$ units"));
        // A failed close is retried as an opener, exactly as the regex would.
        Assert.Equal("$a$b <span class=\"md-mathi\">c</span>", MarkdownHtml.Inline("$a$b $c$"));
    }

    [Fact]
    public void UnicodeSpacingAndWordBoundariesAgreeAcrossPlatforms()
    {
        // `\w` means Unicode here, through ICU: a Cyrillic letter is a word character, so an
        // underscore between letters is not emphasis and a dollar between letters is not a formula.
        var cyrillic = Doc(Ef + "_em_" + Ef + " and " + Ef + "$x$" + Ef);
        Lacks(cyrillic, "<em>");
        Lacks(cyrillic, "<span class=\"md-mathi\">");
        // …and the other half (Kotlin's `underscoreItalicGuardIsUnicodeAware`): the patterns must
        // still MATCH, or a silently broken guard passes every negative assertion above.
        var working = Doc("an _em_ word and $x$ alone");
        Has(working, "<em>em</em>");
        Has(working, "<span class=\"md-mathi\">x</span>");
    }

    [Fact]
    public void IcuWordClassIsSpelledOut()
    {
        // ICU `\w` = Alphabetic + M + Nd + Pc + ZWNJ/ZWJ. A non-ASCII digit, a number-letter, a
        // combining mark and the joiners are word characters…  Every row below was measured
        // against NSRegularExpression's own `(?<![\w$])\$([^$\n]+?)\$(?![\w$])` (2026-09-06):
        // Nd, Nl, Mn, ZWJ, ZWNJ, an enclosed Latin letter and an astral letter block the math;
        // No (`²`, `½`) and an emoji do not.
        Lacks(MarkdownHtml.Inline(ArabicThree + "$x$"), "md-mathi");
        Lacks(MarkdownHtml.Inline(RomanTen + "$x$"), "md-mathi");
        foreach (var joiner in new[] { Acute, Zwj, Zwnj })
        {
            Lacks(MarkdownHtml.Inline("e" + joiner + "$x$"), "md-mathi");
            Lacks(MarkdownHtml.Inline("e" + joiner + "_x_ y"), "<em>");
        }
        // …while `²` and `½` (No) are not — so `²$x$` IS a formula here, where both LaTeX
        // writers (`[\p{L}\p{N}_]`) and Kotlin's HTML (`[\p{L}\p{N}_$]`) read it as prose.
        // Recorded, not resolved (port spec OQ-3); this port follows the Swift HTML writer, whose
        // bytes the golden corpus was captured from.
        Has(MarkdownHtml.Inline(SuperTwo + "$x$"), "md-mathi");
        Has(MarkdownHtml.Inline(Half + "$x$"), "md-mathi");
        // NFD `é`: the mark before the `$` is a word character, so this is prose on Apple and
        // a formula through `[\p{L}\p{N}_]` — the other half of OQ-3.
        Lacks(MarkdownHtml.Inline("e" + Acute + "$x$"), "md-mathi");
        Lacks(MarkdownHtml.Inline(EAcute + "$x$"), "md-mathi");
    }

    [Fact]
    public void WordGuardIsAskedOfTheWholeCodePoint()
    {
        // A .NET regex lookbehind would see a low surrogate before the `$` — not a word
        // character — and typeset `𝐀$x$`; ICU and TypeScript (`u` flag) see an astral letter and
        // leave it as prose. The guards are hand-rolled over Runes for exactly this.
        Lacks(MarkdownHtml.Inline(MathBoldA + "$x$"), "md-mathi");
        Lacks(MarkdownHtml.Inline("$x$" + MathBoldA), "md-mathi");
        Lacks(MarkdownHtml.Inline(MathBoldA + "_x_" + MathBoldA), "<em>");
        // `Other_Alphabetic` outside L/M/Nl: the circled Latin letters (So) are Alphabetic to ICU.
        Lacks(MarkdownHtml.Inline(CircledA + "$x$"), "md-mathi");
        // An emoji is neither: `😀$x$` is a formula on every platform.
        Has(MarkdownHtml.Inline(Cp(0x1F600) + "$x$"), "md-mathi");
    }

    [Fact]
    public void IcuSpaceClassIsTheMeasuredSet()
    {
        // ICU `\s` = [\t\n\v\f\r U+0085 \p{Z}]: U+FEFF and U+200B are allowed inside a URL, NBSP,
        // NEL and VT end it. JavaScript's `\s` takes U+FEFF and .NET's default would need trusting.
        // Measured against NSRegularExpression itself, `^[^)\s]+$` over `x<c>y` (2026-09-06): TAB,
        // NEL, VT, FF and NBSP end the URL; ZWSP and BOM do not. The port spells the class out.
        Assert.Equal("<a href=\"x" + Bom + "y\">a</a>", MarkdownHtml.Inline("[a](x" + Bom + "y)"));
        Assert.Equal("<a href=\"x" + Zwsp + "y\">a</a>", MarkdownHtml.Inline("[a](x" + Zwsp + "y)"));
        Lacks(MarkdownHtml.Inline("[a](x" + Nbsp + "y)"), "<a href");
        Lacks(MarkdownHtml.Inline("[a](x" + Nel + "y)"), "<a href");
        Lacks(MarkdownHtml.Inline("[a](x\vy)"), "<a href");
        // The title separator is the same class: NEL separates a URL from its title.
        Assert.Equal("<a href=\"u\" title=\"t\">a</a>", MarkdownHtml.Inline("[a](u" + Nel + "&quot;t&quot;)".Replace("&quot;", "\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void IcuDotExcludesEveryLineTerminatorInATitle()
    {
        // The title's `(.*?)` compiles without dotall; ICU's `.` stops at all seven terminators,
        // not only `\n` as .NET's would. A title holding NEL, LS or VT is therefore no title — and
        // with the space in it the untitled pattern cannot match either, so nothing links.
        // Measured with `^x.y$` over NSRegularExpression (2026-09-06): none of LF, CR, VT, FF, NEL
        // or LS is crossed. JavaScript's `.` crosses VT, FF and NEL — TypeScript links those.
        foreach (var terminator in new[] { Nel, LineSeparator, "\v", "\f" })
        {
            Lacks(MarkdownHtml.Inline("[a](u \"x" + terminator + "y\")"), "<a href");
            Lacks(MarkdownHtml.Inline("![a](u \"x" + terminator + "y\")"), "<img");
        }
        Assert.Equal("<a href=\"u\" title=\"x y\">a</a>", MarkdownHtml.Inline("[a](u \"x y\")"));
    }

    [Fact]
    public void DelimiterRunsResolveTheWayTheRegexesWould()
    {
        // The two hand-rolled scanners must agree with the patterns they stand in for, including
        // where a delimiter closes one span and opens the next, and where a guard fails and the
        // scan resumes one unit on.
        Assert.Equal("<code>a</code><code>b</code>", MarkdownHtml.Inline("`a``b`"));
        Assert.Equal("`<code>a</code>`", MarkdownHtml.Inline("``a``"));
        Assert.Equal("$a$$b$", MarkdownHtml.Inline("$a$$b$"));
        Assert.Equal("<span class=\"md-mathd\">a</span>b<span class=\"md-mathd\">c</span>", MarkdownHtml.Inline("$$a$$b$$c$$"));
        Assert.Equal("<span class=\"md-mathi\">a</span><span class=\"md-mathi\">b</span>", MarkdownHtml.Inline("\\(a\\)\\(b\\)"));
        Assert.Equal("a<span class=\"md-mathd\">b</span>c", MarkdownHtml.Inline("a\\[b\\]c"));
        Assert.Equal("<em>a</em> <em>b</em> <strong>c</strong> <strong>d</strong> <del>e</del> <code>f</code> <span class=\"md-mathi\">g</span>",
            MarkdownHtml.Inline("*a* _b_ **c** __d__ ~~e~~ `f` $g$"));
        // A footnote id may be a single `_` or `-`; an empty alt with a blank URL is not an image.
        Assert.Equal("<sup class=\"md-fnref\" data-fn=\"_\"></sup>", MarkdownHtml.Inline("[^_]"));
        Assert.Equal("![](  )", MarkdownHtml.Inline("![](  )"));
    }

    [Fact]
    public void AnEntitySpelledOutInTheSourceCannotForgeAnAttribute()
    {
        // Phase 2 escapes the author's own `&`, so the `&quot;` the title patterns look for can
        // only come from a real `"` — a hand-written entity is inert text and the link is not one.
        Assert.Equal("&amp;amp;", MarkdownHtml.Inline("&amp;"));
        Assert.Equal("[a](u &amp;quot;t&amp;quot;)", MarkdownHtml.Inline("[a](u &quot;t&quot;)"));
        // Angle brackets in a URL are escaped where they land, inside the quoted href.
        Assert.Equal("<a href=\"&lt;u&gt;\">a</a>", MarkdownHtml.Inline("[a](<u>)"));
    }

    [Fact]
    public void RestoreLeaksTheTokenForACodeSpanInsideDisplayMath()
    {
        // KNOWN LATENT BUG, REPRODUCED DELIBERATELY: the enclosed code token is restored before its
        // enclosing math is back in the string, so U+E000 0 U+E001 survive (OQ-6; every port).
        Assert.Equal("<span class=\"md-mathd\"> " + TokenOpen + "0" + TokenClose + " </span>", MarkdownHtml.Inline("$$ `x` $$"));
        // And the indices are assigned on a BACK-TO-FRONT walk, so the last match of a pass takes
        // the lowest index: Kotlin walks forward and would leak `0` then `1` here.
        Assert.Equal("<span class=\"md-mathd\"> " + TokenOpen + "1" + TokenClose + " " + TokenOpen + "0" + TokenClose + " </span>",
            MarkdownHtml.Inline("$$ `a` `b` $$"));
    }

    [Fact]
    public void InlineConvertsSoftBreaksOnlyWhenTheParagraphAsks()
    {
        Assert.Equal("a\nb", MarkdownHtml.Inline("a\nb"));
        Assert.Equal("a<br>\nb", MarkdownHtml.Inline("a\nb", softBreaks: true));
        Assert.Equal("<p>one<br>\ntwo</p>", Body("one\ntwo"));
        // Before restore, so a multi-line display formula keeps its own newlines.
        Assert.Equal("<p><span class=\"md-mathd\">a\nb</span></p>", Body("$$a\nb$$"));
        // A hard break (two trailing spaces) keeps its spaces before the `<br>`.
        Assert.Equal("<p>x  <br>\ny</p>", Body("x  \ny"));
    }

    // MARK: - Blocks

    [Fact]
    public void RendersAFlatListNeverAUlOrAnOl()
    {
        var html = Body("- a\n  - b");
        Assert.Equal(
            "<div class=\"md-list\">"
            + "<div class=\"md-item\" style=\"padding-left:0.00em\"><span class=\"md-marker\">&bull;</span><span>a</span></div>"
            + "<div class=\"md-item\" style=\"padding-left:1.60em\"><span class=\"md-marker\">&bull;</span><span>b</span></div>"
            + "</div>", html);
        Lacks(html, "<ul>");
        Lacks(html, "<ol>");
        // Two decimals, `.` separator, whatever the machine's culture.
        Has(Body("- a\n    - b\n      - c"), "padding-left:3.20em");
    }

    [Fact]
    public void ListMarkersTaskBeatsOrdinalAndOrdinalBeatsBullet()
    {
        var html = Body("1. [x] done\n2. plain");
        Has(html, "<div class=\"md-item done\" style=\"padding-left:0.00em\"><span class=\"md-marker\">&#9745;</span>");
        Has(html, "<span class=\"md-marker\">2.</span>");
        Has(Body("- [ ] open"), "<div class=\"md-item\" style=\"padding-left:0.00em\"><span class=\"md-marker\">&#9744;</span>");
        // An unordered item inside an ordered run falls back to a bullet; source numbers are kept.
        Has(Body("1. one\n- two"), "<span class=\"md-marker\">&bull;</span><span>two</span>");
        Has(Body("7. seven\n9. nine"), "<span class=\"md-marker\">9.</span>");
    }

    [Fact]
    public void HtmlTableAlignmentsAndCells()
    {
        var html = Doc("| A | B |\n|:-:|--:|\n| 1 | 2 |");
        Has(html, "text-align:center");
        Has(html, "text-align:right");
        Has(html, "<td");
        Assert.Equal(
            "<table><thead><tr><th style=\"text-align:center\">A</th><th style=\"text-align:right\">B</th></tr></thead>"
            + "<tbody><tr><td style=\"text-align:center\">1</td><td style=\"text-align:right\">2</td></tr></tbody></table>",
            Body("| A | B |\n|:-:|--:|\n| 1 | 2 |"));
    }

    [Fact]
    public void TableEmitsTbodyWithNoRowsAndDefaultsAStrayColumnToLeft()
    {
        Has(Body("| A |\n| --- |"), "<tbody></tbody>");
        var table = new MarkdownBlock.Table(["A"], [], [new[] { "1" }]);
        Assert.Equal("<table><thead><tr><th style=\"text-align:left\">A</th></tr></thead><tbody><tr><td style=\"text-align:left\">1</td></tr></tbody></table>",
            MarkdownHtml.RenderBlock(table));
        // A ragged row renders only its own cells — nothing is padded and nothing is dropped.
        var ragged = new MarkdownBlock.Table(["A", "B"], [ColumnAlignment.Trailing, ColumnAlignment.Center],
            [new[] { "1" }, new[] { "2", "3", "4" }]);
        Assert.Equal(
            "<table><thead><tr><th style=\"text-align:right\">A</th><th style=\"text-align:center\">B</th></tr></thead>"
            + "<tbody><tr><td style=\"text-align:right\">1</td></tr>"
            + "<tr><td style=\"text-align:right\">2</td><td style=\"text-align:center\">3</td><td style=\"text-align:left\">4</td></tr></tbody></table>",
            MarkdownHtml.RenderBlock(ragged));
        // Every cell goes through the inline pass.
        Has(Body("| `a` |\n| --- |\n| *b* |"), "<th style=\"text-align:left\"><code>a</code></th>");
        Has(Body("| `a` |\n| --- |\n| *b* |"), "<td style=\"text-align:left\"><em>b</em></td>");
    }

    [Fact]
    public void RendersTheSimpleKinds()
    {
        Assert.Equal("<hr>", Body("---"));
        Assert.Equal("<div class=\"md-pagebreak\"></div>", Body("\\newpage"));
        Assert.Equal("<blockquote>\n<p>quoted</p>\n</blockquote>", Body("> quoted"));
        Assert.Equal(Lines("<blockquote>", "<p>a</p>", "<blockquote>", "<p>b</p>", "</blockquote>", "</blockquote>"), Body("> a\n>> b"));
        Assert.Equal("<h3>Inside</h3>", MarkdownHtml.RenderBlock(new MarkdownBlock.Heading(3, "Inside")));
    }

    [Fact]
    public void NotesFrontMatterAndFootnoteDefinitionsRenderAsNothing()
    {
        foreach (var block in MarkdownParser.Parse("<!-- note: n -->"))
        {
            Assert.Equal("", MarkdownHtml.RenderBlock(block));
        }
        Assert.Equal("", MarkdownHtml.RenderBlock(new MarkdownBlock.FootnoteDefinition("a", "text")));
        Assert.Equal("", MarkdownHtml.RenderBlock(new MarkdownBlock.FrontMatter([new MetadataField("k", "v")])));
        // Empty blocks still take part in the join and leave blank lines. Observable bytes.
        Assert.Equal("\n<p>Body.</p>", Body(Lines("---", "title: T", "---", "", "Body.")));
        Assert.Equal("<p>before</p>\n\n\n<p>after</p>", Body(Lines("before", "", "<!-- note: one -->", "", "<!-- note: two -->", "", "after")));
        Has(Body(Lines("Text[^a].", "", "[^a]: The note.")), "</p>\n\n<section class=\"md-footnotes\">");
    }

    [Fact]
    public void RenderBlocksJoinsWithASingleNewline()
    {
        var blocks = MarkdownParser.Parse("a\n\n---\n\nb");
        Assert.Equal("<p>a</p>\n<hr>\n<p>b</p>", MarkdownHtml.RenderBlocks(blocks));
        Assert.Equal("", MarkdownHtml.RenderBlocks([]));
    }

    // MARK: - Rich containers

    [Fact]
    public void HtmlMermaidBlockEmitsContainer()
    {
        var html = Doc("```mermaid\ngraph TD\nA-->B\n```");
        Has(html, "<pre class=\"mermaid\">");
        Has(html, "graph TD");
        Lacks(html, "<pre><code>graph TD");
        Has(html, "mermaid.min.js");
        Lacks(html, "katex.min.js");
        Lacks(html, "viz-global.js");
        Assert.Equal("<pre class=\"mermaid\">graph TD\nA--&gt;B</pre>", Body("```mermaid\ngraph TD\nA-->B\n```"));
    }

    [Fact]
    public void HtmlPlantumlBlockEmitsContainer()
    {
        var html = Doc("```plantuml\n@startuml\nA->B\n@enduml\n```");
        Has(html, "<div class=\"plantuml\">");
        Has(html, "@startuml");
        Has(html, "viz-global.js");
        foreach (var language in new[] { "plantuml", "puml", "plant-uml" })
        {
            Assert.Equal("<div class=\"plantuml\">@startuml\n@enduml</div>", Body("```" + language + "\n@startuml\n@enduml\n```"));
        }
    }

    [Fact]
    public void HtmlGraphvizBlockEmitsContainer()
    {
        var html = Doc("```dot\ndigraph { a -> b }\n```");
        Has(html, "<div class=\"graphviz\" data-engine=\"dot\">");
        Has(html, "digraph { a -&gt; b }");
        Lacks(html, "<pre><code>digraph");
        Has(html, "viz-global.js");
        Lacks(html, "mermaid.min.js");
        Lacks(html, "katex.min.js");
    }

    [Fact]
    public void HtmlGraphvizAliasesAndLayoutEngines()
    {
        foreach (var alias in new[] { "graphviz", "gv" })
        {
            Has(Doc("```" + alias + "\ngraph { a -- b }\n```"), "data-engine=\"dot\"");
        }
        foreach (var engine in new[] { "neato", "circo", "fdp", "sfdp", "twopi", "osage", "patchwork" })
        {
            Has(Doc("```" + engine + "\ngraph { a -- b }\n```"), "data-engine=\"" + engine + "\"");
        }
        // Ten keys, eight engines, and the mapped *value* reaches the attribute.
        Assert.Equal(10, MarkdownHtml.GraphvizEngines.Count);
        Assert.Equal(8, MarkdownHtml.GraphvizEngines.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var (word, engine) in MarkdownHtml.GraphvizEngines)
        {
            Assert.Equal("<div class=\"graphviz\" data-engine=\"" + engine + "\">digraph { a -&gt; b }</div>", Body("```" + word + "\ndigraph { a -> b }\n```"));
        }
        // An unrelated language is a code block, never a diagram.
        var swift = Doc("```swift\nlet x = 1\n```");
        Has(swift, "<pre><code class=\"language-swift\">let x = 1");
        Lacks(swift, "class=\"graphviz\"");
        Lacks(swift, "viz-global.js");
    }

    [Fact]
    public void HtmlGraphvizEscapesAngleBrackets()
    {
        var html = Doc("```dot\ndigraph { n [label=<<b>hi</b>>] }\n```");
        Has(html, "&lt;&lt;b&gt;hi&lt;/b&gt;&gt;");
        Lacks(html, "<b>hi</b>");
    }

    [Fact]
    public void HtmlMathFenceEmitsDisplayMath()
    {
        var html = Doc("```math\n\\int_0^1 x\\,dx\n```");
        Has(html, "class=\"md-mathd\"");
        Has(html, "\\int_0^1");
        Has(html, "katex.min.js");
        // A fence is a `<div>`; `$$…$$` in a paragraph is a `<span>`.
        foreach (var language in new[] { "math", "latex", "tex" })
        {
            Assert.Equal("<div class=\"md-mathd\">\\int_0^1 x\\,dx</div>", Body("```" + language + "\n\\int_0^1 x\\,dx\n```"));
        }
    }

    [Fact]
    public void HtmlCodeLanguageEmitsHighlightClassAndLoadsEngine()
    {
        var html = Doc("```Swift\nlet x = 1\n```");
        Has(html, "<pre><code class=\"language-swift\">");
        Has(html, "<script defer src=\"rich/highlight.min.js\"></script>");
        Assert.Equal("<pre><code class=\"language-swift\">let x = 1</code></pre>", Body("```Swift\nlet x = 1\n```"));
        // The class is the escaped info word; the content is escaped too, with no trailing newline.
        Assert.Equal("<pre><code class=\"language-json\">{ &quot;tilde&quot;: &quot;fence&quot; }</code></pre>", Body("~~~json\n{ \"tilde\": \"fence\" }\n~~~"));
    }

    [Fact]
    public void HtmlBareFenceAndSpecialFencesAreNotHighlighted()
    {
        var bare = Doc("```\nplain text\n```");
        Has(bare, "<pre><code>plain text");
        Lacks(bare, "language-");
        Lacks(bare, "highlight.min.js");
        Assert.Equal("<pre><code>plain text</code></pre>", Body("```\nplain text\n```"));
        var mermaid = Doc("```mermaid\ngraph TD\nA-->B\n```");
        Lacks(mermaid, "highlight.min.js");
        Lacks(mermaid, "language-mermaid");
        Lacks(Doc("```dot\ndigraph{a->b}\n```"), "highlight.min.js");
        Lacks(Doc("```math\n\\int_0^1 x\\,dx\n```"), "highlight.min.js");
        Lacks(Doc("```csv\na,b\n1,2\n```"), "highlight.min.js");
        var plot = Doc("```plot\nx: -10..10\nsin(x)\n```");
        Lacks(plot, "highlight.min.js");
        Lacks(plot, "language-plot");
        Has(plot, "<div class=\"plot\"><svg");
    }

    [Fact]
    public void HtmlDiagramMathAndDataFencesAreNotHighlighted()
    {
        // Kotlin's enumeration: the languages with their own handling are never tagged for hljs.
        foreach (var fence in new[]
        {
            "```mermaid\ngraph TD\nA-->B\n```",
            "```dot\ndigraph { a -> b }\n```",
            "```plantuml\n@startuml\nA->B\n@enduml\n```",
            "```math\n\\int_0^1 x\\,dx\n```",
            "```csv\nName,Role\nAnn,Editor\n```",
            "```plot\nx: -10..10\nsin(x)\n```",
        })
        {
            var html = Doc(fence);
            Lacks(html, "class=\"language-");
            Lacks(html, "highlight.min.js");
        }
    }

    [Fact]
    public void HtmlPlainDocumentDoesNotLoadHighlightEngine()
    {
        var html = Doc("# Title\n\nJust prose, `inline code` aside.");
        Lacks(html, "highlight.min.js");
        Lacks(html, "language-");
    }

    [Fact]
    public void InfoWordIsFoldedWithTheInvariantCulture()
    {
        Has(Body("```MERMAID\ngraph TD\n```"), "<pre class=\"mermaid\">");
        Has(Body("```DOT\ndigraph {}\n```"), "data-engine=\"dot\"");
        Has(Body("```PLOT\nsin(x)\n```"), "<div class=\"plot\"><svg ");
        Has(Body("```LATEX\nx\n```"), "<div class=\"md-mathd\">x</div>");
        Has(Body("```TSV\na\tb\n1\t2\n```"), "<td style=\"text-align:right\">2</td>");
    }

    [Fact]
    public void PlotFenceHoldsAFinishedSvgOrItsOwnSourceNeverAHole()
    {
        var html = Body("```plot\nx: -1..1\nsin(x)\n```");
        Assert.StartsWith("<div class=\"plot\"><svg ", html, StringComparison.Ordinal);
        Assert.EndsWith("</svg></div>", html, StringComparison.Ordinal);
        Has(html, "viewBox=\"0 0 600 400\"");
        Lacks(html, "<pre><code>");
        // A broken plot stays readable as its source inside the container.
        Assert.Equal("<div class=\"plot\"><pre>plot: unknown function 'sinc'\nsinc(x)</pre></div>", Body("```plot\nsinc(x)\n```"));
        Assert.Equal("<div class=\"plot\"></div>", Body("```plot\n```"));
    }

    // MARK: - Engine gating

    [Fact]
    public void NeedsNothingForPlainProseACodeSpanOrABareFence()
    {
        Assert.Equal(EngineNeeds.None, Needs("# Title\n\nJust prose."));
        Assert.Equal(EngineNeeds.None, Needs("a `code span` here"));
        Assert.Equal(EngineNeeds.None, Needs("```\nplain\n```"));
        Assert.Equal(EngineNeeds.None, Needs("it costs $5 and $10 today"));
        Assert.Equal("", EngineNeeds.None.ToString());
    }

    [Fact]
    public void NeedsKeyEachEngineOffItsOwnContainer()
    {
        var off = EngineNeeds.None;
        Assert.Equal(off with { Math = true }, Needs("$a+b$"));
        Assert.Equal(off with { Math = true }, Needs("$$a+b$$"));
        Assert.Equal(off with { Math = true }, Needs("```math\nx\n```"));
        Assert.Equal(off with { Mermaid = true }, Needs("```mermaid\ngraph TD\n```"));
        Assert.Equal(off with { Plantuml = true }, Needs("```plantuml\n@startuml\n```"));
        Assert.Equal(off with { Graphviz = true }, Needs("```dot\ndigraph {}\n```"));
        Assert.Equal(off with { Graphviz = true }, Needs("```twopi\ndigraph {}\n```"));
        Assert.Equal(off with { Highlight = true }, Needs("```swift\nlet x = 1\n```"));
        Assert.Equal(off, Needs("```csv\na,b\n1,2\n```"));
        // The class is exactly `plot`: a plot-only document loads nothing, good or broken.
        Assert.Equal(off, Needs("```plot\nsin(x)\n```"));
        Assert.Equal(off, Needs("```plot\nsinc(x)\n```"));
        // ToString is the space-joined list in probe order.
        Assert.Equal("math mermaid plantuml graphviz highlight", new EngineNeeds(true, true, true, true, true).ToString());
        Assert.Equal("mermaid highlight", new EngineNeeds(false, true, false, false, true).ToString());
    }

    [Fact]
    public void NeedsProbeTheEmittedMarkupSoAQuotedDiagramLoadsItsEngine()
    {
        // THE reason the gate reads markup and not the block list: `RenderBlock` recurses.
        Assert.Equal(EngineNeeds.None with { Graphviz = true }, Needs("> ```dot\n> digraph { a }\n> ```"));
        var quoted = Doc("> ```dot\n> digraph { a }\n> ```");
        Has(quoted, "class=\"graphviz\"");
        Has(quoted, "rich/viz-global.js");
        var mermaid = Doc("> ```mermaid\n> graph TD\n> ```");
        Has(mermaid, "<pre class=\"mermaid\">");
        Has(mermaid, "rich/mermaid.min.js");
        var plantuml = Doc("> ```plantuml\n> @startuml\n> ```");
        Has(plantuml, "<div class=\"plantuml\">");
        Has(plantuml, "rich/viz-global.js");
        // A code block that merely quotes a container string cannot trigger it: it is escaped.
        Assert.Equal(EngineNeeds.None with { Highlight = true }, Needs("```html\n<pre class=\"mermaid\">x</pre>\n```"));
        Assert.Equal(EngineNeeds.None, Needs("```\n<div class=\"plantuml\">\n```"));
    }

    [Fact]
    public void NeedsPullKatexInForProseThatMerelyNamesTheClass()
    {
        // The escaping argument is FALSE for math: no `<` or `"` in the probe. Verified, harmless
        // at runtime, a real byte difference — reproduced on purpose.
        Assert.True(Needs("The class md-mathd is mentioned in prose.").Math);
        Has(Doc("md-mathi"), "katex.min.js");
        Assert.True(MarkdownHtml.Needs("md-mathi").Math);
        Assert.Equal(EngineNeeds.None, MarkdownHtml.Needs("<pre class=\"mermaid \">"));
    }

    [Fact]
    public void RawDiagramDocumentsHardCodeTheirNeeds()
    {
        // No Markdown, so no math is possible even when the diagram's own text says so.
        var puml = MarkdownHtml.Body("@startuml\nnote: md-mathi\n@enduml\n", "t", false);
        Assert.Equal(new EngineNeeds(false, false, true, false, false), puml.Needs);
        Assert.Equal("<div class=\"plantuml\">@startuml\nnote: md-mathi\n@enduml\n</div>", puml.Html);
        var dot = MarkdownHtml.Body("digraph G {\n  a -> b; // md-mathd\n}\n", "t", false);
        Assert.Equal(new EngineNeeds(false, false, false, true, false), dot.Needs);
        Assert.Equal("<div class=\"graphviz\" data-engine=\"dot\">digraph G {\n  a -&gt; b; // md-mathd\n}\n</div>", dot.Html);
    }

    [Fact]
    public void HtmlMhchemLoadsWithAndAfterKatex()
    {
        var math = Doc("Reaction $\\ce{H2O}$ here");
        Has(math, "katex.min.js");
        Has(math, "mhchem.min.js");
        Assert.True(math.IndexOf("katex.min.js", StringComparison.Ordinal) < math.IndexOf("mhchem.min.js", StringComparison.Ordinal));
        // mhchem shares KaTeX's `defer`, or it would run before it and find no global to extend.
        Has(math, "<script defer src=\"rich/katex.min.js\"></script>");
        Has(math, "<script defer src=\"rich/mhchem.min.js\"></script>");
    }

    [Fact]
    public void HtmlMhchemAbsentWithoutMath()
    {
        Lacks(Doc("# Just text\n\nA paragraph."), "mhchem.min.js");
        Lacks(Doc("```mermaid\ngraph TD\nA-->B\n```"), "mhchem.min.js");
        Lacks(Doc("it costs $5 and $10 today"), "mhchem.min.js");
    }

    [Fact]
    public void HeadIncludesComeInOrderAndVizIsSharedByPlantumlAndDot()
    {
        foreach (var source in new[] { "```plantuml\n@startuml\n```", "```dot\ndigraph {}\n```" })
        {
            var html = Doc(source);
            Has(html, "</style>\n<script src=\"rich/viz-global.js\"></script>\n</head>");
            Lacks(html, "plantuml.js");
        }
        // Mermaid and Viz are not deferred; the full head for a document that needs everything.
        var all = Doc(Lines("$a$", "", "```mermaid", "graph TD", "```", "", "```dot", "digraph {}", "```", "", "```js", "x", "```"));
        Has(all, Lines(
            "</style><link rel=\"stylesheet\" href=\"rich/katex.min.css\">",
            "<script defer src=\"rich/katex.min.js\"></script>",
            "<script defer src=\"rich/mhchem.min.js\"></script>",
            "<script src=\"rich/mermaid.min.js\"></script>",
            "<script src=\"rich/viz-global.js\"></script>",
            "<script defer src=\"rich/highlight.min.js\"></script>",
            "</head>"));
    }

    [Fact]
    public void ADocumentOfNothingButPlotsIsLight()
    {
        var html = Doc("```plot\nsin(x)\n```");
        foreach (var engine in new[] { "katex.min.js", "mermaid.min.js", "viz-global.js", "highlight.min.js", "mhchem.min.js" })
        {
            Lacks(html, engine);
        }
        Has(html, "<svg ");
    }

    // MARK: - Footnotes

    [Fact]
    public void FootnoteDefinitionIsParsedAndNotDrawnInPlace()
    {
        var html = Doc("Text[^a].\n\n[^a]: The note.");
        Lacks(html, "<p>[^a]: The note.</p>");
        Has(html, "<li id=\"fn-1\">The note.");
        Assert.Equal(
            "<p>Text<sup class=\"md-fnref\" id=\"fnref-1\"><a href=\"#fn-1\">1</a></sup>.</p>\n\n"
            + "<section class=\"md-footnotes\"><hr><ol><li id=\"fn-1\">The note. <a class=\"md-fnback\" href=\"#fnref-1\">&#8617;</a></li></ol></section>",
            Body("Text[^a].\n\n[^a]: The note."));
    }

    [Fact]
    public void FootnotesAreNumberedByFirstReferenceNotDefinitionOrder()
    {
        var html = Doc("See[^b] then[^a].\n\n[^a]: Alpha.\n[^b]: Bravo.");
        Has(html, "<li id=\"fn-1\">Bravo.");
        Has(html, "<li id=\"fn-2\">Alpha.");
        Assert.True(html.IndexOf("#fn-1", StringComparison.Ordinal) < html.IndexOf("#fn-2", StringComparison.Ordinal));
    }

    [Fact]
    public void RepeatedFootnoteReferenceGetsItsOwnAnchor()
    {
        var html = Doc("One[^a] two[^a].\n\n[^a]: Note.");
        Assert.Equal(2, html.Split("href=\"#fn-1\"").Length - 1);
        Has(html, "id=\"fnref-1\"");
        Has(html, "id=\"fnref-1-2\"");
        // The back-link goes to the first citation, never `fnref-1-2`.
        Has(html, "<a class=\"md-fnback\" href=\"#fnref-1\">&#8617;</a>");
        Lacks(html, "href=\"#fnref-1-2\"");
    }

    [Fact]
    public void FootnoteReferenceWithoutDefinitionStaysLiteralText()
    {
        var html = Doc("A claim[^nope].");
        Has(html, "[^nope]");
        // Assert on the emitted markup, not the class name: the stylesheet names every class.
        Lacks(html, "<sup class=\"md-fnref\"");
        Lacks(html, "<section class=\"md-footnotes\">");
        Assert.Equal("<p>A claim[^nope].</p>", Body("A claim[^nope]."));
    }

    [Fact]
    public void UnreferencedFootnoteIsStillPrinted()
    {
        var html = Doc("Body.\n\n[^lone]: Never cited.");
        Has(html, "<li id=\"fn-1\">Never cited.");
        Lacks(html, "<a class=\"md-fnback\"");
        // Cited notes first, then the uncited ones in definition order.
        var mixed = Body("X[^b].\n\n[^a]: A.\n[^b]: B.\n[^c]: C.");
        Has(mixed, "<li id=\"fn-1\">B. <a class=\"md-fnback\" href=\"#fnref-1\">&#8617;</a></li><li id=\"fn-2\">A.</li><li id=\"fn-3\">C.</li>");
    }

    [Fact]
    public void FirstFootnoteDefinitionWinsOnADuplicateId()
    {
        var html = Body("X[^a].\n\n[^a]: One.\n[^a]: Two.");
        Has(html, "<li id=\"fn-1\">One.");
        Lacks(html, "Two.");
        Assert.Equal(1, html.Split("<li id=").Length - 1);
    }

    [Fact]
    public void FootnoteDefinitionsAreGatheredFromTheTopLevelOnly()
    {
        // `Body` walks the top-level blocks; a definition nested in a quote renders as nothing and
        // is never gathered, so the reference naming it has no definition and stays literal text.
        Assert.Equal("<p>X[^a].</p>\n<blockquote>\n\n</blockquote>", Body("X[^a].\n\n> [^a]: quoted definition"));
        // A document that is nothing but a definition still prints it — under the leading newline
        // the section carries, on top of the blank the empty definition block left in the join.
        Assert.Equal("\n<section class=\"md-footnotes\"><hr><ol><li id=\"fn-1\">only a definition</li></ol></section>",
            Body("[^a]: only a definition"));
    }

    [Fact]
    public void FootnoteReferenceInAHeadingIsNumberedAndLeavesTheSlug()
    {
        // The slug is taken from the raw heading text, where `[`, `^` and `]` are dropped and the
        // id keeps only the letters; the reference itself is resolved like any other.
        Assert.Equal(
            "<h1 id=\"headinga\">Heading<sup class=\"md-fnref\" id=\"fnref-1\"><a href=\"#fn-1\">1</a></sup></h1>\n\n"
            + "<section class=\"md-footnotes\"><hr><ol><li id=\"fn-1\">note <a class=\"md-fnback\" href=\"#fnref-1\">&#8617;</a></li></ol></section>",
            Body("# Heading[^a]\n\n[^a]: note"));
    }

    [Fact]
    public void EveryFurtherCitationTakesTheNextAnchorAndTheIdKeepsItsShape()
    {
        // Occurrence 1 is `fnref-1`; the rest are `fnref-1-<occurrence>` — an id, not a number,
        // so a note cited three times still resolves to one entry.
        var html = Body("Text[^a] and[^a] and[^a].\n\n[^a]: thrice");
        Has(html, "id=\"fnref-1\"");
        Has(html, "id=\"fnref-1-2\"");
        Has(html, "id=\"fnref-1-3\"");
        Assert.Equal(3, html.Split("href=\"#fn-1\"").Length - 1);
        Assert.Equal(1, html.Split("<li id=").Length - 1);
        // Ids may hold letters, digits, `_` and `-` — the parser's set and the reference's set.
        Has(Body("a[^A_b-1] z\n\n[^A_b-1]: mixed id"), "<sup class=\"md-fnref\" id=\"fnref-1\">");
    }

    [Fact]
    public void FootnoteTextIsInlineMarkdownAndIsEscaped()
    {
        var html = Doc("X[^a].\n\n[^a]: *Emphasis* and <b>literal</b> & co.");
        Has(html, "<em>Emphasis</em>");
        Has(html, "&lt;b&gt;literal&lt;/b&gt;");
        Has(html, "&amp; co.");
    }

    [Fact]
    public void FootnoteInsideCodeIsNotAReference()
    {
        var html = Doc("Use `arr[^1]` here.\n\n[^1]: Note.");
        Has(html, "<code>arr[^1]</code>");
        Lacks(html, "<code>arr<sup");
    }

    [Fact]
    public void FootnoteReferenceCannotBreakOutIntoMarkup()
    {
        var image = Doc("![alt [^a] here](i.png)\n\n[^a]: N.");
        Lacks(image, "alt=\"alt <sup");
        Lacks(image, "<img");
        // A label holding a reference is not a link — the harmless failure.
        var link = Doc("[see [^a] here](u)\n\n[^a]: N.");
        Lacks(link, "<a href=\"u\">");
        Lacks(link, "</a></a>");
        var plain = Doc("[text](u) and ![a](i.png)");
        Has(plain, "<a href=\"u\">text</a>");
        Has(plain, "<img src=\"i.png\" alt=\"a\">");
        // The placeholder the span pass leaves for the document-wide numbering.
        Assert.Equal("a<sup class=\"md-fnref\" data-fn=\"b\"></sup>", MarkdownHtml.Inline("a[^b]"));
        Assert.Equal("[^caf" + EAcute + "]", MarkdownHtml.Inline("[^caf" + EAcute + "]"));
        Assert.Equal("[^two words]", MarkdownHtml.Inline("[^two words]"));
    }

    [Fact]
    public void FootnoteReferenceNestedInsideANoteBecomesLiteralText()
    {
        var html = Doc("X[^a].\n\n[^a]: see [^b] too.\n[^b]: Other.");
        Lacks(html, "data-fn=");
        Has(html, "[^b]");
        // Kotlin's exact pin: the uncited `b` is still printed, and the nested reference reads
        // as the author typed it.
        var kotlin = Doc("X[^a].\n\n[^a]: See [^b] as well.\n[^b]: Bee.");
        Has(kotlin, "<li id=\"fn-1\">See [^b] as well.");
        Has(kotlin, "<li id=\"fn-2\">Bee.");
        Lacks(kotlin, "data-fn=");
    }

    // MARK: - Raw diagram documents

    [Fact]
    public void RawPlantUmlDocumentRendersAsDiagram()
    {
        var html = MarkdownHtml.Document("@startuml\nAlice -> Bob: hi\nBob --> Alice: hi\n@enduml\n", "d", false);
        Has(html, "<div class=\"plantuml\">");
        Has(html, "rich/viz-global.js");
        Lacks(html, "<p>@startuml");
        // The whole file, trailing newline included, is escaped: the close tag lands on its own line.
        Assert.Equal("<div class=\"plantuml\">@startuml\nA -&gt; B\n@enduml\n</div>", Body("@startuml\nA -> B\n@enduml\n"));

        Assert.True(MarkdownParser.IsRawPlantUml("' header comment\n\n@startmindmap\n* root\n@endmindmap"));
        Assert.True(MarkdownParser.IsRawPlantUml("   \n@startuml\n@enduml"));
        Assert.True(MarkdownParser.IsRawPlantUml("\r\n@startuml\r\nA -> B\r\n@enduml\r\n"));
        Assert.True(MarkdownParser.IsRawPlantUml("' note\r\n\r\n@startmindmap\r\n* r\r\n@endmindmap"));
        // Kotlin adds the lone-CR (classic Mac) case.
        Assert.True(MarkdownParser.IsRawPlantUml("' note\r\r@startuml\r@enduml"));
        Assert.False(MarkdownParser.IsRawPlantUml("# Title\n\nSome prose about @startuml in passing."));
        Assert.False(MarkdownParser.IsRawPlantUml(""));
        var md = MarkdownHtml.Document("# Title\n\nHello.", "d", false);
        Lacks(md, "<div class=\"plantuml\">");
        Has(md, "<h1");
    }

    [Fact]
    public void RawGraphvizDocumentRendersAsDiagram()
    {
        var html = MarkdownHtml.Document("digraph G {\n  a -> b;\n}\n", "d", false);
        Has(html, "<div class=\"graphviz\" data-engine=\"dot\">");
        Has(html, "rich/viz-global.js");
        Lacks(html, "<p>digraph");

        foreach (var yes in new[]
        {
            "digraph { a -> b }", "graph {}", "strict digraph G {\n}", "DiGraph Foo {\n}", "digraph{a}",
            "digraph\n{\n  a\n}", "// generated\n\n/* by hand */\ndigraph { a }", "digraph \"my graph\" {\n}",
            "digraph cafe" + Acute + " {\n}", "digraph caf" + EAcute + " {\n}", "digraph " + RomanTen + " {\n}",
            "// generated" + Nel + "digraph G {" + Nel + "}",
            "\r\ndigraph G {\r\n  a -> b;\r\n}\r\n", "// generated\r\ndigraph G {\r\n}\r\n",
            "// c\rdigraph G {\r}",
        })
        {
            Assert.True(MarkdownParser.IsRawGraphviz(yes), yes);
        }
        foreach (var no in new[]
        {
            "# Notes\n\ngraph { the mental model }",
            "graph theory is a branch of maths.\n\nSee $\\frac{a}{b}$.",
            "digraph models are useful { in theory }", "graphviz is a fine tool { see }", "digraphs are a topic { here }",
            "digraph without a brace", "# Title\n\nSome prose about digraph { } in passing.", "",
        })
        {
            Assert.False(MarkdownParser.IsRawGraphviz(no), no);
        }

        // A diagram nested in a quote still pulls its engine in; a Markdown document that merely
        // contains a dot fence is still Markdown.
        var quoted = MarkdownHtml.Document("> ```dot\n> digraph { a }\n> ```", "d", false);
        Has(quoted, "class=\"graphviz\"");
        Has(quoted, "rich/viz-global.js");
        var mixed = MarkdownHtml.Document("# Title\n\n```dot\ndigraph { a }\n```\n", "d", false);
        Has(mixed, "<h1");
        Has(mixed, "class=\"graphviz\"");
    }

    // MARK: - CSV / TSV fences

    [Fact]
    public void CsvFenceRendersAsATable()
    {
        var html = Doc("```csv\nName,Role\nAnn,Editor\n```");
        Has(html, "<th style=\"text-align:left\">Name</th>");
        Has(html, "<td style=\"text-align:left\">Ann</td>");
        Lacks(html, "<pre><code>Name,Role");
    }

    [Fact]
    public void CsvNumericColumnsAreRightAligned()
    {
        var html = Doc("```csv\nCity,People\nOslo,709037\nBergen,289330\n```");
        Has(html, "<th style=\"text-align:left\">City</th>");
        Has(html, "<th style=\"text-align:right\">People</th>");
        Has(html, "<td style=\"text-align:right\">709037</td>");
        Has(Doc("```csv\nA\n1\nn/a\n```"), "<th style=\"text-align:left\">A</th>");
    }

    [Fact]
    public void DecimalNumberGrammarDecidesTheAlignment()
    {
        // The HTML half of testDecimalNumberGrammarIsExplicitNotDoubleInit: a hex column stays
        // left, and an invisible U+200B is not padding because the cell trim is ASCII space/tab.
        Has(Doc("```csv\nItem,Value\na,0x10\n```"), "<th style=\"text-align:left\">Value</th>");
        Has(Doc("```csv\nItem,Value\na," + Zwsp + "1" + Zwsp + "\n```"), "<th style=\"text-align:left\">Value</th>");
        Has(Doc("```csv\nItem,Value\na, 1 \n```"), "<th style=\"text-align:right\">Value</th>");
    }

    [Fact]
    public void TsvFenceUsesTabs()
    {
        var html = Doc("```tsv\nCity\tPeople\nOslo\t709037\n```");
        Has(html, "<th style=\"text-align:left\">City</th>");
        Has(html, "<td style=\"text-align:right\">709037</td>");
    }

    [Fact]
    public void EmptyCsvFenceStaysACodeBlock()
    {
        var html = Doc("```csv\n\n```");
        Lacks(html, "<table>");
        // The fallback is the *bare* form, which trips no highlight gate. A fence holding one
        // blank line and an empty fence are the same code: the parser joins its collected lines
        // with "\n", so a single empty line is an empty string, not a newline.
        Assert.Equal("<pre><code></code></pre>", Body("```csv\n\n```"));
        Assert.Equal("<pre><code></code></pre>", Body("```csv\n```"));
        Assert.False(Needs("```csv\n```").Highlight);
        Lacks(html, "highlight.min.js");
    }

    // MARK: - The stylesheet

    /// <summary>Everything between <c>&lt;style&gt;</c> and <c>&lt;/style&gt;</c>.</summary>
    private static string StyleBlock(string html)
    {
        var open = html.IndexOf("<style>", StringComparison.Ordinal) + "<style>".Length;
        return html.Substring(open, html.IndexOf("</style>", open, StringComparison.Ordinal) - open);
    }

    /// <summary>Strip comments the way a CSS parser does, not the way a grep does; an unterminated comment swallows the rest.</summary>
    private static string StripCssComments(string sheet)
    {
        var output = new System.Text.StringBuilder();
        var cursor = 0;
        while (true)
        {
            var open = sheet.IndexOf("/*", cursor, StringComparison.Ordinal);
            if (open < 0)
            {
                output.Append(sheet, cursor, sheet.Length - cursor);
                return output.ToString();
            }
            output.Append(sheet, cursor, open - cursor);
            var close = sheet.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0) return output.ToString();
            cursor = close + 2;
        }
    }

    [Fact]
    public void GraphvizInkRulesSurviveCssCommentStripping()
    {
        // If a comment ever closes early, the prose after it becomes the prelude of the next rule
        // and the parser swallows that rule whole — the diagram then draws black on carbon paper,
        // while a test that merely greps for the rule text still passes.
        foreach (var dark in new[] { false, true })
        {
            var css = StyleBlock(Doc("```dot\ndigraph { a }\n```", dark: dark));
            Assert.Equal(css.Split("/*").Length, css.Split("*/").Length);
            var stripped = StripCssComments(css);
            var ink = dark ? "#E7DBC2" : "#2B2620";
            foreach (var rule in new[] { "text:not([fill])", "text[fill=\"black\"]", "[stroke=\"black\"]", "[fill=\"black\"]:not(text)" })
            {
                Has(stripped, ".graphviz svg " + rule);
            }
            // Graphviz writes no fill at all unless asked, so the uncoloured-label rule is the one
            // that matters — and the first casualty of a broken comment.
            Has(stripped, ".graphviz svg text:not([fill]) { fill: " + ink + "; }");
            // The rules must not have been swallowed into a comment: none of them is inside one.
            Lacks(stripped, "/*");
        }
    }

    [Fact]
    public void GraphvizInkRecolorIsScopedToTheEnginesOwnBlack()
    {
        // Kotlin's pin: the recolor must not overwrite an author's `fontcolor` / `color`; each
        // rule is scoped to what the engine emits when nothing was asked for.
        var html = Doc("```dot\ndigraph { a }\n```", dark: true);
        Has(html, ".graphviz svg text:not([fill]) { fill: #E7DBC2; }");
        Has(html, ".graphviz svg text[fill=\"black\"] { fill: #E7DBC2; }");
        Has(html, ".graphviz svg [stroke=\"black\"] { stroke: #E7DBC2; }");
        Has(html, ".graphviz svg [fill=\"black\"]:not(text) { fill: #E7DBC2; }");
        Lacks(html, ".graphviz svg text { fill:");
        Lacks(html, ".graphviz svg [fill=\"black\"] { fill:");
    }

    [Fact]
    public void HighlightThemeIsTheAppsOwnPalette()
    {
        // Hand-written in the paper palette, not a stock hljs theme, and it rides in every
        // document's <style>; only the engine <script> is gated. These are the Swift rules —
        // Kotlin ships a shorter theme with different string tones (#6A5433 / #B79A67), which
        // must never appear here.
        var dark = Doc("```swift\nlet x = 1\n```", dark: true);
        Has(dark, ".hljs-type, .hljs-title, .hljs-section, .hljs-name, .hljs-doctag { color: #C99A55; }");
        Has(dark, ".hljs-comment, .hljs-quote, .hljs-meta { color: #B3A98E; font-style: italic; }");
        Has(dark, ".hljs-attr, .hljs-attribute, .hljs-addition { color: #CDBF9E; }");
        Has(dark, ".hljs-deletion { color: #B3A98E; text-decoration: line-through; }");
        var light = Doc("```swift\nlet x = 1\n```");
        Has(light, ".hljs-type, .hljs-title, .hljs-section, .hljs-name, .hljs-doctag { color: #9C6B2E; }");
        Has(light, ".hljs-comment, .hljs-quote, .hljs-meta { color: #6B635A; font-style: italic; }");
        Has(light, ".hljs-attr, .hljs-attribute, .hljs-addition { color: #4A4034; }");
        // "Georgia" is the family's screen stand-in and "Lucida Sans Typewriter" is md.win's; both
        // are grafted on by an app, never by Core, and a golden would die the day either leaked in.
        foreach (var kotlin in new[] { "#6A5433", "#B79A67", "Georgia", "Lucida Sans Typewriter" })
        {
            Lacks(light, kotlin);
            Lacks(dark, kotlin);
        }
        Has(light, "font-family: \"American Typewriter\", \"Courier New\", serif;");
    }

    [Fact]
    public void StylesheetHasThreeVariantsOf101Lines()
    {
        foreach (var sheet in new[] { MarkdownHtml.Css(false, false), MarkdownHtml.Css(true, false), MarkdownHtml.Css(false, true) })
        {
            Assert.Equal(101, sheet.Split('\n').Length);
            Assert.False(sheet.EndsWith("\n", StringComparison.Ordinal));
            Assert.StartsWith("/* Force", sheet, StringComparison.Ordinal);
            // The EPUB builder decides the `svg` manifest property by searching a document for the
            // literal tag, so the sheet may never contain one.
            Lacks(sheet, "<svg");
            Lacks(sheet, "\r");
        }
        // The dark kill is applied inside Css too, so there is no fourth, never-shipped variant.
        Assert.Equal(MarkdownHtml.Css(false, true), MarkdownHtml.Css(true, true));
        // Line 32 is genuinely blank in the preview sheets and the code-wrapping rule in export.
        Assert.Equal("", MarkdownHtml.Css(false, false).Split('\n')[31]);
        Assert.Equal("pre { white-space: pre-wrap; overflow-wrap: anywhere; }", MarkdownHtml.Css(false, true).Split('\n')[31]);
        Lacks(MarkdownHtml.Css(false, false), "pre-wrap");
        Assert.Equal("font-size: 11pt;", MarkdownHtml.Css(false, true).Split('\n')[11].Trim());
        Assert.Equal("font-size: 13pt;", MarkdownHtml.Css(true, false).Split('\n')[11].Trim());
        // `\newpage` in the comment is ONE backslash.
        Has(MarkdownHtml.Css(false, false), "/* The author's `\\newpage`: a dashed rule on screen;");
    }

    // MARK: - Plot integration (PlotTests.swift section 8, the renderer's share)

    private const string PlotFence = "```plot\nx: -10..10\ntitle: T\nsin(x)\n```";

    [Fact]
    public void IntegrationDispatchesTheFenceToAPlotContainerAndLoadsNoEngine()
    {
        var html = Doc(PlotFence);
        Has(html, "<div class=\"plot\"><svg");
        // The class is exactly `plot`, which keeps all five probes false: no engine, no `needs` flag.
        foreach (var engine in new[] { "mermaid.min.js", "viz-global.js", "katex.min.js", "mhchem.min.js", "highlight.min.js" })
        {
            Lacks(html, engine);
        }
        Lacks(html, "language-plot");
        Assert.Equal(EngineNeeds.None, Needs(PlotFence));
    }

    [Fact]
    public void IntegrationIsStyledByTheScreenAndTheExportStylesheet()
    {
        // The rules are *joined*, never added: `.plot` rides the container rule the other rich
        // blocks share, and `.plot svg` the cap rule. Both variants carry them.
        foreach (var export in new[] { false, true })
        {
            var html = Doc(PlotFence, export: export);
            Has(html, ".mermaid, .plantuml, .graphviz, .plot, .md-mathd {");
            Has(html, ".mermaid svg, .plantuml svg, .graphviz svg, .plot svg { max-width: 100%; height: auto; }");
        }
    }
}
