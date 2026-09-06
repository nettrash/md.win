using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Markdown;

namespace Md.Core.Export;

/// <summary>
/// The pure half of "Export as HTML" — one self-contained file that opens anywhere with no engines,
/// no folder of assets and no network. Port of <c>DocumentExport.renderedHTMLPage</c> /
/// <c>WebRenderer.selfContainedHTML</c> in md.macOS/md/DocumentExport.swift, split the way
/// md.Android splits it (<c>markdown/HtmlExport.kt</c> pure, <c>ui/Exporter</c> impure).
/// </summary>
/// <remarks>
/// <para>
/// THE SPLIT. Nothing here touches a browser. The app: renders <see cref="ExportDocument"/>, loads it
/// into the offscreen WebView2, waits for <c>data-md-render-complete</c>, runs <see cref="CaptureScript"/>
/// and hands the resulting <c>documentElement.outerHTML</c> back to <see cref="PreparePage(string, string, Func{string, byte[]})"/>
/// together with a reader for the bundled <c>rich/</c> assets. What comes back is the file's bytes.
/// </para>
/// <para>
/// WHY THE CAPTURE IS THE LIVE DOM AND NOT THE INPUT. By the time the flag is set, Mermaid, Graphviz
/// and PlantUML have become inline <c>&lt;svg&gt;</c> and KaTeX has expanded its formulas into markup
/// (a <c>```plot</c> fence needed no engine and no wait — it was already <c>&lt;svg&gt;</c> in the
/// bytes the page was loaded from). Nothing is left to run, so the capture script removes every
/// <c>&lt;script&gt;</c> and every stylesheet <c>&lt;link&gt;</c> — in the DOM, not by string surgery
/// on the markup — and the exported file never reaches for an engine that will not be there.
/// </para>
/// <para>
/// WHAT "SELF-CONTAINED" MEANS, EXACTLY. Engine- and font-self-contained, <b>not</b>
/// asset-self-contained: the author's <c>&lt;img src="photo.png"&gt;</c> stays relative and breaks
/// when the file moves. That is a known, accepted gap on all four ports; closing it here alone would
/// be a parity break, not a bug fix.
/// </para>
/// </remarks>
public static class HtmlExport
{
    /// <summary>
    /// The DOM read the app must run against the finished page, verbatim as macOS and Android spell
    /// it. It returns <c>documentElement.outerHTML</c>; an empty or null result is the app's
    /// "The rendered page could not be captured." error, never a page.
    /// </summary>
    /// <remarks>
    /// WebView2's <c>ExecuteScriptAsync</c> hands back the JSON <i>encoding</i> of the result, so the
    /// app must JSON-decode the string before passing it on — exactly as md.Android decodes its own.
    /// Joined with <c>"\n"</c>, never a raw string literal: a raw literal takes its newlines from
    /// the <c>.cs</c> file, and a CRLF checkout would ship a script with <c>\r\n</c> in it.
    /// </remarks>
    public static readonly string CaptureScript = string.Join("\n",
    [
        "(function () {",
        "  document.querySelectorAll('script, link[rel=\"stylesheet\"]').forEach(function (el) {",
        "    el.remove();",
        "  });",
        "  // A stale completion flag would be misleading in a file that has",
        "  // nothing left to complete.",
        "  document.documentElement.removeAttribute('data-md-render-complete');",
        "  return document.documentElement.outerHTML;",
        "})()",
    ]);

    /// <summary><c>outerHTML</c> omits it; without one every browser renders the file in quirks mode.</summary>
    public const string Doctype = "<!DOCTYPE html>\n";

    /// <summary>The bundled stylesheet, as the HTML writer links it and as <see cref="PreparePage(string, string, Func{string, byte[]})"/> asks for it.</summary>
    public const string KatexCssAsset = "rich/katex.min.css";

    /// <summary>Where a face named by <see cref="KatexCssAsset"/> is read from: <c>rich/fonts/&lt;face&gt;.woff2</c>.</summary>
    public const string KatexFontsFolder = "rich/fonts/";

    /// <summary>The page-break rule as the export stylesheet writes it.</summary>
    public const string ExportPageBreakRule = ".md-pagebreak { height: 0; margin: 0; break-after: page; }";

    /// <summary>The preview's dashed rule, in the light border colour — the export page is always light.</summary>
    public const string ScreenPageBreakRule = ".md-pagebreak { border-top: 2px dashed rgba(43,38,32,0.16); margin: 1.6em 0; }";

    /// <summary>The container class that says a captured page carries a Mermaid diagram.</summary>
    public const string MermaidContainerNeedle = "class=\"mermaid\"";

    private const string HeadClose = "</head>";

    /// <summary>
    /// The notice that has to travel with an exported page carrying KaTeX's stylesheet and fonts.
    /// The code is MIT; the faces are <b>not</b> — they are SIL Open Font License 1.1 with reserved
    /// names, and the OFL requires its notice to accompany the fonts wherever they go. Exporting is
    /// the first thing md does that hands those files to somebody else, so this is the first place
    /// the obligation actually bites. Legal text: copy it, never reword it.
    /// </summary>
    public static readonly string KatexNotice = string.Join("\n",
    [
        "<!--",
        "  Mathematics rendered with KaTeX (https://katex.org) — MIT License,",
        "  Copyright (c) 2013-2020 Khan Academy and other contributors.",
        "  The embedded KaTeX_* fonts are licensed under the SIL Open Font",
        "  License 1.1 (https://scripts.sil.org/OFL); \"KaTeX\" is a Reserved Font",
        "  Name. The fonts are embedded unmodified.",
        "-->",
    ]);

    /// <summary>
    /// Mermaid writes its own theme CSS into every diagram it draws, so an exported page carrying a
    /// Mermaid diagram is carrying several kilobytes of Mermaid's <i>source text</i> — not just
    /// generated geometry, the way Graphviz and PlantUML output is. MIT asks for its notice to go
    /// with that, so it does. Legal text: copy it, never reword it.
    /// </summary>
    public static readonly string MermaidNotice = string.Join("\n",
    [
        "<!--",
        "  Diagrams rendered with Mermaid (https://mermaid.js.org) — MIT License,",
        "  Copyright (c) 2014-2022 Knut Sveidqvist. The diagram SVG carries",
        "  Mermaid's own theme stylesheet.",
        "-->",
    ]);

    /// <summary>
    /// Every <c>src:</c> in <c>katex.min.css</c> that names a woff2 face, with the rest of the value
    /// (the woff / ttf alternates) up to the rule's end.
    /// </summary>
    /// <remarks>
    /// The <c>[^;}]*</c> tail is what eats the sibling <c>url(fonts/X.woff) format("woff"),
    /// url(fonts/X.ttf) format("truetype")</c> alternates: they are dead links once the file leaves
    /// this machine, so the whole <c>src</c> run is replaced rather than only the woff2 term. Every
    /// class is spelled out — no <c>\s</c>, <c>\w</c>, <c>\d</c> or <c>.</c>, whose meanings differ
    /// between ICU (which the Swift matches with) and .NET.
    /// </remarks>
    private static readonly Regex Woff2Source = new(
        "src:url\\(fonts/([A-Za-z0-9_-]+)\\.woff2\\) format\\(\"woff2\"\\)[^;}]*",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The page the app must LOAD for an HTML export: the export document, with the page-break rule
    /// swapped back to the one a reader can see.
    /// </summary>
    /// <remarks>
    /// <c>dark: false, export: true</c> unconditionally — <c>Document</c> collapses <c>dark &amp;&amp;
    /// !export</c> before it picks the palette, so a window theme threaded through here could never
    /// take effect anyway. The <c>md.pdfPageSize</c> setting does not reach this document: an HTML
    /// file is not paper, and it keeps A4's <c>48px 56px</c> body margin whatever the PDF menu says.
    /// </remarks>
    public static string ExportDocument(string source, string title) =>
        VisiblePageBreaks(MarkdownHtml.Document(source, title, dark: false, export: true));

    /// <summary>
    /// Put the author's <c>\newpage</c> markers back on screen: in export CSS a page break collapses
    /// to <c>break-after: page</c>, which paper honours and a scrolling reader never sees, so an
    /// exported HTML file — read on screen — gets the preview's dashed rule instead. The PDF and
    /// print paths are untouched and still get a real page break.
    /// </summary>
    /// <remarks>
    /// <b>Every</b> occurrence, unlike <see cref="PdfExport.StyledForExport"/>'s first-only margin
    /// rewrite. The asymmetry is deliberate on all four ports and predates this one: a document that
    /// quotes the export rule in a code block gets the visible rule there too, which is the honest
    /// reading of "show me my page breaks".
    /// </remarks>
    public static string VisiblePageBreaks(string document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Replace(ExportPageBreakRule, ScreenPageBreakRule, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when <paramref name="exportDocument"/> pulled KaTeX in — the same signal the HTML writer
    /// uses to decide whether to link the stylesheet at all, so only a document with real math
    /// carries the font payload.
    /// </summary>
    /// <remarks>
    /// ASK THE INPUT, NEVER THE CAPTURED PAGE. The <c>&lt;link … rich/katex.min.css&gt;</c> tag has
    /// already been removed from the DOM by capture time, so a captured page never matches and no
    /// exported document would ever carry the stylesheet. Do not "simplify" this.
    /// </remarks>
    public static bool NeedsKatex(string exportDocument)
    {
        ArgumentNullException.ThrowIfNull(exportDocument);
        return exportDocument.Contains(KatexCssAsset, StringComparison.Ordinal);
    }

    /// <summary>
    /// KaTeX's stylesheet with its web fonts embedded, ready to be dropped into an exported page — or
    /// <c>null</c> if the assets are missing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A formula is not glyphs alone: <c>katex.min.css</c> positions every piece of it, so an export
    /// that dropped the stylesheet would show the right characters in the wrong places. It cannot be
    /// linked either, since the file has to stand on its own — so it is inlined, and each
    /// <c>@font-face</c> keeps only its <b>woff2</b> source, rewritten as a <c>data:</c> URI. woff2 is
    /// the one format every browser that matters reads; carrying the woff and ttf alternates as well
    /// would quadruple the payload for nothing, and leaving them as relative paths would leave dead
    /// links in the file. Twenty faces, about 300 KB before encoding.
    /// </para>
    /// <para>
    /// <paramref name="readRichAsset"/> reads one bundled asset by its slash-separated path — the
    /// package's <c>rich/</c> folder in the app, a plain file read in the tests, which is what keeps
    /// this function testable off-device. It returns <c>null</c> for an absent file and must not
    /// throw. A missing face leaves its rule alone: a relative path that is merely absent still
    /// degrades to a fallback face, whereas a truncated <c>data:</c> URI is a parse error that takes
    /// the surrounding rules with it. A stylesheet that is not valid UTF-8 is treated as missing,
    /// as Foundation's <c>String(contentsOf:encoding:)</c> does.
    /// </para>
    /// </remarks>
    public static string? EmbeddedKatexCss(Func<string, byte[]?> readRichAsset)
    {
        ArgumentNullException.ThrowIfNull(readRichAsset);

        var bytes = readRichAsset(KatexCssAsset);
        if (bytes is null) return null;

        string css;
        try
        {
            // Throwing decoder, not the replacing default: invalid bytes are a missing stylesheet on
            // the Swift, not a sheet full of U+FFFD.
            css = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        // A match evaluator, because the inserted text is not ours: 300 KB of base64 passed as a
        // replacement *string* would have its `$&`, `$1` and `$$` sequences read, and nobody would
        // ever find that corruption.
        return Woff2Source.Replace(css, match =>
        {
            var face = match.Groups[1].Value;
            var data = readRichAsset(KatexFontsFolder + face + ".woff2");
            if (data is null) return match.Value;
            return "src:url(data:font/woff2;base64," + Convert.ToBase64String(data) + ") format(\"woff2\")";
        });
    }

    /// <summary>Drop the stylesheet and its licence notice into a captured page, ahead of <c>&lt;/head&gt;</c>.</summary>
    public static string WithEmbeddedKatex(string page, string css)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(css);
        // `string.Replace`, never `Regex.Replace` with a replacement string: the CSS is generated
        // text. Swift replaces every occurrence and Kotlin/TypeScript only the first; a rendered
        // document has exactly one `</head>` (all author text is escaped), so they agree.
        return page.Replace(HeadClose, "<style>" + css + "</style>\n" + KatexNotice + "\n" + HeadClose, StringComparison.Ordinal);
    }

    /// <summary>Drop Mermaid's notice into a captured page that holds one of its diagrams.</summary>
    public static string WithMermaidNotice(string page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (!page.Contains(MermaidContainerNeedle, StringComparison.Ordinal)) return page;
        return page.Replace(HeadClose, MermaidNotice + "\n" + HeadClose, StringComparison.Ordinal);
    }

    /// <summary>
    /// The finished file: the captured page with the doctype put back, KaTeX's stylesheet and notice
    /// inlined when <paramref name="hasMath"/>, and Mermaid's notice added when the captured page
    /// carries one of its diagrams.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two gates read different strings on purpose. <paramref name="hasMath"/> is
    /// <see cref="NeedsKatex"/> of the <i>input</i> document (the link is gone from the capture);
    /// Mermaid is asked of the <i>captured page</i>, where the container class survives.
    /// </para>
    /// <para>
    /// With both, the head ends
    /// <c>…&lt;style&gt;KATEXCSS&lt;/style&gt;\n(katex notice)\n(mermaid notice)\n&lt;/head&gt;</c>.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="capturedHtml"/> is empty — the app's "The rendered page could not be
    /// captured." case, which must never be written to a file.
    /// </exception>
    public static string PreparePage(string capturedHtml, Func<string, byte[]?> readRichAsset, bool hasMath)
    {
        ArgumentException.ThrowIfNullOrEmpty(capturedHtml);
        ArgumentNullException.ThrowIfNull(readRichAsset);

        var page = Doctype + capturedHtml;
        if (hasMath)
        {
            var css = EmbeddedKatexCss(readRichAsset);
            // No stylesheet in the package: export anyway, unstyled formulas beat no file.
            if (css is not null) page = WithEmbeddedKatex(page, css);
        }
        return WithMermaidNotice(page);
    }

    /// <summary>
    /// The finished file, given the captured page and the document that produced it — the overload
    /// that reads the math gate off <paramref name="exportDocument"/> itself, so a caller cannot get
    /// it the wrong way round.
    /// </summary>
    public static string PreparePage(string capturedHtml, string exportDocument, Func<string, byte[]?> readRichAsset) =>
        PreparePage(capturedHtml, readRichAsset, NeedsKatex(exportDocument));
}
