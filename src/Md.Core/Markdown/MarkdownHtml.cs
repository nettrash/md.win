using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Text;

namespace Md.Core.Markdown;

/// <summary>Which engines a rendered body turned out to need, in the order the probes run.</summary>
/// <remarks>
/// <see cref="ToString"/> is the space-joined lowercase list in the order math, mermaid, plantuml,
/// graphviz, highlight — the words <c>golden/engine-needs.json</c> records.
/// </remarks>
public sealed record EngineNeeds(bool Math, bool Mermaid, bool Plantuml, bool Graphviz, bool Highlight)
{
    public static readonly EngineNeeds None = new(false, false, false, false, false);

    public override string ToString()
    {
        var words = new List<string>(5);
        if (Math) words.Add("math");
        if (Mermaid) words.Add("mermaid");
        if (Plantuml) words.Add("plantuml");
        if (Graphviz) words.Add("graphviz");
        if (Highlight) words.Add("highlight");
        return string.Join(" ", words);
    }
}

/// <summary>The body markup of a document and the engines it needs — md.vscode's <c>renderBody</c> split.</summary>
public sealed record RenderedBody(string Html, EngineNeeds Needs);

/// <summary>
/// The HTML writer: the parsed block model serialised to a self-contained, themed document — the
/// one rendering path for the preview, print, PDF, "share rendered", the HTML export and (snapshotted
/// before scripts run) EPUB. Byte parity with md, md.macOS, md.Android and md.vscode is the product
/// contract; md.vscode's golden corpus, captured from the Swift bytes, is the tripwire.
/// </summary>
/// <remarks>
/// <para>
/// A hand-written, test-pinned <i>subset</i> of CommonMark with deliberate divergences: no raw HTML,
/// no reference links, no autolinks, no indented code, flat lists, a currency guard on inline math.
/// A CommonMark engine produces different bytes on the first real document.
/// </para>
/// <para>
/// Every literal here is joined with <c>"\n"</c> rather than written as a raw multi-line string, so
/// the <c>.cs</c> file's own line endings cannot leak into the output; the document ends at
/// <c>&lt;/html&gt;</c> with no trailing newline. Relative <c>rich/</c> URLs stay in the bytes on
/// every surface: the host serves the page from an origin under which they resolve, never through
/// a <c>&lt;base href&gt;</c>, which would turn <c>href="#slug"</c> into a cross-document navigation.
/// </para>
/// </remarks>
public static class MarkdownHtml
{
    /// <summary>
    /// Fenced-block info strings that select the bundled Graphviz engine, mapped to the layout
    /// program (<c>neato -Tsvg</c>, <c>circo -Tsvg</c>, …). Ten keys, eight engines; every value
    /// must be a <c>Viz.engines</c> name or the render throws and the block falls back to its
    /// source. The <i>value</i> reaches <c>data-engine</c>, never the author's word, so the attribute
    /// needs no escaping — never widen this with author-controlled text. The LaTeX writer and the
    /// diagram-SVG classifier read the same table.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> GraphvizEngines =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dot"] = "dot", ["graphviz"] = "dot", ["gv"] = "dot",
            ["neato"] = "neato", ["circo"] = "circo", ["fdp"] = "fdp", ["sfdp"] = "sfdp",
            ["twopi"] = "twopi", ["osage"] = "osage", ["patchwork"] = "patchwork",
        };

    /// <summary>
    /// A full HTML document for <paramref name="source"/>. <paramref name="title"/> becomes the
    /// <c>&lt;title&gt;</c> — always the caller's string, never sniffed from an H1 or front matter.
    /// <paramref name="export"/> styles for paper: 11pt, code wraps, page breaks collapse to real
    /// page boundaries, and the page is plain white in the light palette regardless of
    /// <paramref name="dark"/> — the tinted paper and cream-on-carbon ink are screen themes.
    /// </summary>
    public static string Document(string source, string title, bool dark, bool export = false)
    {
        // The first line of the Swift, shadowing the parameter: an export is always light, and the
        // shadowed value feeds both the palette and `data-md-dark`, so Mermaid and PlantUML render
        // light in an export too. Three reachable stylesheets, not four.
        dark = dark && !export;

        var body = Body(source, title, dark);
        var needs = body.Needs;

        // `<head>` ORDER IS LOAD-BEARING. KaTeX and mhchem share `defer` and must stay in that
        // order: deferred classic scripts run in document order, KaTeX defines the global, mhchem
        // registers `\ce{}` onto it, then the deferred module md-init.js renders. Both are the same
        // KaTeX 0.17.0 build and are replaced together. Appended with NO leading newline — glued
        // flush onto `</style>`.
        var head = "";
        if (needs.Math)
        {
            head += string.Join("\n",
            [
                "<link rel=\"stylesheet\" href=\"rich/katex.min.css\">",
                "<script defer src=\"rich/katex.min.js\"></script>",
                "<script defer src=\"rich/mhchem.min.js\"></script>",
            ]);
        }
        if (needs.Mermaid) head += "\n<script src=\"rich/mermaid.min.js\"></script>";
        // Viz.js is Graphviz. It is included for PlantUML too — inherited from md, whose stated
        // reason (PlantUML's Graphviz-backed layouts) does not survive testing, the TeaVM build
        // carrying Smetana — and kept for parity: revisit in all the ports together or not at all.
        // PlantUML itself is never in the head; md-init.js imports it lazily.
        if (needs.Plantuml || needs.Graphviz) head += "\n<script src=\"rich/viz-global.js\"></script>";
        // highlight.js is deferred like KaTeX (Android omits the `defer`; Swift's bytes are canonical).
        if (needs.Highlight) head += "\n<script defer src=\"rich/highlight.min.js\"></script>";

        // An empty head yields `</style>\n</head>`; an empty source leaves a blank line between
        // `<body …>` and the module script. Both are part of the bytes.
        return string.Join("\n",
        [
            "<!DOCTYPE html>",
            "<html lang=\"en\">",
            "<head>",
            "<meta charset=\"utf-8\">",
            "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
            "<title>" + Escape(title) + "</title>",
            "<style>" + Css(dark, export) + "</style>" + head,
            "</head>",
            // Read by md-init.js as `document.body.dataset.mdDark === '1'`; the theme is chosen by
            // this attribute, never by CSS.
            "<body data-md-dark=\"" + (dark ? "1" : "0") + "\">",
            body.Html,
            "<script type=\"module\" src=\"rich/md-init.js\"></script>",
            "</body>",
            "</html>",
        ]);
    }

    /// <summary>
    /// The document body and the engines it needs. Three mutually exclusive paths: a raw PlantUML
    /// file, a raw Graphviz file, or Markdown. Neither <paramref name="title"/> nor
    /// <paramref name="dark"/> influences the body; they are kept for symmetry with
    /// <see cref="Document"/> and md.vscode's <c>renderBody</c>, whose goldens call it this way.
    /// </summary>
    public static RenderedBody Body(string source, string title, bool dark)
    {
        _ = title;
        _ = dark;
        // An opened `.puml` / `.gv`: bare diagram source with no fence, rendered as one diagram
        // rather than parsed into paragraphs of its own text. The whole file, trailing newline
        // included, is escaped, so the close tag lands on its own line. The needs are hard-coded:
        // no Markdown here, so no math, Mermaid or highlightable code is possible.
        if (MarkdownParser.IsRawPlantUml(source))
        {
            return new RenderedBody("<div class=\"plantuml\">" + Escape(source) + "</div>",
                new EngineNeeds(false, false, true, false, false));
        }
        if (MarkdownParser.IsRawGraphviz(source))
        {
            return new RenderedBody("<div class=\"graphviz\" data-engine=\"dot\">" + Escape(source) + "</div>",
                new EngineNeeds(false, false, false, true, false));
        }

        var blocks = MarkdownParser.Parse(source);
        // Top-level headings carry a GitHub-style anchor id from the same `Slug` counter the
        // outline uses, walked in the same order, so `[…](#slug)` links and the contents menu
        // always agree. A heading nested in a quote goes through `RenderBlock`, gets no id and
        // consumes no counter.
        var slugs = new Dictionary<string, int>(StringComparer.Ordinal);
        var rendered = new string[blocks.Count];
        var definitions = new List<(string Id, string Text)>();
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block is MarkdownBlock.Heading heading)
            {
                var level = heading.Level.ToString(CultureInfo.InvariantCulture);
                rendered[i] = "<h" + level + " id=\"" + MarkdownParser.Slug(heading.Text, slugs) + "\">"
                    + Inline(heading.Text) + "</h" + level + ">";
            }
            else
            {
                rendered[i] = RenderBlock(block);
            }
            if (block is MarkdownBlock.FootnoteDefinition definition) definitions.Add((definition.Id, definition.Text));
        }

        // Joined with a single "\n". Blocks that render to "" — notes, front matter, footnote
        // definitions — still take part in the join and leave blank lines. Observable; reproduced.
        // Footnotes are a whole-document affair and need the finished body.
        var body = WithFootnotes(string.Join("\n", rendered), definitions);
        return new RenderedBody(body, Needs(body));
    }

    /// <summary>The paper-and-ink stylesheet: light preview, dark preview, or export (always light).</summary>
    public static string Css(bool dark, bool export) => MarkdownCss.Stylesheet(dark, export);

    /// <summary>
    /// The five probes, exact strings, run on the <i>emitted markup</i> and never on the block
    /// list: a diagram nested in a quote is emitted by the recursive <see cref="RenderBlock"/> and
    /// would otherwise ship without its engine; a code block that merely quotes a container string
    /// cannot trigger it because its content is escaped. That argument is FALSE for math —
    /// <c>md-mathi</c> / <c>md-mathd</c> hold no <c>&lt;</c> or <c>"</c>, so prose naming the class
    /// pulls KaTeX in. Verified, harmless at runtime, a real byte difference; reproduced on purpose.
    /// <c>graphviz</c> matches the tag prefix without <c>&gt;</c> because <c>data-engine</c> varies.
    /// </summary>
    public static EngineNeeds Needs(string body) => new(
        Math: body.Contains("md-mathi", StringComparison.Ordinal) || body.Contains("md-mathd", StringComparison.Ordinal),
        Mermaid: body.Contains("<pre class=\"mermaid\">", StringComparison.Ordinal),
        Plantuml: body.Contains("<div class=\"plantuml\">", StringComparison.Ordinal),
        Graphviz: body.Contains("<div class=\"graphviz\"", StringComparison.Ordinal),
        Highlight: body.Contains("<pre><code class=\"language-", StringComparison.Ordinal));

    // MARK: - Blocks

    /// <summary><see cref="RenderBlock"/> over every block, joined with <c>"\n"</c> — the inside of a quote.</summary>
    public static string RenderBlocks(IReadOnlyList<MarkdownBlock> blocks)
    {
        var parts = new string[blocks.Count];
        for (var i = 0; i < blocks.Count; i++) parts[i] = RenderBlock(blocks[i]);
        return string.Join("\n", parts);
    }

    /// <summary>
    /// One block to markup. A heading reaches here only when nested (inside a quote) and carries no
    /// id — the top-level form is emitted by <see cref="Body"/>, which owns the slug counter.
    /// </summary>
    public static string RenderBlock(MarkdownBlock block) => block switch
    {
        MarkdownBlock.Heading h => "<h" + h.Level.ToString(CultureInfo.InvariantCulture) + ">" + Inline(h.Text)
            + "</h" + h.Level.ToString(CultureInfo.InvariantCulture) + ">",
        // The only caller asking for soft breaks; the conversion happens inside `Inline`, before
        // protected spans are restored, so a multi-line formula keeps its own newlines.
        MarkdownBlock.Paragraph p => "<p>" + Inline(p.Text, softBreaks: true) + "</p>",
        MarkdownBlock.List l => RenderList(l.Items, l.Ordered),
        MarkdownBlock.CodeBlock c => RenderCodeBlock(c.Language, c.Code),
        MarkdownBlock.Quote q => "<blockquote>\n" + RenderBlocks(q.Blocks) + "\n</blockquote>",
        MarkdownBlock.Table t => RenderTable(t.Header, t.Alignments, t.Rows),
        MarkdownBlock.ThematicBreak => "<hr>",
        // A dashed rule in the preview; in export and print the CSS collapses it to a page boundary.
        MarkdownBlock.PageBreak => "<div class=\"md-pagebreak\"></div>",
        // Private author notes live in the editor and the notes panel only.
        MarkdownBlock.Note => "",
        // Metadata about the document, not part of it: parsed for its fields, drawn nowhere.
        MarkdownBlock.FrontMatter => "",
        // Gathered by `Body` and printed at the foot of the page.
        MarkdownBlock.FootnoteDefinition => "",
        _ => "",
    };

    /// <summary>
    /// The info string selects a rich renderer. Order matters, first match wins, and the code
    /// languages are deliberately last so <c>mermaid</c>, <c>dot</c>, <c>csv</c>, <c>tex</c> and
    /// <c>plot</c> can never come back as highlightable source.
    /// </summary>
    private static string RenderCodeBlock(string? language, string code)
    {
        // The parser's first space-delimited word of the info string, or null when empty. Folded
        // with `ScalarText.FullLowercase` — Swift's `lowercased()`, the FULL Unicode mapping,
        // never `ToLowerInvariant`, which is the SIMPLE 1:1 mapping and leaves U+0130 LATIN
        // CAPITAL LETTER I WITH DOT ABOVE alone: ```İstanbul would emit `class="language-İstanbul"`
        // where Swift, Kotlin (`lowercase(Locale.ROOT)`) and TypeScript (`toLowerCase()`) all emit
        // `class="language-i̇stanbul"` (i + U+0307). Culture is never consulted either way — a
        // Turkish locale must not spell `LATEX` differently.
        var lang = ScalarText.FullLowercase(language ?? "");
        switch (lang)
        {
            case "mermaid":
                // Mermaid is a `<pre>`; everything else a `<div>`.
                return "<pre class=\"mermaid\">" + Escape(code) + "</pre>";
            case "plantuml":
            case "puml":
            case "plant-uml":
                return "<div class=\"plantuml\">" + Escape(code) + "</div>";
            case "csv":
            case "tsv":
                // Data pasted out of a spreadsheet, drawn as a table while the source stays data.
                return RenderDelimited(code, lang == "tsv" ? '\t' : ',');
            case "math":
            case "latex":
            case "tex":
                // A fence is a `<div>`; `$$…$$` in a paragraph is a `<span>`; both `.md-mathd`.
                return "<div class=\"md-mathd\">" + Escape(code) + "</div>";
            case "plot":
                // The one rich block with no engine behind it: the finished `<svg>` is already in
                // the string every surface receives, and the class is exactly `plot`, so a plot-only
                // document trips no probe. The container is emitted whatever happens — the SVG
                // export pairs figures with fences by counting `div.plot` in document order.
                return Plot.RenderPlot(code);
        }
        if (GraphvizEngines.TryGetValue(lang, out var engine))
        {
            // Escaping is what makes DOT's HTML-like labels safe: `n [label=<<b>hi</b>>]` becomes
            // `&lt;&lt;b&gt;…`; md-init.js reads `el.textContent`, which decodes it back.
            return "<div class=\"graphviz\" data-engine=\"" + engine + "\">" + Escape(code) + "</div>";
        }
        if (lang.Length > 0)
        {
            // A real code language, tagged for highlight.js. The class must *begin* with
            // `language-`: md-init.js selects `code[class^="language-"]`. An unknown hint the
            // "common" build lacks simply is not highlighted.
            return "<pre><code class=\"language-" + Escape(lang) + "\">" + Escape(code) + "</code></pre>";
        }
        return "<pre><code>" + Escape(code) + "</code></pre>";
    }

    /// <summary>
    /// The flat, level-tagged item list with explicit markers and indentation — mirroring the
    /// on-screen preview rather than rebuilding <c>&lt;ul&gt;</c>/<c>&lt;ol&gt;</c> from a model
    /// that is not a tree. Rows are concatenated with no separator: the whole list is one line.
    /// </summary>
    private static string RenderList(IReadOnlyList<ListItem> items, bool ordered)
    {
        var rows = new StringBuilder();
        foreach (var item in items)
        {
            // "F2" with the invariant culture — a decimal comma would write `padding-left:1,60em`
            // and the rule would be dropped.
            var indent = (item.Level * 1.6).ToString("F2", CultureInfo.InvariantCulture);
            string marker;
            if (item.Task is bool done)
            {
                // Task beats ordered. Entities, not characters: the EPUB builder rewrites `&bull;`
                // to `&#8226;` because named entities are undefined in XML.
                marker = done ? "&#9745;" : "&#9744;";
            }
            else if (ordered && item.Ordinal is int ordinal)
            {
                marker = ordinal.ToString(CultureInfo.InvariantCulture) + ".";
            }
            else
            {
                marker = "&bull;";
            }
            var doneClass = item.Task == true ? " done" : "";
            rows.Append("<div class=\"md-item").Append(doneClass).Append("\" style=\"padding-left:").Append(indent).Append("em\">")
                .Append("<span class=\"md-marker\">").Append(marker).Append("</span>")
                .Append("<span>").Append(Inline(item.Text)).Append("</span></div>");
        }
        return "<div class=\"md-list\">" + rows + "</div>";
    }

    /// <summary>
    /// A <c>```csv</c> / <c>```tsv</c> block as a table. One that parses to nothing stays a
    /// <i>bare</i> code block — nothing the author wrote disappears, and the bare form trips no
    /// highlight gate.
    /// </summary>
    private static string RenderDelimited(string code, char separator)
    {
        var table = DelimitedTable.From(code, separator);
        return table is null
            ? "<pre><code>" + Escape(code) + "</code></pre>"
            : RenderTable(table.Header, table.Alignments, table.Rows);
    }

    /// <summary>
    /// Inline style per cell, no space after the colon; every cell through the inline pass;
    /// <c>&lt;tbody&gt;</c> even with no rows; a column past the alignment array is left. One line.
    /// </summary>
    private static string RenderTable(IReadOnlyList<string> header, IReadOnlyList<ColumnAlignment> alignments,
        IReadOnlyList<IReadOnlyList<string>> rows)
    {
        string Align(int i) => i < alignments.Count
            ? alignments[i] switch
            {
                ColumnAlignment.Center => "center",
                ColumnAlignment.Trailing => "right",
                _ => "left",
            }
            : "left";

        var html = new StringBuilder("<table><thead><tr>");
        for (var i = 0; i < header.Count; i++)
        {
            html.Append("<th style=\"text-align:").Append(Align(i)).Append("\">").Append(Inline(header[i])).Append("</th>");
        }
        html.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            for (var i = 0; i < row.Count; i++)
            {
                html.Append("<td style=\"text-align:").Append(Align(i)).Append("\">").Append(Inline(row[i])).Append("</td>");
            }
            html.Append("</tr>");
        }
        html.Append("</tbody></table>");
        return html.ToString();
    }

    // MARK: - Footnotes

    /// <summary>The placeholder <see cref="Inline"/> leaves, and the only shape <see cref="WithFootnotes"/> recognises.</summary>
    private static readonly Regex FootnoteMarker =
        new("<sup class=\"md-fnref\" data-fn=\"([A-Za-z0-9_-]+)\"></sup>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Turn the placeholder references into numbered links and append the notes. Numbering is by
    /// order of first <i>reference</i> — the order a reader meets them — not definition order. A
    /// reference with no definition goes back to the text the author typed; a definition nobody
    /// cited is still printed, after the cited ones, with no back-link.
    /// </summary>
    private static string WithFootnotes(string body, IReadOnlyList<(string Id, string Text)> definitions)
    {
        var matches = FootnoteMarker.Matches(body);
        if (matches.Count == 0 && definitions.Count == 0) return body;

        // First definition wins on a duplicate id, as a duplicate link reference would.
        var defined = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, text) in definitions) defined.TryAdd(id, text);

        // Walk the references forward once: each id its number, each reference its occurrence,
        // so repeated citations of one note each get a distinct anchor to come back to.
        var references = new List<(int Start, int Length, string Id, int Occurrence)>(matches.Count);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var number = new Dictionary<string, int>(StringComparer.Ordinal);
        var ordered = new List<string>();
        foreach (Match match in matches)
        {
            var id = match.Groups[1].Value;
            occurrences.TryGetValue(id, out var seen);
            var occurrence = seen + 1;
            occurrences[id] = occurrence;
            references.Add((match.Index, match.Length, id, occurrence));
            if (defined.ContainsKey(id) && !number.ContainsKey(id))
            {
                number[id] = ordered.Count + 1;
                ordered.Add(id);
            }
        }
        // Then every still-unnumbered definition, in definition order, takes the trailing numbers.
        foreach (var (id, _) in definitions)
        {
            if (number.ContainsKey(id)) continue;
            number[id] = ordered.Count + 1;
            ordered.Add(id);
        }

        // Substitute back-to-front so the earlier ranges stay valid.
        var result = body;
        for (var i = references.Count - 1; i >= 0; i--)
        {
            var (start, length, id, occurrence) = references[i];
            string replacement;
            if (number.TryGetValue(id, out var n) && defined.ContainsKey(id))
            {
                var number_ = n.ToString(CultureInfo.InvariantCulture);
                var anchor = occurrence == 1 ? "fnref-" + number_ : "fnref-" + number_ + "-" + occurrence.ToString(CultureInfo.InvariantCulture);
                replacement = "<sup class=\"md-fnref\" id=\"" + anchor + "\"><a href=\"#fn-" + number_ + "\">" + number_ + "</a></sup>";
            }
            else
            {
                replacement = Escape("[^" + id + "]");
            }
            result = string.Concat(result.AsSpan(0, start), replacement, result.AsSpan(start + length));
        }

        if (ordered.Count == 0) return result;
        var items = new StringBuilder();
        foreach (var id in ordered)
        {
            var n = number[id].ToString(CultureInfo.InvariantCulture);
            // A note's own text is inline Markdown. A further reference inside it has missed the
            // numbering pass and is cleaned back to literal text below.
            var text = Inline(defined.TryGetValue(id, out var definition) ? definition : "");
            // The back-link targets the *first* citation, never `fnref-n-2`.
            var back = occurrences.ContainsKey(id) ? " <a class=\"md-fnback\" href=\"#fnref-" + n + "\">&#8617;</a>" : "";
            items.Append("<li id=\"fn-").Append(n).Append("\">").Append(text).Append(back).Append("</li>");
        }
        // A leading newline on top of the blank lines the definitions already left in the join.
        result += "\n<section class=\"md-footnotes\"><hr><ol>" + items + "</ol></section>";
        // No `data-fn=` may survive into the page. The template is literally `[^$1]`.
        return FootnoteMarker.Replace(result, "[^$1]");
    }

    // MARK: - Inline

    /// <summary>
    /// A block's inline Markdown to HTML — code and math spans lifted out first, the rest escaped,
    /// span syntax converted, soft breaks (paragraphs only) inserted, the protected spans restored.
    /// See <see cref="MarkdownInlineHtml"/> for the phase order, which is the specification.
    /// </summary>
    public static string Inline(string text, bool softBreaks = false) => MarkdownInlineHtml.Inline(text, softBreaks);

    /// <summary>The four-character HTML escape: <c>&amp;</c> first, then <c>&lt;</c>, <c>&gt;</c>, <c>"</c>; the apostrophe untouched.</summary>
    public static string Escape(string s) => MarkdownInlineHtml.Escape(s);
}
