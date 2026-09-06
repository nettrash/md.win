// Frozen with the seams (shell-final.md §13.2) because IRenderSurfaceFactory takes it; the rest of
// Md.App.Logic.Preview belongs to WP5 and Md.App.Logic.Export to WP6 — neither redefines this enum.
namespace Md.App.Logic.Preview;

/// <summary>
/// Which HTML a renderer serves (§4.3, §7.1): <see cref="Screen"/> — the live preview, Core HTML
/// plus the app-appended Georgia style; <see cref="Paper"/> — Print and PDF, Core HTML with
/// <c>export: true</c> plus the same style; <see cref="Export"/> — HTML / EPUB / SVG, pure Core HTML
/// so nothing Windows-specific reaches a file another port pins byte-for-byte.
/// </summary>
public enum RenderKind
{
    Screen,
    Paper,
    Export,
}
