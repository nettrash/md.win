using Md.App.Logic.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The Md.App half of WP6 (<c>Web/ExportRenderer.cs</c>, <c>Web/ExportHostWindow.cs</c>,
/// <c>Controls/PrintOverlay.xaml(.cs)</c>, <c>Export/ShareBridge.cs</c>) cannot be executed off
/// Windows, and <c>tools/xamlcheck</c> only <i>compiles</i> it — a compile is blind to every number
/// and every enum member in the file. So the design's own literals are pinned against the
/// <b>source</b>, exactly as <see cref="AppSurfaceTests"/> already pins WP4's controls.
/// </summary>
/// <remarks>
/// <para>
/// This suite exists because a refuter's mutation run found the WinUI half completely unguarded:
/// <c>Canvas.Left = -10000</c> → <c>0</c>, <c>Visibility.Visible</c> → <c>Collapsed</c>,
/// <c>MarginTop = geometry.MarginIn</c> → <c>0.0</c> and
/// <c>CoreWebView2PrintDialogKind.Browser</c> → <c>System</c> each left all 985 tests green. Every
/// one of those four is a silent product defect on Windows and only Windows: an on-screen flash of
/// the export page, throttled engine timers turning a two-second diagram into a twenty-second
/// timeout, pages 2…n printed hard against the paper edge, and a print command with no preview.
/// </para>
/// <para>
/// When one of these moves, the design moved first — change both.
/// </para>
/// </remarks>
public sealed class ExportSurfaceTests
{
    static string Source(params string[] segments) => File.ReadAllText(RepoFiles.At(["src", "Md.App", .. segments]));

    /// <summary>
    /// The file with its commentary removed. These files document the very rules this suite pins —
    /// <c>ShareBridge</c>'s header explains why <c>GetForCurrentView</c> is wrong — so only the CODE
    /// may be searched for something forbidden.
    /// </summary>
    static string CodeOnly(params string[] segments)
    {
        var text = Source(segments);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^\s*//.*$", string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    static void Pins(string file, string design, params string[] fragments)
    {
        var source = Source(file.Split('/'));
        foreach (var fragment in fragments)
            Assert.True(source.Contains(fragment, StringComparison.Ordinal),
                $"{file} no longer contains `{fragment}` — shell-design.md {design} fixes it.");
    }

    // ── §7.1 the export renderer ──────────────────────────────────────────────────────────────

    [Fact]
    public void TheExportRendererIsExactlyOnePageWideAndParkedOffEveryMonitor()
    {
        // 595 × 842 CSS px is the family's export viewport; -10000 is far enough left that no
        // monitor arrangement can put it on screen.
        Pins("Web/ExportRenderer.cs", "§7.1",
            "public const double PageWidthCssPx = 595;",
            "public const double PageHeightCssPx = 842;",
            "public const double OffCanvasLeft = -10000;",
            "Canvas.SetLeft(renderer._web, OffCanvasLeft);",
            "renderer._web.Width = PageWidthCssPx;",
            "renderer._web.Height = PageHeightCssPx;");

        // And the Logic half agrees about the page height, so the snapshotter and the control
        // cannot drift into two different viewports.
        Assert.Equal(842d, RichSnapshotter.MinimumHeightCssPx);
    }

    [Fact]
    public void TheOffCanvasRendererIsVisibleBecauseChromiumThrottlesAHiddenPage()
    {
        // The single most consequential line in the file: Chromium takes page visibility from the
        // controller's IsVisible, which the WinUI control derives from XAML Visibility and NOT from
        // screen position. Collapsed throttles setTimeout, and PlantUML's TeaVM scheduler and
        // md-init.js's waitForSvg are both setTimeout-driven.
        Pins("Web/ExportRenderer.cs", "§7.1", "renderer._web.Visibility = Visibility.Visible;");

        Assert.DoesNotContain("Visibility.Collapsed", CodeOnly("Web", "ExportRenderer.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayoutViewportIsPinnedWithCdpSoA150PercentMonitorDoesNotScaleEverySnapshot()
    {
        Pins("Web/ExportRenderer.cs", "§7.1",
            "\"Emulation.setDeviceMetricsOverride\"",
            "\\\"deviceScaleFactor\\\":1",
            "\\\"mobile\\\":false");
    }

    [Fact]
    public void TheSnapshotIsACdpClipInPageCoordinatesTakenBeyondTheViewport()
    {
        // captureBeyondViewport is what lets a page-space rect be photographed without scrolling it
        // into view; the clip is clamped to a pixel because CDP refuses an empty one.
        Pins("Web/ExportRenderer.cs", "§7.1",
            "\"Page.captureScreenshot\"",
            "\\\"format\\\":\\\"png\\\"",
            "\\\"captureBeyondViewport\\\":true",
            "Math.Max(cssRect.Width, 1)",
            "Math.Max(cssRect.Height, 1)");
    }

    [Fact]
    public void EveryNumberTheRendererWritesIntoJsonIsSpelledInvariantly()
    {
        // A comma-decimal culture would emit {"width":595,"height":842,5} and CDP would refuse it.
        var code = Source("Web", "ExportRenderer.cs");
        var interpolations = code.Split("string.Create(", StringSplitOptions.None).Length - 1;
        Assert.True(interpolations >= 2, "the CDP payloads are built with interpolated handlers");
        Assert.Equal(interpolations, code.Split("string.Create(CultureInfo.InvariantCulture,", StringSplitOptions.None).Length - 1);
        var scripts = File.ReadAllText(RepoFiles.At("src", "Md.App.Logic", "Preview", "Scripts.cs"));
        Assert.Contains("scale.ToString(\"R\", CultureInfo.InvariantCulture)", scripts, StringComparison.Ordinal);
        Assert.Contains("ordinal.ToString(CultureInfo.InvariantCulture)", scripts, StringComparison.Ordinal);
    }

    // ── §7.3 the print settings ───────────────────────────────────────────────────────────────

    [Fact]
    public void ThePrintSettingsAreTheDesignsEightLinesWithHalfAnInchOnAllFourSides()
    {
        Pins("Web/ExportRenderer.cs", "§7.3",
            "settings.Orientation = CoreWebView2PrintOrientation.Portrait;",
            "settings.PageWidth = geometry.PageWidthIn;",
            "settings.PageHeight = geometry.PageHeightIn;",
            "settings.ScaleFactor = 1.0;",
            "settings.ShouldPrintBackgrounds = true;",
            "settings.ShouldPrintHeaderAndFooter = false;",
            "settings.ShouldPrintSelectionOnly = false;",
            "settings.MarginTop = geometry.MarginIn;",
            "settings.MarginBottom = geometry.MarginIn;",
            "settings.MarginLeft = geometry.MarginIn;",
            "settings.MarginRight = geometry.MarginIn;");

        // The number those four sides carry, from the Logic half that a test can execute.
        Assert.Equal(0.5, PrintGeometry.DefaultMarginIn);
        Assert.Equal(0.5, PrintGeometry.Paper.MarginIn);
    }

    [Fact]
    public void ThePdfIsWrittenToATempFileAndReadBackRatherThanOverThePickersFile()
    {
        // PickSaveFileAsync creates the destination but leaves it empty (Appendix A); PrintToPdfAsync
        // answers false on a path it cannot write, so writing straight there would leave an empty
        // file as the export's only trace.
        Pins("Web/ExportRenderer.cs", "§7.3",
            "TemporaryFiles.Scratch(\".pdf\")",
            "await _core.PrintToPdfAsync(temporary, settings)",
            "throw new PdfPaginationException();",
            "TemporaryFiles.Forget(temporary);");
    }

    // ── §7.2 print ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PrintOpensChromiumsBrowserPreviewAndKeepsTheSystemDialogOnlyAsTheFallback()
    {
        // WebView2Feedback #3361: the Browser preview is drawn inside the control's own rectangle
        // and is not displayed at all for a hidden one. System has no preview — it is the fallback,
        // and the overlay must never call it by default.
        Pins("Web/ExportRenderer.cs", "§7.2",
            "public void ShowPrintUi() => _core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);",
            "public void ShowSystemPrintUi() => _core.ShowPrintUI(CoreWebView2PrintDialogKind.System);");

        Assert.Contains("renderer.ShowPrintUi();", Source("Controls", "PrintOverlay.xaml.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSystemPrintUi", CodeOnly("Controls", "PrintOverlay.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePrintOverlayHostsAVisibleRendererAndOffersPrintAgainAndDone()
    {
        // ShowPrintUI raises no event when its dialog closes, which is the whole reason for Done.
        Pins("Controls/PrintOverlay.xaml.cs", "§7.2",
            "ExportRenderer.InPlaceAsync(PageHost",
            "PrintButton.Content = Strings.Exports.PrintAgain;",
            "DoneButton.Content = Strings.Buttons.Done;",
            "DoneButton.Click += (_, _) => Dismiss();");

        var xaml = File.ReadAllText(RepoFiles.At("src", "Md.App", "Controls", "PrintOverlay.xaml"));
        foreach (var name in new[] { "\"Root\"", "\"PageHost\"", "\"Bar\"", "\"Progress\"", "\"PrintButton\"", "\"DoneButton\"" })
            Assert.Contains(name, xaml, StringComparison.Ordinal);
    }

    // ── §7.1 the written-and-off fallback host ────────────────────────────────────────────────

    [Fact]
    public void TheFallbackHostWindowIsShownWithoutActivationRatherThanNeverShown()
    {
        // A Window that is merely constructed has no host-visible XamlRoot, the control sets its
        // controller invisible, and Chromium throttles exactly the timers this whole design keeps
        // alive. Show(false) is the fix; "create without Activate()" is the fidelity defect.
        Pins("Web/ExportHostWindow.cs", "§7.1",
            "appWindow.Show(activateWindow: false);",
            "appWindow.IsShownInSwitchers = false;",
            "appWindow.Move(new PointInt32(ParkedX, ParkedY));",
            "public const int ParkedX = -10000;");
    }

    // ── §7.8 share ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShareGoesThroughTheDesktopInteropAndNeverGetForCurrentView()
    {
        // GetForCurrentView() throws in a desktop app — there is no CoreWindow to get it for.
        Pins("Export/ShareBridge.cs", "§7.8",
            "DataTransferManager.As<IDataTransferManagerInterop>()",
            "interop.GetForWindow(handle, ref iid)",
            "WinRT.MarshalInterface<DataTransferManager>.FromAbi",
            "e.Request.Data.Properties.Title = title;",
            "e.Request.Data.SetStorageItems([file]);",
            "interop.ShowShareUIForWindow(handle);");

        Assert.DoesNotContain("GetForCurrentView", CodeOnly("Export", "ShareBridge.cs"), StringComparison.Ordinal);

        // The two IIDs, verified against learn.microsoft.com: a wrong one is a silent runtime failure.
        var interop = Source("Interop", "NativeMethods.cs");
        Assert.Contains("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8", interop, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c", interop, StringComparison.Ordinal);
    }

    // ── §7.1 the renderer is always taken down ────────────────────────────────────────────────

    [Fact]
    public void TheRendererLeavesNoWebView2BehindIt()
    {
        Pins("Web/ExportRenderer.cs", "§7.1",
            "_host.Children.Remove(_web);",
            "_web.Close();");
    }
}
