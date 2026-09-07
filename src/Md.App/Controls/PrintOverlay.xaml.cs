// Print (shell-design.md §7.2). The whole reason this control exists rather than a call on the
// off-canvas export renderer: Chromium's Browser print dialog is drawn inside the WebView2's own
// rectangle, and WebView2Feedback #3361 shows it is not displayed at all for a hidden control. So
// the paper page is loaded in a visible WebView2 the reader is looking at, the dialog opens over it,
// and — because ShowPrintUI raises no event when it closes — a Done button ends the overlay.
using Md.App.Logic;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Services;
using Md.App.Web;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Controls;

/// <summary>
/// The in-window print sheet: a dimmed paper backdrop, the rendered page, and Print… / Done.
/// <see cref="ShowAsync"/> is exactly the <c>showPrintOverlay</c> delegate
/// <c>ExportPipeline.PrintAsync</c> takes, and the task it returns completes when the reader is done.
/// </summary>
public sealed partial class PrintOverlay : UserControl
{
    ExportRenderer? _renderer;
    TaskCompletionSource? _dismissed;

    /// <summary>
    /// This control's thread. <see cref="ShowAsync"/> is handed to <c>ExportPipeline.PrintAsync</c>
    /// as a delegate, and the pipeline's own gate (<c>await _gate.WaitAsync().ConfigureAwait(false)</c>)
    /// resumes on the thread pool whenever an export is already in flight — so Print pressed during
    /// an EPUB arrives here off the UI thread and the first `Visibility =` would throw. Same rule as
    /// every other adapter behind a frozen seam; see <see cref="UiDispatch"/>.
    /// </summary>
    readonly DispatcherQueue _ui;

    public PrintOverlay()
    {
        InitializeComponent();
        _ui = DispatcherQueue;

        Visibility = Visibility.Collapsed;
        Root.Padding = new Thickness(24);
        Root.RowSpacing = 12;
        Bar.Spacing = 8;
        PrintButton.Content = Strings.Exports.PrintAgain;
        DoneButton.Content = Strings.Buttons.Done;
        PrintButton.IsEnabled = false;                 // nothing to print until the page has rendered

        PrintButton.Click += (_, _) => _renderer?.ShowPrintUi();
        DoneButton.Click += (_, _) => Dismiss();

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>
    /// The UI-thread timer the render-complete poll uses. The window sets it once; without one the
    /// poll falls back to a plain delay, which is correct but not drivable from a test.
    /// </summary>
    public IScheduler? Scheduler { get; set; }

    /// <summary>
    /// Show the overlay over its window, render <paramref name="paperHtml"/>, open Chromium's print
    /// dialog, and complete when the reader presses Done. A render that fails still shows the overlay
    /// (with Print… disabled) rather than leaving the window looking as if nothing happened — the Mac
    /// swallows print failures and so does the pipeline, so this is the only trace there is.
    /// </summary>
    public Task ShowAsync(string paperHtml)
    {
        ArgumentNullException.ThrowIfNull(paperHtml);
        return UiDispatch.OnAsync(_ui, () => ShowCoreAsync(paperHtml));
    }

    async Task ShowCoreAsync(string paperHtml)
    {
        if (_dismissed is not null) return;            // already up; ShowPrintUI would refuse a second dialog anyway

        var dismissed = new TaskCompletionSource();
        _dismissed = dismissed;
        Visibility = Visibility.Visible;
        Progress.IsActive = true;

        try
        {
            var renderer = await ExportRenderer.InPlaceAsync(PageHost, Scheduler ?? throw new InvalidOperationException(
                "PrintOverlay.Scheduler must be set by the window before Print is invoked."));
            _renderer = renderer;

            await renderer.LoadAsync(paperHtml, CancellationToken.None);
            Progress.IsActive = false;
            PrintButton.IsEnabled = true;
            renderer.ShowPrintUi();
        }
        catch (Exception)
        {
            // Nothing actionable to say: the page is what it is, and Done is still the way out.
            Progress.IsActive = false;
        }

        await dismissed.Task;
    }

    void Dismiss()
    {
        Visibility = Visibility.Collapsed;
        PrintButton.IsEnabled = false;

        var renderer = _renderer;
        _renderer = null;
        if (renderer is not null) _ = renderer.DisposeAsync().AsTask();

        var dismissed = _dismissed;
        _dismissed = null;
        dismissed?.TrySetResult();
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        // The backdrop is the window's own paper, not a scrim: the page underneath is a document, and
        // a black wash over it is the one thing that would make the preview harder to judge.
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
    }
}
