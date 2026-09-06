using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// The outline, the notes panel and the anchor slug. The slug is the join between the Contents
/// outline and the ids the HTML writer assigns: one shared, positional counter walked in document
/// order, so a line one side counts as a heading and the other does not drifts every later anchor
/// and a table-of-contents link scrolls to nothing.
/// </summary>
public class OutlineAndNotesTests
{
    private static string Slug(string text) => MarkdownParser.Slug(text, new Dictionary<string, int>(StringComparer.Ordinal));

    // MARK: - Outline

    [Fact]
    public void OutlineLevelsSlugsAndLines()
    {
        var outline = MarkdownParser.Outline("# One\n\ntext\n\n## Two\n\n```\n# not a heading\n```\n\nSetext\n---");
        Assert.Equal(3, outline.Count);
        Assert.Equal(new OutlineEntry(1, "One", "one", 0), outline[0]);
        Assert.Equal("two", outline[1].Slug);
        // A setext entry points at the text line, not the underline.
        Assert.Equal(new OutlineEntry(2, "Setext", "setext", 10), outline[2]);
    }

    [Fact]
    public void DuplicateHeadingSlugsAreDeduped()
    {
        Assert.Equal(["same", "same-1"], MarkdownParser.Outline("# Same\n\n# Same").Select(e => e.Slug));
    }

    [Fact]
    public void OutlineSkipsUnderlineAfterMultiLineParagraph()
    {
        // Only a single buffered line can be setext — the rule `parse` applies.
        Assert.Empty(MarkdownParser.Outline("line1\nline2\n---"));
        Assert.Single(MarkdownParser.Outline("only\n---"));
        Assert.Equal([new OutlineEntry(1, "Title", "title", 0)], MarkdownParser.Outline("  Title  \n==="));
    }

    [Fact]
    public void OutlineCountsLinesWithTheParsersOwnSplitter()
    {
        Assert.Equal([0, 2], MarkdownParser.Outline("# One\r\n\r\n# Two").Select(e => e.Line));
    }

    [Fact]
    public void OutlineSkipsACommentClosedOrNot()
    {
        Assert.Equal(["After"], MarkdownParser.Outline("<!-- x -->\n\n# After").Select(e => e.Text));
        Assert.Empty(MarkdownParser.Outline("<!-- never closed\n# Swallowed"));
    }

    [Fact]
    public void OutlineSkipsHeadingsInsideAQuote()
    {
        // A quoted heading gets no id and consumes no counter in the HTML writer either.
        Assert.Equal(["g"], MarkdownParser.Outline("> # h\n\n# g").Select(e => e.Text));
    }

    [Fact]
    public void OutlineDoesNotUnderlineWhatIsNotPlainText()
    {
        Assert.Empty(MarkdownParser.Outline("\\newpage\n---"));
        Assert.Equal(["h"], MarkdownParser.Outline("- item\n# h").Select(e => e.Text));
    }

    [Fact]
    public void OutlineSeesSetextHeadingsParseDoesNot()
    {
        // Shipped divergence, replicated by every port: a table header row and a list-item
        // continuation line are not excluded from plain text, so each yields a phantom entry (and
        // consumes a slug) that `parse` never emits.
        Assert.Equal(["| a |"], MarkdownParser.Outline("| a |\n---").Select(e => e.Text));
        Assert.DoesNotContain(BlockKind.Heading, Blocks.Kinds("| a |\n---"));
        Assert.Equal(["continuation"], MarkdownParser.Outline("- item\ncontinuation\n---").Select(e => e.Text));
    }

    [Fact]
    public void FootnoteDefinitionDoesNotLeakIntoTheOutline()
    {
        // `parse` claims the definition line, so the `---` under it is a rule, not an underline.
        Assert.Equal(["Real"], MarkdownParser.Outline("# Real\n\n[^a]: The note.\n---\n\nText[^a].").Select(e => e.Text));
    }

    [Fact]
    public void FrontMatterDoesNotLeakIntoTheOutlineOrNotes()
    {
        // The closing `---` underlines the last metadata line; a raw scan would read a setext
        // heading the rendered document does not contain and steal a slug.
        var outline = MarkdownParser.Outline("---\ntitle: My Post\n---\n\n# Hello\n");
        Assert.Equal(["Hello"], outline.Select(e => e.Text));
        Assert.Equal("hello", outline[0].Slug);
        Assert.Equal("hello-x", MarkdownParser.Outline("---\nHello: x\n---\n\n# Hello: x\n")[0].Slug);
        Assert.Empty(MarkdownParser.Outline("---\ntitle: A\n---"));
        // Unclosed front matter is ordinary text, and its `---` is a rule.
        Assert.Equal(["h"], MarkdownParser.Outline("---\ntitle: A\n\n# h").Select(e => e.Text));

        Assert.Empty(MarkdownParser.Notes("---\ntitle: X\n<!-- note: hidden -->\n---\n\nBody."));
        Assert.Single(MarkdownParser.Notes("---\ntitle: X\n---\n\n<!-- note: real -->"));
    }

    // MARK: - Notes

    [Fact]
    public void NotesHelperFindsLine()
    {
        Assert.Equal([new NoteEntry("fix me", 2)], MarkdownParser.Notes("start\n\n<!-- note: fix me -->\n\nend"));
    }

    [Fact]
    public void NotesListEveryNoteWithTheLineItOpensOn()
    {
        Assert.Equal([new NoteEntry("one\ntwo", 1), new NoteEntry("three", 3)], MarkdownParser.Notes("a\n<!-- note: one\ntwo -->\n<!-- note: three -->"));
        Assert.Equal([new NoteEntry("open\nmore", 0)], MarkdownParser.Notes("<!-- note: open\nmore"));
        Assert.Equal([new NoteEntry("x", 0)], MarkdownParser.Notes("   <!-- note: x -->"));
    }

    [Fact]
    public void NotesIgnoreAFenceAQuoteAndATabIndentedComment()
    {
        Assert.Empty(MarkdownParser.Notes("```\n<!-- note: not really -->\n```"));
        // The scan does not recurse into quotes, and a tab before `<!--` is not a comment start.
        Assert.Empty(MarkdownParser.Notes("> <!-- note: q -->"));
        Assert.Empty(MarkdownParser.Notes("\t<!-- note: x -->"));
    }

    [Fact]
    public void ParserNoteAndFenceTrimsAreFoundationsOwn()
    {
        // The body is trimmed with the newline-bearing set: U+0085 is whitespace here and is not
        // a blank line anywhere else. Kotlin's own set kept it, and a note vanished from its panel.
        Assert.Equal(["n"], MarkdownParser.Notes("<!--" + Blocks.Nel + " note: n -->").Select(n => n.Text));
        Assert.Equal(["n"], MarkdownParser.Notes("<!-- note: n " + Blocks.Nel + "-->").Select(n => n.Text));
        Assert.Equal(["n"], MarkdownParser.Notes("<!-- note:\n\u2028n\u2029\n -->").Select(n => n.Text));
    }

    // MARK: - Slug

    [Fact]
    public void SlugLowercasesKeepsLettersAndDigitsAndMapsASpaceToAHyphen()
    {
        Assert.Equal("getting-started", Slug("Getting Started"));
        Assert.Equal("chapter-12", Slug("Chapter 12"));
        Assert.Equal("hello-world-2026", Slug("Hello, World! 2026"));
        Assert.Equal("ünïcödé-ñ", Slug("Ünïcödé Ñ"));
        Assert.Equal("日本語-テスト", Slug("日本語 テスト"));
        // Every U+0020 becomes a hyphen, so two spaces are two hyphens.
        Assert.Equal("a--b", Slug("a  b"));
        Assert.Equal("--", Slug("  "));
    }

    [Fact]
    public void SlugDropsPunctuationLikeGitHub()
    {
        Assert.Equal("c--f", Slug("C# & F#!"));
    }

    [Fact]
    public void SlugKeepsHyphenAndUnderscoreButDropsEveryOtherWhitespace()
    {
        Assert.Equal("a-b_c", Slug("a-b_c"));
        Assert.Equal("---", Slug("---"));
        Assert.Equal("_", Slug("_"));
        Assert.Equal("ab", Slug("a\tb"));
        Assert.Equal("ab", Slug("a" + Blocks.Nbsp + "b"));
        Assert.Equal("ab", Slug("a\u2003b"));
    }

    [Fact]
    public void ParserSlugKeepsWhatTheAndroidPortKeeps()
    {
        // A grapheme walk dropped the hyphen and the mark together; a code-point walk keeps both.
        Assert.Equal("a--\u0301b-c", Slug("a -\u0301b c"));
        // A ZWJ is a format character and is kept by neither.
        Assert.Equal("heading", Slug("Heading\u200D"));
        // A combining mark on a letter rides along.
        Assert.Equal("cafe\u0301", Slug("Cafe\u0301"));
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal("getting-started", MarkdownParser.Slug("Getting Started", used));
        Assert.Equal("getting-started-1", MarkdownParser.Slug("Getting Started", used));
    }

    [Fact]
    public void SlugKeepsAnAstralLetterWhichAUtf16WalkWouldDrop()
    {
        Assert.Equal("\U0001D400-heading", Slug("\U0001D400 Heading"));
    }

    [Fact]
    public void SlugLowercasesTheWholeStringWithTheFullMapping()
    {
        // U+0130 lowercases to two scalars in Swift and JavaScript, and the second is a mark the
        // slug keeps. A simple 1:1 mapping gives `i`.
        Assert.Equal("i\u0307", Slug("\u0130"));
        Assert.Equal("k\u00E5", Slug("\u212A\u212B"));
    }

    [Fact]
    public void SlugDoesNotApplyFinalSigma()
    {
        // Swift maps each scalar on its own, so a Greek capital sigma is always `σ`, never the
        // word-final `ς` that Java and JavaScript produce. md.macOS is the source of truth.
        Assert.Equal("οδοσ-σασ-σ", Slug("ΟΔΟΣ ΣΑΣ Σ"));
    }

    [Fact]
    public void SlugKeepsTheEnclosedAlphabetics()
    {
        // Unicode's Alphabetic property reaches past letters, marks and letter-numbers to the
        // circled and squared Latin letters (general category So); Swift and TypeScript keep them.
        Assert.Equal("\u24D0\U0001F130-x", Slug("\u24B6\U0001F130 x"));
    }

    [Fact]
    public void SlugKeepsNumbersOfEveryKind()
    {
        // Nl, No, a CJK numeral (a letter anyway) and a non-ASCII decimal digit all survive.
        Assert.Equal("ⅹ-½-三-٣", Slug("Ⅹ ½ 三 ٣"));
    }

    [Fact]
    public void SlugNeverNormalises()
    {
        // NFC and NFD are different anchors on every platform alike.
        Assert.NotEqual(Slug("café"), Slug("cafe\u0301"));
    }

    [Fact]
    public void SlugFallsBackToSectionWhenNothingSurvives()
    {
        Assert.Equal("section", Slug("!!!"));
        Assert.Equal("section", Slug(""));
        // …and the fallback shares the counter with a heading literally named `section`.
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal(["section", "section-1", "section-2"], new[] { "!", "?", "section" }.Select(t => MarkdownParser.Slug(t, used)));
    }

    [Fact]
    public void SlugDeduplicatesThroughTheCallersCounter()
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal("getting-started", MarkdownParser.Slug("Getting Started", used));
        Assert.Equal("getting-started-1", MarkdownParser.Slug("Getting Started", used));
        Assert.Equal("getting-started-2", MarkdownParser.Slug("Getting Started", used));
    }

    [Fact]
    public void SlugCanStillProduceADuplicateIdExactlyAsGitHubDoes()
    {
        // The counter counts the bare slug: `Same`, `Same 1`, `Same` yield `same`, `same-1`,
        // `same-1`. Replicated, not fixed.
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        Assert.Equal(["same", "same-1", "same-1"], new[] { "Same", "Same 1", "Same" }.Select(t => MarkdownParser.Slug(t, used)));
    }

    [Fact]
    public void OutlineSlugsFollowDocumentOrderAcrossEveryHeadingKind()
    {
        // The invariant the HTML writer relies on: the same counter, walked in the same order,
        // skipping front matter, fences and footnote definitions.
        var source = Blocks.Lines(
            "---", "title: Skipped", "---", "", "# Same", "", "## Same", "", "```", "# not a heading", "```", "",
            "[^a]: a definition, not plain text", "---", "", "Setext", "===");
        Assert.Equal(["same", "same-1", "setext"], MarkdownParser.Outline(source).Select(e => e.Slug));
    }
}
