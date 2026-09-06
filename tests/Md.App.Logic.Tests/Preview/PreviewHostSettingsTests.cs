using System.Text.RegularExpressions;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// §4.1 and §4.4 are the two paragraphs of the preview design that live entirely in <c>src/Md.App</c>
/// — a <c>CoreWebView2EnvironmentOptions</c> and thirteen <c>CoreWebView2Settings</c> properties — so
/// no behaviour of theirs can be exercised without a Windows machine and a WebView2 runtime.
///
/// What these tests do is the one thing that is possible here: read the source and check that every
/// line the design names is still written. That is weak — it proves the assignment exists, not that
/// it took effect, and <c>xamlcheck</c> already proves the property names and types are real. It is
/// still worth having, because each of these is a single line that does something invisible (no zoom,
/// no autofill, no host objects, a pruned context menu, timers that keep running off-canvas), a
/// dropped one shows up as nothing at all, and the <c>--selftest</c> runner of §11.4 drives the
/// export renderer rather than the live preview. The real proof stays the day-1 Windows checklist.
/// </summary>
public class PreviewHostSettingsTests
{
    static string Source(params string[] segments) => File.ReadAllText(RepoFiles.At(segments));

    static string PreviewHost => Source("src", "Md.App", "Controls", "PreviewHost.cs");
    static string Environment => Source("src", "Md.App", "Web", "WebViewEnvironment.cs");

    static bool Assigns(string source, string property, string value) =>
        Regex.IsMatch(source, @"\b" + Regex.Escape(property) + @"\s*=\s*" + Regex.Escape(value) + @"\s*[;,]");

    // ───────────────────────────── §4.1 the environment ─────────────────────────────

    [Fact]
    public void TheEnvironmentKeepsTimersRunningOnAHiddenPage()
    {
        // Chromium throttles a hidden page's timers, page visibility follows XAML Visibility, and
        // PlantUML's scheduler and its waitForSvg poll are both setTimeout-driven — so without this
        // the off-canvas export renderer (§7.1) turns a 2 s render into a 20 s timeout (§4.10).
        Assert.Contains("--disable-background-timer-throttling", Environment, StringComparison.Ordinal);
        Assert.True(Assigns(Environment, "AdditionalBrowserArguments", "BrowserArguments"),
            "§4.1: AdditionalBrowserArguments must carry the throttling flag");
    }

    [Fact]
    public void TheEnvironmentIsCreatedOnceWithTheDocumentedOptions()
    {
        Assert.True(Assigns(Environment, "AreBrowserExtensionsEnabled", "false"));
        Assert.True(Assigns(Environment, "EnableTrackingPrevention", "false"));
        Assert.Contains("CreateWithOptionsAsync", Environment, StringComparison.Ordinal);

        // One environment and one user-data folder for the preview and every export renderer:
        // WebView2s sharing a user-data folder must share browser arguments.
        Assert.Contains("_creating ??=", Environment, StringComparison.Ordinal);

        // Package.Current throws unpackaged, so the path must not come from it.
        Assert.DoesNotContain("Package.Current", Environment, StringComparison.Ordinal);
    }

    // ───────────────────────────── §4.4 the settings ─────────────────────────────

    [Theory]
    [InlineData("AreBrowserAcceleratorKeysEnabled", "false")]   // Chromium must not act on Ctrl+P / Ctrl+F / F5
    [InlineData("IsZoomControlEnabled", "false")]
    [InlineData("IsPinchZoomEnabled", "false")]
    [InlineData("IsSwipeNavigationEnabled", "false")]
    [InlineData("IsStatusBarEnabled", "false")]
    [InlineData("AreDefaultScriptDialogsEnabled", "false")]
    [InlineData("IsGeneralAutofillEnabled", "false")]
    [InlineData("IsPasswordAutosaveEnabled", "false")]
    [InlineData("AreHostObjectsAllowed", "false")]              // so even a failed link guard can only post a scroll
    [InlineData("IsBuiltInErrorPageEnabled", "false")]
    [InlineData("IsWebMessageEnabled", "true")]                 // the scroll channel
    [InlineData("IsScriptEnabled", "true")]                     // the engines
    [InlineData("AreDefaultContextMenusEnabled", "true")]       // kept, then pruned
    public void EverySettingOfTheTable(string property, string value) =>
        Assert.True(Assigns(PreviewHost, property, value), $"§4.4: {property} = {value} is missing from PreviewHost");

    [Fact]
    public void DevToolsAreOnlyForDebugBuilds()
    {
        Assert.Matches(@"#if\s+DEBUG\s*\n\s*settings\.AreDevToolsEnabled\s*=\s*true;\s*\n\s*#else\s*\n\s*settings\.AreDevToolsEnabled\s*=\s*false;",
            PreviewHost.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void TheContextMenuKeepsOnlyWhatWkWebViewOffers()
    {
        // Copy and Select all; back / forward / reload / print / save / inspect go.
        Assert.Contains("\"copy\", \"selectAll\"", PreviewHost, StringComparison.Ordinal);
        Assert.Contains("ContextMenuRequested", PreviewHost, StringComparison.Ordinal);
    }

    [Fact]
    public void ScrollbarsFollowTheAppTheme()
    {
        Assert.Contains("Profile.PreferredColorScheme", PreviewHost, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2PreferredColorScheme.Dark", PreviewHost, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2PreferredColorScheme.Light", PreviewHost, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaperColourIsPaintedBeforeTheRuntimeExists()
    {
        // DefaultBackgroundColor set before EnsureCoreWebView2Async, or the runtime-startup gap and
        // every reload flash white behind a page whose CSS paints the same value.
        var source = PreviewHost;
        var applyInConstructor = source.IndexOf("ApplyTheme(dark: false)", StringComparison.Ordinal);
        var ensure = source.IndexOf("EnsureCoreWebView2Async", StringComparison.Ordinal);

        Assert.True(applyInConstructor >= 0, "§4.4: the paper colour is not painted in the constructor");
        Assert.True(applyInConstructor < ensure, "§4.4: the paper colour must be set before EnsureCoreWebView2Async");
        Assert.Contains("DefaultBackgroundColor", source, StringComparison.Ordinal);
    }

    // ───────────────────────────── §4.7 process recovery ─────────────────────────────

    [Fact]
    public void OnlyAFatalProcessFailureReAttachesTheHost()
    {
        // ProcessFailed for RenderProcessUnresponsive is documented to "run every few seconds until
        // the process becomes responsive again" — which is what a heavy PlantUML render looks like.
        // Re-running the attach on those ticks would leave a second WebResourceRequested handler, a
        // second copy of both injected scripts and a second set of event handlers behind each time.
        var source = PreviewHost;

        Assert.Contains("CoreWebView2ProcessFailedKind.BrowserProcessExited", source, StringComparison.Ordinal);
        Assert.Contains("CoreWebView2ProcessFailedKind.RenderProcessExited", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreWebView2ProcessFailedKind.RenderProcessUnresponsive", source, StringComparison.Ordinal);

        // And the attach itself is guarded, whoever calls it.
        Assert.Contains("ReferenceEquals(core, _attached)", source, StringComparison.Ordinal);
    }
}
