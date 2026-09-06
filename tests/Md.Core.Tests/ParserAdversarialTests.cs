using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial parity tests added by review. Every expected value below was produced by the real
/// md.macOS parser — <c>MarkdownParser.swift</c> + <c>ScalarText.swift</c> compiled into a dump
/// oracle and run on these exact inputs — never by reading the C# output back. Each test names the
/// defect or the surviving mutant that motivated it.
/// </summary>
public class ParserAdversarialTests
{
    private static readonly string[] Marks = ["́", "️", "‍"];

    /// <summary>
    /// The copy under review shipped with mutant M36 live (<c>line.Length == 0</c> as the blank-line
    /// test). Every other branch refuses a whitespace-only line, the paragraph loop breaks before
    /// appending, the cursor never moves, and <c>Parse("a\n   \nb")</c> spun forever — the test host
    /// hung and the suite reported "Test host process crashed" after 149 tests. Swift trims the WS
    /// set: such a line is a separator. Bounded so a regression fails instead of hanging the run.
    /// </summary>
    [Fact]
    public void WhitespaceOnlyLineIsABlankLineAndNeverHangsTheParser()
    {
        var task = Task.Run(() => MarkdownParser.Parse("a\n   \nb\n\t\nc\n​\nd\n \ne\n　\nf\n"));
        Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "parser did not return: whitespace-only line loops forever");
        Assert.Equal(
            [
                new MarkdownBlock.Paragraph("a"), new MarkdownBlock.Paragraph("b"), new MarkdownBlock.Paragraph("c"),
                new MarkdownBlock.Paragraph("d"), new MarkdownBlock.Paragraph("e"), new MarkdownBlock.Paragraph("f"),
            ],
            task.Result);

        // The same separator inside every container: it ends a list, splits a quote's paragraphs,
        // ends a footnote definition, and ends a table's rows (the next row becomes prose).
        Assert.Equal([BlockKind.List, BlockKind.List], MarkdownParser.Parse("- a\n   \n- b\n").Select(b => b.Kind));
        var quote = Assert.IsType<MarkdownBlock.Quote>(Assert.Single(MarkdownParser.Parse("> a\n>    \n> b\n")));
        Assert.Equal([new MarkdownBlock.Paragraph("a"), new MarkdownBlock.Paragraph("b")], quote.Blocks);
        Assert.Equal([new MarkdownBlock.FootnoteDefinition("a", "x"), new MarkdownBlock.Paragraph("y")], MarkdownParser.Parse("[^a]: x\n \t \ny\n"));
        var table = MarkdownParser.Parse("a | b\n--- | ---\n1 | 2\n   \n3 | 4\n");
        Assert.Equal([BlockKind.Table, BlockKind.Paragraph], table.Select(b => b.Kind));
        Assert.Equal(new MarkdownBlock.Paragraph("3 | 4"), table[1]);
        // A document of only spaces or only a ZWSP parses to nothing, like the empty document.
        Assert.Empty(MarkdownParser.Parse("   "));
        Assert.Empty(MarkdownParser.Parse("​"));
    }

    /// <summary>
    /// Survivor K05: the front-matter blank guard compared <c>lines[1].Length</c>; a line of spaces
    /// straight after the opener must defeat front matter exactly as an empty line does — the
    /// <c>---</c> is then a rule and <c>title: A</c> a setext H2 (which the outline also lists).
    /// </summary>
    [Fact]
    public void FrontMatterBlankGuardUsesTheWhitespaceSet()
    {
        var blocks = MarkdownParser.Parse("---\n   \ntitle: A\n---\n\nbody\n");
        Assert.Equal(
            [new MarkdownBlock.ThematicBreak(), new MarkdownBlock.Heading(2, "title: A"), new MarkdownBlock.Paragraph("body")],
            blocks);
        Assert.Equal([new OutlineEntry(2, "title: A", "title-a", 2)], MarkdownParser.Outline("---\n   \ntitle: A\n---\n\nbody\n"));
        Assert.Empty(MarkdownParser.FrontMatter("---\n   \ntitle: A\n---\n\nbody\n"));
        // A ZWSP-only line is blank to the WS set as well.
        Assert.Equal(BlockKind.ThematicBreak, MarkdownParser.Parse("---\n​\ntitle: A\n---\nbody\n")[0].Kind);
        // Whereas a document that is nothing but front matter is one block, closed or not by EOF.
        Assert.Equal([new MarkdownBlock.FrontMatter([new MetadataField("title", "X")])], MarkdownParser.Parse("---\ntitle: X\n---"));
        Assert.Equal([new MarkdownBlock.FrontMatter([new MetadataField("title", "X")])], MarkdownParser.Parse("---\ntitle: X\n---\n\n   \n"));
        Assert.Equal([new MarkdownBlock.ThematicBreak(), new MarkdownBlock.Paragraph("title: X")], MarkdownParser.Parse("---\ntitle: X\n"));
        // A closer on line 1 leaves no field, so it is never metadata.
        Assert.Equal(BlockKind.ThematicBreak, MarkdownParser.Parse("---\n---\ntitle: A\n---\nbody\n")[0].Kind);
    }

    /// <summary>
    /// Survivor K06: a closing fence must carry nothing but whitespace after its run. A line that
    /// starts with the fence run but goes on is code, on both fence characters; and a fence opened
    /// with four backticks is not closed by three, while three is closed by four.
    /// </summary>
    [Fact]
    public void ClosingFenceMustBeBareAndAtLeastAsLong()
    {
        Assert.Equal(
            [new MarkdownBlock.CodeBlock(null, "code\n``` x\nmore"), new MarkdownBlock.Paragraph("after")],
            MarkdownParser.Parse("```\ncode\n``` x\nmore\n```\nafter\n"));
        Assert.Equal(
            [new MarkdownBlock.CodeBlock("js", "code\n~~~ js\nmore")],
            MarkdownParser.Parse("~~~js\ncode\n~~~ js\nmore\n~~~"));
        Assert.Equal(
            [new MarkdownBlock.CodeBlock(null, "code\n```\nmore"), new MarkdownBlock.Paragraph("after")],
            MarkdownParser.Parse("````\ncode\n```\nmore\n````\nafter\n"));
        Assert.Equal(
            [new MarkdownBlock.CodeBlock(null, "code"), new MarkdownBlock.Paragraph("after")],
            MarkdownParser.Parse("```\ncode\n````\nafter\n"));
        // A tilde fence is never closed by backticks.
        Assert.Equal([new MarkdownBlock.CodeBlock(null, "code\n```\nmore")], MarkdownParser.Parse("~~~\ncode\n```\nmore\n~~~\n"));
        // The outline and the notes walk fences with the same rule, so a `# heading` after a
        // non-closing "``` x" line stays inside the fence for them too.
        Assert.Empty(MarkdownParser.Outline("```\n``` x\n# inside\n```\n"));
        Assert.Empty(MarkdownParser.Notes("```\n``` x\n<!-- note: inside -->\n```\n"));
    }

    /// <summary>
    /// Mutant K07 (a mixed <c>=</c>/<c>-</c> run accepted as an underline) was killed by the
    /// existing suite; this pins the oracle's answers directly: a setext underline is all <c>=</c> or
    /// all <c>-</c>, a mixed run is prose, and so the two lines stay one paragraph. Trimmed with the
    /// WS set on both sides.
    /// </summary>
    [Fact]
    public void SetextUnderlineRefusesAMixedRun()
    {
        Assert.Equal([new MarkdownBlock.Paragraph("Title\n-=-")], MarkdownParser.Parse("Title\n-=-\n"));
        Assert.Equal([new MarkdownBlock.Paragraph("Title\n=-=")], MarkdownParser.Parse("Title\n=-=\n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "Title")], MarkdownParser.Parse("Title\n  ===  \n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "Title")], MarkdownParser.Parse("Title\n === \n"));
        Assert.Empty(MarkdownParser.Outline("Title\n-=-\n"));
        // An underline straight after a list item is a rule (the item's continuation loop breaks on
        // it), while `===` is not a rule and is absorbed into the item.
        Assert.Equal([BlockKind.List, BlockKind.ThematicBreak], MarkdownParser.Parse("- a\n---\n").Select(b => b.Kind));
        var list = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.Parse("- a\n===\n")));
        Assert.Equal(["a ==="], list.Items.Select(i => i.Text));
    }

    /// <summary>
    /// Mutant K09 (<c>(indent + 1) / 2</c>) was killed by the existing suite; this pins the odd
    /// indents and the tab stops from the oracle: the level is <c>indent / 2</c> with integer
    /// division, so one and three spaces round down; tabs advance to the next 4-column stop (and
    /// only in the list-marker indent).
    /// </summary>
    [Fact]
    public void ListLevelIsIndentDividedByTwoRoundedDown()
    {
        var odd = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.Parse("- a\n - b\n   - c\n     - d\n")));
        Assert.Equal([0, 0, 1, 2], odd.Items.Select(i => i.Level));
        var tabs = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.Parse("- a\n\t- b\n  \t- c\n    - d\n \t- e\n\t\t- f\n")));
        Assert.Equal(["a", "b", "c", "d", "e", "f"], tabs.Items.Select(i => i.Text));
        Assert.Equal([0, 2, 2, 2, 2, 4], tabs.Items.Select(i => i.Level));
    }

    /// <summary>
    /// Survivor K11: a list item's continuation loop breaks on a comment start, so a note written
    /// under an item is a note block (and listed by <c>Notes</c>), not text glued to the item. The
    /// footnote loop breaks on it as well; the plain comment is then dropped.
    /// </summary>
    [Fact]
    public void ListAndFootnoteContinuationBreakOnACommentStart()
    {
        Assert.Equal(
            [new MarkdownBlock.List(false, [new ListItem("item", 0, null, null)]), new MarkdownBlock.Note("n"), new MarkdownBlock.Paragraph("rest")],
            MarkdownParser.Parse("- item\n<!-- note: n -->\nrest\n"));
        Assert.Equal([new NoteEntry("n", 1)], MarkdownParser.Notes("- item\n<!-- note: n -->\nrest\n"));
        Assert.Equal(
            [new MarkdownBlock.List(true, [new ListItem("one", 0, 1, null)]), new MarkdownBlock.Paragraph("after")],
            MarkdownParser.Parse("1. one\n   <!-- plain -->\nafter"));
        Assert.Equal(
            [new MarkdownBlock.FootnoteDefinition("a", "x"), new MarkdownBlock.Paragraph("y")],
            MarkdownParser.Parse("[^a]: x\n<!-- c -->\ny"));
        // A tab before `<!--` is not a comment start, so that line IS continuation.
        var tabbed = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.Parse("- item\n\t<!-- note: n -->")));
        Assert.Equal(["item <!-- note: n -->"], tabbed.Items.Select(i => i.Text));
    }

    /// <summary>
    /// Survivor K13: the CSV walker keeps a final field that has no separator and no newline after
    /// it — a one-column file without a trailing newline is still a row. Derived from the Swift
    /// <c>if !field.isEmpty || !row.isEmpty</c>, which the report reproduces verbatim.
    /// </summary>
    [Fact]
    public void DelimitedLastFieldWithoutSeparatorOrNewlineIsARow()
    {
        Assert.Equal<IEnumerable<string>>([["x"]], DelimitedTable.ParseDelimited("x", ','));
        Assert.Equal<IEnumerable<string>>([["a"], ["b"]], DelimitedTable.ParseDelimited("a\nb", ','));
        Assert.Equal<IEnumerable<string>>([["a", "b"], ["c"]], DelimitedTable.ParseDelimited("a,b\nc", ','));
        // …but a quoted empty field at the very end is dropped, as in Swift (`""` → no row).
        Assert.Empty(DelimitedTable.ParseDelimited("\"\"", ','));
        var table = DelimitedTable.From("n\n1\n2", ',');
        Assert.NotNull(table);
        Assert.Equal(["n"], table!.Header);
        Assert.Equal([ColumnAlignment.Trailing], table.Alignments);
        Assert.Equal<IEnumerable<string>>([["1"], ["2"]], table.Rows);
    }

    /// <summary>
    /// The brief's remaining invented inputs, each checked against the Swift oracle: CR-only and
    /// CRLF-only files, a footnote definition inside a quote, a delimiter row carrying a mark, the
    /// 32/33/34-deep quote boundary, blank-separated items, and a heading whose closing hashes carry
    /// a mark on the last (or an inner) hash.
    /// </summary>
    [Fact]
    public void BriefsInventedInputsMatchTheSwiftOracle()
    {
        // CRLF-only and CR-only documents parse as their LF twins would.
        var crlf = MarkdownParser.Parse("# Title\r\n\r\n- a\r\n- b\r\n\r\n> q\r\n> r\r\n\r\n| a | b |\r\n| --- | --- |\r\n| 1 | 2 |\r\n\r\n```js\r\ncode\r\n```\r\n\r\n[^a]: note\r\n  more\r\n\r\n---\r\ntitle: x\r\n---\r\n");
        // The trailing `---` / `title: x` / `---` is a rule and then a setext H2 — this is not the
        // top of the document, so it is never front matter (oracle: HR, H2|title: x).
        Assert.Equal(
            [BlockKind.Heading, BlockKind.List, BlockKind.Quote, BlockKind.Table, BlockKind.CodeBlock, BlockKind.FootnoteDefinition, BlockKind.ThematicBreak, BlockKind.Heading],
            crlf.Select(b => b.Kind));
        Assert.Equal(new MarkdownBlock.FootnoteDefinition("a", "note more"), crlf[5]);
        Assert.Equal(new MarkdownBlock.Quote([new MarkdownBlock.Paragraph("q\nr")]), crlf[2]);
        Assert.Equal(new MarkdownBlock.Heading(2, "title: x"), crlf[7]);
        // CRLF counts as one line for the outline's line numbers: the setext text sits on line 20.
        Assert.Equal(
            [new OutlineEntry(1, "Title", "title", 0), new OutlineEntry(2, "title: x", "title-x", 20)],
            MarkdownParser.Outline("# Title\r\n\r\n- a\r\n- b\r\n\r\n> q\r\n> r\r\n\r\n| a | b |\r\n| --- | --- |\r\n| 1 | 2 |\r\n\r\n```js\r\ncode\r\n```\r\n\r\n[^a]: note\r\n  more\r\n\r\n---\r\ntitle: x\r\n---\r\n"));
        Assert.Equal(
            [new MarkdownBlock.Heading(1, "Title"), new MarkdownBlock.Paragraph("para\nline2"), new MarkdownBlock.List(false, [new ListItem("a", 0, null, null), new ListItem("b", 0, null, null)])],
            MarkdownParser.Parse("# Title\r\rpara\rline2\r\r- a\r- b\r"));

        // A footnote definition inside a quote, with its continuation; a bare `>` line separates.
        Assert.Equal(
            [new MarkdownBlock.Quote([new MarkdownBlock.FootnoteDefinition("a", "note more"), new MarkdownBlock.Paragraph("text[^a]")])],
            MarkdownParser.Parse("> [^a]: note\n> more\n>\n> text[^a]\n"));
        Assert.Equal(
            [new MarkdownBlock.Quote([new MarkdownBlock.FootnoteDefinition("a", "note"), new MarkdownBlock.List(false, [new ListItem("item", 0, null, null)])])],
            MarkdownParser.Parse("> [^a]: note\n> - item\n"));

        // A mark anywhere in a delimiter cell makes the pair prose; a mark after a header pipe does not.
        foreach (var mark in Marks)
        {
            Assert.Equal([new MarkdownBlock.Paragraph("a | b\n---" + mark + " | ---\n1 | 2")], MarkdownParser.Parse("a | b\n---" + mark + " | ---\n1 | 2\n"));
            Assert.Equal([new MarkdownBlock.Paragraph("a | b\n:---: | ---:" + mark + "\n1 | 2")], MarkdownParser.Parse("a | b\n:---: | ---:" + mark + "\n1 | 2\n"));
            var table = Assert.IsType<MarkdownBlock.Table>(Assert.Single(MarkdownParser.Parse("a |" + mark + " b\n--- | ---\n1 | 2\n")));
            Assert.Equal(["a", mark + " b"], table.Header);
        }

        // The depth cap: 32 markers nest 32 deep with `x`; 33 nest 33 deep with `x`; 34 nest 33 deep
        // and the innermost paragraph keeps the one marker the cap refused to recurse into.
        static (int Depth, MarkdownBlock Inner) Descend(string source)
        {
            var depth = 0;
            MarkdownBlock current = Assert.Single(MarkdownParser.Parse(source));
            while (current is MarkdownBlock.Quote quote)
            {
                depth++;
                current = Assert.Single(quote.Blocks);
            }
            return (depth, current);
        }
        Assert.Equal((32, new MarkdownBlock.Paragraph("x")), Descend(new string('>', 32) + " x\n"));
        Assert.Equal((33, new MarkdownBlock.Paragraph("x")), Descend(new string('>', 33) + " x\n"));
        Assert.Equal((33, new MarkdownBlock.Paragraph("> x")), Descend(new string('>', 34) + " x\n"));

        // Blank-separated items are separate lists; a run's ordered flag is per run.
        Assert.Equal(
            [new MarkdownBlock.List(false, [new ListItem("a", 0, null, null)]), new MarkdownBlock.List(false, [new ListItem("b", 0, null, null)]), new MarkdownBlock.List(true, [new ListItem("c", 0, 1, null)])],
            MarkdownParser.Parse("- a\n\n- b\n\n1. c\n"));

        // Closing hashes: a mark on the last hash means the text does not end in `#`, so nothing is
        // stripped; a mark on an inner hash leaves a one-hash run preceded by the mark, not a space —
        // also kept. Only a run preceded by U+0020 / U+0009 is stripped, and a run preceded by NBSP is not.
        Assert.Equal([new MarkdownBlock.Heading(1, "Title ##́")], MarkdownParser.Parse("# Title ##́\n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "Title #́#")], MarkdownParser.Parse("# Title #́#\n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "Title")], MarkdownParser.Parse("# Title\t##\n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "Title ##")], MarkdownParser.Parse("# Title ##\n"));
        Assert.Equal([new MarkdownBlock.Heading(1, "")], MarkdownParser.Parse("# ##\n"));
        Assert.Equal([new OutlineEntry(1, "Title ##́", "title-́", 0)], MarkdownParser.Outline("# Title ##́\n"));

        // Slugs: no Final_Sigma, full mapping for U+0130, enclosed alphabetics kept, marks kept,
        // ZWJ/ZWSP dropped, the bare-slug counter producing GitHub's duplicate id.
        var slugs = MarkdownParser.Outline("# ΟΔΟΣ ΣΑΣ Σ\n# İstanbul\n# ǅ ẞ\n# Ⓐ \U0001F130 \U0001F150 \U0001F170 ⓐ\n# Ⅻ ½ ٣ ²\n# \U0001D400 a‍b c​d\n# Same\n# Same 1\n# Same\n# same-1\n").Select(e => e.Slug);
        Assert.Equal(
            ["οδοσ-σασ-σ", "i̇stanbul", "ǆ-ß", "ⓐ-\U0001F130-\U0001F150-\U0001F170-ⓐ", "ⅻ-½-٣-²", "\U0001D400-ab-cd", "same", "same-1", "same-1", "same-1-1"],
            slugs);

        // Ordinals: nine digits parse, ten do not (the line becomes a continuation), leading zeros go.
        var ordinals = Assert.IsType<MarkdownBlock.List>(Assert.Single(MarkdownParser.Parse("123456789. nine\n1234567890. ten\n0. zero\n007) seven\n")));
        Assert.Equal([123456789, 0, 7], ordinals.Items.Select(i => i.Ordinal));
        Assert.Equal("nine 1234567890. ten", ordinals.Items[0].Text);

        // Footnote ids of one `-` / `_`, an empty text, a tab before the text, and `[^ a]` refused
        // (so it is absorbed as continuation).
        Assert.Equal(
            [new MarkdownBlock.FootnoteDefinition("-", "dash"), new MarkdownBlock.FootnoteDefinition("_", "underscore"), new MarkdownBlock.FootnoteDefinition("a", ""), new MarkdownBlock.FootnoteDefinition("a", "x [^ a]: sp")],
            MarkdownParser.Parse("[^-]: dash\n[^_]: underscore\n[^a]:\n[^a]:\t x\n[^ a]: sp\n"));
    }
}
