// The other part of the record WP0 froze in Seams/PrintGeometry.cs: the conversion that needs
// Md.Core (shell-final.md §7.3). Kept apart so the seam stays free of Core types.
using Md.Core.Document;

namespace Md.App.Logic.Export;

public sealed partial record PrintGeometry
{
    /// <summary>PostScript points to the inch. The whole table is in points; the print settings are in inches.</summary>
    public const double PointsPerInch = 72.0;

    /// <summary>
    /// The margin decision, all four sides (§7.3, §14 row 12). The Mac inherits Page Setup, Android
    /// sets 0.5 in, md.vscode zero — this port follows Android, and for Android's reason: the body's
    /// CSS padding wraps the <em>whole document</em>, so with a zero printer margin pages 2…n would
    /// touch the paper edge. It is a decision, not a port, which is why it is a named constant and
    /// the self-test measures it.
    /// </summary>
    public const double DefaultMarginIn = 0.5;

    /// <summary>
    /// The paper a <see cref="PageSize"/> asks for, in inches. The size table is the family's, to a
    /// tenth of a point (A4 stays 595.2 × 841.8 — see <see cref="PageSize"/>), so the division is the
    /// only arithmetic between the shared table and <c>CoreWebView2PrintSettings</c>.
    /// </summary>
    public static PrintGeometry For(PageSize size)
    {
        ArgumentNullException.ThrowIfNull(size);
        return new PrintGeometry(size.Width / PointsPerInch, size.Height / PointsPerInch, DefaultMarginIn);
    }

    /// <summary>
    /// The paper Print means: always A4, never <c>md.pdfPageSize</c> — the trim size is an
    /// export-file concern and macOS, Android and this port all agree on that.
    /// </summary>
    /// <remarks>
    /// Nothing passes this to a printer. <c>ShowPrintUI</c> accepts no settings at all, so the paper
    /// Print actually uses is whatever the reader's own dialog holds (§14 row 13). This is here as
    /// the written statement of the default, and as what the PDF self-test measures A4 against.
    /// </remarks>
    public static PrintGeometry Paper { get; } = For(PageSize.A4);
}
