// The offscreen renderer every export drives (shell-final.md §7.1). One fresh WebView2 per export,
// living inside the requesting window's ExportCanvas at Canvas.Left = -10000 and — this is the part
// that is not obvious — Visibility.Visible: Chromium decides a page is hidden from the controller's
// IsVisible, which the WinUI control derives from XAML Visibility and not from screen position, and
// a hidden page has its timers throttled. PlantUML's TeaVM scheduler and md-init.js's waitForSvg are
// both setTimeout-driven, so a Collapsed renderer turns a two-second diagram into a twenty-second
// timeout with the source text restored.
//
// Every decision this file makes is in Md.App.Logic — the flows in ExportPipeline, the wait in
// RenderCompletePoller, the scripts in Scripts, the geometry in PrintGeometry. What is left is the
// wiring, which is the half a Mac cannot compile.
using System.Globalization;
using System.Text.Json;
using Md.App.Export;
using Md.App.Logic;
using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Md.App.Web;

internal sealed class ExportRenderer : IRenderSurface
{
    /// <summary>The layout viewport the whole family renders exports in: 595 × 842 CSS px.</summary>
    public const double PageWidthCssPx = 595;

    public const double PageHeightCssPx = 842;

    /// <summary>Far enough left that no monitor arrangement can put it on screen.</summary>
    public const double OffCanvasLeft = -10000;

    readonly Panel _host;
    readonly WebView2 _web = new();
    readonly RenderCompletePoller _poller;
    readonly IScheduler _scheduler;

    /// <summary>
    /// The thread this renderer's WebView2, its host panel and its scheduler all belong to. Every
    /// public member hops onto it through <see cref="UiDispatch"/>, because <c>ExportPipeline</c> is
    /// thread-agnostic by design and calls in from wherever <c>ConfigureAwait(false)</c> left it —
    /// see <see cref="UiDispatch"/> for the whole story.
    /// </summary>
    readonly DispatcherQueue _ui;

    CoreWebView2 _core = null!;          // assigned by InitialiseAsync before any caller sees the renderer
    string _html = string.Empty;
    int _captures;

    ExportRenderer(Panel host, IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        _host = host;
        _scheduler = scheduler;
        _ui = host.DispatcherQueue;
        _poller = new RenderCompletePoller(scheduler);
    }

    /// <summary>
    /// The export renderer of §7.1: parked off-canvas inside the window's own <c>ExportCanvas</c>, at
    /// exactly the page size, and visible so Chromium keeps its timers running.
    /// </summary>
    public static async Task<ExportRenderer> OffCanvasAsync(Canvas host, IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(host);

        var renderer = new ExportRenderer(host, scheduler);
        renderer._web.Width = PageWidthCssPx;
        renderer._web.Height = PageHeightCssPx;
        renderer._web.Visibility = Visibility.Visible;
        Canvas.SetLeft(renderer._web, OffCanvasLeft);
        Canvas.SetTop(renderer._web, 0);

        await renderer.InitialiseAsync();
        return renderer;
    }

    /// <summary>
    /// The same renderer, filling a panel the reader can see — what <c>PrintOverlay</c> needs,
    /// because Chromium's print preview is drawn inside the control's own rectangle and is not shown
    /// at all for a hidden one (WebView2Feedback #3361).
    /// </summary>
    public static async Task<ExportRenderer> InPlaceAsync(Panel host, IScheduler scheduler)
    {
        ArgumentNullException.ThrowIfNull(host);

        var renderer = new ExportRenderer(host, scheduler);
        renderer._web.HorizontalAlignment = HorizontalAlignment.Stretch;
        renderer._web.VerticalAlignment = VerticalAlignment.Stretch;

        await renderer.InitialiseAsync();
        return renderer;
    }

    async Task InitialiseAsync()
    {
        _host.Children.Add(_web);

        var environment = await WebViewEnvironment.GetAsync();
        await _web.EnsureCoreWebView2Async(environment);
        _core = _web.CoreWebView2;

        // Same origin machinery as the live preview, so the generated HTML keeps its relative rich/
        // URLs — but none of the injected scripts: an export must never carry the scroll-sync
        // listener or the link guard, and neither has anything to do here.
        AssetHost.Attach(_core, () => _html);
        ApplySettings(_core);

        await ApplyMetricsAsync(PageHeightCssPx);
    }

    static void ApplySettings(CoreWebView2 core)
    {
        var settings = core.Settings;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsWebMessageEnabled = false;          // nothing posts to the host from an export page
        settings.IsScriptEnabled = true;               // the engines are the whole point
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = false;
    }

    /// <summary>
    /// Pin the layout viewport with CDP, so a 150 % monitor does not scale every EPUB screenshot by
    /// 1.5 and reflow the paper. <c>deviceScaleFactor: 1</c> makes the capture DPI-independent; the
    /// 2× of a snapshot then comes from the clip's own scale, exactly as
    /// <c>WKSnapshotConfiguration.snapshotWidth</c> does on the Mac.
    /// </summary>
    async Task ApplyMetricsAsync(double heightCssPx) =>
        await _core.CallDevToolsProtocolMethodAsync(
            "Emulation.setDeviceMetricsOverride",
            string.Create(CultureInfo.InvariantCulture,
                $"{{\"width\":{PageWidthCssPx},\"height\":{heightCssPx},\"deviceScaleFactor\":1,\"mobile\":false}}"));

    public Task LoadAsync(string html, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(html);

        _html = html;
        return NavigateAsync(AssetOrigin.IndexUrl, ct);
    }

    /// <summary>
    /// Navigate anywhere and wait for render-complete. Exports always take
    /// <see cref="LoadAsync"/> — the index URL, whose bytes <c>AssetHost</c> serves from memory — but
    /// the self-test also has to open an exported <c>file://</c> page to prove it stands alone.
    /// </summary>
    public Task NavigateAsync(string url, CancellationToken ct) =>
        UiDispatch.OnAsync(_ui, () => NavigateCoreAsync(url, ct));

    async Task NavigateCoreAsync(string url, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(url);
        ct.ThrowIfCancellationRequested();

        _captures = 0;

        var navigated = new TaskCompletionSource<bool>();
        void OnCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e) => navigated.TrySetResult(e.IsSuccess);

        _web.NavigationCompleted += OnCompleted;
        try
        {
            using var registration = ct.Register(() => navigated.TrySetCanceled(ct));

            // For an export this is always the same URL: AssetHost serves the current _html for it,
            // which is what makes a second LoadAsync on one renderer (a book's articles) work at all.
            _core.Navigate(url);
            if (!await navigated.Task)
                throw new ExportException(Strings.Exports.PageNotCaptured);
        }
        finally
        {
            _web.NavigationCompleted -= OnCompleted;
        }

        await _poller.WaitAsync(this, ct);
    }

    public Task<string> EvalAsync(string script) => UiDispatch.OnAsync(_ui, async () =>
    {
        try
        {
            return await _core.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // A page that went away mid-script is "no answer"; every caller decodes that as null and
            // has a defined behaviour for it.
            return "null";
        }
    });

    public Task SetHeightAsync(double cssPx) => UiDispatch.OnAsync(_ui, async () =>
    {
        var height = Math.Max(PageHeightCssPx, cssPx);
        _web.Height = height;
        _web.UpdateLayout();
        await ApplyMetricsAsync(height);
    });

    /// <summary>
    /// A genuine 2× bitmap of one page-space rectangle — WebView2's analogue of
    /// <c>WKSnapshotConfiguration</c>. <c>CapturePreviewAsync</c> is viewport-only and would need the
    /// control grown to the whole document and the result cropped, so this goes through the DevTools
    /// protocol instead: a clip in page coordinates, <c>captureBeyondViewport</c> so it need not be
    /// scrolled into view, and a scale that re-renders rather than upscales — which is what lets it
    /// photograph a KaTeX formula, HTML and CSS with no vector in it at all.
    /// </summary>
    public Task<byte[]> CaptureRegionPngAsync(RectD cssRect, double scale) =>
        UiDispatch.OnAsync(_ui, () => CaptureRegionPngCoreAsync(cssRect, scale));

    async Task<byte[]> CaptureRegionPngCoreAsync(RectD cssRect, double scale)
    {
        var ordinal = _captures++;
        var clip = string.Create(CultureInfo.InvariantCulture,
            $"{{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{{\"x\":{cssRect.X},\"y\":{cssRect.Y},\"width\":{Math.Max(cssRect.Width, 1)},\"height\":{Math.Max(cssRect.Height, 1)},\"scale\":{scale}}}}}");

        try
        {
            var answer = await _core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", clip);
            return Convert.FromBase64String(Base64Of(answer) ?? throw new ExportException(Strings.Exports.RichImagesNotCaptured));
        }
        catch (Exception e) when (e is not ExportException)
        {
            // The contingency of §7.1, for a runtime where the CDP call is refused. It rasterises the
            // element's own <svg> through a canvas, so it photographs the three diagram engines and
            // never a formula — md.vscode's known parity break, taken only when the good path is gone.
            // It works by ordinal, which is sound because the EPUB contract captures in DOM order.
            return await RasteriseAsync(ordinal, scale);
        }
    }

    static string? Base64Of(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.String
                ? data.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    async Task<byte[]> RasteriseAsync(int ordinal, double scale)
    {
        await EvalAsync(Scripts.CanvasRasterise(ordinal, scale));

        // ExecuteScriptAsync does not await a promise, so the starter parks its answer on the window
        // and this polls for it — the same shape as the render-complete wait, on a shorter leash.
        for (var attempt = 0; attempt < CanvasRasteriseAttempts; attempt++)
        {
            if (JsonScript.String(await EvalAsync(Scripts.CanvasRasteriseResult)) is { Length: > 0 } base64)
                return Convert.FromBase64String(base64);

            await ExportDelay.For(_scheduler, CanvasRasteriseInterval, CancellationToken.None);
        }

        throw new ExportException(Strings.Exports.RichImagesNotCaptured);
    }

    const int CanvasRasteriseAttempts = 40;
    static readonly TimeSpan CanvasRasteriseInterval = TimeSpan.FromMilliseconds(50);

    public Task<byte[]> PdfAsync(PrintGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        return UiDispatch.OnAsync(_ui, () => PdfCoreAsync(geometry));
    }

    async Task<byte[]> PdfCoreAsync(PrintGeometry geometry)
    {
        var settings = _core.Environment.CreatePrintSettings();
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.PageWidth = geometry.PageWidthIn;
        settings.PageHeight = geometry.PageHeightIn;
        settings.ScaleFactor = 1.0;
        settings.ShouldPrintBackgrounds = true;          // the export sheet already carries print-color-adjust: exact
        settings.ShouldPrintHeaderAndFooter = false;     // no title, no https://md.assets/index.html
        settings.ShouldPrintSelectionOnly = false;
        settings.MarginTop = geometry.MarginIn;
        settings.MarginBottom = geometry.MarginIn;
        settings.MarginLeft = geometry.MarginIn;
        settings.MarginRight = geometry.MarginIn;

        // To a temp file and back, never straight over the picker's file: PrintToPdfAsync answers
        // false on a path it cannot write, and by then the picker has already created an empty file
        // that would be left behind as the export's only trace.
        var temporary = TemporaryFiles.Scratch(".pdf");
        try
        {
            if (!await _core.PrintToPdfAsync(temporary, settings))
                throw new PdfPaginationException();

            return await File.ReadAllBytesAsync(temporary);
        }
        finally
        {
            TemporaryFiles.Forget(temporary);
        }
    }

    /// <summary>
    /// Chromium's own print dialog, over this renderer's rectangle. It has no completion event of any
    /// kind — "doesn't open a new print dialog if it is already open" is all the API promises — which
    /// is why the overlay that hosts it carries a Done button.
    /// </summary>
    public void ShowPrintUi() => _core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);

    /// <summary>The system dialog: no preview, but it is displayed even for a control that is not visible.</summary>
    public void ShowSystemPrintUi() => _core.ShowPrintUI(CoreWebView2PrintDialogKind.System);

    public ValueTask DisposeAsync() => new(UiDispatch.OnAsync(_ui, () =>
    {
        // Both of these are the control's own thread's business, and an export that has just come
        // back from a thread-pool continuation is exactly where a renderer is disposed.
        _host.Children.Remove(_web);
        _web.Close();
    }));
}

/// <summary>
/// One <see cref="ExportRenderer"/> per export, inside the requesting window's export canvas. The
/// <see cref="RenderKind"/> chooses the HTML, and the pipeline has already applied it to the string
/// it hands to <c>LoadAsync</c> — so the renderer itself is the same either way, and this only
/// records which kind was asked for.
/// </summary>
internal sealed class ExportRendererFactory(Canvas host, IScheduler scheduler) : IRenderSurfaceFactory
{
    /// <summary>
    /// The first hop of every export. A WebView2 must be constructed, parented and initialised on the
    /// UI thread, and this is the one call the pipeline is still guaranteed to make from there — so
    /// marshalling here is belt to <see cref="ExportRenderer"/>'s braces, not instead of them: the
    /// pipeline's next await resumes on the thread pool whatever this one did.
    /// </summary>
    public Task<IRenderSurface> CreateAsync(RenderKind kind) =>
        UiDispatch.OnAsync<IRenderSurface>(host.DispatcherQueue, async () => await ExportRenderer.OffCanvasAsync(host, scheduler));
}
