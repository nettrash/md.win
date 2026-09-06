using System.Globalization;
using Md.Core.Document;

namespace Md.Core.Export;

/// <summary>
/// The paper styling of a PDF export: the one rule in the export stylesheet that scales with the
/// chosen trim size. Port of <c>DocumentExport.styledForExport</c> in md.macOS/md/DocumentExport.swift,
/// with md.vscode's <c>@page</c> box offered separately for the Chromium print pipeline WebView2 uses.
/// </summary>
/// <remarks>
/// <para>
/// Everything else about a PDF is the app's: the offscreen WebView2 that lays the page out, the wait
/// for <c>data-md-render-complete</c>, <c>PrintToPdfAsync</c> and the print settings that carry the
/// paper geometry. This class only rewrites a string.
/// </para>
/// <para>
/// WHAT SCALES WITH THE PAPER: exactly one declaration, the body's <c>padding</c>. A 6 × 9" booklet
/// wearing A4 margins would waste a quarter of its width, so each axis is scaled by its own dimension
/// (see <see cref="PageSize.CssPadding"/>) — US Letter is <i>wider</i> than A4, so its horizontal
/// margin grows to 58px. WHAT DOES NOT: the 11pt body size, the white paper, <c>pre-wrap</c> code, the
/// collapsed <c>break-after: page</c> rule, the print-colour-adjust rule and every colour. They are
/// the <c>export: true</c> stylesheet, chosen by <c>MarkdownHtml.Document(…, export: true)</c> before
/// this class ever sees the string, and identical on every trim size.
/// </para>
/// <para>
/// WHAT IS UNAFFECTED: the preview, the self-contained HTML export and the EPUB. macOS applies
/// <c>styledForExport</c> on the two PDF paths only (Share ▸ Rendered PDF, Export ▸ PDF, and the two
/// book equivalents) — never on print, which is always A4, never on the HTML export, whose document
/// keeps A4's <c>48px 56px</c>, and never on the EPUB, which lifts the stylesheet out of the export
/// document and adds its own <c>body { padding: 0.5em 5%; }</c>. <c>md.pdfPageSize</c> is a
/// PDF-file setting; it must not reach any other artifact.
/// </para>
/// </remarks>
public static class PdfExport
{
    /// <summary>
    /// The body margin as the export stylesheet writes it — the needle rewritten for a trim size.
    /// It is one line of <c>MarkdownCss.Stylesheet</c>; the two must be edited together.
    /// </summary>
    public const string BodyPaddingRule = "padding: 48px 56px;";

    /// <summary>
    /// <paramref name="html"/> with the body margin rewritten to <paramref name="pageSize"/>'s scaled
    /// padding. For A4 the replacement equals the original, so an A4 export is byte-for-byte what it
    /// was before trim sizes existed. Returns the input unchanged when the rule is absent.
    /// </summary>
    /// <remarks>
    /// Only the <b>first</b> occurrence is touched: that is the body rule inside the head's
    /// <c>&lt;style&gt;</c>, which always precedes any user content, so a document that happens to
    /// quote that exact CSS in a code block keeps its text. (Contrast
    /// <see cref="HtmlExport.VisiblePageBreaks"/>, which deliberately replaces <i>every</i>
    /// occurrence. The asymmetry is intentional in Swift, Kotlin and TypeScript alike.)
    /// Spliced by index rather than through a regex: the replacement is generated text, and a
    /// <c>Regex.Replace</c> replacement string would read <c>$</c> sequences inside it.
    /// </remarks>
    public static string StyledForExport(string html, PageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageSize);

        var index = html.IndexOf(BodyPaddingRule, StringComparison.Ordinal);
        if (index < 0) return html;

        return string.Concat(
            html.AsSpan(0, index),
            "padding: " + pageSize.CssPadding + ";",
            html.AsSpan(index + BodyPaddingRule.Length));
    }

    /// <summary>
    /// The CSS page box for <paramref name="pageSize"/>: md.vscode's <c>@page</c> block, for a
    /// Chromium print pipeline that is asked to take its paper from the document rather than from the
    /// print settings. <b>Opt-in</b> — see <see cref="WithPageBox"/> for when not to use it.
    /// </summary>
    /// <remarks>
    /// Points are written with the invariant culture: a German culture would spell <c>595,2pt</c> and
    /// the browser would drop the declaration, silently.
    /// </remarks>
    public static string PageBoxCss(PageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(pageSize);

        return "<style>@page { size: "
            + pageSize.Width.ToString(CultureInfo.InvariantCulture) + "pt "
            + pageSize.Height.ToString(CultureInfo.InvariantCulture) + "pt; margin: 0; }\n"
            // The Chromium spelling of the `-webkit-print-color-adjust: exact` already in the sheet:
            // without it the paper background and the diagram fills are dropped by the print
            // pipeline and a themed document prints as bare text.
            + "html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }</style>";
    }

    /// <summary>
    /// <paramref name="html"/> with <see cref="PageBoxCss"/> appended after the first
    /// <c>&lt;/style&gt;</c> — never woven into the sheet, which stays byte-identical to the one
    /// every other surface uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// md.win's PDF path does <b>not</b> call this. On WebView2 the paper is
    /// <c>CoreWebView2PrintSettings.PageWidth/PageHeight</c> (inches: points ÷ 72), which is the same
    /// single source of truth <c>NSPrintInfo.paperSize</c> is on macOS; a CSS <c>@page size</c> would
    /// be a second one for the same number, and WebView2 exposes no <c>preferCSSPageSize</c> to say
    /// which wins. Worse, <c>margin: 0</c> from an author sheet overrides the printer margins the app
    /// sets, and those are a deliberate decision (Android uses 0.5 in because the body's padding only
    /// wraps the whole document — a middle page would otherwise touch the paper edge).
    /// </para>
    /// <para>
    /// It is kept, and pinned, for the host that has no print settings to set: md.vscode declares the
    /// page box because the VS Code host owns the dialog. A Windows path that ever prints through a
    /// surface it cannot configure has the block ready and does not have to re-derive it.
    /// </para>
    /// </remarks>
    public static string WithPageBox(string html, PageSize pageSize)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(pageSize);

        const string close = "</style>";
        var index = html.IndexOf(close, StringComparison.Ordinal);
        if (index < 0) return html;

        var after = index + close.Length;
        return string.Concat(html.AsSpan(0, after), "\n" + PageBoxCss(pageSize), html.AsSpan(after));
    }
}
