using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// The parse-side half of the fixture-driven suite over the shared TestData corpus (mirrored
/// byte-identically in md, md.macOS, md.Android and md.vscode, except <c>test.md</c>'s platform
/// lines). Every fixture must parse, and each per-feature file must carry the construct its name
/// promises, so a fixture edit that loses a feature fails here rather than silently weakening the
/// corpus. The HTML-side assertions come with the HTML writer's suite.
/// </summary>
public class TestDataParseTests
{
    private static readonly string[] FixtureNames =
    [
        "blockquotes", "code", "edge-cases", "headings", "images",
        "inline", "lists", "math", "mermaid", "notes", "outline",
        "page-breaks", "plantuml", "tables", "test", "thematic-breaks",
    ];

    private static readonly string[] ExampleNames =
    [
        "01-Welcome", "02-Formatting", "03-Tables", "04-Code",
        "05-Images", "06-Math", "07-Diagrams", "08-Plots", "09-Writer Tools",
    ];

    private static string Load(string name) => Fixtures.Read(System.IO.Path.Combine("testdata", name + ".md"));

    private static string LoadExample(string name) => Fixtures.Read(System.IO.Path.Combine("examples", name + ".md"));

    [Fact]
    public void CorpusIsComplete()
    {
        var names = Directory.GetFiles(Fixtures.Path("testdata"), "*.md")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(FixtureNames, names);
        // Every fixture ends with exactly one LF, as the corpus promises.
        foreach (var name in FixtureNames)
        {
            var source = Load(name);
            Assert.EndsWith("\n", source, StringComparison.Ordinal);
            Assert.False(source.EndsWith("\n\n", StringComparison.Ordinal), name);
            Assert.DoesNotContain("\r", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryFixtureParses()
    {
        foreach (var name in FixtureNames)
        {
            var source = Load(name);
            var blocks = MarkdownParser.Parse(source);
            Assert.NotEmpty(blocks);
            // The one invariant the VS Code port asserts over the whole corpus: `parse` and
            // `parseWithLines` are projections of one scanner.
            var placed = MarkdownParser.ParseWithLines(source);
            Assert.Equal(blocks, placed.Select(p => p.Block));
            // Lines are 0-based, ascending, and each one starts a non-blank line.
            var lines = Text.Whitespace.NormalizedLines(source);
            var previous = -1;
            foreach (var item in placed)
            {
                Assert.True(item.Line > previous, $"{name}: block on line {item.Line} after {previous}");
                Assert.InRange(item.Line, 0, lines.Count - 1);
                Assert.NotEqual("", Text.Whitespace.TrimWS(lines[item.Line]));
                previous = item.Line;
            }
        }
    }

    [Fact]
    public void HeadingsFixtureCoversAllSixLevels()
    {
        var levels = MarkdownParser.Parse(Load("headings")).OfType<MarkdownBlock.Heading>().Select(h => h.Level).ToHashSet();
        Assert.Superset(new HashSet<int> { 1, 2, 3, 4, 5, 6 }, levels);
        // The fixture's own promises: seven hashes and a missing space are paragraphs, closing
        // hashes go, `C#` and `F#` keep theirs, and both setext levels appear.
        var blocks = MarkdownParser.Parse(Load("headings"));
        Assert.Contains(blocks, b => b is MarkdownBlock.Paragraph p && p.Text.StartsWith("####### ", StringComparison.Ordinal));
        Assert.Contains(blocks, b => b is MarkdownBlock.Paragraph p && p.Text.StartsWith("#No space", StringComparison.Ordinal));
        Assert.Contains(new MarkdownBlock.Heading(2, "Closing hashes are stripped"), blocks);
        Assert.Contains(new MarkdownBlock.Heading(3, "C# and F# keep the hash glued to the word"), blocks);
        Assert.Contains(new MarkdownBlock.Heading(1, "Setext level 1"), blocks);
        Assert.Contains(new MarkdownBlock.Heading(2, "Setext level 2"), blocks);
    }

    [Fact]
    public void TablesFixtureParsesTables()
    {
        // Three real tables; the delimiter-less pair of lines is not one.
        var blocks = MarkdownParser.Parse(Load("tables"));
        var tables = blocks.OfType<MarkdownBlock.Table>().ToList();
        Assert.Equal(3, tables.Count);
        Assert.Contains(tables, t => t.Alignments.SequenceEqual([ColumnAlignment.Leading, ColumnAlignment.Center, ColumnAlignment.Trailing]));
        Assert.Contains(tables, t => t.Header.Contains("Cell with | escaped pipe"));
        Assert.Contains(blocks, b => b is MarkdownBlock.Paragraph p && p.Text.Contains("| a | b |", StringComparison.Ordinal));
    }

    [Fact]
    public void ListsFixtureCarriesOrderedAndUnordered()
    {
        var lists = MarkdownParser.Parse(Load("lists")).OfType<MarkdownBlock.List>().ToList();
        Assert.Contains(lists, l => l.Ordered);
        Assert.Contains(lists, l => !l.Ordered);
        // Nesting, a lazily continued line and task boxes are all in the file.
        Assert.Contains(lists, l => l.Items.Any(i => i.Level > 0));
        Assert.Contains(lists, l => l.Items.Any(i => i.Text.Contains("lazily continued", StringComparison.Ordinal)));
        Assert.Contains(lists, l => l.Items.Any(i => i.Task == true) && l.Items.Any(i => i.Task == false));
    }

    [Fact]
    public void PageBreaksFixtureCarriesBothSpellings()
    {
        var source = Load("page-breaks");
        Assert.Equal(2, MarkdownParser.Parse(source).Count(b => b.Kind == BlockKind.PageBreak));
        Assert.Contains("\\newpage", source, StringComparison.Ordinal);
        Assert.Contains("\\pagebreak", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NotesFixtureHasTwoNotesAndOnePlainComment()
    {
        var source = Load("notes");
        var notes = MarkdownParser.Notes(source);
        Assert.Equal(2, notes.Count);
        Assert.Contains(notes, n => n.Text.Contains("private author note", StringComparison.Ordinal));
        Assert.Contains(notes, n => n.Text.Contains("multi-line note\nspanning two source lines", StringComparison.Ordinal));
        var blocks = MarkdownParser.Parse(source);
        Assert.Equal(2, blocks.Count(b => b.Kind == BlockKind.Note));
        Assert.DoesNotContain(blocks, b => b is MarkdownBlock.Paragraph p && p.Text.Contains("plain comment", StringComparison.Ordinal));
        Assert.Contains(new MarkdownBlock.Paragraph("Visible prose before the notes."), blocks);
        Assert.Contains(new MarkdownBlock.Paragraph("Visible prose after the notes."), blocks);
    }

    [Fact]
    public void OutlineFixtureDedupesAndSlugs()
    {
        var outline = MarkdownParser.Outline(Load("outline"));
        var slugs = outline.Select(e => e.Slug).ToList();
        Assert.Contains("section", slugs);
        Assert.Contains("section-1", slugs);
        Assert.Contains("c--f", slugs);
        Assert.DoesNotContain(outline, e => e.Text.Contains("not a heading", StringComparison.Ordinal));
        Assert.Equal("Setext also counts", outline[^1].Text);
        // The outline's slugs are the parser's headings' slugs, walked in document order.
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        var expected = MarkdownParser.Parse(Load("outline")).OfType<MarkdownBlock.Heading>().Select(h => MarkdownParser.Slug(h.Text, used));
        Assert.Equal(expected, slugs);
    }

    [Fact]
    public void OutlineAndHeadingsAgreeAcrossTheCorpus()
    {
        // Every top-level heading `Parse` emits is an outline entry with the same text and level,
        // in the same order — the shared slug counter depends on it.
        foreach (var name in FixtureNames.Concat(ExampleNames.Select(e => "examples:" + e)))
        {
            var source = name.StartsWith("examples:", StringComparison.Ordinal) ? LoadExample(name[9..]) : Load(name);
            var headings = MarkdownParser.Parse(source).OfType<MarkdownBlock.Heading>().Select(h => (h.Level, h.Text));
            var outline = MarkdownParser.Outline(source).Select(e => (e.Level, e.Text));
            Assert.Equal(headings, outline);
        }
    }

    [Fact]
    public void RichFixturesCarryTheirFences()
    {
        // The parser does not know these languages; it only has to hand them on intact, case kept.
        var math = MarkdownParser.Parse(Load("math")).OfType<MarkdownBlock.CodeBlock>().Select(c => c.Language);
        Assert.Contains("math", math);
        Assert.Equal(3, MarkdownParser.Parse(Load("mermaid")).OfType<MarkdownBlock.CodeBlock>().Count(c => c.Language == "mermaid"));
        var plantuml = MarkdownParser.Parse(Load("plantuml")).OfType<MarkdownBlock.CodeBlock>().Select(c => c.Language).ToList();
        Assert.Equal(2, plantuml.Count(l => l == "plantuml"));
        Assert.Contains("puml", plantuml);
    }

    [Fact]
    public void CodeFixtureStripsAnIndentedFence()
    {
        var code = MarkdownParser.Parse(Load("code")).OfType<MarkdownBlock.CodeBlock>().ToList();
        Assert.Contains(code, c => c.Language == "swift");
        Assert.Contains(code, c => c.Language == "json");
        Assert.Contains(code, c => c.Language == "text" && c.Code.StartsWith("an indented fence", StringComparison.Ordinal));
        Assert.Contains(code, c => c.Code == "# not a heading\n- not a list\n**not bold**");
    }

    [Fact]
    public void ThematicBreaksFixtureHasFourRules()
    {
        Assert.Equal(4, MarkdownParser.Parse(Load("thematic-breaks")).Count(b => b.Kind == BlockKind.ThematicBreak));
    }

    [Fact]
    public void BlockquotesFixtureNests()
    {
        var quotes = MarkdownParser.Parse(Load("blockquotes")).OfType<MarkdownBlock.Quote>().ToList();
        Assert.NotEmpty(quotes);
        Assert.Contains(quotes, q => q.Blocks.Any(b => b.Kind == BlockKind.List) && q.Blocks.Any(b => b.Kind == BlockKind.CodeBlock));
        Assert.Contains(quotes, q => q.Blocks.OfType<MarkdownBlock.Quote>().Any(inner => inner.Blocks.Any(b => b.Kind == BlockKind.Quote)));
    }

    [Fact]
    public void ExamplesAreCompleteAndParse()
    {
        var names = Directory.GetFiles(Fixtures.Path("examples"), "*.md")
            .Select(System.IO.Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExampleNames, names);
        foreach (var name in ExampleNames)
        {
            var source = LoadExample(name);
            var blocks = MarkdownParser.Parse(source);
            Assert.NotEmpty(blocks);
            Assert.Equal(blocks, MarkdownParser.ParseWithLines(source).Select(p => p.Block));
        }
    }
}
