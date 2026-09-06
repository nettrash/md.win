// One assembled ExportPipeline per window (shell-design.md §7). Everything below the line is
// Md.App.Logic; this file only decides which adapter goes where, so that a window can start every
// export, print and share with a single call and nothing WinUI-shaped leaks into the flows.
using Md.App.Controls;
using Md.App.Logic.Documents;
using Md.App.Logic.Export;
using Md.App.Logic.Seams;
using Md.App.Services;
using Md.App.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Export;

/// <summary>
/// The export half of one window: the pipeline, wired to that window's pickers, alerts, share sheet
/// and export canvas.
/// </summary>
/// <remarks>
/// The window owns the two XAML pieces this needs — a <see cref="Canvas"/> in the root grid for the
/// off-canvas renderers, and a <see cref="PrintOverlay"/> over the content — and hands both here.
/// If the off-canvas renderer ever misbehaves on a real Windows build, the whole swap is to build
/// this with an <see cref="ExportHostWindow"/>'s <c>Host</c> canvas instead of the window's own.
/// </remarks>
internal sealed class DocumentExports
{
    readonly PrintOverlay _overlay;

    public DocumentExports(Window window, Canvas exportCanvas, PrintOverlay printOverlay, IScheduler scheduler, IAlerts? alerts = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(exportCanvas);
        ArgumentNullException.ThrowIfNull(printOverlay);
        ArgumentNullException.ThrowIfNull(scheduler);

        _overlay = printOverlay;
        _overlay.Scheduler = scheduler;

        var dialogs = alerts ?? new WinUiAlerts(window);

        Pipeline = new ExportPipeline(
            new ExportRendererFactory(exportCanvas, scheduler),
            new Pickers(window, dialogs),
            new ShareBridge(window),
            dialogs,
            SystemIoFileSystem.Instance,
            RichAssets.Read,
            scheduler,
            FileIdentity.Instance,
            TemporaryFiles.Folder);
    }

    /// <summary>Every flow of §7. The window's command handlers call straight into it.</summary>
    public ExportPipeline Pipeline { get; }

    /// <summary>
    /// File ▸ Print… and Book ▸ Print Book… — the pipeline builds the paper HTML and the overlay
    /// shows it; the returned task completes when the reader presses Done.
    /// </summary>
    public Task PrintAsync(string source, string title) => Pipeline.PrintAsync(source, title, _overlay.ShowAsync);

    /// <summary>The window is closing: whatever is rendering stops rather than talking to a dead XamlRoot.</summary>
    public void Cancel() => Pipeline.Cancel();
}
