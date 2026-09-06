using System.Globalization;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

// The two pure halves of "export one diagram as a standalone SVG": which blocks a
// document offers (diagrams yes, math and plain code no), and the fix-up that turns
// a rendered `<svg>` into a standalone file. The offscreen capture in between is a
// five-line DOM read in the app, not covered here.
//
// Port of mdTests.swift's "Diagram -> standalone SVG" section (eight tests) plus the
// three the Kotlin suite adds (both dimensions in percent, the 40/41 cap boundary, the
// stroke-width guard) and the two PlotTests integration cases that pin the plot fence
// into this list. The rest are this port's own: the whole Graphviz alias table, the
// single-quoted attribute branch, the no-root case, and the three places where the
// family's four "what is whitespace" answers could have diverged.
//
// Every string assertion is ordinal: Assert.Contains(string, string) compares by the
// current culture, which is the class of bug this port exists to keep out.
public class DiagramSvgTests
{
    private const char Ellipsis = (char)0x2026;
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
    private const string Xmlns = "xmlns=\"http://www.w3.org/2000/svg\"";

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static void Has(string haystack, string needle, string? note = null) =>
        Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected to contain '" + needle + "' in: " + haystack);

    private static void Lacks(string haystack, string needle, string? note = null) =>
        Assert.False(haystack.Contains(needle, StringComparison.Ordinal),
            (note ?? "") + " expected NOT to contain '" + needle + "' in: " + haystack);

    private static string[] Engines(IReadOnlyList<DiagramSvg.Diagram> diagrams) =>
        diagrams.Select(d => d.Engine).ToArray();

    private static string?[] Layouts(IReadOnlyList<DiagramSvg.Diagram> diagrams) =>
        diagrams.Select(d => d.Layout).ToArray();

    private static int[] Ordinals(IReadOnlyList<DiagramSvg.Diagram> diagrams) =>
        diagrams.Select(d => d.Ordinal).ToArray();

    // MARK: - Enumeration

    [Fact]
    public void OffersOnlyDiagramsInDocumentOrder()
    {
        // Inline math, a math fence and a plain code block are all NOT diagrams; the
        // three engine fences are, in document order, each with the ordinal the DOM
        // query will index it by.
        var source = Lines(
            "Inline $a^2$ math.", "",
            "```math", "E=mc^2", "```", "",
            "```swift", "let x = 1", "```", "",
            "```mermaid", "graph TD; A-->B", "```", "",
            "```dot", "digraph { a -> b }", "```", "",
            "```plantuml", "@startuml", "A->B", "@enduml", "```");
        var diagrams = DiagramSvg.Diagrams(source);
        Assert.Equal(new[] { "mermaid", "graphviz", "plantuml" }, Engines(diagrams));
        Assert.Equal(new[] { 0, 1, 2 }, Ordinals(diagrams));
        Assert.Equal(new string?[] { null, "dot", null }, Layouts(diagrams));
        // The label is the first non-empty source line, so two diagrams read apart in
        // the menu.
        Assert.Equal("graph TD; A-->B", diagrams[0].Label);
        Assert.Equal("@startuml", diagrams[2].Label);
        // Source is the fence body verbatim, newlines and all (a closed fence keeps no
        // trailing newline - the parser's own documented shape).
        Assert.Equal("@startuml\nA->B\n@enduml", diagrams[2].Source);
    }

    [Fact]
    public void OffersNothingWithoutDiagrams()
    {
        // Prose, a formula and plain code - nothing that renders to an <svg>.
        var none = DiagramSvg.Diagrams(Lines(
            "# Title", "", "Text $x^2$ here.", "",
            "```swift", "let y = 1", "```", "",
            "```math", "E=mc^2", "```"));
        Assert.Empty(none);
        Assert.Empty(DiagramSvg.Diagrams(""));
    }

    [Fact]
    public void RecursesIntoQuotesAndCoversLayoutAliases()
    {
        // A diagram nested in a block quote keeps its place - the HTML writer renders
        // quoted blocks in line, so its container is first in the DOM - and a
        // layout-named Graphviz fence is offered with its layout.
        var diagrams = DiagramSvg.Diagrams(Lines(
            "> ```mermaid", "> graph TD; A-->B", "> ```", "",
            "```neato", "graph { a -- b }", "```"));
        Assert.Equal(new[] { "mermaid", "graphviz" }, Engines(diagrams));
        Assert.Equal(new string?[] { null, "neato" }, Layouts(diagrams));
        Assert.Equal(new[] { 0, 1 }, Ordinals(diagrams));
    }

    [Fact]
    public void RawDiagramDocumentIsASingleDiagram()
    {
        // An opened `.puml` / `.gv` is one whole-file diagram (the writer renders it
        // without parsing Markdown), so it is exactly one entry.
        var puml = DiagramSvg.Diagrams(Lines("@startuml", "A->B", "@enduml"));
        Assert.Equal(new[] { "plantuml" }, Engines(puml));
        Assert.Equal(0, puml[0].Ordinal);
        Assert.Equal("@startuml", puml[0].Label);
        // The whole file is the source, not just its first line.
        Assert.Equal("@startuml\nA->B\n@enduml", puml[0].Source);

        var dot = DiagramSvg.Diagrams("digraph { a -> b }");
        Assert.Equal(new[] { "graphviz" }, Engines(dot));
        Assert.Equal("dot", dot[0].Layout);
        // The default layout is not named in the menu, even for a raw document.
        Assert.Equal("Graphviz: digraph { a -> b }", dot[0].MenuTitle);
    }

    [Fact]
    public void EveryGraphvizLayoutAliasIsOfferedWithItsLayoutProgram()
    {
        // The alias table the HTML writer dispatches on, walked end to end: an alias
        // this list forgets is a container the DOM query still returns, and every
        // later figure exports as the wrong one.
        var aliases = new (string Fence, string Layout)[]
        {
            ("dot", "dot"), ("graphviz", "dot"), ("gv", "dot"), ("neato", "neato"),
            ("circo", "circo"), ("fdp", "fdp"), ("sfdp", "sfdp"), ("twopi", "twopi"),
            ("osage", "osage"), ("patchwork", "patchwork"),
        };
        foreach (var (fence, layout) in aliases)
        {
            var diagrams = DiagramSvg.Diagrams(Lines("```" + fence, "digraph { a -> b }", "```"));
            var only = Assert.Single(diagrams);
            Assert.Equal("graphviz", only.Engine);
            Assert.Equal(layout, only.Layout);
        }
        // The fold is ToLowerInvariant, exactly as the HTML writer folds its info
        // string - the two must agree or a shouted fence is a diagram on one side of
        // the pairing only.
        Assert.Equal("graphviz", DiagramSvg.Diagrams(Lines("```NEATO", "graph {}", "```"))[0].Engine);
        Assert.Equal("mermaid", DiagramSvg.Diagrams(Lines("```Mermaid", "graph TD; A-->B", "```"))[0].Engine);
        // The PlantUML aliases, and a near miss that is not one.
        foreach (var fence in (string[])["plantuml", "puml", "plant-uml"])
        {
            Assert.Equal("plantuml", DiagramSvg.Diagrams(Lines("```" + fence, "@startuml", "```"))[0].Engine);
        }
        Assert.Empty(DiagramSvg.Diagrams(Lines("```plantuml2", "@startuml", "```")));
    }

    [Fact]
    public void PlotFenceIsADiagramAndTheDomSelectorNamesItsContainer()
    {
        // PlotTests: the figure is offered to the SVG export and found in the DOM. The
        // walk and the DOM query must agree about what a diagram is, or every later
        // figure exports as the wrong one.
        var fence = Lines("```plot", "x: -10..10", "y: -2..2", "title: Damped oscillation",
                          "sin(x) * exp(-abs(x)/5)", "```");
        var document = Lines(fence, "", "```dot", "digraph {}", "```");
        var found = DiagramSvg.Diagrams(document);
        Assert.Equal(new[] { "plot", "graphviz" }, Engines(found));
        Assert.Equal("Plot", found[0].TypeName);
        Assert.Equal("Plot: x: -10..10", found[0].MenuTitle);
        Has(DiagramSvg.DomSelector, "div.plot");
        Assert.Equal("pre.mermaid, div.plantuml, div.graphviz, div.plot", DiagramSvg.DomSelector);
        // A quoted plot keeps its place, because the renderer recurses into quotes.
        Assert.Equal(new[] { "plot" },
            Engines(DiagramSvg.Diagrams(Lines("> ```plot", "> sin(x)", "> ```"))));
    }

    [Fact]
    public void PlotSavesAsAStandaloneSvgWithoutAFixUp()
    {
        // PlotTests: the root already carries `xmlns`, `width` and `height`, so the
        // only thing the writer adds is the XML prolog.
        var file = DiagramSvg.StandaloneDocument(Plot.PlotSvg("sin(x)"));
        Assert.True(file.StartsWith(Prolog + "<svg xmlns=", StringComparison.Ordinal), file[..60]);
        Has(file, "width=\"600\" height=\"400\"");
    }

    // MARK: - Menu titles and labels

    [Fact]
    public void MenuTitlesNameEngineAndSourceSnippet()
    {
        var mermaid = DiagramSvg.Diagram.Of(0, DiagramSvg.Mermaid, null, "graph TD");
        Assert.Equal("Mermaid", mermaid.TypeName);
        Assert.Equal("Mermaid: graph TD", mermaid.MenuTitle);

        // The default `dot` layout isn't named; a non-default one is.
        var dot = DiagramSvg.Diagram.Of(1, DiagramSvg.Graphviz, "dot", "g");
        Assert.Equal("Graphviz", dot.TypeName);
        var neato = DiagramSvg.Diagram.Of(2, DiagramSvg.Graphviz, "neato", "");
        Assert.Equal("Graphviz (neato)", neato.TypeName);
        Assert.Equal("Graphviz (neato)", neato.MenuTitle);   // no label -> type only
        Assert.Equal("PlantUML", DiagramSvg.Diagram.Of(3, DiagramSvg.PlantUml, null, "").TypeName);

        // A long first line is capped so one diagram can't dwarf the menu.
        var long_ = DiagramSvg.Diagrams(Lines("```mermaid", new string('x', 100), "```"));
        var only = Assert.Single(long_);
        Assert.EndsWith(Ellipsis.ToString(), only.Label, StringComparison.Ordinal);
        Assert.True(only.Label.Length <= 41, only.Label.Length.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FirstLineLabelCapsAtFortyCharacters()
    {
        // Kotlin pins the boundary exactly: 40 characters pass through whole, 41 are
        // capped to 40 + an ellipsis.
        var forty = new string('a', 40);
        var atForty = DiagramSvg.Diagrams(Lines("```mermaid", forty, "```"))[0].Label;
        Assert.Equal(forty, atForty);
        Lacks(atForty, Ellipsis.ToString());

        var overByOne = DiagramSvg.Diagrams(Lines("```mermaid", new string('a', 41), "```"))[0].Label;
        Assert.Equal(41, overByOne.Length);                 // 40 + ellipsis
        Assert.EndsWith(Ellipsis.ToString(), overByOne, StringComparison.Ordinal);
    }

    [Fact]
    public void LabelCapCountsGraphemeClustersAsMacOsDoes()
    {
        // Swift caps at 40 `Character`s - grapheme clusters. TypeScript counts code
        // points and Kotlin UTF-16 units, either of which would cut this line at 20
        // letters. macOS is the source of truth, so the cap counts text elements.
        var acute = ((char)0x0301).ToString();              // COMBINING ACUTE ACCENT
        var letter = "e" + acute;                           // one cluster, two UTF-16 units
        var label = DiagramSvg.Diagrams(Lines("```mermaid", string.Concat(Enumerable.Repeat(letter, 41)), "```"))[0].Label;
        Assert.Equal(string.Concat(Enumerable.Repeat(letter, 40)) + Ellipsis, label);
        Assert.Equal(81, label.Length);                     // 40 clusters x 2 units + the ellipsis
    }

    [Fact]
    public void LabelSkipsBlankLinesByTheFoundationWhitespaceSet()
    {
        // The label trim is Foundation's `.whitespaces`, which still counts U+200B
        // ZERO WIDTH SPACE (Apple's frozen tables call it Zs). `string.Trim()` would
        // not, and would hand the menu an invisible label.
        var zwsp = ((char)0x200B).ToString();
        var label = DiagramSvg.Diagrams(Lines("```mermaid", zwsp, "  real  ", "```"))[0].Label;
        Assert.Equal("real", label);

        // And the splitter is the wide newline set, not the block scanner's CR/LF one:
        // the parser hands this fence one line, and U+2028 still ends it here.
        var ls = ((char)0x2028).ToString();
        Assert.Equal("first", DiagramSvg.Diagrams(Lines("```mermaid", ls + "first", "```"))[0].Label);
        // An all-blank source has no label at all, and then the menu shows the type.
        var blank = DiagramSvg.Diagrams(Lines("```mermaid", "   ", "```"))[0];
        Assert.Equal("", blank.Label);
        Assert.Equal("Mermaid", blank.MenuTitle);
    }

    // MARK: - The standalone SVG fix-up

    [Fact]
    public void StandaloneSvgResolvesMermaidSizeFromViewBox()
    {
        // Mermaid's root is `width="100%"` with no height - unusable in a file. The
        // standalone document must carry the XML prolog, keep the SVG namespace, and
        // take real pixel width/height from the viewBox.
        var svg = "<svg id=\"m\" class=\"flowchart\" viewBox=\"0 0 200 100\" "
            + "style=\"max-width: 200px;\" width=\"100%\" "
            + "xmlns=\"http://www.w3.org/2000/svg\"><g></g></svg>";
        var result = DiagramSvg.StandaloneDocument(svg);
        Assert.StartsWith(Prolog, result, StringComparison.Ordinal);
        Has(result, Xmlns);
        Has(result, "width=\"200\"");
        Has(result, "height=\"100\"");
        Lacks(result, "width=\"100%\"", "the percentage width must be gone");
    }

    [Fact]
    public void StandaloneSvgResolvesPercentWidthAndHeight()
    {
        // A root whose width AND height are BOTH percentages is still unsized - pins
        // that the `%` guard decides it, not the mere presence of a height attribute.
        var result = DiagramSvg.StandaloneDocument(
            "<svg viewBox=\"0 0 300 150\" width=\"100%\" height=\"100%\" "
            + "xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        Has(result, "width=\"300\"");
        Has(result, "height=\"150\"");
        Lacks(result, "100%", "neither percentage dimension may survive");
    }

    [Fact]
    public void StandaloneSvgLeavesSizedRootAloneButAddsProlog()
    {
        // Graphviz / PlantUML already write absolute width/height, so the viewBox must
        // NOT overwrite them - only the prolog is added.
        var svg = "<svg width=\"120pt\" height=\"48pt\" viewBox=\"0.00 0.00 120.00 48.00\" "
            + "xmlns=\"http://www.w3.org/2000/svg\"><g/></svg>";
        var result = DiagramSvg.StandaloneDocument(svg);
        Assert.StartsWith(Prolog, result, StringComparison.Ordinal);
        Assert.Equal(Prolog + svg, result);
        Has(result, "width=\"120pt\"");
        Has(result, "height=\"48pt\"");
        Lacks(result, "width=\"120.00\"", "the viewBox must not resize a sized root");
    }

    [Fact]
    public void StandaloneSvgAddsNamespaceWhenMissing()
    {
        // A root without a default namespace must gain one, without disturbing an
        // already-absolute size.
        var result = DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 10 10\" width=\"10\" height=\"10\"><g/></svg>");
        Has(result, Xmlns);
        Has(result, "width=\"10\"");
        Has(result, "height=\"10\"");
        // The declaration goes right after `<svg`, before the other attributes.
        Assert.Equal(Prolog + "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\" width=\"10\" height=\"10\"><g/></svg>", result);
        // A namespaced root is left with exactly one declaration.
        var already = DiagramSvg.StandaloneDocument("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"5\" height=\"5\"></svg>");
        Assert.Equal(1, already.Split("xmlns=").Length - 1);
    }

    [Fact]
    public void WidthGuardDoesNotCaptureStrokeWidth()
    {
        // The leading-space guard on the attribute scan means a root that carries
        // `stroke-width` but no real `width` is still unsized - the scan must not read
        // the stroke's value as the width - so it resizes from the viewBox rather than
        // adopting "2".
        var result = DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 60 40\" stroke-width=\"2\"><path/></svg>");
        Has(result, "width=\"60\"");
        Has(result, "height=\"40\"");
        Has(result, " width=\"60\"");
        Lacks(result, " width=\"2\"", "the stroke width must not become the root width");
        Has(result, "stroke-width=\"2\"", "the stroke-width attribute is left exactly as it was");
    }

    [Fact]
    public void SingleQuotedAttributesAreReadAndPercentIsStillCaught()
    {
        // The scan tries `"` first and then `'`, so an engine that single-quotes its
        // root is read the same way.
        var result = DiagramSvg.StandaloneDocument("<svg viewBox='0 0 20 10' width='100%'><g/></svg>");
        // An existing value is replaced inside its own quotes, so the root keeps the
        // engine's quoting; an attribute that was missing is added double-quoted just
        // past `<svg`. Both are what the Swift does, character for character.
        Has(result, " width='20'");
        Has(result, " height=\"10\"");
        Lacks(result, "100%");
        // A single-quoted xmlns still counts as a declaration.
        var already = DiagramSvg.StandaloneDocument("<svg xmlns='http://www.w3.org/2000/svg' width='5' height='5'></svg>");
        Assert.Equal(1, already.Split("xmlns=").Length - 1);
    }

    [Fact]
    public void MarkupWithoutAnSvgRootGetsOnlyTheProlog()
    {
        // A capture that came back as something else (or an unterminated tag) is
        // passed through untouched apart from the prolog - never rewritten blindly.
        Assert.Equal(Prolog + "<div>not a diagram</div>", DiagramSvg.StandaloneDocument("<div>not a diagram</div>"));
        Assert.Equal(Prolog + "<svg viewBox=\"0 0 1 1\"", DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 1 1\""));
        Assert.Equal(Prolog, DiagramSvg.StandaloneDocument(""));
    }

    [Fact]
    public void ViewBoxNeedsFourTokensAndIsCopiedVerbatim()
    {
        // Fewer or more than four tokens is not a size, so the root is left alone;
        // four tokens are copied as written, decimals and all.
        Lacks(DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 10\"><g/></svg>"), "width=");
        Lacks(DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 10 10 10\"><g/></svg>"), "width=");
        Has(DiagramSvg.StandaloneDocument("<svg viewBox=\"0.00,0.00,120.00,48.00\"><g/></svg>"), " width=\"120.00\"");
        // Runs of separators do not manufacture a phantom fifth token.
        Has(DiagramSvg.StandaloneDocument("<svg viewBox=\"0  0 , 30 20\"><g/></svg>"), " width=\"30\"");
        // A unit-bearing width is absolute, so a viewBox never overrides it; a bare
        // `.5` is absolute too, and an empty one is not.
        Has(DiagramSvg.StandaloneDocument("<svg width=\".5\" height=\".5\" viewBox=\"0 0 9 9\"><g/></svg>"), "width=\".5\"");
        Has(DiagramSvg.StandaloneDocument("<svg width=\"\" height=\"\" viewBox=\"0 0 9 9\"><g/></svg>"), " width=\"9\"");
    }

    [Fact]
    public void DiagramEqualityIsStructuralAcrossTheLayout()
    {
        // The record's extra `Layout` is real state, not a decoration: two Graphviz
        // rows with different layouts are different diagrams.
        var neato = DiagramSvg.Diagram.Of(0, DiagramSvg.Graphviz, "neato", "graph {}");
        var dot = DiagramSvg.Diagram.Of(0, DiagramSvg.Graphviz, "dot", "graph {}");
        Assert.NotEqual(neato, dot);
        Assert.Equal(neato, DiagramSvg.Diagram.Of(0, DiagramSvg.Graphviz, "neato", "graph {}"));
    }
}
