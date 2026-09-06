using System.Globalization;
using Md.Core.Book;
using Md.Core.Export;
using Md.Core.Markdown;
using Md.Core.Text;

namespace Md.Core.Tests;

// The .tex writer. Pure string work with no rendering behind it, so it is covered densely:
// every block kind, the escaping table, and the handful of places where LaTeX's own syntax
// bites back (an optional argument eaten off the front of an item, a code block that closes
// its own verbatim environment, a footnote in a longtable's head).
//
// Port of the 100 `testLaTeX*` cases in md.macOS/mdTests/mdTests.swift, case for case and
// name for name (minus the `test` prefix), plus the thirteen the Kotlin suite has and the
// Swift cannot express (surrogate pairs, the default locale, ordering properties), the
// `plot` fence case from PlotTests.swift §8, and the guards that are .NET's own: a code-unit
// regex engine, culture-sensitive string overloads, and an ICU `\s` / `.` that had to be
// spelled out.
//
// Every string assertion here is ordinal. xUnit's Assert.Contains / StartsWith / EndsWith on
// strings are culture-sensitive by default, so they are never used on strings.
public class LaTeXExportTests
{
    // MARK: - Helpers

    /// <summary>The body between `\begin{document}` and `\end{document}`, trimmed — most assertions are about the content, not the preamble.</summary>
    private static string TexBody(string source)
    {
        var tex = LaTeXExport.Document(source);
        const string opening = "\\begin{document}\n";
        const string closing = "\n\\end{document}";
        var start = tex.IndexOf(opening, StringComparison.Ordinal);
        var end = tex.IndexOf(closing, StringComparison.Ordinal);
        Assert.True(start >= 0 && end >= 0, "the document is not wrapped in a document environment");
        var from = start + opening.Length;
        return Whitespace.TrimWSNL(tex.Substring(from, end - from));
    }

    /// <summary>The preamble: everything before `\begin{document}`.</summary>
    private static string TexPreamble(string source)
    {
        var tex = LaTeXExport.Document(source);
        var start = tex.IndexOf("\\begin{document}", StringComparison.Ordinal);
        return start < 0 ? tex : tex.Substring(0, start);
    }

    private static void AssertContains(string text, string part, string? because = null) =>
        Assert.True(text.Contains(part, StringComparison.Ordinal), because ?? ("expected to find <" + part + "> in <" + text + ">"));

    private static void AssertNotContains(string text, string part, string? because = null) =>
        Assert.False(text.Contains(part, StringComparison.Ordinal), because ?? ("did not expect <" + part + "> in <" + text + ">"));

    private static void AssertStartsWith(string text, string prefix) =>
        Assert.True(text.StartsWith(prefix, StringComparison.Ordinal), "expected <" + text + "> to start with <" + prefix + ">");

    private static void AssertEndsWith(string text, string suffix) =>
        Assert.True(text.EndsWith(suffix, StringComparison.Ordinal), "expected <" + text + "> to end with <" + suffix + ">");

    private static int Occurrences(string text, string part) => ScalarText.Split(text, part).Count - 1;

    private static string Chr(int codePoint) => char.ConvertFromUtf32(codePoint);

    // Three ways to make the character in front part of a longer grapheme: a combining acute,
    // a variation selector, a zero-width joiner. Built from their codes so nothing invisible is
    // typed into this file.
    private static readonly string Acute = Chr(0x0301);
    private static readonly string Vs16 = Chr(0xFE0F);
    private static readonly string Zwj = Chr(0x200D);
    private static readonly string[] Marks = { Acute, Vs16, Zwj };
    private static readonly string E000 = Chr(0xE000);
    private static readonly string E001 = Chr(0xE001);
    private static readonly string EmDash = Chr(0x2014);
    private static readonly string Sup2 = Chr(0x00B2);

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    /// <summary>A two-chapter book in the reading order the PDF compile and the EPUB already use.</summary>
    private static StructuredBook SampleBook() => new(
        "The Book",
        [new BookUnit("Preface", "Opening words.")],
        [
            new BookSection("One", [
                new BookUnit("First", "# Inner\n\nA[^a].\n\n[^a]: n1"),
                new BookUnit("Second", "B[^b].\n\n[^b]: n2"),
            ]),
            new BookSection("Two", [new BookUnit("Third", "C[^c] C[^c].\n\n[^c]: n3")]),
        ]);

    private static StructuredBook OneChapter(string chapter, params BookUnit[] units) =>
        new("B", [], [new BookSection(chapter, units)]);

    private static void WithCulture(string name, Action body)
    {
        var saved = CultureInfo.CurrentCulture;
        var savedUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
            CultureInfo.CurrentUICulture = savedUi;
        }
    }

    // MARK: - Escaping

    [Fact]
    public void LaTeXEscapesEverySpecialCharacter()
    {
        // The ten characters TeX reserves. Three have no backslash form and need a command
        // instead — a leading backslash on `~` or `^` would be an accent waiting for its letter.
        Assert.Equal("\\#", LaTeXExport.Escape("#"));
        Assert.Equal("\\$", LaTeXExport.Escape("$"));
        Assert.Equal("\\%", LaTeXExport.Escape("%"));
        Assert.Equal("\\&", LaTeXExport.Escape("&"));
        Assert.Equal("\\_", LaTeXExport.Escape("_"));
        Assert.Equal("\\{", LaTeXExport.Escape("{"));
        Assert.Equal("\\}", LaTeXExport.Escape("}"));
        Assert.Equal("\\textasciitilde{}", LaTeXExport.Escape("~"));
        Assert.Equal("\\textasciicircum{}", LaTeXExport.Escape("^"));
        Assert.Equal("\\textbackslash{}", LaTeXExport.Escape("\\"));
    }

    [Fact]
    public void LaTeXEscapeDoesNotEscapeItsOwnOutput()
    {
        // The regression a chain of Replace calls would produce: `\` becomes `\textbackslash{}`,
        // whose own braces and backslash are then escaped again.
        Assert.Equal("a\\textbackslash{}b", LaTeXExport.Escape("a\\b"));
        Assert.Equal("\\textasciitilde{}\\textasciicircum{}", LaTeXExport.Escape("~^"));
        // One pass, in order, whatever the mix.
        Assert.Equal("100\\% of \\{a\\_b\\} \\& \\textbackslash{}c \\textasciitilde{} \\textasciicircum{} \\$\\#",
                     LaTeXExport.Escape("100% of {a_b} & \\c ~ ^ $#"));
    }

    [Fact]
    public void LaTeXEscapeLeavesUnreservedPunctuationAlone()
    {
        // `<`, `>` and `|` are not TeX specials, and the ports agree on this table character for character.
        Assert.Equal("a < b > c | d", LaTeXExport.Escape("a < b > c | d"));
    }

    [Fact]
    public void LaTeXEscapeWalksScalarsNotGraphemeClusters()
    {
        // A special followed by a combining mark is one grapheme in Swift and not `"#"`; a live `%`
        // comments away the rest of the author's sentence. A code-unit walk sees the special.
        Assert.Equal("\\%" + Acute, LaTeXExport.Escape("%" + Acute));
        Assert.Equal("\\#" + Vs16 + Chr(0x20E3), LaTeXExport.Escape("#" + Vs16 + Chr(0x20E3)));
        Assert.Equal("a\\%" + Acute + "b", LaTeXExport.EscapeUrl("a%" + Acute + "b"));
        // Whole-document form: the sentence after the special survives.
        Assert.Equal("Fifty\\%" + Acute + " percent, and the rest survives.",
                     TexBody("Fifty%" + Acute + " percent, and the rest survives."));
    }

    [Fact]
    public void LaTeXPercentDecodesAnImagePath()
    {
        // `my%20dir` is a directory on no disk anywhere: the author meant `my dir`.
        Assert.Equal("my dir/a.png", LaTeXExport.PercentDecoded("my%20dir/a.png"));
        Assert.Equal("a/b c.png", LaTeXExport.PercentDecoded("a%2Fb%20c.png"));
        // Multi-byte sequences come back as the character they encode.
        Assert.Equal(Chr(0x041F) + ".png", LaTeXExport.PercentDecoded("%D0%9F.png"));
        // What is not percent-encoding is a file name with a `%` in it, handed back exactly as it came.
        Assert.Equal("100%.png", LaTeXExport.PercentDecoded("100%.png"));
        Assert.Equal("a%zzb", LaTeXExport.PercentDecoded("a%zzb"));
        Assert.Equal("trailing%2", LaTeXExport.PercentDecoded("trailing%2"));
        // Bytes that are not UTF-8 are not repaired into U+FFFD either.
        Assert.Equal("a%C0%80b", LaTeXExport.PercentDecoded("a%C0%80b"));
        Assert.Equal("plain.png", LaTeXExport.PercentDecoded("plain.png"));
    }

    [Fact]
    public void PercentDecodeEncodesSurrogatePairsWholeAndRefusesLoneOnes()
    {
        // .NET-only. The bytes are produced a *scalar* at a time: encoding UTF-16 unit by unit
        // turns one emoji into two U+FFFD, and the strict decode then hands back a file name
        // nobody has. Swift cannot express either half of this — its strings are well formed and
        // its scalar view is the only view.
        var grinning = Chr(0x1F600);
        Assert.Equal(grinning + " x.png", LaTeXExport.PercentDecoded(grinning + "%20x.png"));
        Assert.Equal(grinning + ".png", LaTeXExport.PercentDecoded("%F0%9F%98%80.png"));
        // An unpaired surrogate is text this cannot encode, and the rule for everything it cannot
        // handle is the same one: the path comes back exactly as it came.
        var lone = new string((char)0xD83D, 1);   // not Chr: ConvertFromUtf32 refuses a surrogate
        Assert.Equal(lone + "%20x.png", LaTeXExport.PercentDecoded(lone + "%20x.png"));
    }

    [Fact]
    public void LaTeXURLEscapeGuardsOnlyWhatBreaksTheArgument()
    {
        // hyperref reads its URL argument with its own catcodes, so `_` and `&` arrive intact.
        Assert.Equal("https://x.com/a_b?c=1&d=2", LaTeXExport.EscapeUrl("https://x.com/a_b?c=1&d=2"));
        // A comment character would swallow the rest of the line and a parameter character is illegal there.
        Assert.Equal("https://x.com/100\\%25\\#top", LaTeXExport.EscapeUrl("https://x.com/100%25#top"));
        // An unbalanced brace would end the argument early.
        Assert.Equal("a\\{b\\}c", LaTeXExport.EscapeUrl("a{b}c"));
    }

    // MARK: - Preamble

    [Fact]
    public void LaTeXPlainDocumentHasShortPreamble()
    {
        // Nothing but the class and the input encoding: a document with no image, no link, no
        // table and no strikethrough owes no packages.
        Assert.Equal("\\documentclass{article}\n\\usepackage[utf8]{inputenc}\n", TexPreamble("# Title\n\nJust prose."));
    }

    [Fact]
    public void LaTeXPackagesFollowTheFeaturesUsed()
    {
        AssertContains(TexPreamble("![a](x.png)"), "\\usepackage{graphicx}");
        AssertContains(TexPreamble("[a](https://x.com)"), "\\usepackage{hyperref}");
        AssertContains(TexPreamble("| a |\n|---|\n| b |"), "\\usepackage{longtable}");
        // `normalem` is not decoration: plain ulem redefines `\emph` to underline.
        AssertContains(TexPreamble("~~gone~~"), "\\usepackage[normalem]{ulem}");
        AssertNotContains(TexPreamble("~~gone~~"), "\\usepackage{ulem}");
    }

    [Fact]
    public void LaTeXPackagesAreNotTriggeredByQuotedCode()
    {
        // The flags are set by the renderer as it emits each command, not by scanning the file —
        // a code block *about* LaTeX is not a link, an image or a table.
        var preamble = TexPreamble("```\n\\href{x}{y} \\includegraphics{z} \\begin{tabular}{l}\n```");
        AssertNotContains(preamble, "hyperref");
        AssertNotContains(preamble, "graphicx");
        AssertNotContains(preamble, "longtable");
    }

    [Fact]
    public void LaTeXHyperrefIsLoadedLast()
    {
        // The one package with a documented loading order.
        var preamble = TexPreamble("[a](https://x.com) ![b](c.png) ~~d~~\n\n| a |\n|---|\n| b |");
        var hyperref = preamble.IndexOf("\\usepackage{hyperref}", StringComparison.Ordinal);
        Assert.True(hyperref >= 0, "hyperref should be loaded");
        foreach (var other in new[] { "graphicx", "ulem", "longtable" })
        {
            var index = preamble.IndexOf(other, StringComparison.Ordinal);
            Assert.True(index >= 0, other + " should be loaded");
            Assert.True(index < hyperref, other + " must come before hyperref");
        }
    }

    [Fact]
    public void LaTeXCyrillicPullsInFontencWithT2ALast()
    {
        // Without a Cyrillic encoding pdfTeX does not stop — the Russian is simply not in the PDF.
        var preamble = TexPreamble("Привет, мир!");
        AssertContains(preamble, "\\usepackage[T1,T2A]{fontenc}");
        // fontenc makes the *last* encoding the default, so `[T2A,T1]` would drop exactly the
        // letters it was added for.
        AssertNotContains(preamble, "[T2A,T1]");
    }

    [Fact]
    public void LaTeXPlainEnglishGetsNoFontenc()
    {
        AssertNotContains(TexPreamble("Plain English prose."), "fontenc");
    }

    [Fact]
    public void LaTeXCyrillicInsideCodeStillPullsInFontenc()
    {
        // Verbatim content is typeset too.
        AssertContains(TexPreamble("```\n// комментарий\n```"), "fontenc");
    }

    [Fact]
    public void LaTeXCyrillicOnlyInAPrivateNoteDoesNotPullInFontenc()
    {
        // A private note never reaches the document, so it cannot make the document need anything.
        AssertNotContains(TexPreamble("English.\n\n<!-- note: Привет -->"), "fontenc");
    }

    [Fact]
    public void CyrillicScanCoversEveryBlockItClaims()
    {
        // Kotlin-only. Each of the four blocks at its first character — the one that gets
        // forgotten is the low end of the main block: U+0401 Ё is an everyday Russian letter.
        Assert.True(LaTeXExport.ContainsCyrillic(Chr(0x0400)), "U+0400");
        Assert.True(LaTeXExport.ContainsCyrillic(Chr(0x0401)), "U+0401");
        Assert.True(LaTeXExport.ContainsCyrillic(Chr(0x0501)), "U+0501");
        Assert.True(LaTeXExport.ContainsCyrillic(Chr(0x2DE0)), "U+2DE0");
        Assert.True(LaTeXExport.ContainsCyrillic(Chr(0xA640)), "U+A640");
        Assert.False(LaTeXExport.ContainsCyrillic(Chr(0x03FF)), "just below the block");
        Assert.False(LaTeXExport.ContainsCyrillic("Plain Latin, ancient Greek: α β γ"));
    }

    [Fact]
    public void CyrillicScanIsNotFooledBySurrogatePairs()
    {
        // Kotlin-only. Both halves of a supplementary character sit in D800–DFFF, clear of every
        // Cyrillic block, so the unit-by-unit scan is exact.
        Assert.False(LaTeXExport.ContainsCyrillic("emoji " + Chr(0x1F600) + " and " + Chr(0x1D504) + " and 中文"));
        Assert.True(LaTeXExport.ContainsCyrillic("mostly English but ё"));
        // …and the pair itself survives the escaper intact rather than as two lone surrogates.
        Assert.Equal("a " + Chr(0x1F600) + " b", LaTeXExport.Escape("a " + Chr(0x1F600) + " b"));
    }

    // MARK: - Front matter and the title block

    [Fact]
    public void LaTeXFrontMatterBecomesTitleBlock()
    {
        var tex = LaTeXExport.Document(Lines(
            "---", "title: On Escaping", "author: nettrash", "date: 24 July 2026",
            "slug: on-escaping", "tags: latex, md", "---", "", "Body."));
        AssertContains(tex, "\\title{On Escaping}");
        AssertContains(tex, "\\author{nettrash}");
        AssertContains(tex, "\\date{24 July 2026}");
        AssertContains(tex, "\\maketitle");
        // Everything else is metadata *about* the document and has nowhere to go in a typeset one.
        AssertNotContains(tex, "on-escaping");
        AssertNotContains(tex, "tags");
    }

    [Fact]
    public void LaTeXTitleBlockWithoutADateDoesNotInventOne()
    {
        // `\maketitle` stamps today when no date is given — a date nobody wrote.
        AssertContains(LaTeXExport.Document("---\ntitle: Untimed\n---\n\nBody."), "\\date{}");
    }

    [Fact]
    public void LaTeXWithoutFrontMatterHasNoTitleBlock()
    {
        var tex = LaTeXExport.Document("# Heading\n\nBody.");
        AssertNotContains(tex, "\\title");
        AssertNotContains(tex, "\\maketitle");
    }

    [Fact]
    public void LaTeXTitleFieldsAreEscaped()
    {
        AssertContains(LaTeXExport.Document("---\ntitle: 100% & more_stuff\n---\n\nBody."), "\\title{100\\% \\& more\\_stuff}");
    }

    [Fact]
    public void RepeatedFrontMatterKeyKeepsTheFirst()
    {
        // Kotlin-only. The parser records every line the author wrote; the rest of the app reads
        // the first, and so does the title block.
        var tex = LaTeXExport.Document("---\ntitle: First\ntitle: Second\n---\n\nBody.");
        AssertContains(tex, "\\title{First}");
        AssertNotContains(tex, "Second");
    }

    [Fact]
    public void FrontMatterKeysFoldAgainstTheRootLocale()
    {
        // Kotlin-only. `ToLower()` under a Turkish culture turns "TITLE" into "tıtle" with a
        // dotless ı, and the title block would vanish on that machine alone.
        WithCulture("tr-TR", () =>
            AssertContains(LaTeXExport.Document("---\nTITLE: Kitap\n---\n\nBody."), "\\title{Kitap}"));
    }

    [Fact]
    public void CyrillicOnlyInTheTitleBlockStillPullsInFontenc()
    {
        // Kotlin-only. The title is typeset like any other text and is not part of the body the
        // scan walks, so it is scanned in its own right.
        AssertContains(TexPreamble("---\ntitle: Привет\n---\n\nEnglish body."), "fontenc");
    }

    // MARK: - Headings

    [Fact]
    public void LaTeXHeadingLevelsMapOntoSectioning()
    {
        var expected = new[]
        {
            "\\section{H}", "\\subsection{H}", "\\subsubsection{H}",
            "\\paragraph{H}", "\\subparagraph{H}", "\\subparagraph{H}",
        };
        for (var level = 1; level <= 6; level++)
        {
            Assert.Equal(expected[level - 1], TexBody(new string('#', level) + " H"));
        }
    }

    [Fact]
    public void LaTeXHeadingTextIsEscapedAndFormatted()
    {
        Assert.Equal("\\section{50\\% \\textbf{off}}", TexBody("# 50% **off**"));
    }

    // MARK: - Mathematics

    [Fact]
    public void LaTeXInlineMathPassesThroughUnescaped()
    {
        // The reason the format exists: the formula comes back as the source the author typed.
        Assert.Equal("Then $a_1^{2} \\frac{x}{y}$ follows.", TexBody("Then $a_1^{2} \\frac{x}{y}$ follows."));
    }

    [Fact]
    public void LaTeXDisplayMathUsesTheBracketForm()
    {
        Assert.Equal("\\[x^2\\]", TexBody("$$x^2$$"));
        Assert.Equal("\\[x^2\\]", TexBody("\\[x^2\\]"));
        Assert.Equal("\\[\nx^2\n\\]", TexBody("```math\nx^2\n```"));
        // ```latex and ```tex name the same thing.
        Assert.Equal("\\[\nx^2\n\\]", TexBody("```latex\nx^2\n```"));
        Assert.Equal("\\[\nx^2\n\\]", TexBody("```tex\nx^2\n```"));
    }

    [Fact]
    public void LaTeXParenMathBecomesDollarMath()
    {
        Assert.Equal("A $a_i$ here.", TexBody("A \\(a_i\\) here."));
    }

    [Fact]
    public void LaTeXCurrencyIsNotMistakenForMathematics()
    {
        // The same guard the preview uses, so the two agree about what a formula is.
        Assert.Equal("It costs \\$5 and \\$10.", TexBody("It costs $5 and $10."));
    }

    [Fact]
    public void LaTeXWordGuardIsSpelledOutRatherThanBackslashW()
    {
        // `\w` is three different classes on three engines; the guard is `[\p{L}\p{N}_]` on every
        // port. A superscript two is `\p{N}` and *not* in ICU's `\w`, so it stops what follows
        // being a formula only under the spelled-out guard.
        Assert.Equal(Sup2 + "\\$x\\$", TexBody(Sup2 + "$x$"));
        Assert.Equal(Sup2 + "\\_x\\_", TexBody(Sup2 + "_x_"));
        // A combining mark is in ICU's `\w` and is not a letter, a number or an underscore.
        Assert.Equal("e" + Acute + "$x$", TexBody("e" + Acute + "$x$"));
        Assert.Equal("e" + Acute + "\\emph{x}", TexBody("e" + Acute + "_x_"));
    }

    [Fact]
    public void LaTeXMathInsideBackticksStaysCode()
    {
        // Code wins over math, exactly as in the preview.
        Assert.Equal("Use \\texttt{\\$x\\$} literally.", TexBody("Use `$x$` literally."));
    }

    [Fact]
    public void UnderscoreItalicGuardIsUnicodeAware()
    {
        // Kotlin-only. A Cyrillic word with an underscore in it is prose on every platform.
        Assert.Equal("ф\\_em\\_ф", TexBody("ф_em_ф"));
    }

    [Fact]
    public void AstralLettersAndDigitsGuardTheDelimitersLikeBmpOnes()
    {
        // .NET-only. Its regex is code-unit based: a lookbehind before `$` sees the low surrogate
        // of U+1D504 (category Cs) and lets the formula through where ICU, the JVM and JS (`u`)
        // see a letter and refuse it. Mathematical alphanumerics next to a formula are realistic.
        var frakturA = Chr(0x1D504);       // Lu
        var doubleStruckZero = Chr(0x1D7D8); // Nd
        var grinning = Chr(0x1F600);       // So — neither letter nor number, on every port
        Assert.Equal(frakturA + "\\$x\\$", TexBody(frakturA + "$x$"));
        Assert.Equal("\\$x\\$" + frakturA, TexBody("$x$" + frakturA));
        Assert.Equal(doubleStruckZero + "\\_x\\_", TexBody(doubleStruckZero + "_x_"));
        Assert.Equal("\\_x\\_" + doubleStruckZero, TexBody("_x_" + doubleStruckZero));
        Assert.Equal(grinning + "$x$", TexBody(grinning + "$x$"));
        Assert.Equal(grinning + "\\emph{x}", TexBody(grinning + "_x_"));
        // A rejected opener resumes the scan one position later, as ICU's failed lookbehind does,
        // so a legitimate formula further along the line is still found.
        Assert.Equal(frakturA + "\\$a\\$ and $b$", TexBody(frakturA + "$a$ and $b$"));
        Assert.Equal("a\\_b\\_c and \\emph{d}", TexBody("a_b_c and _d_"));

        // `\p{N}` is the three number categories, not the digits: a Roman numeral (Nl), a
        // vulgar fraction (No) and an Arabic-Indic digit (Nd) all stop a delimiter, and each is
        // one that at least one engine's `\w` would have let through. The whole guard was run
        // against the JavaScript port's `u`-flagged regexes over 7876 strings built from these
        // neighbours; the two agree match for match.
        Assert.Equal(Chr(0x2160) + "\\$x\\$", TexBody(Chr(0x2160) + "$x$"));
        Assert.Equal(Chr(0x00BD) + "\\$x\\$", TexBody(Chr(0x00BD) + "$x$"));
        Assert.Equal(Chr(0x06F1) + "\\_x\\_", TexBody(Chr(0x06F1) + "_x_"));
        // A zero-width space is neither, so it stops nothing. (Mid-line: the parser trims one at
        // the head of a paragraph, U+200B being white space to Foundation and so to Whitespace.)
        Assert.Equal("a" + Chr(0x200B) + "$x$", TexBody("a" + Chr(0x200B) + "$x$"));
    }

    // MARK: - Inline formatting

    [Fact]
    public void LaTeXEmphasisBecomesCommands()
    {
        Assert.Equal("\\textbf{b} \\textbf{b} \\emph{i} \\emph{i} \\sout{s}", TexBody("**b** __b__ *i* _i_ ~~s~~"));
    }

    [Fact]
    public void LaTeXSnakeCaseSurvivesUnderscoreItalic()
    {
        Assert.Equal("a\\_b\\_c is one word", TexBody("a_b_c is one word"));
    }

    [Fact]
    public void LaTeXInlineCodeIsTexttt()
    {
        // `\texttt` rather than `\verb`: this text has to survive inside a section title, a
        // caption and a table cell. Its content is escaped like any other text.
        Assert.Equal("Type \\texttt{a\\_b \\& c\\%}.", TexBody("Type `a_b & c%`."));
    }

    [Fact]
    public void LaTeXLinkBecomesHref()
    {
        Assert.Equal("See \\href{https://x.com/a_b}{the docs}.", TexBody("See [the docs](https://x.com/a_b)."));
    }

    [Fact]
    public void LaTeXLinkLabelKeepsItsOwnMarkup()
    {
        // The label stays ordinary text through the link pass, so the emphasis pass still finds it.
        Assert.Equal("\\href{https://x.com}{a \\textbf{bold} label}", TexBody("[a **bold** label](https://x.com)"));
    }

    [Fact]
    public void LaTeXLinkTitleIsConsumedNotPrinted()
    {
        // A title is a browser tooltip; a typeset page has nowhere to put one — but it must not be
        // left behind as stray prose either.
        Assert.Equal("\\href{https://x.com}{a}", TexBody("[a](https://x.com \"hover text\")"));
    }

    [Fact]
    public void LinkTitleSeparatorIsIcuWhitespaceAndTitleIsIcuDot()
    {
        // .NET-only. The Swift patterns use `\s` and `.`, which NSRegularExpression evaluates with
        // ICU's sets — measured on macOS: `\s` is [\t\n\v\f\r\u0085\p{Z}] (U+200B, U+FEFF and
        // U+180E excluded) and `.` refuses every line terminator (\n \v \f \r U+0085 U+2028
        // U+2029) but nothing else. .NET's own `\s` happens to agree; its `.` refuses only `\n`.
        var nbsp = Chr(0x00A0);
        var zwsp = Chr(0x200B);
        var nel = Chr(0x0085);
        var lsep = Chr(0x2028);
        // NBSP separates URL from title, so the title is consumed.
        Assert.Equal("\\href{https://x.com}{a}", TexBody("[a](https://x.com" + nbsp + "\"hover\")"));
        // ZWSP does not, so the whole run is the URL — quote and all.
        Assert.Equal("\\href{u" + zwsp + "\"t\"}{a}", TexBody("[a](u" + zwsp + "\"t\")"));
        // A line terminator inside the title stops `.`; neither link form matches and the text
        // stays the author's, escaped.
        Assert.Equal("[a](https://x.com \"ho" + nel + "ver\")", TexBody("[a](https://x.com \"ho" + nel + "ver\")"));
        Assert.Equal("[a](https://x.com \"ho" + lsep + "ver\")", TexBody("[a](https://x.com \"ho" + lsep + "ver\")"));
        // A zero-width space inside the title is an ordinary character to `.`.
        Assert.Equal("\\href{https://x.com}{a}", TexBody("[a](https://x.com \"ho" + zwsp + "ver\")"));
        // A byte-order mark is not in ICU's set either (JavaScript's `\s` does hold it), so it
        // belongs to the URL like the zero-width space.
        Assert.Equal("\\href{u" + Chr(0xFEFF) + "\"t\"}{a}", TexBody("[a](u" + Chr(0xFEFF) + "\"t\")"));
        // An Ogham space mark is Zs, and every Z is in the set.
        Assert.Equal("\\href{https://x.com}{a}", TexBody("[a](https://x.com" + Chr(0x1680) + "\"hover\")"));
        // The image forms share the classes.
        Assert.Equal("\\noindent\\includegraphics[width=\\linewidth]{p.png}", TexBody("![](p.png" + nbsp + "\"t\")"));
    }

    [Fact]
    public void LaTeXImageWithAltTextBecomesACaptionedFigure()
    {
        Assert.Equal(Lines(
            "\\begin{figure}[ht]",
            "\\centering",
            "\\includegraphics[width=\\linewidth]{img/a_1.png}",
            "\\caption{A wide shot}",
            "\\end{figure}"), TexBody("![A wide shot](img/a_1.png)"));
    }

    [Fact]
    public void LaTeXImageWithoutAltTextIsJustTheGraphic()
    {
        // No caption (an empty one prints a bare "Figure 1") and a `\noindent`, because a
        // `\linewidth` graphic starting a paragraph is pushed over the margin by the indent.
        Assert.Equal("\\noindent\\includegraphics[width=\\linewidth]{img/a.png}", TexBody("![](img/a.png)"));
    }

    [Fact]
    public void LaTeXAltTextIsRenderedFromTheMarkupTheAuthorWrote()
    {
        // The alt text is captured from a string the literal-span pass has already tokenised;
        // rendering it where it stands would leave that pass's tokens inside the caption. So the
        // caption is a fresh, complete pass over the Markdown they actually wrote.
        AssertContains(TexBody("![Alt with $x$ set](f.png)"), "\\caption{Alt with $x$ set}");
        AssertContains(TexBody("![Alt `c` and **b**](f.png)"), "\\caption{Alt \\texttt{c} and \\textbf{b}}");
        // Nothing of either pass is left in the file.
        AssertNotContains(TexBody("![Alt with $x$ and `c`](f.png)"), E000);
        AssertNotContains(TexBody("![Alt with $x$ and `c`](f.png)"), E001);
    }

    [Fact]
    public void CaptionKeepsTheAltTextsOwnMarkup()
    {
        // Kotlin-only. The caption is the alt text rendered as inline Markdown, as a moving argument.
        AssertContains(TexBody("![a **bold** shot](x.png)"), "\\caption{a \\textbf{bold} shot}");
    }

    [Fact]
    public void LaTeXImagePathIsPercentDecodedBeforeItIsWritten()
    {
        Assert.Equal("\\noindent\\includegraphics[width=\\linewidth]{my dir/a.png}", TexBody("![](my%20dir/a.png)"));
    }

    [Fact]
    public void LaTeXImagePathLaTeXCannotReadIsSkippedAndNamed()
    {
        // graphicx reads its argument as a file name, and there is no spelling of `%` or `#` that
        // works in one — so the file is skipped and named, and the alt text set in its place.
        var body = TexBody("Before ![alt](a#b.png) after.");
        AssertNotContains(body, "\\includegraphics");
        AssertContains(body, "\\emph{alt}", "the author's words survive");
        AssertContains(body, "% md: image skipped");
        AssertContains(body, "a#b.png", "the author is told which file");
        AssertContains(body, "Before");
        AssertContains(body, "after.", "nothing after the image is lost");
        // The comment comes last and ends its own line: `%` runs to the end of the physical line.
        AssertContains(body, "\\emph{alt}\n% md: image skipped");
        AssertContains(body, "LaTeX can read.\n");
        // A path that is not percent-encoding keeps its `%` and is refused for it.
        AssertContains(TexBody("![](100%.png)"), "% md: image skipped");
        // A package is not loaded for a graphic that was never drawn.
        AssertNotContains(TexPreamble("![](a#b.png)"), "graphicx");
    }

    // MARK: - Lists

    [Fact]
    public void LaTeXListsUseItemizeAndEnumerate()
    {
        Assert.Equal(Lines("\\begin{itemize}", "\\item a", "\\item b", "\\end{itemize}"), TexBody("- a\n- b"));
        Assert.Equal(Lines("\\begin{enumerate}", "\\item a", "\\item b", "\\end{enumerate}"), TexBody("1. a\n2. b"));
    }

    [Fact]
    public void LaTeXNestedListsNestEnvironments()
    {
        Assert.Equal(Lines(
            "\\begin{itemize}",
            "\\item a",
            "\\begin{itemize}",
            "\\item b",
            "\\begin{itemize}",
            "\\item c",
            "\\end{itemize}",
            "\\end{itemize}",
            "\\item d",
            "\\end{itemize}"), TexBody("- a\n  - b\n    - c\n- d"));
    }

    [Fact]
    public void LaTeXListStartingIndentedOpensOnlyOneEnvironment()
    {
        // Two `\begin{itemize}` in a row is "perhaps a missing \item".
        var body = TexBody("  - already indented\n    - deeper");
        AssertNotContains(body, "\\begin{itemize}\n\\begin{itemize}", "no environment may open without an item before it");
        Assert.Equal(2, Occurrences(body, "\\begin{itemize}"));
        Assert.Equal(2, Occurrences(body, "\\end{itemize}"));
    }

    [Fact]
    public void LaTeXDeepListStopsAtLaTeXsNestingLimit()
    {
        // LaTeX refuses to nest lists more than four deep; every item still has to appear.
        var source = string.Join("\n", Enumerable.Range(0, 6).Select(i => new string(' ', i * 2) + "- l" + i.ToString(CultureInfo.InvariantCulture)));
        var body = TexBody(source);
        Assert.Equal(4, Occurrences(body, "\\begin{itemize}"));
        for (var level = 0; level < 6; level++)
        {
            AssertContains(body, "\\item l" + level.ToString(CultureInfo.InvariantCulture), "l" + level + " must survive");
        }
    }

    [Fact]
    public void LaTeXTaskListRendersLiteralCheckboxes()
    {
        // `\item [x]` reads the bracket as the item's optional *label*; the empty group stops it.
        Assert.Equal(Lines("\\begin{itemize}", "\\item {}[\\,] open", "\\item {}[x] done", "\\end{itemize}"),
                     TexBody("- [ ] open\n- [x] done"));
    }

    [Fact]
    public void LaTeXItemBeginningWithABracketIsGuarded()
    {
        AssertContains(TexBody("- [draft] not a task"), "\\item {}[draft]");
        AssertContains(TexBody("- plain"), "\\item plain");
    }

    // MARK: - Code and diagrams

    [Fact]
    public void LaTeXCodeBlockIsVerbatimAndUnescaped()
    {
        Assert.Equal(Lines("\\begin{verbatim}", "if (a & b) { x_1 = 100%; }", "\\end{verbatim}"),
                     TexBody("```\nif (a & b) { x_1 = 100%; }\n```"));
    }

    [Fact]
    public void LaTeXCodeBlockQuotingVerbatimEndIsSplit()
    {
        // LaTeX's verbatim terminator is matched as *characters*, so a block that quotes
        // `\end{verbatim}` would close the environment early.
        var body = TexBody("```\nbefore\n\\end{verbatim}\nafter\n```");
        AssertContains(body, "\\verb|\\end{verbatim}|", "the terminator itself must be set with \\verb");
        AssertContains(body, "before");
        AssertContains(body, "after", "nothing after the hazard may be lost");
        var opens = Occurrences(body, "\\begin{verbatim}");
        var closes = Occurrences(body, "\\end{verbatim}");
        Assert.Equal(2, opens);
        Assert.Equal(opens + 1, closes);
    }

    [Fact]
    public void LaTeXDiagramSourceSurvivesAsVerbatim()
    {
        // LaTeX has no Mermaid, PlantUML or Graphviz; dropping the diagram would lose a figure.
        foreach (var language in new[] { "mermaid", "plantuml", "dot", "neato" })
        {
            var body = TexBody("```" + language + "\nA -> B\n```");
            AssertStartsWith(body, "% " + language + " diagram source");
            AssertContains(body, "\\begin{verbatim}\nA -> B\n\\end{verbatim}", language + " source must survive verbatim");
        }
        // The comment in full, to the byte — the dash is U+2014 in all four ports, and the
        // language is the author's own spelling, not the lowercased one the dispatch matched on.
        Assert.Equal("% Mermaid diagram source " + EmDash + " LaTeX has no renderer for it, so it is kept as written.\n"
                     + "\\begin{verbatim}\nA -> B\n\\end{verbatim}", TexBody("```Mermaid\nA -> B\n```"));
    }

    [Fact]
    public void IntegrationKeepsTheSourceInTheLaTeXExportUnderAComment()
    {
        // PlotTests.swift §8: the one rich fence the app can draw itself still travels as source.
        var tex = LaTeXExport.Document(Lines("```plot", "x: -10..10", "sin(x)", "```"));
        AssertContains(tex, "% plot diagram source");
        AssertContains(tex, "\\begin{verbatim}");
        AssertContains(tex, "sin(x)");
    }

    [Fact]
    public void FenceLanguageFoldsAgainstNoCulture()
    {
        // .NET-only. The dispatch lowercases the info string; a culture-sensitive lowercase
        // under Turkish turns `MERMAID` into `mermaıd` and the diagram becomes plain code.
        WithCulture("tr-TR", () =>
        {
            var body = TexBody("```MERMAID\nA -> B\n```");
            AssertStartsWith(body, "% MERMAID diagram source");
            Assert.Equal("\\[\nx^2\n\\]", TexBody("```MATH\nx^2\n```"));
            // The `data:` scheme test folds the same way.
            AssertContains(TexBody("![](DATA:image/png;base64,iVBORw0KGgo=)"), "% md: image skipped");
        });
    }

    // MARK: - Tables

    [Fact]
    public void LaTeXTableColumnSpecFollowsTheAlignments()
    {
        Assert.Equal(Lines(
            "\\begin{longtable}{lcr}",
            "\\hline",
            "\\textbf{L} & \\textbf{C} & \\textbf{R} \\\\",
            "\\hline",
            "\\endhead",
            "a & b & c \\\\",
            "\\hline",
            "\\end{longtable}"), TexBody("| L | C | R |\n|:--|:-:|--:|\n| a | b | c |"));
    }

    [Fact]
    public void LaTeXTableCellsAreEscapedAndKeepInlineMarkup()
    {
        AssertContains(TexBody("| a | b |\n|---|---|\n| 50% | $x^2$ **b** |"), "50\\% & $x^2$ \\textbf{b} \\\\");
    }

    [Fact]
    public void LaTeXTableRowBeginningWithABracketIsGuarded()
    {
        // `\\` followed by `[` is a row with a vertical skip.
        AssertContains(TexBody("| a |\n|---|\n| [draft] |"), "{}[draft] \\\\");
    }

    [Fact]
    public void LaTeXCSVBlockBecomesTheSameTable()
    {
        // The parse and the "a column of figures is right-aligned" rule are shared with the HTML renderer.
        var body = TexBody("```csv\nName,Qty\nBolt,12\nNut,3.5\n```");
        AssertContains(body, "\\begin{longtable}{lr}", "the numeric column is right-aligned");
        AssertContains(body, "\\textbf{Name} & \\textbf{Qty} \\\\");
        AssertContains(body, "Bolt & 12 \\\\");
        // A tab-separated block is the same table.
        AssertContains(TexBody("```tsv\nName\tQty\nBolt\t12\n```"), "\\begin{longtable}{lr}");
    }

    [Fact]
    public void ARowWiderThanItsHeaderKeepsEveryCell()
    {
        // Kotlin-only. The column count is the widest row, not the header's; the header's empty
        // third cell is not bold.
        var body = TexBody("```csv\na,b\nx,y,z\n```");
        AssertContains(body, "\\begin{longtable}{lll}");
        AssertContains(body, "\\textbf{a} & \\textbf{b} &  \\\\");
        AssertContains(body, "x & y & z \\\\", "the third cell must survive");
    }

    [Fact]
    public void DelimitedTableIsTheOneTheHtmlRendererUses()
    {
        // Kotlin-only. The shared parse is what keeps the two exports agreeing.
        var table = DelimitedTable.From("Name,Qty\nBolt,12\nNut,3.5", ',');
        Assert.NotNull(table);
        Assert.Equal(new[] { "Name", "Qty" }, table.Header);
        Assert.Equal(new[] { ColumnAlignment.Leading, ColumnAlignment.Trailing }, table.Alignments);
        Assert.Equal<IEnumerable<string>>(new[] { new[] { "Bolt", "12" }, new[] { "Nut", "3.5" } }, table.Rows);
    }

    [Fact]
    public void LaTeXUnparseableDelimitedBlockStaysCode()
    {
        // Nothing the author wrote disappears: a ```csv block that parses to no table is still their text.
        Assert.Equal("\\begin{verbatim}\n\n\\end{verbatim}", TexBody("```csv\n\n```"));
    }

    // MARK: - Quotes, rules and breaks

    [Fact]
    public void LaTeXQuotesRecurse()
    {
        Assert.Equal(Lines(
            "\\begin{quote}",
            "outer",
            "",
            "\\begin{quote}",
            "inner",
            "\\end{quote}",
            "\\end{quote}"), TexBody("> outer\n>\n> > inner"));
    }

    [Fact]
    public void LaTeXThematicBreakAndPageBreak()
    {
        Assert.Equal("\\par\\noindent\\hrulefill\\par", TexBody("***"));
        Assert.Equal("a\n\n\\newpage\n\nb", TexBody("a\n\n\\newpage\n\nb"));
    }

    [Fact]
    public void LaTeXPrivateNotesAreDropped()
    {
        Assert.Equal("before\n\nafter", TexBody("before\n\n<!-- note: private -->\n\nafter"));
    }

    [Fact]
    public void LaTeXSoftBreaksBecomeLineBreaks()
    {
        // md shows a soft break as a break in the preview, the HTML and the PDF; the .tex agrees.
        Assert.Equal("one\\\\\ntwo\\\\\nthree", TexBody("one\ntwo\nthree"));
        // Never a trailing `\\` — "there's no line here to end" is an error.
        Assert.False(TexBody("one\ntwo").EndsWith("\\\\", StringComparison.Ordinal));
    }

    [Fact]
    public void LaTeXSoftBreakBeforeABracketIsGuarded()
    {
        // `\\` followed by `[` is read as `\\[length]`.
        Assert.Equal("line\\\\\n{}[bracketed]", TexBody("line\n[bracketed]"));
    }

    [Fact]
    public void LaTeXSoftBreakBeforeAnUndefinedFootnoteIsGuarded()
    {
        // While the lines are being split the reference is still a token; the guard has to run on
        // the restored line, like the other two.
        Assert.Equal("line one\\\\\n{}[\\textasciicircum{}missing] line two", TexBody("line one\n[^missing] line two"));
    }

    // MARK: - Footnotes

    [Fact]
    public void LaTeXFootnoteIsInlinedAtItsFirstReference()
    {
        // What a LaTeX footnote *is* — which is why this export has no collected list at the foot.
        Assert.Equal("Text\\footnote{The note with \\emph{emphasis}.} here.",
                     TexBody("Text[^a] here.\n\n[^a]: The note with *emphasis*."));
    }

    [Fact]
    public void LaTeXRepeatedFootnoteReferencesCiteTheNumber()
    {
        Assert.Equal("A\\footnote{one} B\\footnote{two} C\\footnotemark[1].",
                     TexBody("A[^a] B[^b] C[^a].\n\n[^a]: one\n\n[^b]: two"));
    }

    [Fact]
    public void FootnoteNumbersFollowReadingOrderNotSpliceOrder()
    {
        // Kotlin-only. Replacements are built in reading order and only then spliced; build them
        // backwards and the texts land under the wrong marks.
        Assert.Equal("A\\footnote{n1} B\\footnotemark[1] C\\footnote{n2}.",
                     TexBody("A[^a] B[^a] C[^b].\n\n[^a]: n1\n\n[^b]: n2"));
    }

    [Fact]
    public void LaTeXUndefinedFootnoteReferenceStaysLiteral()
    {
        Assert.Equal("Text[\\textasciicircum{}missing] here.", TexBody("Text[^missing] here."));
    }

    [Fact]
    public void LaTeXUncitedFootnoteIsStillPrinted()
    {
        var body = TexBody("Prose.\n\n[^unused]: Nobody points at this.");
        AssertContains(body, "% Footnotes defined but never referenced");
        AssertContains(body, "% Footnotes defined but never referenced " + EmDash + " kept so nothing is lost.");
        AssertContains(body, "\\footnote{Nobody points at this.}");
    }

    [Fact]
    public void LaTeXFootnoteInsideAFootnoteDegradesToText()
    {
        // LaTeX cannot nest footnotes; the inner reference becomes text, and its definition — now
        // uncited — is printed at the end rather than lost.
        var body = TexBody("A[^a].\n\n[^a]: See[^b].\n\n[^b]: The target.");
        AssertContains(body, "\\footnote{See[\\textasciicircum{}b].}");
        AssertContains(body, "\\footnote{The target.}");
    }

    [Fact]
    public void LaTeXFootnoteInAHeadingIsProtected()
    {
        // A section title is a moving argument — a bare `\footnote` there is a compile error.
        Assert.Equal("\\section{Head\\protect\\footnote{note}}", TexBody("# Head[^a]\n\n[^a]: note"));
    }

    [Fact]
    public void LaTeXFootnoteDefinitionRendersNothingWhereItWasWritten()
    {
        Assert.Equal("A\\footnote{note}.\n\nB.", TexBody("A[^a].\n\n[^a]: note\n\nB."));
    }

    // MARK: - Restricted places

    [Fact]
    public void LaTeXDisplayMathInATableCellIsSetInline()
    {
        // `\[` inside a table stops the *file*, not the cell.
        var body = TexBody("| formula |\n|---------|\n| $$x^2$$ |");
        AssertContains(body, "$x^2$ \\\\");
        AssertNotContains(body, "\\[");
        AssertContains(TexBody("| a |\n|---|\n| \\[x^2\\] |"), "$x^2$ \\\\");
    }

    [Fact]
    public void LaTeXDisplayMathInAFootnoteIsSetInline()
    {
        Assert.Equal("A\\footnote{$x^2$ ends it.}.", TexBody("A[^a].\n\n[^a]: $$x^2$$ ends it."));
    }

    [Fact]
    public void LaTeXImageInATableCellIsNotAFloat()
    {
        // "LaTeX Error: Not in outer par mode." — and the whole document stops there.
        var body = TexBody("| picture |\n|---------|\n| ![A caption](a.png) |");
        AssertContains(body, "\\includegraphics[width=\\linewidth]{a.png} \\emph{A caption} \\\\");
        AssertNotContains(body, "\\begin{figure}");
        AssertNotContains(body, "\\caption");
    }

    [Fact]
    public void LaTeXImageInAFootnoteIsNotAFloat()
    {
        var body = TexBody("Text[^a] here.\n\n[^a]: See ![A caption](a.png) for detail.");
        AssertContains(body, "\\footnote{See \\includegraphics[width=\\linewidth]{a.png} \\emph{A caption} for detail.}");
        AssertNotContains(body, "\\begin{figure}");
    }

    [Fact]
    public void LaTeXFootnoteCitedFromATableCellKeepsItsWords()
    {
        // A `longtable` is not a float, so an ordinary `\footnote` sets its own words in the cell.
        var body = TexBody("| cell |\n|------|\n| A[^a] |\n\n[^a]: Zarquon lives here.");
        AssertContains(body, "A\\footnote{Zarquon lives here.} \\\\");
        AssertNotContains(body, "\\footnotemark");
        AssertNotContains(body, "\\footnotetext");
    }

    [Fact]
    public void LaTeXFootnoteNumbersKeepAgreeingAcrossATable()
    {
        var body = TexBody(Lines("| cell |", "|------|", "| A[^a] B[^a] |", "", "Then C[^b].", "", "[^a]: first", "", "[^b]: second"));
        AssertContains(body, "A\\footnote{first} B\\footnotemark[1]");
        AssertContains(body, "Then C\\footnote{second}.");
    }

    [Fact]
    public void LaTeXRestrictionIsInheritedByAFootnoteRaisedFromATable()
    {
        var body = TexBody("| a |\n|---|\n| A[^a] |\n\n[^a]: $$x^2$$ and ![cap](a.png)");
        AssertContains(body, "\\footnote{$x^2$ and \\includegraphics[width=\\linewidth]{a.png} \\emph{cap}}");
        AssertNotContains(body, "\\begin{figure}");
        AssertNotContains(body, "\\[");
    }

    [Fact]
    public void LaTeXTableEndsAtItsOwnEnd()
    {
        AssertEndsWith(TexBody("| a |\n|---|\n| b |"), "\\end{longtable}");
        AssertNotContains(TexBody("| a |\n|---|\n| b |"), "\\footnotetext");
    }

    // MARK: - Books

    [Fact]
    public void LaTeXBookUsesTheBookClassAndTheReadingOrder()
    {
        var tex = LaTeXExport.Book(SampleBook());
        AssertStartsWith(tex, "\\documentclass{book}\n");
        AssertContains(tex, "\\title{The Book}");
        AssertContains(tex, "\\maketitle");
        // Root articles first, then each chapter with its articles.
        var order = new[]
        {
            "\\section{Preface}", "\\chapter{One}", "\\section{First}",
            "\\section{Second}", "\\chapter{Two}", "\\section{Third}",
        };
        var cursor = 0;
        foreach (var piece in order)
        {
            var found = tex.IndexOf(piece, cursor, StringComparison.Ordinal);
            Assert.True(found >= 0, piece + " is missing or out of order");
            cursor = found + piece.Length;
        }
    }

    [Fact]
    public void LaTeXBookPushesArticleHeadingsBelowTheirSection()
    {
        // An article is already a `\section`, so its own `#` has to become a `\subsection`.
        AssertContains(LaTeXExport.Book(SampleBook()), "\\subsection{Inner}");
    }

    [Fact]
    public void LaTeXBookRestartsFootnoteNumbersAtEachChapter()
    {
        // `book` resets the footnote counter at every `\chapter`: the third chapter's repeated
        // note is number 1 again, not number 3.
        AssertContains(LaTeXExport.Book(SampleBook()), "C\\footnote{n3} C\\footnotemark[1].");
    }

    [Fact]
    public void BookFootnoteNumbersRunOnAcrossArticles()
    {
        // Kotlin-only. A chapter is one footnote counter, however many articles it holds — and an
        // uncited definition is printed as a real `\footnote`, so it takes a number too.
        var book = OneChapter("C",
            new BookUnit("Orphan", "Prose.\n\n[^x]: nobody cites this"),
            new BookUnit("Cites", "D[^d] D[^d].\n\n[^d]: n"));
        AssertContains(LaTeXExport.Book(book), "D\\footnote{n} D\\footnotemark[2].");
    }

    [Fact]
    public void BookArticlesDoNotBorrowEachOthersFootnotes()
    {
        // Kotlin-only. A definition belongs to the article it was written in.
        var book = OneChapter("C",
            new BookUnit("Defines", "P[^a].\n\n[^a]: only here"),
            new BookUnit("Borrows", "Q[^a]."));
        var tex = LaTeXExport.Book(book);
        AssertContains(tex, "P\\footnote{only here}.");
        AssertContains(tex, "Q[\\textasciicircum{}a].");
    }

    [Fact]
    public void BookChapterForgetsTheNumbersItHandedOut()
    {
        // Kotlin-only. Resetting the counter at a `\chapter` is only half of it: the id -> number
        // map has to go with it, or the second chapter cites the first chapter's number.
        var book = new StructuredBook("B", [], [
            new BookSection("One", [new BookUnit("A", "X[^a] Y[^a].\n\n[^a]: first")]),
            new BookSection("Two", [new BookUnit("B", "Z[^a] W[^a].\n\n[^a]: second")]),
        ]);
        var tex = LaTeXExport.Book(book);
        AssertContains(tex, "X\\footnote{first} Y\\footnotemark[1].");
        AssertContains(tex, "Z\\footnote{second} W\\footnotemark[1].");
    }

    [Fact]
    public void LaTeXBookCollectsPackagesAcrossEveryArticle()
    {
        // The preamble belongs to the whole file.
        var book = OneChapter("C",
            new BookUnit("Plain", "Nothing special."),
            new BookUnit("Rich", "![a](x.png) ~~b~~"));
        var tex = LaTeXExport.Book(book);
        AssertContains(tex, "\\usepackage{graphicx}");
        AssertContains(tex, "\\usepackage[normalem]{ulem}");
    }

    [Fact]
    public void LaTeXBookTitlesAreEscaped()
    {
        var book = new StructuredBook("R&D 100%", [], [new BookSection("A_B", [])]);
        var tex = LaTeXExport.Book(book);
        AssertContains(tex, "\\title{R\\&D 100\\%}");
        AssertContains(tex, "\\chapter{A\\_B}");
    }

    [Fact]
    public void LaTeXBookArticlesEachNumberTheirOwnNotes()
    {
        // Two articles of one chapter, each numbering its own notes from `[^1]` — carrying the
        // numbers across made the second's `[^1]` a `\footnotemark[1]` citing the first's note.
        var book = OneChapter("C",
            new BookUnit("One", "First[^1].\n\n[^1]: The first note."),
            new BookUnit("Two", "Second[^1].\n\n[^1]: The second note."));
        var tex = LaTeXExport.Book(book);
        AssertContains(tex, "First\\footnote{The first note.}");
        AssertContains(tex, "Second\\footnote{The second note.}", "the second article's own note must be printed");
        AssertNotContains(tex, "\\footnotemark");
    }

    // MARK: - Whole documents

    [Fact]
    public void LaTeXDocumentIsWellFormed()
    {
        var tex = LaTeXExport.Document("# H\n\nBody with $x$ and a [link](https://x.com).");
        AssertStartsWith(tex, "\\documentclass{article}\n");
        AssertEndsWith(tex, "\\end{document}\n");
        Assert.Equal(1, Occurrences(tex, "\\begin{document}"));
        Assert.Equal(1, Occurrences(tex, "\\end{document}"));
    }

    [Fact]
    public void LaTeXEmptyDocumentStillCompilesAsOne()
    {
        var tex = LaTeXExport.Document("");
        AssertContains(tex, "\\begin{document}");
        AssertContains(tex, "\\end{document}");
        // Three blank lines: the empty body between the two empty spacer lines.
        Assert.Equal("\\documentclass{article}\n\\usepackage[utf8]{inputenc}\n\\begin{document}\n\n\n\n\\end{document}\n", tex);
    }

    [Fact]
    public void TitleOverloadWritesNoTitle()
    {
        // The app-layer shape (core-api.md Part B) carries the file's name for the save step;
        // a file name is not part of a document, so nothing of it reaches the .tex.
        var tex = LaTeXExport.Document("Body.", "My File");
        Assert.Equal(LaTeXExport.Document("Body."), tex);
        AssertNotContains(tex, "\\title");
        AssertNotContains(tex, "My File");
    }

    [Fact]
    public void LaTeXNoCommandItEmitsIsEverEscaped()
    {
        // After a document full of specials and markup, no command this file produced has been
        // through the escaper (`\textbackslash{}textbf` would be the symptom).
        var tex = LaTeXExport.Document(Lines(
            "**bold** with 100% & _under_ and `a_b`, a [link](https://x.com/a_b),",
            "![alt](p.png), $x_1^2$ and ~~gone~~.",
            "",
            "| a & b |",
            "|-------|",
            "| 50%   |"));
        AssertNotContains(tex, "\\textbackslash{}text");
        AssertNotContains(tex, "\\textbackslash{}begin");
        AssertNotContains(tex, "\\{}", "no command's braces were escaped");
        AssertContains(tex, "\\textbf{bold}");
        AssertContains(tex, "$x_1^2$");
    }

    // MARK: - Scalar-exact string work (regression — third review)
    //
    // Swift's stdlib matches grapheme clusters, so every delimiter guard was blind to an ASCII
    // character with a mark after it. C# is UTF-16 like Kotlin; these pin that the ordinal
    // operations chosen here keep firing.

    [Fact]
    public void LaTeXEveryEscapeFiresWithACombiningMarkAfterIt()
    {
        var table = new[]
        {
            ("#", "\\#"), ("$", "\\$"), ("%", "\\%"), ("&", "\\&"), ("_", "\\_"), ("{", "\\{"), ("}", "\\}"),
            ("~", "\\textasciitilde{}"), ("^", "\\textasciicircum{}"), ("\\", "\\textbackslash{}"),
        };
        foreach (var (special, escaped) in table)
        {
            foreach (var mark in Marks)
            {
                Assert.Equal("a" + escaped + mark + "b", LaTeXExport.Escape("a" + special + mark + "b"));
            }
        }
        // …and the URL table, which is a different set of characters.
        foreach (var (special, escaped) in new[] { ("%", "\\%"), ("#", "\\#"), ("{", "\\{"), ("}", "\\}"), ("\\", "\\textbackslash{}") })
        {
            foreach (var mark in Marks)
            {
                Assert.Equal("a" + escaped + mark + "b", LaTeXExport.EscapeUrl("a" + special + mark + "b"));
            }
        }
    }

    [Fact]
    public void LaTeXEveryGuardFiresWithACombiningMarkAfterIt()
    {
        foreach (var mark in Marks)
        {
            // The bracket guard, at all three of its callers.
            AssertContains(TexBody("- [" + mark + "draft] item"), "\\item {}[", "list item guard");
            AssertContains(TexBody("line\n[" + mark + "draft]"), "\\\\\n{}[", "soft-break guard");
            AssertContains(TexBody("| a |\n|---|\n| [" + mark + "draft] |"), "{}[", "table row guard");

            // The percent-decoder's own early exit.
            Assert.Equal("a%" + mark + "b", LaTeXExport.PercentDecoded("a%" + mark + "b"));
            AssertContains(TexBody("![](a%" + mark + "b.png)"), "% md: image skipped", "an unreadable path must still be refused");
        }
    }

    [Fact]
    public void LaTeXVerbatimIsSplitAroundATerminatorCarryingAMark()
    {
        foreach (var mark in Marks)
        {
            var body = TexBody("```\nbefore\n\\end{verbatim}" + mark + "\nafter\n```");
            AssertContains(body, "\\verb|\\end{verbatim}|", "the terminator must be set with \\verb");
            AssertContains(body, "after", "nothing after it may be lost");
            Assert.Equal(2, Occurrences(body, "\\begin{verbatim}"));
        }
    }

    [Fact]
    public void LaTeXTokenRestoreSurvivesAMarkAfterTheToken()
    {
        foreach (var mark in Marks)
        {
            var body = TexBody("A `code` span" + mark + " and $x^2$" + mark + " after.");
            AssertNotContains(body, E000, "no token may reach the file");
            AssertNotContains(body, E001);
            Assert.True(ScalarText.Contains(body, "\\texttt{code}"));
            Assert.True(ScalarText.Contains(body, "$x^2$"));
        }
    }

    [Fact]
    public void LaTeXSoftBreaksSplitOnEveryNewlineIncludingCRLF()
    {
        // `\r\n` is one grapheme cluster in Swift; the split here is on code units.
        Assert.Equal(new[] { "a\r", "b" }, ScalarText.Split("a\r\nb", "\n"));
        Assert.Equal(new[] { "a", "b", "c" }, ScalarText.Split("a\nb\nc", "\n"));
        Assert.Equal(new[] { "" }, ScalarText.Split("", "\n"));
        Assert.Equal(new[] { "a", "b", "c" }, ScalarText.Split("aXbXc", "X"));
        Assert.Equal(new[] { "", "a", "" }, ScalarText.Split("XaX", "X"));
        Assert.Equal(1, "\r\n".IndexOf("\n", StringComparison.Ordinal));
        // A CRLF document is normalised by the parser before it reaches the writer.
        Assert.Equal("a\\\\\nb", TexBody("a\r\nb"));
    }

    [Fact]
    public void LaTeXScalarHelpersAreExactWhereTheStdlibIsNot()
    {
        // Stated as the property the writer depends on. (The Swift-stdlib negative halves have no
        // counterpart: the ordinal BCL agrees with ScalarText here.)
        var marked = "a%" + Acute + "b";
        Assert.True(ScalarText.Contains(marked, "%"));
        Assert.True(marked.Contains("%", StringComparison.Ordinal));

        Assert.True(ScalarText.HasPrefix("[" + Vs16 + "x", "["));
        Assert.True(("[" + Vs16 + "x").StartsWith("[", StringComparison.Ordinal), "ordinal StartsWith, not xUnit's culture-sensitive Assert.StartsWith");

        Assert.True(ScalarText.HasSuffix("x\n", "\n"));
        Assert.False(ScalarText.HasSuffix("x", "\n"));

        Assert.Equal("a!" + Acute + "b", ScalarText.Replacing("a" + E000 + "0" + E001 + Acute + "b", E000 + "0" + E001, "!"));
        Assert.Equal("a!" + Acute + "b", ("a" + E000 + "0" + E001 + Acute + "b").Replace(E000 + "0" + E001, "!", StringComparison.Ordinal));
    }

    // MARK: - Tables break across pages (regression — third review)

    [Fact]
    public void LaTeXTableIsALongtableAndNotAFloat()
    {
        // A float cannot break across a page, so a table taller than one is *truncated* at exit 0.
        var rows = string.Join("\n", Enumerable.Range(1, 70).Select(i => "| r" + i.ToString(CultureInfo.InvariantCulture) + " | c" + i.ToString(CultureInfo.InvariantCulture) + " |"));
        var body = TexBody("| A | B |\n|---|---|\n" + rows);
        AssertStartsWith(body, "\\begin{longtable}{ll}");
        AssertEndsWith(body, "\\end{longtable}");
        AssertNotContains(body, "\\begin{table}");
        AssertNotContains(body, "\\begin{tabular}");
        for (var index = 1; index <= 70; index++)
        {
            var n = index.ToString(CultureInfo.InvariantCulture);
            AssertContains(body, "r" + n + " & c" + n + " \\\\", "row " + n);
        }
    }

    [Fact]
    public void LaTeXLongtableRepeatsItsHeaderOnEveryPage()
    {
        var body = TexBody("| A | B |\n|---|---|\n| a | b |");
        var head = body.IndexOf("\\endhead", StringComparison.Ordinal);
        Assert.True(head >= 0, "the header must be marked as one");
        AssertContains(body.Substring(0, head), "\\textbf{A} & \\textbf{B}", "the header row belongs above \\endhead");
        AssertContains(body.Substring(head + "\\endhead".Length), "a & b \\\\", "the body rows below it");
    }

    // MARK: - Images LaTeX cannot include (regression — third review)

    [Fact]
    public void LaTeXImagePathWithBracesOrABackslashIsSkipped()
    {
        // `escapeURL` turns each of these into a control sequence, which is fatal for graphicx.
        foreach (var path in new[] { "img/{a}.png", "img/a}.png", "img\\b.png" })
        {
            var body = TexBody("Before ![alt](" + path + ") after.");
            AssertNotContains(body, "\\includegraphics", path);
            AssertContains(body, "% md: image skipped", path);
            AssertContains(body, "\\emph{alt}", "the alt text survives " + path);
            AssertContains(body, "after.", "nothing after it is lost");
        }
        // A path with none of them is still an image.
        AssertContains(TexBody("![](img/a-b_1.png)"), "\\includegraphics");
    }

    [Fact]
    public void LaTeXRemoteAndDataImagesAreSkippedAndNamed()
    {
        // TeX fetches nothing, and a `data:` URI is a picture with no file name at all.
        foreach (var path in new[] { "https://nettrash.me/favicon.ico", "http://x.com/a.png", "data:image/png;base64,iVBORw0KGgo=" })
        {
            var body = TexBody("![A picture](" + path + ")");
            AssertNotContains(body, "\\includegraphics", path);
            AssertContains(body, "% md: image skipped", path);
            AssertContains(body, path, "the author is told which one");
            AssertContains(body, "\\emph{A picture}", "the alt text survives");
        }
        // A relative path with a colon in it is a file somebody can really have.
        AssertContains(TexBody("![](notes:draft.png)"), "\\includegraphics");
    }

    [Fact]
    public void LaTeXAltTextSurvivesWhereThereIsNoCaption()
    {
        // Three places the author's own description of a picture used to be dropped without a word.
        AssertContains(TexBody("| p |\n|---|\n| ![In a cell](a.png) |"), "\\emph{In a cell}");
        AssertContains(TexBody("A[^a].\n\n[^a]: ![In a note](a.png)"), "\\emph{In a note}");
        AssertContains(TexBody("![On a skipped one](a#b.png)"), "\\emph{On a skipped one}");
        // It is the Markdown the author wrote, rendered — not the raw text.
        AssertContains(TexBody("| p |\n|---|\n| ![Alt with `c`](a.png) |"), "\\emph{Alt with \\texttt{c}}");
    }

    [Fact]
    public void LaTeXSoftBreakAfterAnImageAloneOnItsLineIsNotWritten()
    {
        // `\end{figure}\\` is "There's no line here to end" — a float begins no line.
        var figure = TexBody("![A caption](a.png)\nafter");
        AssertContains(figure, "\\end{figure}\nafter");
        AssertNotContains(figure, "\\end{figure}\\\\");

        // A skipped image with alt text does set a line, so the break after it is written.
        var skipped = TexBody("![Alt](https://x.com/a.png)\nafter");
        AssertContains(skipped, "\\emph{Alt}");
        AssertContains(skipped, "after");
        AssertNotContains(skipped, "\\\\\n\n", "never a blank line after a \\\\");

        // Without alt text there is nothing but the comment, so no break before it and none after.
        var bare = TexBody("![](https://x.com/a.png)\nafter");
        AssertContains(bare, "% md: image skipped");
        AssertContains(bare, "after");
        AssertNotContains(bare, "\\\\", "a comment begins no line to end");
        AssertNotContains(bare, "\n\n", "and no paragraph break either");

        // An ordinary pair of lines still breaks.
        Assert.Equal("one\\\\\ntwo", TexBody("one\ntwo"));
    }

    // MARK: - Mathematics that carries its own separators

    [Fact]
    public void LaTeXDisplayMathWithItsOwnSeparatorsGetsAnAligned()
    {
        // `\[a &= b\]` is "Misplaced alignment tab character" in body text; in a table cell a
        // top-level `\\` ends the *row* from inside math mode.
        Assert.Equal("\\[\\begin{aligned}a &= b \\\\ c &= d\\end{aligned}\\]", TexBody("$$a &= b \\\\ c &= d$$"));
        AssertContains(TexBody("| f |\n|---|\n| $$a \\\\ b$$ |"), "$\\begin{aligned}a \\\\ b\\end{aligned}$ \\\\");
        // A formula that opens an environment of its own already owns its separators.
        Assert.Equal("\\[\\begin{aligned} p &= q \\end{aligned}\\]", TexBody("$$\\begin{aligned} p &= q \\end{aligned}$$"));
        // And a formula with neither is left exactly as written.
        Assert.Equal("\\[x^2\\]", TexBody("$$x^2$$"));
        // The helper itself.
        Assert.Equal("x^2", LaTeXExport.Aligned("x^2"));
        Assert.Equal("\\begin{aligned}a &= b\\end{aligned}", LaTeXExport.Aligned("a &= b"));
        Assert.Equal("\\begin{cases} a \\\\ b \\end{cases}", LaTeXExport.Aligned("\\begin{cases} a \\\\ b \\end{cases}"));
    }

    [Fact]
    public void LaTeXAmsmathIsLoadedForAnyMathematics()
    {
        // `\begin{aligned}` in a ```math fence is "Environment aligned undefined" without it.
        foreach (var source in new[]
        {
            "$x^2$", "$$x^2$$", "\\[x^2\\]", "\\(x^2\\)",
            "```math\n\\begin{aligned}a &= b\\end{aligned}\n```",
            "| f |\n|---|\n| $x$ |",
        })
        {
            AssertContains(TexPreamble(source), "\\usepackage{amsmath}", source);
        }
        AssertNotContains(TexPreamble("Just prose."), "amsmath");
        AssertNotContains(TexPreamble("```\n$x^2$\n```"), "amsmath", "a code block quoting a formula is not one");
    }

    // MARK: - The last silent losses (regression — fourth review)

    [Fact]
    public void LaTeXFootnoteCitedFromATableHeaderKeepsItsWords()
    {
        // A `longtable` typesets its header row *once*, into the box `\endhead` reinserts — and
        // LaTeX throws a footnote insertion made inside a box away. So the head carries the mark
        // and the note's text is written after the table.
        var body = TexBody("| h1[^1] | h2 |\n|---|---|\n| c1 | c2 |\n\n[^1]: Zarquon in the head.");
        AssertContains(body, "\\textbf{h1\\stepcounter{footnote}\\footnotemark[1]}");
        AssertContains(body, "\\end{longtable}\n\\footnotetext[1]{Zarquon in the head.}");
        AssertNotContains(body, "\\footnote{Zarquon", "never an insertion inside the saved head box");

        // `\footnotemark[n]` does not step LaTeX's counter, so the head steps it by hand.
        var mixed = TexBody(Lines(
            "| ha[^a] | hb |", "|--------|----|", "| b[^b]  | c  |", "", "Tail[^c].", "",
            "[^a]: note a", "", "[^b]: note b", "", "[^c]: note c"));
        AssertContains(mixed, "\\textbf{ha\\stepcounter{footnote}\\footnotemark[1]}");
        AssertContains(mixed, "b\\footnote{note b}");
        AssertContains(mixed, "Tail\\footnote{note c}.");
        AssertContains(mixed, "\\end{longtable}\n\\footnotetext[1]{note a}");

        // A note cited again from a body cell is the number, as ever.
        AssertContains(TexBody("| h[^a] |\n|---|\n| A[^a] |\n\n[^a]: n"), "A\\footnotemark[1] \\\\");

        // A note first cited from a body cell keeps the plain `\footnote` it always had.
        var plain = TexBody("| h |\n|---|\n| A[^a] |\n\n[^a]: n");
        AssertContains(plain, "A\\footnote{n} \\\\");
        AssertNotContains(plain, "\\footnotetext");
        AssertEndsWith(plain, "\\end{longtable}");
    }

    [Fact]
    public void LaTeXHeaderCellCarryingAnAlignmentTabIsGrouped()
    {
        // `\textbf` reads its argument with a delimited macro that a top-level `&` ends the row
        // out from under; an extra group is the whole fix.
        AssertContains(TexBody("| $a &= b$ | h |\n|---|---|\n| c | d |"), "\\textbf{{$\\begin{aligned}a &= b\\end{aligned}$}}");
        AssertContains(TexBody("| [x](http://e.com/?a=1&b=2) | h |\n|---|---|\n| c | d |"), "\\textbf{{\\href{http://e.com/?a=1&b=2}{x}}}");

        // Written only where there is a tab to guard.
        AssertContains(TexBody("| A | B |\n|---|---|\n| a | b |"), "\\textbf{A} & \\textbf{B}");
        AssertContains(TexBody("| a & b | B |\n|---|---|\n| c | d |"), "\\textbf{a \\& b} & \\textbf{B}");

        // A body cell needs none of it.
        AssertContains(TexBody("| h |\n|---|\n| $a &= b$ |"), "$\\begin{aligned}a &= b\\end{aligned}$ \\\\");
    }

    [Fact]
    public void LaTeXImagePathWithADoubleQuoteIsSkipped()
    {
        // graphicx quotes a file name with spaces in a pair of `"`, so one the author wrote breaks
        // graphicx's own parser.
        Assert.NotNull(LaTeXExport.UnreadableImage("a\"b.png"));
        var body = TexBody("Before ![alt](a\"b.png) after.");
        AssertNotContains(body, "\\includegraphics");
        AssertContains(body, "% md: image skipped " + EmDash + " a\"b.png");
        AssertContains(body, "\\emph{alt}", "the alt text survives");
        AssertContains(body, "after.", "and so does the rest of the sentence");

        // A `"` in a *link* is refused nothing.
        Assert.Null(LaTeXExport.UnreadableImage("a-b_1.png"));
        AssertContains(TexBody("[x](http://e.com/a\"b)"), "\\href{http://e.com/a\"b}{x}");
        // The two reasons, verbatim.
        Assert.Equal("is not a file name LaTeX can read", LaTeXExport.UnreadableImage("a#b.png"));
        Assert.Equal("is a URL, and LaTeX has nothing to fetch it with", LaTeXExport.UnreadableImage("https://x.com/a.png"));
        // A Windows drive letter is not a scheme (no `//`, and not `data:`), so it is refused for
        // its backslash and nothing else — and the same path with a forward slash is a file.
        Assert.Equal("is not a file name LaTeX can read", LaTeXExport.UnreadableImage("C:" + Chr(0x5C) + "x.png"));
        Assert.Null(LaTeXExport.UnreadableImage("C:/x.png"));
    }

    [Fact]
    public void LaTeXAuthorsOwnTokenSentinelsCannotBeReadAsTokens()
    {
        // U+E000 and U+E001 are what an inline pass wraps a span index in; a document carrying
        // them of its own had its *own* characters read back as a token index.
        Assert.Equal("text \\textbf{b 0 x} more", TexBody("text **b " + E000 + "0" + E001 + " x** more"));
        // Every other place the author's text is read: a formula, a code span, a table cell, and
        // the front matter, which never reaches an inline pass at all.
        Assert.Equal("A $x_1$ and \\texttt{cd} here.", TexBody("A $x" + E000 + "_1" + E001 + "$ and `c" + E000 + "d` here."));
        AssertContains(TexBody("| h" + E001 + "1 | b |\n|---|---|\n| c | d |"), "\\textbf{h1} & \\textbf{b}");
        AssertContains(TexPreamble("---\ntitle: Ti" + E000 + "tle\n---\n\nBody."), "\\title{Title}");

        // The strip itself, and that it is the only thing it touches.
        Assert.Equal("abc", LaTeXExport.WithoutSentinels("a" + E000 + "b" + E001 + "c"));
        Assert.Equal("plain", LaTeXExport.WithoutSentinels("plain"));
        Assert.Equal(Chr(0xE002) + Chr(0xF8FF), LaTeXExport.WithoutSentinels(Chr(0xE002) + Chr(0xF8FF)));
        // A string with nothing to strip comes back as the same instance.
        var untouched = "nothing here";
        Assert.Same(untouched, LaTeXExport.WithoutSentinels(untouched));
    }

    [Fact]
    public void LaTeXSkippedImageNamesItsFileBelowTheAltText()
    {
        // The alt text comes *first* and the comment after it: `%` runs to the end of its physical
        // line, so a comment in front of the words would swallow the words it is there to explain.
        AssertContains(LaTeXExport.Document("![Alt words](https://x.com/a.png)"),
            "\\emph{Alt words}\n% md: image skipped " + EmDash + " https://x.com/a.png is a URL, and LaTeX has nothing to fetch it with.\n\n");

        // And an image is a captioned float wherever it stands in body text.
        var sentence = TexBody("See ![the chart](c.png) for details.");
        AssertStartsWith(sentence, "See \\begin{figure}[ht]");
        AssertContains(sentence, "\\caption{the chart}");
        AssertEndsWith(sentence, "\\end{figure} for details.");
    }

    // MARK: - The shipped corpus

    /// <summary>Every environment this writer opens, and the one place it writes a bare terminator on purpose.</summary>
    private static readonly string[] Environments =
        { "document", "figure", "longtable", "itemize", "enumerate", "quote", "verbatim", "aligned" };

    private static void AssertWellFormed(string tex, string documentClass, string what)
    {
        AssertStartsWith(tex, "\\documentclass{" + documentClass + "}\n");
        AssertEndsWith(tex, "\\end{document}\n");
        AssertNotContains(tex, E000, what + " must carry no token");
        AssertNotContains(tex, E001, what);
        foreach (var environment in Environments)
        {
            var opens = Occurrences(tex, "\\begin{" + environment + "}");
            var closes = Occurrences(tex, "\\end{" + environment + "}");
            // The one deliberate extra: a code block quoting the verbatim terminator has it set
            // with `\verb`, which is a terminator that closes nothing.
            if (environment == "verbatim") closes -= Occurrences(tex, "\\verb|\\end{verbatim}|");
            Assert.True(opens == closes, what + ": " + environment + " opened " + opens + " times, closed " + closes);
        }
    }

    [Fact]
    public void EveryShippedExampleExportsAWellFormedDocumentAndBook()
    {
        // The nine documents the app ships, run through the whole writer — the closest thing to
        // the compile check a machine with tectonic on it would do. They between them exercise
        // every block kind: tables, math, all four diagram fences, plots, images, task lists.
        var files = Directory.GetFiles(Fixtures.Path("examples"), "*.md").OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(9, files.Count);
        var units = new List<BookUnit>();
        foreach (var file in files)
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            var source = File.ReadAllText(file);
            AssertWellFormed(LaTeXExport.Document(source), "article", name);
            units.Add(new BookUnit(name, source));
        }

        // …and the same nine as one book, which is the other entry point and the only place the
        // chapter reset and the cross-article package collection run.
        var tex = LaTeXExport.Book(new StructuredBook("Examples", [], [new BookSection("Examples", units)]));
        AssertWellFormed(tex, "book", "the example book");
        AssertContains(tex, "\\chapter{Examples}");
        Assert.Equal(9, Occurrences(tex, "\\section{"));
    }

    // MARK: - setsSomething, the soft-break oracle

    [Fact]
    public void SetsSomethingSeesThroughCommentsAndFloatsButNotEscapes()
    {
        Assert.False(LaTeXExport.SetsSomething(""));
        Assert.False(LaTeXExport.SetsSomething("  \t\r\n"));
        Assert.False(LaTeXExport.SetsSomething("% a comment\n  % another"));
        Assert.False(LaTeXExport.SetsSomething("\\begin{figure}[ht]\n\\centering\n\\end{figure}"));
        Assert.False(LaTeXExport.SetsSomething("\\begin{figure} never closed"));
        Assert.True(LaTeXExport.SetsSomething("\\end{figure} words"));
        Assert.True(LaTeXExport.SetsSomething("% comment\nthen words"));
        // An escaped percent is a character, not a comment — deliberately no backslash case.
        Assert.True(LaTeXExport.SetsSomething("\\% of it"));
        Assert.True(LaTeXExport.SetsSomething("\\emph{Alt}\n% md: image skipped"));
    }
}
