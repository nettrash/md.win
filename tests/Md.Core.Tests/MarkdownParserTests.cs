using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// The shape most parser tests assert, shared by every parser-side suite. Every expected value
/// below was produced by the real md.macOS parser (the Swift source compiled as an oracle), not
/// reasoned about — the point of the family is byte parity with what ships.
/// </summary>
internal static class Blocks
{
    /// <summary>
    /// The three characters the whole parity family is written around: COMBINING ACUTE ACCENT,
    /// VARIATION SELECTOR-16, ZERO WIDTH JOINER. Each fuses onto the ASCII delimiter before it in
    /// a grapheme-aware string model; a UTF-16 walk passes these by construction, which is exactly
    /// why they stay — they pin outputs, and a port that reached for <c>StringInfo</c>,
    /// <c>Normalize()</c> or a culture-sensitive search would break them silently.
    /// </summary>
    public static readonly string[] Marks = ["\u0301", "\uFE0F", "\u200D"];

    public const string Nbsp = "\u00A0";
    public const string Nel = "\u0085";
    public const string Zwsp = "\u200B";

    public static IReadOnlyList<MarkdownBlock> Parse(string source) => MarkdownParser.Parse(source);

    public static List<BlockKind> Kinds(string source) => Parse(source).Select(b => b.Kind).ToList();

    public static T As<T>(MarkdownBlock block) where T : MarkdownBlock => Assert.IsType<T>(block);

    public static T First<T>(string source) where T : MarkdownBlock
    {
        var blocks = Parse(source);
        Assert.NotEmpty(blocks);
        return As<T>(blocks[0]);
    }

    public static T Single<T>(string source) where T : MarkdownBlock
    {
        var blocks = Parse(source);
        Assert.Single(blocks);
        return As<T>(blocks[0]);
    }

    public static void AssertRows(string[][] expected, IReadOnlyList<IReadOnlyList<string>> actual) =>
        Assert.Equal<IEnumerable<string>>(expected, actual);

    public static string Lines(params string[] lines) => string.Join("\n", lines);
}

public class MarkdownParserTests
{
    // MARK: - Headings

    [Fact]
    public void HeadingLevels()
    {
        for (var level = 1; level <= 6; level++)
        {
            var heading = Blocks.First<MarkdownBlock.Heading>(new string('#', level) + " Title");
            Assert.Equal(level, heading.Level);
            Assert.Equal("Title", heading.Text);
        }
    }

    [Fact]
    public void HeadingRequiresSpace()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("#Title"));
        // A tab does not count either: the marker test is against U+0020 alone.
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("#\tTitle"));
        Assert.Equal(new MarkdownBlock.Paragraph("#\t"), Blocks.Parse("#\t")[0]);
    }

    [Fact]
    public void HeadingSevenHashesIsParagraph()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("####### too deep"));
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("######## deeper still"));
    }

    [Fact]
    public void HeadingClosingHashesStripped()
    {
        Assert.Equal("Title", Blocks.First<MarkdownBlock.Heading>("## Title ##").Text);
        Assert.Equal("Title", Blocks.First<MarkdownBlock.Heading>("## Title #").Text);
        Assert.Equal("Title", Blocks.First<MarkdownBlock.Heading>("# Title ##   ").Text);
        Assert.Equal("Title", Blocks.First<MarkdownBlock.Heading>("#   Title   ").Text);
    }

    [Fact]
    public void HeadingPreservesTrailingHashInWord()
    {
        // The closing-hash strip needs SPTAB before the run, so `C#` and `F#` keep theirs.
        Assert.Equal("C#", Blocks.First<MarkdownBlock.Heading>("# C#").Text);
        Assert.Equal("F# notes", Blocks.First<MarkdownBlock.Heading>("# F# notes").Text);
        Assert.Equal("Title#", Blocks.First<MarkdownBlock.Heading>("# Title#").Text);
    }

    [Fact]
    public void HeadingOfOnlyHashesIsEmpty()
    {
        // A bare `#` is a level-1 heading with no text; a text of only `#`s strips to nothing.
        Assert.Equal(new MarkdownBlock.Heading(1, ""), Blocks.Single<MarkdownBlock.Heading>("#"));
        Assert.Equal(new MarkdownBlock.Heading(1, ""), Blocks.Single<MarkdownBlock.Heading>("# #"));
        Assert.Equal(new MarkdownBlock.Heading(1, ""), Blocks.Single<MarkdownBlock.Heading>("#  ##"));
    }

    [Fact]
    public void HeadingAllowsUnboundedIndentation()
    {
        // Asymmetric with a fence, which caps at three spaces. Recorded, not fixed.
        Assert.Equal(new MarkdownBlock.Heading(1, "Deep"), Blocks.Single<MarkdownBlock.Heading>("        # Deep"));
    }

    [Fact]
    public void SetextHeadings()
    {
        var h1 = Blocks.Parse("My Title\n===");
        Assert.Single(h1);
        Assert.Equal(new MarkdownBlock.Heading(1, "My Title"), h1[0]);

        // Exactly one block: the underline must not also emit a thematic break.
        var h2 = Blocks.Parse("My Title\n---");
        Assert.Single(h2);
        Assert.Equal(new MarkdownBlock.Heading(2, "My Title"), h2[0]);

        // The underline is WS-trimmed and the text is too.
        Assert.Equal(new MarkdownBlock.Heading(1, "Title"), Blocks.Single<MarkdownBlock.Heading>("Title\n  ===  "));
        Assert.Equal(new MarkdownBlock.Heading(1, "Title"), Blocks.Single<MarkdownBlock.Heading>("  Title  \n==="));
    }

    [Fact]
    public void SetextUnderlineMayBeShort()
    {
        // Setext is tested before the break set, so a lone `-` (a list marker) and `--` (never
        // a rule) both underline; `- - -` keeps its interior spaces and is a rule after a paragraph.
        Assert.Equal(new MarkdownBlock.Heading(2, "Title"), Blocks.Single<MarkdownBlock.Heading>("Title\n-"));
        Assert.Equal(new MarkdownBlock.Heading(2, "Title"), Blocks.Single<MarkdownBlock.Heading>("Title\n--"));
        Assert.Equal([BlockKind.Paragraph, BlockKind.ThematicBreak], Blocks.Kinds("Title\n- - -"));
    }

    [Fact]
    public void SetextFiresOnlyWithOneBufferedLine()
    {
        // Two buffered lines and the `---` is a rule — the rule `outline()` applies too.
        Assert.Equal([BlockKind.Paragraph, BlockKind.ThematicBreak], Blocks.Kinds("line1\nline2\n---"));
        // An ATX heading is not a buffered paragraph line.
        Assert.Equal([BlockKind.Heading, BlockKind.ThematicBreak], Blocks.Kinds("# h\n---"));
    }

    [Fact]
    public void StandaloneRuleStillParsesAfterSetextChange()
    {
        Assert.Equal([BlockKind.ThematicBreak], Blocks.Kinds("---"));
    }

    // MARK: - Paragraphs

    [Fact]
    public void ParagraphPreservesSoftBreaks()
    {
        Assert.Equal("line one\nline two", Blocks.First<MarkdownBlock.Paragraph>("line one\nline two").Text);
    }

    [Fact]
    public void BlankLineSeparatesParagraphs()
    {
        Assert.Equal([BlockKind.Paragraph, BlockKind.Paragraph], Blocks.Kinds("first\n\nsecond"));
    }

    [Fact]
    public void ParagraphKeepsRawLines()
    {
        // Only a setext heading's text is trimmed; leading and trailing spaces survive.
        Assert.Equal("  spaced  \n  again  ", Blocks.First<MarkdownBlock.Paragraph>("  spaced  \n  again  ").Text);
    }

    [Fact]
    public void EmptySourceParsesToNothing()
    {
        Assert.Empty(Blocks.Parse(""));
        Assert.Empty(Blocks.Parse("\n"));
        Assert.Empty(Blocks.Parse("\t"));
    }

    [Fact]
    public void ParserBlankLineSetIsFoundationsOwn()
    {
        // U+200B is a space separator to Apple's frozen tables and a format character to every
        // current one; a line holding one is a blank line on all three shipping platforms.
        Assert.Equal(2, Blocks.Parse("para\n" + Blocks.Zwsp + "\nnext").Count);
        Assert.Equal(2, Blocks.Parse("para\n \t" + Blocks.Nbsp + "\nnext").Count);
        // It separates two lists as well.
        Assert.Equal([BlockKind.List, BlockKind.List], Blocks.Kinds("- a\n" + Blocks.Zwsp + "\n- b"));
    }

    [Fact]
    public void ParserLineEndingsAreScalarExact()
    {
        // CR, LF and CRLF each end exactly one line.
        Assert.Equal("one\ntwo\nthree\nfour", Blocks.First<MarkdownBlock.Paragraph>("one\r\ntwo\rthree\nfour").Text);
        Assert.Equal(2, Blocks.Parse("a\r\n\r\nb").Count);
        Assert.Equal([new MarkdownBlock.Paragraph("a\nb"), new MarkdownBlock.Paragraph("c")], Blocks.Parse("a\rb\r\rc"));
        Assert.Equal(new MarkdownBlock.CodeBlock(null, "a"), Blocks.Single<MarkdownBlock.CodeBlock>("```\r\na\r\n```\r\n"));
    }

    [Fact]
    public void WiderNewlineSetIsContentToTheBlockScanner()
    {
        // NEL, LS and PS split lines for the raw-diagram probes and nowhere else.
        Assert.Equal(new MarkdownBlock.Paragraph("a\u0085b"), Blocks.Single<MarkdownBlock.Paragraph>("a\u0085b"));
        Assert.Equal(new MarkdownBlock.Paragraph("a\u2028\u2029b"), Blocks.Single<MarkdownBlock.Paragraph>("a\u2028\u2029b"));
    }

    // MARK: - Lists

    [Fact]
    public void UnorderedList()
    {
        var list = Blocks.First<MarkdownBlock.List>("- a\n- b\n* c\n+ d");
        Assert.False(list.Ordered);
        Assert.Equal(["a", "b", "c", "d"], list.Items.Select(i => i.Text));
        Assert.All(list.Items, i => Assert.Null(i.Ordinal));
    }

    [Fact]
    public void OrderedList()
    {
        var list = Blocks.First<MarkdownBlock.List>("1. one\n2. two\n3) three");
        Assert.True(list.Ordered);
        Assert.Equal([1, 2, 3], list.Items.Select(i => i.Ordinal));
        Assert.Equal([1, 2], Blocks.First<MarkdownBlock.List>("1) a\n2) b").Items.Select(i => i.Ordinal));
    }

    [Fact]
    public void MixedRunIsOrdered()
    {
        // One flag for the whole run; the bullet item keeps a null ordinal.
        var list = Blocks.First<MarkdownBlock.List>("- a\n1. b");
        Assert.True(list.Ordered);
        Assert.Equal([null, 1], list.Items.Select(i => i.Ordinal));
    }

    [Fact]
    public void NestedListLevels()
    {
        Assert.Equal([0, 1, 2], Blocks.First<MarkdownBlock.List>("- top\n  - nested\n    - deeper").Items.Select(i => i.Level));
    }

    [Fact]
    public void TabIndentedNestedListRecognised()
    {
        var list = Blocks.First<MarkdownBlock.List>("- top\n\t- nested");
        Assert.Equal(["top", "nested"], list.Items.Select(i => i.Text));
        Assert.True(list.Items[1].Level > list.Items[0].Level);
        // A tab advances to the next 4-column stop and the level is indent / 2 — the only place a
        // tab indents anything in this grammar.
        Assert.Equal([0, 1, 4], Blocks.First<MarkdownBlock.List>("- a\n   - b\n\t\t- c").Items.Select(i => i.Level));
    }

    [Fact]
    public void TaskList()
    {
        var list = Blocks.First<MarkdownBlock.List>("- [ ] todo\n- [x] done\n- [X] also");
        Assert.Equal([false, true, true], list.Items.Select(i => i.Task));
        Assert.Equal(["todo", "done", "also"], list.Items.Select(i => i.Text));
    }

    [Fact]
    public void TaskBoxNeedsASpaceOrTheEndOfTheLine()
    {
        Assert.Equal(new ListItem("[ ]x", 0, null, null), Blocks.First<MarkdownBlock.List>("- [ ]x").Items[0]);
        Assert.Equal(new ListItem("", 0, null, false), Blocks.First<MarkdownBlock.List>("- [ ]").Items[0]);
        Assert.Equal(new ListItem("", 0, null, true), Blocks.First<MarkdownBlock.List>("- [X]").Items[0]);
        // The spaces after the box go; the text's own trailing spaces stay.
        Assert.Equal(new ListItem("spaced  ", 1, null, true), Blocks.First<MarkdownBlock.List>("  - [x]  spaced  ").Items[0]);
    }

    [Fact]
    public void BareMarkerIsAnItemWithEmptyText()
    {
        foreach (var marker in new[] { "-", "*", "+" })
        {
            var list = Blocks.Single<MarkdownBlock.List>(marker);
            Assert.Equal([new ListItem("", 0, null, null)], list.Items);
        }
    }

    [Fact]
    public void MarkerFollowedByTabIsNotAnItem()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("-\tfoo"));
    }

    [Fact]
    public void ListItemContinuationIsAbsorbed()
    {
        var blocks = Blocks.Parse("- First item\n  with continuation\n- Second item");
        Assert.Single(blocks);
        var list = Blocks.As<MarkdownBlock.List>(blocks[0]);
        Assert.Equal(2, list.Items.Count);
        Assert.Equal("First item with continuation", list.Items[0].Text);
        Assert.Equal("Second item", list.Items[1].Text);
    }

    [Fact]
    public void ListBreaksIntoATableButAbsorbsAFootnoteDefinition()
    {
        // The three continuation break-sets are deliberately not unified: the list loop carries
        // the table lookahead and not the footnote check.
        Assert.Equal([BlockKind.List, BlockKind.Table], Blocks.Kinds("- item\n| a |\n| --- |"));
        Assert.Equal("item [^a]: note", Blocks.First<MarkdownBlock.List>("- item\n[^a]: note").Items[0].Text);
    }

    [Fact]
    public void ListContinuationBreaksOnEveryOtherBlockStart()
    {
        Assert.Equal([BlockKind.List, BlockKind.Heading], Blocks.Kinds("- a\n# h"));
        // Heading indentation is unbounded, so an indented `#` line ends the item too.
        Assert.Equal([BlockKind.List, BlockKind.Heading], Blocks.Kinds("- item\n  # heading"));
        Assert.Equal([BlockKind.List, BlockKind.Quote], Blocks.Kinds("- a\n> q"));
        Assert.Equal([BlockKind.List, BlockKind.ThematicBreak], Blocks.Kinds("- a\n***"));
        Assert.Equal([BlockKind.List, BlockKind.PageBreak], Blocks.Kinds("- a\n\\newpage"));
        Assert.Equal([BlockKind.List], Blocks.Kinds("- a\n<!-- c -->"));
        Assert.Equal([BlockKind.List, BlockKind.CodeBlock], Blocks.Kinds("- a\n```\nx\n```"));
    }

    [Fact]
    public void ParserOrderedListMarkerIsASCIIDigitsOnly()
    {
        // `Character.isNumber` admitted ½ and ٣ on Apple and `toIntOrNull` read ٣ as 3 on Android;
        // CommonMark says ASCII digits, and with that set every port agrees.
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("½. half"));
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("٣. three"));
        // A digit carrying a mark ends the run before the `.`, so the line joins the item above.
        foreach (var mark in Blocks.Marks)
        {
            var list = Blocks.Single<MarkdownBlock.List>("1. one\n2" + mark + ". two");
            Assert.Equal(["one 2" + mark + ". two"], list.Items.Select(i => i.Text));
        }
        var plain = Blocks.First<MarkdownBlock.List>("1. one\n2. two");
        Assert.True(plain.Ordered);
        Assert.Equal([1, 2], plain.Items.Select(i => i.Ordinal));
    }

    [Fact]
    public void OrderedMarkerLosesLeadingZerosAndCapsAtNineDigits()
    {
        Assert.Equal(7, Blocks.First<MarkdownBlock.List>("007. item").Items[0].Ordinal);
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("1234567890. too many digits"));
        Assert.Equal(123456789, Blocks.First<MarkdownBlock.List>("123456789. nine").Items[0].Ordinal);
    }

    // MARK: - Code fences

    [Fact]
    public void FencedCodeWithLanguage()
    {
        Assert.Equal(new MarkdownBlock.CodeBlock("swift", "let x = 1"), Blocks.Single<MarkdownBlock.CodeBlock>("```swift\nlet x = 1\n```"));
    }

    [Fact]
    public void TildeFence()
    {
        Assert.Equal(new MarkdownBlock.CodeBlock(null, "plain"), Blocks.Single<MarkdownBlock.CodeBlock>("~~~\nplain\n~~~"));
        // A closing fence must use the opening character.
        Assert.Equal(new MarkdownBlock.CodeBlock(null, "~~~\nx"), Blocks.Single<MarkdownBlock.CodeBlock>("```\n~~~\nx\n```"));
    }

    [Fact]
    public void FenceContentIsNotInterpreted()
    {
        Assert.Equal("# not a heading", Blocks.First<MarkdownBlock.CodeBlock>("```\n# not a heading\n```").Code);
    }

    [Fact]
    public void UnclosedFenceConsumesToEnd()
    {
        Assert.Equal("a\nb", Blocks.First<MarkdownBlock.CodeBlock>("```\na\nb").Code);
    }

    [Fact]
    public void UnclosedFenceGainsTheTrailingEmptyLine()
    {
        // Wart, replicated: the line splitter always appends a final element, so an unclosed fence
        // in a source ending with a newline gains a trailing "\n". A closed fence is unaffected.
        Assert.Equal("a\n", Blocks.First<MarkdownBlock.CodeBlock>("```\na\n").Code);
        Assert.Equal("a", Blocks.First<MarkdownBlock.CodeBlock>("```\na\n```\n").Code);
    }

    [Fact]
    public void IndentedFenceStripsIndent()
    {
        Assert.Equal("indented", Blocks.First<MarkdownBlock.CodeBlock>("  ```\n  indented\n  ```").Code);
        Assert.Equal(new MarkdownBlock.CodeBlock("js", "x"), Blocks.Single<MarkdownBlock.CodeBlock>("  ```js\n  x\n  ```"));
        // Up to the opening indent is removed, U+0020 only, stopping at the first non-space.
        Assert.Equal("  x\ny\nz", Blocks.First<MarkdownBlock.CodeBlock>("   ```\n     x\n  y\nz\n   ```").Code);
    }

    [Fact]
    public void FenceIndentedFourSpacesOrByATabIsAParagraph()
    {
        // There is no indented-code branch anywhere, so both fall through to a paragraph.
        Assert.Equal([new MarkdownBlock.Paragraph("    ```\n    code\n    ```")], Blocks.Parse("    ```\n    code\n    ```"));
        Assert.Equal([new MarkdownBlock.Paragraph("\t```\ncode")], Blocks.Parse("\t```\ncode"));
    }

    [Fact]
    public void ClosingFenceMayBeIndentedArbitrarilyAndBeLonger()
    {
        var blocks = Blocks.Parse("```\ncode\n        `````\nafter");
        Assert.Equal([new MarkdownBlock.CodeBlock(null, "code"), new MarkdownBlock.Paragraph("after")], blocks);
        // …but never shorter.
        Assert.Equal(new MarkdownBlock.CodeBlock(null, "```\nx"), Blocks.Single<MarkdownBlock.CodeBlock>("````\n```\nx\n````"));
    }

    [Fact]
    public void BacktickInInfoStringIsRefusedForABacktickFenceOnly()
    {
        // The refused opener is prose; the would-be closer then opens an unclosed, empty fence.
        Assert.Equal([new MarkdownBlock.Paragraph("```js `x`\ncode"), new MarkdownBlock.CodeBlock(null, "")], Blocks.Parse("```js `x`\ncode\n```"));
        Assert.Equal(new MarkdownBlock.CodeBlock("js", "code"), Blocks.Single<MarkdownBlock.CodeBlock>("~~~js `x`\ncode\n~~~"));
    }

    [Fact]
    public void LanguageIsTheFirstSpaceDelimitedWordOfTheInfoString()
    {
        // The trim is the WS set; the split is on U+0020 alone, so a tab or an NBSP is interior.
        Assert.Equal("js", Blocks.First<MarkdownBlock.CodeBlock>("```js extra\nx\n```").Language);
        Assert.Equal("js\tfoo", Blocks.First<MarkdownBlock.CodeBlock>("```js\tfoo\nx\n```").Language);
        Assert.Equal("js" + Blocks.Nbsp + "extra", Blocks.First<MarkdownBlock.CodeBlock>("```" + Blocks.Nbsp + "js" + Blocks.Nbsp + "extra\nx\n```").Language);
        // Never lowercased here — the renderer folds case at dispatch.
        Assert.Equal("JS", Blocks.First<MarkdownBlock.CodeBlock>("```JS\nx\n```").Language);
    }

    [Fact]
    public void ParserNoteAndFenceTrimsAreFoundationsOwn()
    {
        // The Android port trimmed with space and tab only, so a fence padded with U+00A0 kept a
        // language nobody typed — and a closing fence padded with U+00A0 did not close.
        Assert.Equal(new MarkdownBlock.CodeBlock("js", "code()"), Blocks.Single<MarkdownBlock.CodeBlock>("```" + Blocks.Nbsp + "js\ncode()\n```"));
        Assert.Equal([BlockKind.CodeBlock, BlockKind.Paragraph], Blocks.Kinds("```\ncode()\n```" + Blocks.Nbsp + "\n\nafter"));
        // The note half lives in OutlineAndNotesTests.
        Assert.Equal(["n"], MarkdownParser.Notes("<!--" + Blocks.Nel + " note: n -->").Select(n => n.Text));
    }

    [Fact]
    public void FenceWinsOverEveryOtherBlockStart()
    {
        Assert.Equal([new MarkdownBlock.CodeBlock(null, "---\n# nope\n> nope\n- nope")], Blocks.Parse("```\n---\n# nope\n> nope\n- nope\n```"));
    }

    // MARK: - Block quotes

    [Fact]
    public void BlockQuote()
    {
        var quote = Blocks.First<MarkdownBlock.Quote>("> quoted\n> text");
        Assert.Equal([new MarkdownBlock.Paragraph("quoted\ntext")], quote.Blocks);
    }

    [Fact]
    public void QuoteEndsAtABlankLineAndAtAnUnquotedLine()
    {
        // No lazy continuation.
        Assert.Equal([BlockKind.Quote, BlockKind.Quote], Blocks.Kinds("> a\n\n> b"));
        Assert.Equal([BlockKind.Quote, BlockKind.Paragraph], Blocks.Kinds("> a\nb"));
    }

    [Fact]
    public void QuoteMarkerStripsExactlyOneOptionalSpace()
    {
        Assert.Equal([new MarkdownBlock.Paragraph(" two spaces")], Blocks.First<MarkdownBlock.Quote>(">  two spaces").Blocks);
        Assert.Equal([new MarkdownBlock.Paragraph("x")], Blocks.First<MarkdownBlock.Quote>("   > x").Blocks);
        // A marker with nothing behind it is an empty quote.
        Assert.Empty(Blocks.Single<MarkdownBlock.Quote>(">").Blocks);
        Assert.Empty(Blocks.Single<MarkdownBlock.Quote>("> ").Blocks);
    }

    [Fact]
    public void NestedBlockQuote()
    {
        var outer = Blocks.First<MarkdownBlock.Quote>("> > deep");
        Assert.Equal(BlockKind.Quote, outer.Blocks[0].Kind);

        // The stripped inner text is re-parsed whole, so deeper markers nest inside the paragraph.
        var levels = Blocks.Single<MarkdownBlock.Quote>("> a\n>> b\n>>> c");
        Assert.Equal(BlockKind.Paragraph, levels.Blocks[0].Kind);
        var second = Blocks.As<MarkdownBlock.Quote>(levels.Blocks[1]);
        Assert.Equal(new MarkdownBlock.Paragraph("b"), second.Blocks[0]);
        Assert.Equal([new MarkdownBlock.Paragraph("c")], Blocks.As<MarkdownBlock.Quote>(second.Blocks[1]).Blocks);
    }

    [Fact]
    public void QuoteHoldsAnyBlock()
    {
        Assert.Equal([new MarkdownBlock.CodeBlock(null, "x")], Blocks.Single<MarkdownBlock.Quote>("> ```\n> x\n> ```").Blocks);
        Assert.Equal(BlockKind.List, Blocks.Single<MarkdownBlock.Quote>("> - a\n> - b").Blocks[0].Kind);
        var table = Blocks.As<MarkdownBlock.Table>(Blocks.Single<MarkdownBlock.Quote>("> a | b\n> --- | ---\n> 1 | 2").Blocks[0]);
        Assert.Equal(["a", "b"], table.Header);
        Blocks.AssertRows([["1", "2"]], table.Rows);
    }

    [Fact]
    public void DeeplyNestedQuoteDoesNotOverflow()
    {
        var blocks = MarkdownParser.Parse(new string('>', 5000) + " deep");
        Assert.NotEmpty(blocks);
        Assert.Equal(BlockKind.Quote, blocks[0].Kind);
    }

    [Fact]
    public void QuoteNestingCapsAtThirtyThreeLevels()
    {
        // The cap is part of the output shape: quote nodes at depths 0…32, the innermost holding
        // one paragraph with the still-`>`-laden remainder. A .NET stack would survive more; the
        // shape is what parity is about.
        var depth = 0;
        MarkdownBlock current = MarkdownParser.Parse(new string('>', 5000) + " deep")[0];
        while (current is MarkdownBlock.Quote quote)
        {
            depth++;
            current = quote.Blocks[0];
        }
        Assert.Equal(33, depth);
        Assert.Equal(new MarkdownBlock.Paragraph(new string('>', 4967) + " deep"), current);

        depth = 0;
        current = MarkdownParser.Parse(new string('>', 40) + " deep")[0];
        while (current is MarkdownBlock.Quote quote)
        {
            depth++;
            current = quote.Blocks[0];
        }
        Assert.Equal(33, depth);
        Assert.Equal(new MarkdownBlock.Paragraph(">>>>>>> deep"), current);
    }

    // MARK: - Thematic breaks

    [Fact]
    public void ThematicBreaks()
    {
        foreach (var rule in new[] { "---", "***", "___", "- - -", "****", "----------", " - -\t- " })
        {
            Assert.Equal([BlockKind.ThematicBreak], Blocks.Kinds(rule));
        }
    }

    [Fact]
    public void DashesUnderTextAreNotRuleWhenTooShort()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("--"));
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("**"));
    }

    [Fact]
    public void ThematicBreakFiltersOnlySpaceAndTab()
    {
        // Narrower than every other predicate, which trim the WS set: an NBSP disqualifies.
        Assert.Equal([new MarkdownBlock.Paragraph(Blocks.Nbsp + "---")], Blocks.Parse(Blocks.Nbsp + "---"));
    }

    [Fact]
    public void ThematicBreakRefusesAMixedRun()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("--*"));
    }

    // MARK: - Tables

    [Fact]
    public void TableParsing()
    {
        var table = Blocks.Single<MarkdownBlock.Table>(Blocks.Lines(
            "| Name | Age |",
            "| :--- | ---: |",
            "| Ann  | 30 |",
            "| Bob  | 25 |"));
        Assert.Equal(["Name", "Age"], table.Header);
        Assert.Equal([ColumnAlignment.Leading, ColumnAlignment.Trailing], table.Alignments);
        Blocks.AssertRows([["Ann", "30"], ["Bob", "25"]], table.Rows);
    }

    [Fact]
    public void TableCenterAlignment()
    {
        Assert.Equal([ColumnAlignment.Center, ColumnAlignment.Center], Blocks.First<MarkdownBlock.Table>("| A | B |\n|:-:|:-:|\n| 1 | 2 |").Alignments);
    }

    [Fact]
    public void TableCannotDistinguishColonDashFromDash()
    {
        // There is no "explicitly left" alignment in this model.
        Assert.Equal([ColumnAlignment.Leading], Blocks.First<MarkdownBlock.Table>("| A |\n| :--- |\n| 1 |").Alignments);
    }

    [Fact]
    public void TableEscapedPipe()
    {
        Blocks.AssertRows([["a | b"]], Blocks.First<MarkdownBlock.Table>("| Col |\n| --- |\n| a \\| b |").Rows);
        // Any other escape stays as written, and a trailing lone backslash is kept.
        Blocks.AssertRows([["a\\b"]], Blocks.First<MarkdownBlock.Table>("| a |\n| --- |\n| a\\b |").Rows);
        Blocks.AssertRows([["x\\"]], Blocks.First<MarkdownBlock.Table>("| a |\n| --- |\n| x\\").Rows);
    }

    [Fact]
    public void NotATableWithoutDelimiterRow()
    {
        Assert.Equal([BlockKind.Paragraph], Blocks.Kinds("a | b | c\nx | y | z"));
    }

    [Fact]
    public void DelimiterRowMustHaveExactlyAsManyCellsAsTheHeader()
    {
        Assert.Equal([new MarkdownBlock.Paragraph("| a | b |\n| --- |\n| 1 | 2 |")], Blocks.Parse("| a | b |\n| --- |\n| 1 | 2 |"));
    }

    [Fact]
    public void RowsArePaddedAndTruncatedToTheHeaderWidth()
    {
        var table = Blocks.Single<MarkdownBlock.Table>("| a | b |\n| --- | --- |\n| 1 |\n| 1 | 2 | 3 |");
        Blocks.AssertRows([["1", ""], ["1", "2"]], table.Rows);
    }

    [Fact]
    public void TableRowsEndAtALineWithoutAPipe()
    {
        var blocks = Blocks.Parse("| a |\n| --- |\n| 1 |\nplain");
        Assert.Equal([BlockKind.Table, BlockKind.Paragraph], blocks.Select(b => b.Kind));
        Blocks.AssertRows([["1"]], Blocks.As<MarkdownBlock.Table>(blocks[0]).Rows);
        // A row of a single pipe is one empty cell (the trailing test runs after the leading drop).
        var single = Blocks.Parse("| a |\n| --- |\n   |   \nx");
        Assert.Equal([BlockKind.Table, BlockKind.Paragraph], single.Select(b => b.Kind));
        Blocks.AssertRows([[""]], Blocks.As<MarkdownBlock.Table>(single[0]).Rows);
    }

    [Fact]
    public void TableCannotInterruptAParagraph()
    {
        // The paragraph loop carries no table lookahead. Reachable, shipped.
        Assert.Equal([new MarkdownBlock.Paragraph("Intro\n| a |\n| --- |")], Blocks.Parse("Intro\n| a |\n| --- |"));
    }

    [Fact]
    public void HeadingWinsOverTable()
    {
        Assert.Equal([new MarkdownBlock.Heading(1, "a | b"), new MarkdownBlock.Paragraph("---|---")], Blocks.Parse("# a | b\n---|---"));
    }

    [Fact]
    public void TableWinsOverListAndQuoteOnlyWhenItsDelimiterRowIsClean()
    {
        var table = Blocks.Single<MarkdownBlock.Table>("- a | b\n--- | ---");
        Assert.Equal(["- a", "b"], table.Header);
        Assert.Empty(table.Rows);
        // `> ---` fails the `:?-+:?` test at the top level, so the quote branch takes the run and
        // the table is found inside it.
        var quote = Blocks.Single<MarkdownBlock.Quote>("> a | b\n> --- | ---");
        Assert.Equal(["a", "b"], Blocks.As<MarkdownBlock.Table>(quote.Blocks[0]).Header);
    }

    // MARK: - Front matter

    [Fact]
    public void YAMLFrontMatterIsParsedAndHidden()
    {
        var blocks = Blocks.Parse(Blocks.Lines("---", "title: My Book", "author: nettrash", "date: 2026-07-24", "---", "", "# Chapter One", "", "Text."));
        var matter = Blocks.As<MarkdownBlock.FrontMatter>(blocks[0]);
        Assert.Equal([
            new MetadataField("title", "My Book"),
            new MetadataField("author", "nettrash"),
            new MetadataField("date", "2026-07-24"),
        ], matter.Fields);
        // The opening `---` must not survive as a thematic break, nor the metadata as prose.
        Assert.DoesNotContain(BlockKind.ThematicBreak, blocks.Select(b => b.Kind));
        Assert.Equal([BlockKind.FrontMatter, BlockKind.Heading, BlockKind.Paragraph], blocks.Select(b => b.Kind));
    }

    [Fact]
    public void TOMLFrontMatterUsesEquals()
    {
        var matter = Blocks.First<MarkdownBlock.FrontMatter>("+++\ntitle = \"Quoted\"\ndraft = false\n+++\n\nBody.");
        Assert.Equal([new MetadataField("title", "Quoted"), new MetadataField("draft", "false")], matter.Fields);
        // A colon is not a TOML separator, and `+++` is not a rule: the whole thing is prose.
        Assert.Equal([new MarkdownBlock.Paragraph("+++\nk: v\n+++")], Blocks.Parse("+++\nk: v\n+++\n"));
        Assert.Equal([new MetadataField("a", "1")], Blocks.First<MarkdownBlock.FrontMatter>("+++\n# c\na = 1\n+++").Fields);
    }

    [Fact]
    public void FrontMatterOnlyAtTheVeryTopAndOnlyWhenClosed()
    {
        // An unclosed opener stays a thematic break.
        Assert.Equal(BlockKind.ThematicBreak, Blocks.Kinds("---\n\nJust a rule above.")[0]);
        Assert.Equal([BlockKind.ThematicBreak, BlockKind.Paragraph], Blocks.Kinds("---\ntitle: A"));

        // A rule, prose, and a rule again: every word survives.
        var divider = Blocks.Parse("---\n\nIntro the reader must see.\n\n---\n\nMore.");
        Assert.Equal(BlockKind.ThematicBreak, divider[0].Kind);
        Assert.Contains(new MarkdownBlock.Paragraph("Intro the reader must see."), divider);

        // The blank-line guard on its own — the only input that isolates it. Kotlin additionally
        // pins how it then reads: rule, setext h2, paragraph.
        var blankFirst = Blocks.Parse("---\n\ntitle: A\n---\n\nbody");
        Assert.DoesNotContain(BlockKind.FrontMatter, blankFirst.Select(b => b.Kind));
        Assert.Equal([
            new MarkdownBlock.ThematicBreak(),
            new MarkdownBlock.Heading(2, "title: A"),
            new MarkdownBlock.Paragraph("body"),
        ], blankFirst);

        // No `key: value` anywhere means it was never metadata: a break plus a setext heading.
        var setext = Blocks.Parse("---\nChapter One\n---\n\nText.");
        Assert.DoesNotContain(BlockKind.FrontMatter, setext.Select(b => b.Kind));
        Assert.Contains(new MarkdownBlock.Heading(2, "Chapter One"), setext);
        Assert.Equal([BlockKind.ThematicBreak, BlockKind.Heading, BlockKind.ThematicBreak, BlockKind.Paragraph], Blocks.Kinds("---\n# c\n---\nBody"));

        // A fence further down is an ordinary thematic break too.
        Assert.DoesNotContain(BlockKind.FrontMatter, Blocks.Kinds("# Title\n\n---\n\ntitle: not metadata\n\n---"));
        // And it is never front matter inside a block quote.
        var quote = Blocks.First<MarkdownBlock.Quote>("> ---\n> title: x\n> ---");
        Assert.Equal([new MarkdownBlock.ThematicBreak(), new MarkdownBlock.Heading(2, "title: x")], quote.Blocks);
    }

    [Fact]
    public void FrontMatterSkipsWhatTheFlatScanCannotRead()
    {
        var blocks = Blocks.Parse(Blocks.Lines("---", "title: Deep", "# a comment", "tags:", "  - one", "  - two", "---", "Body."));
        var matter = Blocks.As<MarkdownBlock.FrontMatter>(blocks[0]);
        Assert.Equal(new MetadataField("title", "Deep"), matter.Fields[0]);
        Assert.Contains(new MetadataField("tags", ""), matter.Fields);
        Assert.DoesNotContain(matter.Fields, f => f.Key.StartsWith('-'));
        // Exactly one block follows, and it is the body paragraph: the block is consumed whole.
        Assert.Equal(2, blocks.Count);
        Assert.Equal(new MarkdownBlock.Paragraph("Body."), blocks[1]);
    }

    [Fact]
    public void FrontMatterReadsEveryLineWithASeparator()
    {
        // The scan is flat: an indented child with its own colon is a field too, a line with an
        // empty key is skipped, and a value keeps every colon after the first.
        Assert.Equal([new MetadataField("k", "v"), new MetadataField("child", "1")], Blocks.First<MarkdownBlock.FrontMatter>("---\nk: v\n  child: 1\n---\n").Fields);
        Assert.Equal([new MetadataField("k", "v")], Blocks.First<MarkdownBlock.FrontMatter>("---\n: x\nk: v\n---\n").Fields);
        Assert.Equal([new MetadataField("time", "12:30:00")], Blocks.First<MarkdownBlock.FrontMatter>("---\ntime: 12:30:00\n---\n").Fields);
        // The closer may be the last line of the document.
        Assert.Equal([new MarkdownBlock.FrontMatter([new MetadataField("title", "A")])], Blocks.Parse("---\ntitle: A\n---"));
    }

    [Fact]
    public void FrontMatterKeepsDuplicateKeysAndTheirOrder()
    {
        // A record of what the author wrote, not a dictionary.
        Assert.Equal([new MetadataField("tag", "a"), new MetadataField("tag", "b")], Blocks.First<MarkdownBlock.FrontMatter>("---\ntag: a\ntag: b\n---\n\nBody.").Fields);
        Assert.Equal([new MetadataField("a", "1"), new MetadataField("a", "2")], MarkdownParser.FrontMatter("---\na: 1\na: 2\n---\n"));
    }

    [Fact]
    public void FrontMatterStripsOneLayerOfMatchingQuotes()
    {
        // `"` is tried before `'`; both ends must carry the same quote; a lone quote is content.
        Assert.Equal([
            new MetadataField("t", "x"),
            new MetadataField("u", "\"y'"),
            new MetadataField("v", "'"),
        ], Blocks.First<MarkdownBlock.FrontMatter>("---\nt: 'x'\nu: \"y'\nv: '\n---\n\nb").Fields);
    }

    [Fact]
    public void FrontMatterAcceptsDotsAsAYamlCloser()
    {
        Assert.Equal([new MetadataField("title", "A")], MarkdownParser.FrontMatter("---\ntitle: A\n...\n\nbody"));
    }

    [Fact]
    public void FrontMatterAccessor()
    {
        Assert.Equal([new MetadataField("author", "Ann")], MarkdownParser.FrontMatter("---\nauthor: Ann\n---\n\nHi."));
        // Empty, not null — the Swift contract; the VS Code port returns null and is the odd one out.
        Assert.Empty(MarkdownParser.FrontMatter("# Plain\n\nNo metadata."));
    }

    // MARK: - Footnote definitions

    [Fact]
    public void FootnoteDefinitionIsParsedAndNotDrawnInPlace()
    {
        var blocks = Blocks.Parse("Text[^a].\n\n[^a]: The note.");
        Assert.Equal(new MarkdownBlock.FootnoteDefinition("a", "The note."), blocks[^1]);
    }

    [Fact]
    public void FootnoteDefinitionAbsorbsWrappedLines()
    {
        var blocks = Blocks.Parse("X[^a].\n\n[^a]: first line\n  second line\n\nAfter.");
        Assert.Equal(new MarkdownBlock.FootnoteDefinition("a", "first line second line"), blocks[1]);
        Assert.Equal(new MarkdownBlock.Paragraph("After."), blocks[^1]);
        // Each absorbed line is WS-trimmed before the joining space.
        Assert.Equal("n more", Blocks.First<MarkdownBlock.FootnoteDefinition>("[^a]: n\n" + Blocks.Nbsp + " more" + Blocks.Nbsp + " ").Text);
    }

    [Fact]
    public void ConsecutiveFootnoteDefinitionsAreSeparate()
    {
        Assert.Equal([new MarkdownBlock.FootnoteDefinition("a", "one"), new MarkdownBlock.FootnoteDefinition("b", "two")], Blocks.Parse("[^a]: one\n[^b]: two"));
    }

    [Fact]
    public void FootnoteContinuationBreaksOnAListItemButNotOnATable()
    {
        // The footnote loop carries the footnote check and the list check and no table lookahead.
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.List], Blocks.Kinds("[^a]: note\n- item"));
        Assert.Equal(new MarkdownBlock.FootnoteDefinition("a", "note | a | | --- |"), Blocks.Single<MarkdownBlock.FootnoteDefinition>("[^a]: note\n| a |\n| --- |"));
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.Heading], Blocks.Kinds("[^a]: n\n# h"));
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.Quote], Blocks.Kinds("[^a]: n\n> q"));
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.ThematicBreak], Blocks.Kinds("[^a]: n\n---"));
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.PageBreak], Blocks.Kinds("[^a]: n\n\\pagebreak"));
        Assert.Equal([BlockKind.FootnoteDefinition], Blocks.Kinds("[^a]: n\n<!-- c -->"));
        Assert.Equal([BlockKind.FootnoteDefinition, BlockKind.CodeBlock], Blocks.Kinds("[^a]: n\n```\nx\n```"));
        // The paragraph loop has no footnote check: a definition cannot interrupt prose.
        Assert.Equal([new MarkdownBlock.Paragraph("p\n[^a]: n")], Blocks.Parse("p\n[^a]: n"));
    }

    [Fact]
    public void FootnoteIdentifierCharacterSet()
    {
        Assert.Equal(("a-1_B", "ok"), MarkdownParser.ParseFootnoteDefinition("[^a-1_B]: ok"));
        Assert.Equal(("a-", "x"), MarkdownParser.ParseFootnoteDefinition("[^a-]: x"));
        Assert.Equal(("1", "x"), MarkdownParser.ParseFootnoteDefinition("[^1]: x"));
        // Non-ASCII is rejected: the renderer's reference pattern is ASCII-only, so a Unicode
        // identifier would be a definition no reference could ever name.
        foreach (var bad in new[]
        {
            "[^café]: no", "[^сн]: no", "[^two words]: no", "[^a.b]: no", "[^]: no",
            "[a]: not a footnote", "[^a] no colon", "[^a]]: x", "[^ a]: x",
        })
        {
            Assert.Null(MarkdownParser.ParseFootnoteDefinition(bad));
        }
    }

    [Fact]
    public void FootnoteDefinitionTextIsTrimmedWithFoundationWhitespace()
    {
        Assert.Equal(("a", "x"), MarkdownParser.ParseFootnoteDefinition("[^a]:x"));
        Assert.Equal(("a", "x  y"), MarkdownParser.ParseFootnoteDefinition("[^a]:  x  y  "));
        Assert.Equal(("a", ""), MarkdownParser.ParseFootnoteDefinition("  [^a]:"));
        Assert.Equal(("a", "note"), MarkdownParser.ParseFootnoteDefinition("[^a]:" + Blocks.Nbsp + "note"));
    }

    [Fact]
    public void UnicodeSpacingAndWordBoundariesAgreeAcrossPlatforms()
    {
        // Padded with a non-breaking space, a fence was metadata on Apple and a paragraph on
        // Android. The inline half of the Swift test belongs to the HTML writer.
        Assert.Equal([new MetadataField("title", "A")], MarkdownParser.FrontMatter("---\ntitle: A\n---" + Blocks.Nbsp + "\n\nbody"));
        Assert.NotNull(MarkdownParser.ParseFootnoteDefinition(Blocks.Nbsp + "[^a]: note"));
        Assert.Equal("note", MarkdownParser.ParseFootnoteDefinition("[^a]: note" + Blocks.Nbsp)?.Text);
    }

    // MARK: - Page breaks

    [Fact]
    public void PageBreakParses()
    {
        var blocks = Blocks.Parse("before\n\n\\newpage\n\nafter");
        Assert.Equal(3, blocks.Count);
        Assert.Equal(new MarkdownBlock.PageBreak(), blocks[1]);
        Assert.Equal([BlockKind.PageBreak], Blocks.Kinds("\\pagebreak"));
    }

    [Fact]
    public void PageBreakVariantInterruptsParagraph()
    {
        var blocks = Blocks.Parse("line one\n\\pagebreak\nline two");
        Assert.Equal([BlockKind.Paragraph, BlockKind.PageBreak, BlockKind.Paragraph], blocks.Select(b => b.Kind));
    }

    [Fact]
    public void PageBreakNeedsTheLineToItself()
    {
        Assert.Equal([new MarkdownBlock.Paragraph("\\newpage now")], Blocks.Parse("\\newpage now"));
    }

    // MARK: - Author notes and HTML comments

    [Fact]
    public void NoteCommentBecomesNoteBlock()
    {
        Assert.Equal([new MarkdownBlock.Note("check the intro")], Blocks.Parse("<!-- note: check the intro -->"));
    }

    [Fact]
    public void NotePrefixIsCaseFolded()
    {
        Assert.Equal("shouty", Blocks.First<MarkdownBlock.Note>("<!-- NOTE: shouty -->").Text);
        Assert.Equal("titled", Blocks.First<MarkdownBlock.Note>("<!-- Note: titled -->").Text);
    }

    [Fact]
    public void PlainCommentIsDropped()
    {
        Assert.Equal([BlockKind.Paragraph, BlockKind.Paragraph], Blocks.Kinds("a\n\n<!-- just a comment -->\n\nb"));
        Assert.Empty(Blocks.Parse("<!-- x -->"));
        // A comment interrupts a paragraph like any other block start.
        Assert.Equal([new MarkdownBlock.Paragraph("p"), new MarkdownBlock.Paragraph("q")], Blocks.Parse("p\n<!-- c -->\nq"));
    }

    [Fact]
    public void MultilineNote()
    {
        Assert.Equal(new MarkdownBlock.Note("first\nsecond"), Blocks.Single<MarkdownBlock.Note>("<!-- note: first\nsecond -->"));
    }

    [Fact]
    public void EmptyNoteIsStillANote()
    {
        Assert.Equal([new MarkdownBlock.Note("")], Blocks.Parse("<!-- note:-->"));
    }

    [Fact]
    public void TextAfterTheClosingMarkerIsDiscarded()
    {
        // Known wart: the whole closing line is consumed.
        Assert.Empty(Blocks.Parse("<!-- c --> visible text"));
    }

    [Fact]
    public void UnclosedCommentSwallowsToTheEnd()
    {
        Assert.Equal([new MarkdownBlock.Note("open\nmore")], Blocks.Parse("<!-- note: open\nmore"));
        Assert.Empty(Blocks.Parse("<!-- never closed\n# Swallowed"));
    }

    [Fact]
    public void CommentStartAllowsSpacesOnly()
    {
        // A tab before `<!--` disqualifies the line: the indentation drop is U+0020 alone.
        Assert.Equal([new MarkdownBlock.Paragraph("\t<!-- x -->")], Blocks.Parse("\t<!-- x -->"));
        Assert.Equal([new MarkdownBlock.Note("x")], Blocks.Parse("   <!-- note: x -->"));
    }

    // MARK: - Mixed document

    [Fact]
    public void MixedDocumentBlockSequence()
    {
        var kinds = Blocks.Kinds(Blocks.Lines(
            "# Title", "", "Intro paragraph.", "", "- one", "- two", "", "> a quote", "", "```", "code", "```", "", "---"));
        Assert.Equal([
            BlockKind.Heading, BlockKind.Paragraph, BlockKind.List, BlockKind.Quote, BlockKind.CodeBlock, BlockKind.ThematicBreak,
        ], kinds);
    }

    [Fact]
    public void ParagraphBreaksOnEveryBlockStartExceptTableAndFootnote()
    {
        Assert.Equal([BlockKind.Paragraph, BlockKind.List], Blocks.Kinds("p\n- a"));
        Assert.Equal([BlockKind.Paragraph, BlockKind.Heading], Blocks.Kinds("p\n# h"));
        Assert.Equal([BlockKind.Paragraph, BlockKind.Quote], Blocks.Kinds("p\n> q"));
        Assert.Equal([BlockKind.Paragraph, BlockKind.CodeBlock], Blocks.Kinds("p\n```\nx\n```"));
    }

    // MARK: - Scalar-exact block delimiters
    //
    // Every expected value is what the Kotlin port already produced and what the Swift now
    // produces through ScalarText. A UTF-16 scanner passes these by construction; they stay
    // because they pin outputs, not mechanisms.

    [Fact]
    public void ParserListMarkerSurvivesAMarkOnItsSpace()
    {
        foreach (var mark in Blocks.Marks)
        {
            var list = Blocks.First<MarkdownBlock.List>("- " + mark + "[draft]");
            Assert.False(list.Ordered);
            Assert.Equal([mark + "[draft]"], list.Items.Select(i => i.Text));
        }
    }

    [Fact]
    public void ParserFenceSurvivesAMarkOnItsOpeningRun()
    {
        foreach (var mark in Blocks.Marks)
        {
            Assert.Equal(new MarkdownBlock.CodeBlock(mark + "js", "code()"), Blocks.First<MarkdownBlock.CodeBlock>("```" + mark + "js\ncode()\n```"));
        }
    }

    [Fact]
    public void ParserTableRowSurvivesAMarkOnItsOnlyPipe()
    {
        foreach (var mark in Blocks.Marks)
        {
            var blocks = Blocks.Parse("a | b\n--- | ---\n1 |" + mark + " 2");
            Assert.Single(blocks);
            Blocks.AssertRows([["1", mark + " 2"]], Blocks.As<MarkdownBlock.Table>(blocks[0]).Rows);
        }
    }

    [Fact]
    public void ParserFootnoteDefinitionSurvivesAMarkOnItsColon()
    {
        foreach (var mark in Blocks.Marks)
        {
            Assert.Equal(new MarkdownBlock.FootnoteDefinition("a", mark + " the note"), Blocks.First<MarkdownBlock.FootnoteDefinition>("[^a]:" + mark + " the note"));
            Assert.Equal(("a", mark + " the note"), MarkdownParser.ParseFootnoteDefinition("[^a]:" + mark + " the note"));
        }
    }

    [Fact]
    public void ParserQuoteMarkerSurvivesAMarkAfterIt()
    {
        foreach (var mark in Blocks.Marks)
        {
            Assert.Equal([new MarkdownBlock.Paragraph(mark + " quoted")], Blocks.First<MarkdownBlock.Quote>(">" + mark + " quoted").Blocks);
            // The stripper eats `>` and exactly one space; the author's mark stays.
            Assert.Equal([new MarkdownBlock.Paragraph(mark + "text")], Blocks.First<MarkdownBlock.Quote>("> " + mark + "text").Blocks);
        }
    }

    [Fact]
    public void ParserHeadingSurvivesAMarkOnItsMarkerSpace()
    {
        foreach (var mark in Blocks.Marks)
        {
            Assert.Equal(new MarkdownBlock.Heading(1, mark + "Heading"), Blocks.First<MarkdownBlock.Heading>("# " + mark + "Heading"));
            Assert.Equal([mark + "Heading"], MarkdownParser.Outline("# " + mark + "Heading").Select(e => e.Text));
        }
    }

    [Fact]
    public void ParserCommentEndSurvivesAMarkAfterIt()
    {
        // The loudest of them: a mark on the closing `-->` left the comment open on Apple and the
        // rest of the document was swallowed into it.
        foreach (var mark in Blocks.Marks)
        {
            Assert.Equal([new MarkdownBlock.Note("private"), new MarkdownBlock.Paragraph("after")], Blocks.Parse("<!-- note: private -->" + mark + "\n\nafter"));
            Assert.Equal(["After"], MarkdownParser.Outline("<!-- x -->" + mark + "\n\n# After").Select(e => e.Text));
            Assert.Equal(["n"], MarkdownParser.Notes("<!-- note: n -->" + mark).Select(n => n.Text));
            // A mark right after `<!--` still opens a comment; it is plain, so only `after` remains.
            Assert.Equal([new MarkdownBlock.Paragraph("after")], Blocks.Parse("<!--" + mark + " hidden -->\n\nafter"));
        }
    }

    [Fact]
    public void ParserFrontMatterFieldSurvivesAMarkOnItsSeparator()
    {
        foreach (var mark in Blocks.Marks)
        {
            var matter = Blocks.First<MarkdownBlock.FrontMatter>("---\ntitle: T\nauthor:" + mark + " A\n---\n\nbody");
            Assert.Equal(["title", "author"], matter.Fields.Select(f => f.Key));
            Assert.Equal(["T", mark + " A"], matter.Fields.Select(f => f.Value));
            // Quote stripping counts units, so the mark behind the opening quote survives.
            Assert.Equal([new MetadataField("title", mark + "Q")], Blocks.First<MarkdownBlock.FrontMatter>("---\ntitle: \"" + mark + "Q\"\n---\n\nbody").Fields);
        }
    }

    [Fact]
    public void ParserTaskBoxIsScalarExact()
    {
        foreach (var mark in Blocks.Marks)
        {
            var list = Blocks.First<MarkdownBlock.List>("- [ ] " + mark + "task");
            Assert.Equal([false], list.Items.Select(i => i.Task));
            Assert.Equal([mark + "task"], list.Items.Select(i => i.Text));
        }
    }

    // MARK: - ParseWithLines

    [Fact]
    public void ParseWithLinesIsTheSameWalkAsParse()
    {
        // Holds by construction — both are projections of one scanner — and is asserted over the
        // whole fixture corpus in TestDataParseTests.
        var source = Blocks.Lines(
            "---", "title: T", "---", "", "# Heading", "", "Paragraph.", "", "- item", "", "> quoted", "",
            "```js", "code()", "```", "", "| a | b |", "| --- | --- |", "| 1 | 2 |", "", "---", "",
            "\\newpage", "", "<!-- note: n -->", "", "[^a]: note");
        Assert.Equal(MarkdownParser.Parse(source), MarkdownParser.ParseWithLines(source).Select(p => p.Block));
    }

    [Fact]
    public void ParseWithLinesRecordsTheLineEachTopLevelBlockStartedOn()
    {
        var placed = MarkdownParser.ParseWithLines(Blocks.Lines("# Heading", "", "Paragraph", "continued", "", "```", "code", "```"));
        Assert.Equal([0, 2, 5], placed.Select(p => p.Line));
    }

    [Fact]
    public void ParseWithLinesPlacesFrontMatterOnLineZero()
    {
        var placed = MarkdownParser.ParseWithLines(Blocks.Lines("---", "title: T", "---", "", "# H"));
        Assert.Equal([(BlockKind.FrontMatter, 0), (BlockKind.Heading, 4)], placed.Select(p => (p.Block.Kind, p.Line)));
    }

    [Fact]
    public void ParseWithLinesGivesASetextHeadingTheLineOfItsText()
    {
        var placed = MarkdownParser.ParseWithLines(Blocks.Lines("Intro", "", "Title", "==="));
        Assert.Equal([(BlockKind.Paragraph, 0), (BlockKind.Heading, 2)], placed.Select(p => (p.Block.Kind, p.Line)));
        // Blocks nested in a quote carry no line; the quote itself does.
        Assert.Equal([(BlockKind.Quote, 1), (BlockKind.Paragraph, 3)], MarkdownParser.ParseWithLines("\n> q\n\np").Select(p => (p.Block.Kind, p.Line)));
    }

    // MARK: - Raw diagram documents

    [Fact]
    public void RawPlantUMLDocumentIsRecognised()
    {
        foreach (var source in new[]
        {
            "@startuml\nAlice -> Bob: hi\n@enduml\n",
            "' header comment\n\n@startmindmap\n* root\n@endmindmap",
            "   \n@startuml\n@enduml",
            "\r\n@startuml\r\nA -> B\r\n@enduml\r\n",
            "' note\r\n\r\n@startmindmap\r\n* r\r\n@endmindmap",
            "' note\r\r@startuml\r@enduml",
            // The probe splits on the wide newline set, so NEL and LS end lines here.
            "' c\u0085@startuml\u0085@enduml",
            "\u2028@startjson\u2028{}\u2028@endjson",
        })
        {
            Assert.True(MarkdownParser.IsRawPlantUml(source), source);
        }
        // The first qualifying line decides, so prose that merely mentions the opener stays Markdown.
        Assert.False(MarkdownParser.IsRawPlantUml("# Title\n\nSome prose about @startuml in passing."));
        Assert.False(MarkdownParser.IsRawPlantUml(""));
        Assert.False(MarkdownParser.IsRawPlantUml("' only a comment"));
    }

    [Fact]
    public void RawGraphvizDocumentMatchesDotsGrammar()
    {
        // `graph` is an ordinary English word, so the opener is matched against
        // `[strict] (graph | digraph) [ID] '{'` rather than by prefix.
        foreach (var yes in new[]
        {
            "digraph { a -> b }",
            "graph {}",
            "strict digraph G {\n}",
            "DiGraph Foo {\n}",
            "digraph{a}",
            "digraph\n{\n  a\n}",
            "// generated\n\n/* by hand */\ndigraph { a }",
            "digraph \"my graph\" {\n}",
            "digraph cafe\u0301 {\n}",
            "digraph café {\n}",
            "digraph Ⅹ {\n}",
            "digraph \U0001D518 {\n}",
            "digraph my_graph2 {",
            "// generated\u0085digraph G {\u0085}",
            "\r\ndigraph G {\r\n  a -> b;\r\n}\r\n",
            "// generated\r\ndigraph G {\r\n}\r\n",
            "  strict graph\tG\t{",
        })
        {
            Assert.True(MarkdownParser.IsRawGraphviz(yes), yes);
        }
        foreach (var no in new[]
        {
            // A `#` line is deliberately not a DOT comment: every Markdown heading starts with one.
            "# Notes\n\ngraph { the mental model }",
            "graph theory is a branch of maths.\n\nSee $\\frac{a}{b}$.",
            "digraph models are useful { in theory }",
            "graphviz is a fine tool { see }",
            "digraphs are a topic { here }",
            "digraph without a brace",
            "# Title\n\nSome prose about digraph { } in passing.",
            "digraph \"unterminated {",
            "digraph a b {",
            // `strict` must be followed by U+0020: the keyword test is a literal prefix.
            "strict\tgraph {",
            "",
        })
        {
            Assert.False(MarkdownParser.IsRawGraphviz(no), no);
        }
    }

    [Fact]
    public void RawDiagramLineSplitterIsTheWideNewlineSet()
    {
        Assert.Equal(["a", "b", "c", "d", "e", "f", "g", ""], MarkdownParser.Lines("a\nb\r\nc\rd\u0085e\u2028f\u2029g\n"));
        Assert.Equal(["a", "b", "c"], MarkdownParser.Lines("a\u000Bb\u000Cc"));
    }
}
