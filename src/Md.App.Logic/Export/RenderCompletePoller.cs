using Md.App.Logic.Preview;
using Md.App.Logic.Seams;

namespace Md.App.Logic.Export;

/// <summary>
/// The render-complete handshake (§4.8, export.md §3.3), the Mac's loop line for line: read
/// <c>data-md-render-complete</c>, and if it is not yet the flag, wait 250 ms and read it again, up
/// to <see cref="MaxAttempts"/> more times. <b>The timeout is a success, not a failure</b> — after
/// ~two minutes the export proceeds with whatever the DOM holds, because a diagram that never
/// arrived has already restored its own source text and throwing away a whole export over one figure
/// is worse than exporting it as text. Only a navigation failure aborts, and that is the surface's
/// business, not this loop's.
/// </summary>
/// <remarks>
/// <para>
/// The comparison is against the three characters <c>"1"</c>, quotes included:
/// <c>ExecuteScriptAsync</c> hands back the JSON <i>encoding</i> of the result and the flag is a
/// string attribute, which is exactly what md.Android compares (<c>value == "\"1\""</c>). Anything
/// else — <c>null</c> from a dead page, a number, whitespace — is "not yet".
/// </para>
/// <para>
/// The live preview never waits: it renders while you type and a half-drawn diagram is the point.
/// This is exports only.
/// </para>
/// </remarks>
public sealed class RenderCompletePoller(IScheduler scheduler)
{
    /// <summary>Retries after the first read: 480 × 250 ms ≈ 120 s, the number all four ports use.</summary>
    public const int MaxAttempts = 480;

    /// <summary>The gap between reads.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Returns when the page says it has finished rendering, or when the attempts run out — both are
    /// success. <paramref name="surface"/> is the loading surface itself: <c>ExportRenderer.LoadAsync</c>
    /// awaits its own navigation and then hands <c>this</c> here.
    /// </summary>
    public async Task WaitAsync(IRenderSurface surface, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(surface);

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            var json = await surface.EvalAsync(Scripts.RenderComplete).ConfigureAwait(false);
            if (string.Equals(json, Scripts.RenderCompleteResult, StringComparison.Ordinal)) return;
            if (attempt >= MaxAttempts) return;

            await ExportDelay.For(scheduler, Interval, ct).ConfigureAwait(false);
        }
    }
}
