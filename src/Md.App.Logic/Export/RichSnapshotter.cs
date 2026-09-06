using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.Core.Export;

namespace Md.App.Logic.Export;

/// <summary>
/// The photographs an EPUB needs (§7.5, core-api.md §A4 "epub"): every formula and every diagram
/// becomes a 2× PNG, because an EPUB reader has no KaTeX, no Mermaid and no Graphviz. Md.Core plans
/// the book and pairs the images with the markup; this drives the browser for the one thing Core
/// cannot do.
/// </summary>
/// <remarks>
/// <para>
/// Per unit that has rich content — and only those; a heading page's <c>Document</c> is deliberately
/// empty and never opens a renderer — the sequence is the Mac's:
/// load the unit's own page and wait for render-complete, grow the surface to
/// <c>max(842, scrollHeight)</c>, let it repaint, measure, check the count, then capture.
/// </para>
/// <para>
/// The 300 ms repaint wait is <b>kept</b> even though <c>captureBeyondViewport</c> would photograph
/// past the viewport without it: Mermaid's <c>max-width: 100%</c> means its layout is not final until
/// the surface has been resized and laid out again, and a rect read before that is a rect of the old
/// width.
/// </para>
/// <para>
/// The count check is Android's guard, adopted here because the alternative is silent corruption:
/// Swift's <c>zip</c> pairs up to the shorter list, so one missing measurement shifts every later
/// image onto the wrong element and the book still saves. Md.Core throws
/// <see cref="EpubSnapshotCountException"/> on the same mismatch on the way in; this catches it one
/// step earlier, before a single pixel is captured, and says so in the user's words.
/// </para>
/// </remarks>
public sealed class RichSnapshotter(IRenderSurfaceFactory renderers, IScheduler? scheduler = null)
{
    /// <summary>The macOS repaint wait after the surface grows (Android sleeps 250 ms; the Mac is the source of truth).</summary>
    public static readonly TimeSpan RepaintDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>The offscreen layout viewport: 595 × 842 CSS px, and never shorter, whatever the document.</summary>
    public const double MinimumHeightCssPx = 842;

    /// <summary>A genuine 2× bitmap, as <c>WKSnapshotConfiguration.snapshotWidth = width * 2</c> gives on the Mac.</summary>
    public const double Scale = 2.0;

    /// <summary>
    /// One list of PNGs per unit that has rich content, keyed by <see cref="EpubUnitPlan.Index"/> and
    /// in document order within the unit — exactly what <see cref="EpubExport.Assemble"/> takes.
    /// Empty (and no renderer at all) for a plan with nothing to photograph.
    /// </summary>
    /// <exception cref="ExportException">
    /// The DOM measured a different number of rich elements than the markup holds, or a capture came
    /// back empty.
    /// </exception>
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>> CaptureAsync(EpubPlan plan, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var snapshots = new Dictionary<int, IReadOnlyList<RichSnapshot>>();
        var units = plan.RichUnits;
        if (units.Count == 0) return snapshots;

        // Export, never Paper: an EPUB is read on someone else's device, so the page must be the
        // pure Core HTML that every port pins byte for byte — no Windows font style anywhere near it.
        await using var surface = await renderers.CreateAsync(RenderKind.Export).ConfigureAwait(false);

        foreach (var unit in units)
        {
            ct.ThrowIfCancellationRequested();
            snapshots[unit.Index] = await CaptureUnitAsync(surface, unit, ct).ConfigureAwait(false);
        }

        return snapshots;
    }

    async Task<IReadOnlyList<RichSnapshot>> CaptureUnitAsync(IRenderSurface surface, EpubUnitPlan unit, CancellationToken ct)
    {
        await surface.LoadAsync(unit.Document, ct).ConfigureAwait(false);

        // Grow to content: the capture is in page coordinates, so every element must lie inside the
        // laid-out area. A page that will not answer keeps the default height rather than shrinking.
        var height = JsonScript.Number(await surface.EvalAsync(Scripts.ScrollHeight).ConfigureAwait(false)) ?? MinimumHeightCssPx;
        await surface.SetHeightAsync(Math.Max(MinimumHeightCssPx, height)).ConfigureAwait(false);
        await ExportDelay.For(scheduler, RepaintDelay, ct).ConfigureAwait(false);

        var rects = RichRects.Parse(await surface.EvalAsync(Scripts.RichElements).ConfigureAwait(false));
        if (rects.Count != unit.RichElements.Count) throw new ExportException(Strings.Exports.RichImagesNotCaptured);

        var taken = new List<RichSnapshot>(rects.Count);
        foreach (var measured in rects)
        {
            ct.ThrowIfCancellationRequested();

            // Clamped for the capture — CDP refuses a zero-sized clip — but the *unclamped* width is
            // what the <img> carries, exactly as the Mac writes Int(rect.width.rounded()).
            var rect = measured.Rect;
            var clip = new RectD(rect.X, rect.Y, Math.Max(rect.Width, 1), Math.Max(rect.Height, 1));
            var png = await surface.CaptureRegionPngAsync(clip, Scale).ConfigureAwait(false);
            if (png.Length == 0) throw new ExportException(Strings.Exports.RichImagesNotCaptured);

            taken.Add(new RichSnapshot(png, rect.Width));
        }

        return taken;
    }
}
