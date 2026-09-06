using Md.App.Logic.Export;

namespace Md.App.Logic.Seams;

/// <summary>
/// One offscreen renderer for one export (§7.1): load + render-complete wait, eval, region
/// screenshot, height for grow-to-content, PDF. App: <c>ExportRenderer</c> (a fresh WebView2 per
/// export, WP6); tests: <c>FakeRenderSurface</c>. Disposed after the export.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IRenderSurface : IAsyncDisposable
{
    /// <summary>Navigate to the served HTML and wait for <c>data-md-render-complete</c> (timeout is success).</summary>
    Task LoadAsync(string html, CancellationToken ct);

    /// <summary>Raw JSON result of <c>ExecuteScriptAsync</c>.</summary>
    Task<string> EvalAsync(string script);

    /// <summary>PNG of <paramref name="cssRect"/> at <paramref name="scale"/> (CDP Page.captureScreenshot).</summary>
    Task<byte[]> CaptureRegionPngAsync(RectD cssRect, double scale);

    Task SetHeightAsync(double cssPx);

    /// <summary>The PDF bytes (PrintToPdfAsync into a temp file, read back).</summary>
    Task<byte[]> PdfAsync(PrintGeometry geometry);
}
