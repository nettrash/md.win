using System.Globalization;
using Md.Core.Markdown;
using Md.Core.Text;

namespace Md.Core.Export;

/// <summary>
/// The pure side of "export one diagram as a real vector <c>.svg</c> file": which blocks a
/// document offers, and the fix-up that turns a diagram's rendered root <c>&lt;svg&gt;</c>
/// (read out of the offscreen WebView2 DOM as <c>outerHTML</c>) into a self-standing SVG
/// document. Port of macOS <c>DiagramSVG</c> in <c>DocumentExport.swift</c>.
/// </summary>
/// <remarks>
/// <para>
/// Only the three <i>diagram</i> engines qualify — Mermaid, Graphviz and PlantUML each render to
/// an inline <c>&lt;svg&gt;</c> — plus the <c>```plot</c> fence, which is already a real
/// <c>&lt;svg&gt;</c> in the markup before any script runs. Math does <b>not</b>: KaTeX lays a
/// formula out as HTML + CSS, never SVG, so a formula has no vector to export and is deliberately
/// never offered.
/// </para>
/// <para>
/// <see cref="Diagrams"/> and <c>querySelectorAll(<see cref="DomSelector"/>)</c> must enumerate the
/// same set in the same order: the capture step pulls the matching <c>&lt;svg&gt;</c> back out by
/// <see cref="Diagram.Ordinal"/>, so one kind counted here and not there shifts every later figure
/// onto the wrong source. It is deliberately a <i>different</i> set from the EPUB rich containers —
/// this one has <c>div.plot</c> and no formulas, that one has formulas and no plot. Do not derive
/// one from the other.
/// </para>
/// <para>
/// The tag scanning is plain ordinal string work, not the scalar-careful matching the parser needs:
/// the input is a serializer's ASCII tag syntax, never author prose, so there is no combining-mark
/// hazard here. The Swift, Kotlin and TypeScript ports all make the same exemption, for the same
/// reason. The one place author text does flow through — the fence info string — is folded and
/// matched exactly as the HTML writer folds it.
/// </para>
/// </remarks>
public static class DiagramSvg
{
    /// <summary>Mermaid — rendered into <c>&lt;pre class="mermaid"&gt;</c>.</summary>
    public const string Mermaid = "mermaid";

    /// <summary>PlantUML — rendered into <c>&lt;div class="plantuml"&gt;</c>.</summary>
    public const string PlantUml = "plantuml";

    /// <summary>Graphviz — rendered into <c>&lt;div class="graphviz" data-engine="…"&gt;</c>.</summary>
    public const string Graphviz = "graphviz";

    /// <summary>The <c>```plot</c> fence — already an <c>&lt;svg&gt;</c> inside <c>&lt;div class="plot"&gt;</c>.</summary>
    public const string Plot = "plot";

    /// <summary>
    /// The DOM query that finds the rendered containers <see cref="Diagrams"/> describes, in the
    /// same document order. The four engine names are also the four container classes, which is
    /// what keeps this string and <see cref="Classify"/> honest.
    /// </summary>
    public const string DomSelector = "pre.mermaid, div.plantuml, div.graphviz, div.plot";

    private const string XmlProlog = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n";
    private const string SvgNamespaceAttribute = " xmlns=\"http://www.w3.org/2000/svg\"";
    private const string DefaultLayout = "dot";
    private const int LabelLimit = 40;
    private const char Ellipsis = (char)0x2026;   // U+2026 HORIZONTAL ELLIPSIS

    /// <summary>
    /// The Graphviz layout aliases, exactly as the HTML writer's table has them — a fence named
    /// <c>gv</c> and one named <c>dot</c> are the same <c>dot</c> layout.
    /// </summary>
    /// <remarks>
    /// html.md names <c>MarkdownHtml.GraphvizEngines</c> the owner of this table and this class its
    /// second reader; the HTML writer lands after this file, so the table is spelled out here for
    /// now. When both are in the tree, keep ONE copy (read the writer's) — a layout added to one
    /// table and not the other offers a fence the renderer will not draw, and the ordinals part
    /// company.
    /// </remarks>
    private static readonly Dictionary<string, string> GraphvizEngines = new(StringComparer.Ordinal)
    {
        ["dot"] = "dot",
        ["graphviz"] = "dot",
        ["gv"] = "dot",
        ["neato"] = "neato",
        ["circo"] = "circo",
        ["fdp"] = "fdp",
        ["sfdp"] = "sfdp",
        ["twopi"] = "twopi",
        ["osage"] = "osage",
        ["patchwork"] = "patchwork",
    };

    /// <summary>One diagram the document offers for SVG export, in document order.</summary>
    /// <param name="Ordinal">
    /// 0-based position among the document's rendered diagram containers — the index the DOM query
    /// reports them in, so the app picks the nth match of <see cref="DomSelector"/>. The saved file
    /// is named with <c>Ordinal + 1</c>.
    /// </param>
    /// <param name="Engine">
    /// Which engine draws it: <see cref="Mermaid"/>, <see cref="PlantUml"/>, <see cref="Graphviz"/>
    /// or <see cref="Plot"/> (Swift's <c>Diagram.Kind</c> raw value, and the container's CSS class).
    /// </param>
    /// <param name="Source">The diagram's own source — the fence body, or the whole file for a raw
    /// <c>.puml</c> / <c>.gv</c> document.</param>
    /// <param name="MenuTitle">The menu row, as <see cref="Of"/> composes it.</param>
    public sealed record Diagram(int Ordinal, string Engine, string Source, string MenuTitle)
    {
        /// <summary>
        /// The Graphviz layout program (<c>dot</c> / <c>neato</c> / …) for a <see cref="Graphviz"/>
        /// diagram; <see langword="null"/> for the others. Swift calls this field <c>engine</c>;
        /// here that name is taken by the family above, which the dictated record shape spells as a
        /// non-null string. Menu label only — the capture reads the DOM, not this.
        /// </summary>
        public string? Layout { get; init; }

        /// <summary>
        /// A short label lifted from the diagram's source — its first non-empty line, trimmed and
        /// capped — so a reader can tell two diagrams apart in the menu. Empty when the source has
        /// no non-blank line. A pure function of <see cref="Source"/>, as it is in every port.
        /// </summary>
        public string Label => FirstLine(Source);

        /// <summary>
        /// The engine's display name, naming the Graphviz layout when it is not the default
        /// <c>dot</c> (a <c>neato</c> graph reads quite differently). An engine this class does not
        /// know comes back verbatim rather than throwing — a menu row is not worth a crash.
        /// </summary>
        public string TypeName => Engine switch
        {
            Mermaid => "Mermaid",
            PlantUml => "PlantUML",
            Graphviz => Layout is { } layout && !string.Equals(layout, DefaultLayout, StringComparison.Ordinal)
                ? "Graphviz (" + layout + ")"
                : "Graphviz",
            Plot => "Plot",
            _ => Engine,
        };

        /// <summary>
        /// The diagram as the menu shows it: the type, plus the source label when there is one.
        /// </summary>
        public static Diagram Of(int ordinal, string engine, string? layout, string source)
        {
            var draft = new Diagram(ordinal, engine, source, "") { Layout = layout };
            var label = draft.Label;
            return draft with { MenuTitle = label.Length == 0 ? draft.TypeName : draft.TypeName + ": " + label };
        }
    }

    /// <summary>
    /// The diagrams a document offers, in document order.
    /// </summary>
    /// <remarks>
    /// Mirrors exactly how the HTML writer decides what becomes a diagram, so this list pairs
    /// index-for-index with the rendered DOM's containers:
    /// <list type="bullet">
    /// <item>a raw <c>.puml</c> / <c>.gv</c> document is one diagram — the whole file, which the
    /// writer renders without parsing Markdown at all;</item>
    /// <item>otherwise every fenced block whose info string names Mermaid, PlantUML, a Graphviz
    /// layout or <c>plot</c> — <b>including one nested in a block quote</b>, which the writer draws
    /// by recursing into the quote, so the walk recurses too and the quoted diagram keeps its
    /// place. Only quotes recurse; lists cannot contain fences in this parser.</item>
    /// </list>
    /// Math fences and every other code block are skipped: a formula is not SVG, and ordinary code
    /// is not a diagram.
    /// </remarks>
    public static IReadOnlyList<Diagram> Diagrams(string source)
    {
        if (MarkdownParser.IsRawPlantUml(source)) return [Diagram.Of(0, PlantUml, null, source)];
        if (MarkdownParser.IsRawGraphviz(source)) return [Diagram.Of(0, Graphviz, DefaultLayout, source)];
        var found = new List<Diagram>();
        Append(MarkdownParser.Parse(source), found);
        return found;
    }

    private static void Append(IReadOnlyList<MarkdownBlock> blocks, List<Diagram> into)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case MarkdownBlock.CodeBlock code:
                    var classified = Classify(code.Language);
                    if (classified is null) continue;
                    into.Add(Diagram.Of(into.Count, classified.Value.Engine, classified.Value.Layout, code.Code));
                    break;
                case MarkdownBlock.Quote quote:
                    Append(quote.Blocks, into);
                    break;
            }
        }
    }

    /// <summary>
    /// Classify a fence info string the way the HTML writer does — same fold, same families, same
    /// alias table — or <see langword="null"/> for anything that is not a diagram (math, csv, plain
    /// code, a bare fence).
    /// </summary>
    /// <remarks>
    /// The fold is <c>ToLowerInvariant</c>, which is what html.md dictates for the writer's
    /// <c>lang</c>; the two must fold identically or a <c>```MERMAID</c> fence is a diagram on one
    /// side of the pairing only.
    /// </remarks>
    private static (string Engine, string? Layout)? Classify(string? language)
    {
        var lang = (language ?? "").ToLowerInvariant();
        if (string.Equals(lang, Mermaid, StringComparison.Ordinal)) return (Mermaid, null);
        if (lang is "plantuml" or "puml" or "plant-uml") return (PlantUml, null);
        if (GraphvizEngines.TryGetValue(lang, out var layout)) return (Graphviz, layout);
        // A plot is a diagram here even though no engine draws it: the renderer already put a
        // finished `<svg>` in the container, which is exactly what this command saves. It must be
        // in this list, and `div.plot` in DomSelector, or every later diagram exports the wrong
        // figure.
        if (string.Equals(lang, Plot, StringComparison.Ordinal)) return (Plot, null);
        return null;
    }

    /// <summary>
    /// The first non-empty line of <paramref name="source"/>, trimmed and capped so one long line
    /// cannot dwarf the menu. Purely cosmetic — a human reads it, nothing re-parses it.
    /// </summary>
    /// <remarks>
    /// The splitter is the wide, Foundation <c>.newlines</c> set (Swift's <c>Character.isNewline</c>,
    /// U+0085 / U+2028 / U+2029 included, CRLF as one), not the block scanner's CR/LF one; the trim
    /// is <c>.whitespaces</c> (no line terminators, U+200B in), never <c>string.Trim()</c>. The cap
    /// counts .NET text elements — the closest thing to the grapheme clusters Swift's
    /// <c>Character</c> count caps at, and identical to it for combining marks, emoji, ZWJ
    /// sequences, regional-indicator flags, keycaps and Hangul jamo. It is <b>not</b> identical
    /// everywhere: .NET's breaker does not apply UAX #29's GB9c, so an Indic conjunct
    /// (<c>क</c> + virama + <c>ष</c>) counts two here and one on macOS, and a Devanagari label is
    /// cut sooner than macOS cuts it (pinned in <c>DiagramsvgAdversarialTests</c>). TypeScript
    /// counts code points and Kotlin UTF-16 units, either of which cuts a 41-emoji line far
    /// shorter. macOS is the source of truth; this is the nearest the platform offers, and the
    /// difference is a menu row's truncation point, nothing a file carries.
    /// </remarks>
    private static string FirstLine(string source)
    {
        foreach (var raw in Whitespace.NewlineSetLines(source))
        {
            var trimmed = Whitespace.TrimWS(raw);
            if (trimmed.Length == 0) continue;
            var cut = PrefixLength(trimmed, LabelLimit);
            return cut is null ? trimmed : Whitespace.TrimWS(trimmed[..cut.Value]) + Ellipsis;
        }
        return "";
    }

    /// <summary>
    /// The UTF-16 length of the first <paramref name="limit"/> text elements of
    /// <paramref name="text"/>, or <see langword="null"/> when it has no more than that many (so
    /// the whole line passes through, uncapped and unaltered).
    /// </summary>
    private static int? PrefixLength(string text, int limit)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        var seen = 0;
        var cut = 0;
        while (elements.MoveNext())
        {
            seen++;
            if (seen == limit) cut = elements.ElementIndex + ((string)elements.Current).Length;
            else if (seen > limit) return cut;
        }
        return null;
    }

    // MARK: - SVG fix-up

    /// <summary>
    /// Turn a diagram's rendered root <c>&lt;svg …&gt;…&lt;/svg&gt;</c> into a standalone
    /// <c>.svg</c> document: guarantee the SVG namespace, give an unsized root real pixel
    /// dimensions from its <c>viewBox</c>, and prepend the XML prolog so the file is a well-formed
    /// standalone document any browser or vector editor opens.
    /// </summary>
    /// <remarks>
    /// Mermaid emits <c>width="100%"</c> and no <c>height</c> — fine inside a flowing page (the page
    /// CSS caps it), useless in a file, where it renders at zero or full-viewport height. Graphviz
    /// and PlantUML already write absolute <c>width</c>/<c>height</c>, and a plot writes both plus
    /// the namespace, so those are left exactly as they were drawn.
    /// </remarks>
    public static string StandaloneDocument(string svgOuterHtml) =>
        XmlProlog + WithResolvedSize(InNamespaced(svgOuterHtml));

    /// <summary>
    /// The root <c>&lt;svg …&gt;</c> opening tag as <c>[Start, End)</c>, or <see langword="null"/>
    /// when there is not one — then only the prolog is added. Engine <c>outerHTML</c> never puts a
    /// <c>&gt;</c> inside the root tag's attribute values, so the first <c>&gt;</c> really does
    /// close it.
    /// </summary>
    private static (int Start, int End)? OpeningTag(string svg)
    {
        var open = svg.IndexOf("<svg", StringComparison.Ordinal);
        if (open < 0) return null;
        var close = svg.IndexOf('>', open + "<svg".Length);
        return close < 0 ? null : (open, close + 1);
    }

    /// <summary>
    /// Ensure the root carries the default SVG namespace so the standalone file is well-formed.
    /// Every engine already declares it, but a file must not lean on that.
    /// </summary>
    private static string InNamespaced(string svg)
    {
        if (OpeningTag(svg) is not { } tag) return svg;
        if (Attribute("xmlns", svg[tag.Start..tag.End]) is not null) return svg;
        // Right after `<svg`, before the other attributes.
        var at = tag.Start + "<svg".Length;
        return svg[..at] + SvgNamespaceAttribute + svg[at..];
    }

    /// <summary>
    /// Give the root real dimensions when it lacks them. If both <c>width</c> and <c>height</c> are
    /// already absolute lengths the engine sized it — leave it untouched. Otherwise, when a
    /// 4-number <c>viewBox</c> is present, set <c>width</c>/<c>height</c> to the viewBox's own width
    /// and height (copied verbatim, so <c>0.00 0.00 120.00 48.00</c> would give <c>"120.00"</c>),
    /// which is what makes a Mermaid <c>width="100%"</c> file open at its true size.
    /// </summary>
    private static string WithResolvedSize(string svg)
    {
        if (OpeningTag(svg) is not { } range) return svg;
        var tag = svg[range.Start..range.End];
        if (IsAbsoluteLength(Attribute("width", tag)) && IsAbsoluteLength(Attribute("height", tag))) return svg;
        if (ViewBox(tag) is not { Length: 4 } box) return svg;
        var resized = SetAttribute("width", box[2], tag);
        resized = SetAttribute("height", box[3], resized);
        return svg[..range.Start] + resized + svg[range.End..];
    }

    /// <summary>
    /// The value span of a whole attribute <c>name="…"</c> (or <c>name='…'</c>) inside an opening
    /// tag. <b>The leading space is load-bearing</b>: it matches only a whole attribute, so
    /// <c>width</c> never captures <c>stroke-width</c>.
    /// </summary>
    private static (int Start, int End)? AttributeValueRange(string name, string tag)
    {
        foreach (var quote in (char[])['"', '\''])
        {
            var key = " " + name + "=" + quote;
            var at = tag.IndexOf(key, StringComparison.Ordinal);
            if (at < 0) continue;
            var valueStart = at + key.Length;
            var close = tag.IndexOf(quote, valueStart);
            if (close < 0) continue;
            return (valueStart, close);
        }
        return null;
    }

    private static string? Attribute(string name, string tag) =>
        AttributeValueRange(name, tag) is { } range ? tag[range.Start..range.End] : null;

    /// <summary>Set <paramref name="name"/>'s value, or add <c>name="value"</c> just past <c>&lt;svg</c> when absent.</summary>
    private static string SetAttribute(string name, string value, string tag)
    {
        if (AttributeValueRange(name, tag) is { } range) return tag[..range.Start] + value + tag[range.End..];
        var at = "<svg".Length;
        return tag[..at] + " " + name + "=\"" + value + "\"" + tag[at..];
    }

    /// <summary>
    /// Whether an attribute value is an absolute SVG length: present, and a number (optionally with
    /// a unit like <c>pt</c>/<c>px</c>), but not a percentage. A missing value and
    /// <c>width="100%"</c> are both "not absolute", which is exactly what makes a Mermaid root get
    /// resized and a Graphviz root not.
    /// </summary>
    /// <remarks>
    /// The digit test is ASCII, as md.vscode's is. The three ports do <b>not</b> agree in general:
    /// Swift asks <c>Character.isNumber</c>, which is true for every numeric scalar
    /// (<c>٥ ５ Ⅰ 〇 一 ½ ① 𝟠</c> all included), and Kotlin asks <c>Char.isDigit</c> (Nd only), so a
    /// root sized <c>width="٥"</c> is left alone on macOS and resized here. They agree on ASCII,
    /// which is the whole of the real input set — this reads one engine's own <c>outerHTML</c>,
    /// and Mermaid, Graphviz, PlantUML and the plot writer all emit ASCII numerals. Pinned, with
    /// macOS's answer spelled out, in <c>DiagramsvgAdversarialTests</c>.
    /// The trim is the <c>.whitespaces</c> set, not <c>string.Trim()</c>.
    /// </remarks>
    private static bool IsAbsoluteLength(string? value)
    {
        if (value is null) return false;
        var trimmed = Whitespace.TrimWS(value);
        if (trimmed.Length == 0 || trimmed[^1] == '%') return false;
        var first = trimmed[0];
        return first == '.' || (first >= '0' && first <= '9');
    }

    /// <summary>
    /// The <c>viewBox</c>'s space/comma-separated tokens, or <see langword="null"/>. Empty tokens
    /// are dropped, so a run of spaces does not manufacture a phantom fifth token.
    /// </summary>
    private static string[]? ViewBox(string tag) =>
        Attribute("viewBox", tag)?.Split((char[])[' ', ','], StringSplitOptions.RemoveEmptyEntries);
}
