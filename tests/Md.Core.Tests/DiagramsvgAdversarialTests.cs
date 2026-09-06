using System.Globalization;
using Md.Core.Export;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial parity tests added by review for <see cref="DiagramSvg"/> and
/// <see cref="ExportFileNames"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every expected value below came from an oracle, never from reading this port's output back:
/// the <b>Swift</b> column is <c>md.macOS/md/DocumentExport.swift</c>'s own
/// <c>DiagramSVG.firstLine / standaloneDocument</c> and <c>DocumentExport.sanitized</c>, lifted
/// verbatim into a file compiled with <c>swiftc</c> and fed these exact inputs; the
/// <b>TypeScript</b> column is <c>md.vscode/src/export/svg.ts</c> bundled with esbuild and driven
/// under node. Where the three ports disagree the test says so and names each answer.
/// </para>
/// <para>
/// Two of them close holes a one-character mutation walked straight through: swapping the two
/// halves of the SVG fix-up (same attributes, different bytes) and widening the control-character
/// test to <c>&lt;= ' '</c> (which eats a title's spaces the moment the title also has a line
/// break) both left the module's own thirty tests green. Two more pin divergences from macOS the
/// oracles found — the ASCII digit test and the Indic-conjunct cluster count — with the macOS
/// answer written out beside them. The rest pin behaviour the ports agree on and nothing asserted.
/// </para>
/// </remarks>
public class DiagramsvgAdversarialTests
{
    private const string Prolog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
    private const string Ns = "xmlns=\"http://www.w3.org/2000/svg\"";
    private const char Ellipsis = (char)0x2026;

    private static string Lines(params string[] lines) => string.Join("\n", lines);

    private static string[] Engines(IReadOnlyList<DiagramSvg.Diagram> diagrams) =>
        diagrams.Select(d => d.Engine).ToArray();

    // MARK: - The byte order of the fix-up

    [Fact]
    public void FixUpNamespacesTheRootBeforeItSizesIt()
    {
        // `standaloneDocument` is `withResolvedSize(inNamespaced(svg))`, and BOTH steps insert
        // their attribute immediately after `<svg`, so the order decides the bytes of the saved
        // file whenever a root needs the namespace AND has no width/height to overwrite. Doing
        // it the other way round produces the same attributes in a different order - a silent
        // byte-parity break that no assertion in the module's own suite could see.
        //
        // Swift (swiftc oracle) and md.vscode (node oracle) both return exactly this:
        Assert.Equal(
            Prolog + "<svg height=\"40\" width=\"60\" " + Ns + " viewBox=\"0 0 60 40\" stroke-width=\"2\"><path/></svg>",
            DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 60 40\" stroke-width=\"2\"><path/></svg>"));

        // The mixed case: `width` exists (single-quoted, a percentage) so it is REPLACED inside
        // the engine's own quotes, while `height` is missing and is INSERTED double-quoted just
        // past `<svg` - ahead of the namespace, which went in first.
        Assert.Equal(
            Prolog + "<svg height=\"10\" " + Ns + " viewBox='0 0 20 10' width='20'><g/></svg>",
            DiagramSvg.StandaloneDocument("<svg viewBox='0 0 20 10' width='100%'><g/></svg>"));
    }

    [Fact]
    public void TheFirstSvgWinsEvenInsideAnHtmlComment()
    {
        // The scan is `indexOf("<svg")`, not a parser, so a commented-out root is the root it
        // fixes up - and the real one after it is left alone. Not a defect to fix: the input is
        // one element's `outerHTML` read back out of the DOM, never a page. Pinned because all
        // three ports do exactly this, byte for byte (Swift and md.vscode oracles agree), so a
        // future "tidy-up" that starts parsing would be a divergence, not an improvement.
        Assert.Equal(
            Prolog + "<!-- <svg " + Ns + " width=\"1\" height=\"1\"/> --><svg viewBox=\"0 0 4 4\" width=\"100%\"><g/></svg>",
            DiagramSvg.StandaloneDocument("<!-- <svg width=\"1\" height=\"1\"/> --><svg viewBox=\"0 0 4 4\" width=\"100%\"><g/></svg>"));
    }

    // MARK: - Documented divergences from macOS

    [Fact]
    public void AbsoluteLengthAsksForAnAsciiDigitWhereMacOsAsksForAnyNumber()
    {
        // DIVERGENCE, deliberate and shared with md.vscode. Swift's guard is
        // `first == "." || first.isNumber`, and `Character.isNumber` is true for every numeric
        // scalar - the swiftc oracle answers "N" for ٥ (U+0665), ５ (U+FF15), Ⅰ (U+2160), 〇, 一,
        // ½, ① and 𝟠 alike - so macOS reads `width="٥"` as already sized and leaves the root
        // untouched. This port (and md.vscode) test `'0'..'9'`, so the same root counts as
        // unsized and is resized from the viewBox. Kotlin is a third answer again (`isDigit`,
        // Nd only). Unreachable in practice: the input is one engine's own `outerHTML`, and
        // Mermaid, Graphviz, PlantUML and the plot writer all emit ASCII numerals.
        var arabicIndicFive = ((char)0x0665).ToString();
        var svg = "<svg width=\"" + arabicIndicFive + "\" height=\"" + arabicIndicFive
            + "\" viewBox=\"0 0 9 9\"><g/></svg>";
        Assert.Equal(
            Prolog + "<svg " + Ns + " width=\"9\" height=\"9\" viewBox=\"0 0 9 9\"><g/></svg>",
            DiagramSvg.StandaloneDocument(svg));
        // macOS, from the oracle, keeps the numeral:
        //   <?xml …?>\n<svg xmlns="…" width="٥" height="٥" viewBox="0 0 9 9"><g/></svg>
        // ASCII is where the three ports do agree, and that is the whole of the real input set.
        Assert.Equal(
            Prolog + "<svg " + Ns + " width=\"5\" height=\"5\" viewBox=\"0 0 9 9\"><g/></svg>",
            DiagramSvg.StandaloneDocument("<svg width=\"5\" height=\"5\" viewBox=\"0 0 9 9\"><g/></svg>"));
    }

    [Fact]
    public void TheLabelCapCountsDotNetTextElementsWhichSplitAnIndicConjunct()
    {
        // DIVERGENCE, and the one place the "text elements are Swift's Characters" claim fails.
        // .NET's grapheme breaker does not apply UAX #29's GB9c (the Indic conjunct rule added
        // in Unicode 15.1), so क् + ष is TWO text elements; Swift's `Character` joins them, so
        // the swiftc oracle keeps 40 whole क्ष conjuncts (121 UTF-16 units) where this port keeps
        // 20 (61 units). md.vscode counts code points and keeps 13 conjuncts plus a stray क
        // (41 units) - three ports, three answers. Cosmetic only: the label is a menu row, and every other cluster the
        // suite exercises (combining marks, emoji, ZWJ families, flags, keycaps, Hangul jamo)
        // does agree with macOS.
        var ka = ((char)0x0915).ToString();
        var virama = ((char)0x094D).ToString();
        var ssa = ((char)0x0937).ToString();
        var conjunct = ka + virama + ssa;
        var label = DiagramSvg.Diagrams(Lines("```mermaid", string.Concat(Enumerable.Repeat(conjunct, 41)), "```"))[0].Label;
        Assert.Equal(string.Concat(Enumerable.Repeat(conjunct, 20)) + Ellipsis, label);
        Assert.Equal(61, label.Length);
    }

    // MARK: - Enumeration the DOM has to pair with

    [Fact]
    public void RawDiagramProbesShortCircuitTheMarkdownWalk()
    {
        // A `.puml` / `.gv` document is rendered without parsing Markdown at all, so it is ONE
        // diagram - the whole file - even when the file's body would otherwise parse into
        // fences of its own. Get this wrong and the walk offers two rows while the DOM holds
        // one container, and the second export saves the first figure. Expectations from the
        // md.vscode oracle on these exact inputs.
        var puml = Lines("@startuml", "```mermaid", "graph TD", "```", "@enduml");
        var fromPuml = DiagramSvg.Diagrams(puml);
        Assert.Equal(new[] { "plantuml" }, Engines(fromPuml));
        Assert.Equal("@startuml", fromPuml[0].Label);
        Assert.Equal(puml, fromPuml[0].Source);          // the whole file, fence and all

        var gv = Lines("digraph G {", " a -> b", "}", "", "```mermaid", "graph TD", "```");
        var fromGv = DiagramSvg.Diagrams(gv);
        Assert.Equal(new[] { "graphviz" }, Engines(fromGv));
        Assert.Equal("dot", fromGv[0].Layout);
        Assert.Equal("digraph G {", fromGv[0].Label);
        Assert.Equal(gv, fromGv[0].Source);
    }

    [Fact]
    public void OrdinalsPairWithTheDomAcrossQuotesMathAndPlots()
    {
        // The walk's index IS the index the capture indexes `querySelectorAll(DomSelector)` by,
        // so this document - plot, quoted graphviz, math (not a diagram), quoted mermaid,
        // plantuml - must come back as exactly four rows numbered 0..3 in that order. The
        // md.vscode oracle returns exactly this list.
        var source = Lines(
            "```plot", "sin(x)", "```", "",
            "> ```dot", "> digraph{}", "> ```", "",
            "```math", "x", "```", "",
            "> ```mermaid", "> graph TD", "> ```", "",
            "```plantuml", "@startuml", "```");
        var found = DiagramSvg.Diagrams(source);
        Assert.Equal(new[] { "plot", "graphviz", "mermaid", "plantuml" }, Engines(found));
        Assert.Equal(new[] { 0, 1, 2, 3 }, found.Select(d => d.Ordinal).ToArray());
        Assert.Equal(
            new[] { "Plot: sin(x)", "Graphviz: digraph{}", "Mermaid: graph TD", "PlantUML: @startuml" },
            found.Select(d => d.MenuTitle).ToArray());
        // A thrice-nested quote still lands in the list, because the writer keeps recursing.
        Assert.Equal(new[] { "graphviz" },
            Engines(DiagramSvg.Diagrams(Lines("> > > ```neato", "> > > graph {}", "> > > ```"))));
    }

    // MARK: - The Windows layer next to the family rule

    [Fact]
    public void TheWindowsLayerReplacesControlCharactersWithoutTouchingRealSpaces()
    {
        // The realistic title: words, spaces, and a line break pasted in from somewhere. Only
        // the break is illegal on Win32; the spaces are ordinary characters and must survive,
        // interior runs included. (The family stem keeps the break too - Swift and md.vscode
        // both return "My Draft\nNotes" and "Draft  Notes" for these.)
        Assert.Equal("My Draft\nNotes", ExportFileNames.PortableStem("My Draft\nNotes"));
        Assert.Equal("My Draft-Notes.svg", ExportFileNames.Sanitized("My Draft\nNotes", "svg"));

        Assert.Equal("Draft  Notes", ExportFileNames.PortableStem("  Draft  Notes  "));
        Assert.Equal("Draft  Notes.pdf", ExportFileNames.Sanitized("  Draft  Notes  ", "pdf"));

        // A control character next to a space, which is where a "<= ' '" slip would show:
        // exactly one dash, and the space beside it untouched.
        Assert.Equal("a- b.svg", ExportFileNames.Sanitized("a" + (char)0x0001 + " b", "svg"));

        // And the diagram name the SVG export composes, `{stem}-{ordinal + 1}.svg`, on a title
        // that needs both layers.
        var ordinal = 2;
        Assert.Equal("My Draft-Notes-3.svg",
            ExportFileNames.Sanitized("My Draft\nNotes", "") + "-"
            + (ordinal + 1).ToString(CultureInfo.InvariantCulture) + ".svg");
    }
}
