// Frozen with the seams (shell-final.md §13.2) because IRenderSurface.PdfAsync takes it. This part
// is the shape only; WP6 adds `public static PrintGeometry For(PageSize size)` in
// Export/PrintGeometry.cs as another part of this partial record (inches = pt / 72) — it needs
// Md.Core.Export.PageSize, which WP0 must not reference.
namespace Md.App.Logic.Export;

/// <summary>
/// The page a PDF is laid out on, in inches — what <c>CoreWebView2PrintSettings</c> takes
/// (§7.3): page width/height = pt / 72, portrait, and 0.5 in margins all round, the decision the
/// design records (the Mac inherits Page Setup, Android 0.5, VS Code 0).
/// </summary>
public sealed partial record PrintGeometry(double PageWidthIn, double PageHeightIn, double MarginIn = 0.5);
