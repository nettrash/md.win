namespace Md.App.Logic.View;

/// <summary>
/// The link between the two panes (macOS §10.3): each side reports how far down it is as a
/// fraction of its own scrollable height, and the other side is told to go to the same fraction.
/// Proportional, not line-mapped — the Mac's choice, and the reason a 2000-line document scrolls
/// smoothly without the preview being re-measured.
/// </summary>
/// <remarks>
/// Deliberately not observable: a scroll must never re-render or re-lay out anything, or the two
/// panes chase each other. Echo suppression is each side's own job — the preview's injected script
/// carries a 300 ms programmatic window, the editor uses <see cref="ScrollSyncGuard"/>.
/// </remarks>
public sealed class ScrollSync
{
    /// <summary>Set by the editor pane: scroll the editor to this fraction. Re-handed on every pane rebuild.</summary>
    public Action<double>? ScrollEditor { get; set; }

    /// <summary>Set by the preview host: <c>window.__mdSyncScrollTo(fraction)</c>.</summary>
    public Action<double>? ScrollPreview { get; set; }

    public void EditorDidScroll(double fraction) => ScrollPreview?.Invoke(Clamp(fraction));

    public void PreviewDidScroll(double fraction) => ScrollEditor?.Invoke(Clamp(fraction));

    /// <summary>
    /// [0, 1]. NaN becomes 0: the fraction is a division by a scrollable height that can be zero on
    /// either side, and a NaN reaching <c>ChangeView</c> or <c>window.scrollTo</c> is a jump to the
    /// top on one platform and nothing at all on the other.
    /// </summary>
    public static double Clamp(double fraction) =>
        double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
}
