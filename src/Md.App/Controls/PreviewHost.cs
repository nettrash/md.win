// The live preview (shell-final.md §4): a WebView2 plus the adapters the pure PreviewCoordinator
// drives. Every decision here is in Md.App.Logic — the re-render policy in PreviewCoordinator, the
// navigation policy in LinkPolicy, the scripts in Scripts, the JSON in JsonScript — so this file is
// only wiring, which is the half a Mac cannot compile.
using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Web;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace Md.App.Controls;

internal sealed class PreviewHost : UserControl
{
    /// <summary>The two entries WKWebView offers on the Mac; back / forward / reload / print / save / inspect go.</summary>
    static readonly string[] KeptContextMenuItems = ["copy", "selectAll"];

    readonly WebView2 _web = new();
    readonly WebViewSurface _surface;

    CoreWebView2? _core;
    CoreWebView2? _attached;            // the core the host, settings, scripts and handlers are on
    Task? _initialising;
    string? _pendingUrl;
    bool _dark;

    public PreviewHost()
    {
        _surface = new WebViewSurface(this);
        ApplyTheme(dark: false);            // before EnsureCoreWebView2Async, or the runtime-start gap flashes white
        Content = _web;

        _web.NavigationCompleted += OnNavigationCompleted;
        _web.CoreProcessFailed += OnCoreProcessFailed;
        Loaded += (_, _) => _ = EnsureInitialisedAsync();
    }

    /// <summary>What <see cref="PreviewCoordinator"/> drives.</summary>
    public IPreviewSurface Surface => _surface;

    /// <summary>A scroll the reader made, as a fraction of the scrollable range; echoes of our own scrolling are dropped.</summary>
    public event Action<double>? PreviewDidScroll;

    /// <summary>Editor → preview, the other half of the sync.</summary>
    public void ApplyScrollFraction(double fraction) => _ = EvalAsync(Scripts.SyncScrollTo(fraction));

    /// <summary>
    /// §1.4 route 3: the window is closing. The browser process outlives the XAML tree unless the
    /// control is told to go, so every window closes the WebView2 it hosts — this is that call, made
    /// here rather than by reaching for <c>Content</c> from the window (WP3's integration note 6).
    /// The host is finished afterwards.
    /// </summary>
    public void Close() => _web.Close();

    /// <summary>
    /// The paper colour behind the page, painted before the page exists and again before a re-render,
    /// so neither the runtime-startup gap nor a reload ever shows white. The CSS paints the identical
    /// value on html, body. The owner calls this from ActualThemeChanged, before
    /// <see cref="PreviewCoordinator.Update"/> with the new flag.
    /// </summary>
    public void ApplyTheme(bool dark)
    {
        _dark = dark;
        var (a, r, g, b) = Palette.Channels(Palette.For(dark).Paper);
        _web.DefaultBackgroundColor = Windows.UI.Color.FromArgb(a, r, g, b);

        if (_core is not null)
            _core.Profile.PreferredColorScheme =
                dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
    }

    /// <summary>Idempotent: the first caller creates the core, everyone else awaits the same task.</summary>
    public Task EnsureInitialisedAsync() => _initialising ??= InitialiseAsync();

    async Task InitialiseAsync()
    {
        var environment = await WebViewEnvironment.GetAsync();
        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;
        _core = core;

        // EnsureCoreWebView2Async on a control that already has a core hands back the same core, so
        // a second pass through the attach would leave a second WebResourceRequested handler, a
        // second copy of both injected scripts and a second set of event handlers behind. One attach
        // per core, whoever asks.
        if (!ReferenceEquals(core, _attached))
        {
            _attached = core;

            AssetHost.Attach(core, () => _surface.Html);
            AssetHost.LogRoot();
            ApplySettings(core);
            ApplyTheme(_dark);

            // Document start, not document end as on the Mac: neither script may touch the DOM at
            // injection time, and neither does — they register listeners and define functions.
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Scripts.ScrollSync);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(Scripts.LinkGuard);

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ContextMenuRequested += OnContextMenuRequested;
        }

        if (_pendingUrl is { } url)
        {
            _pendingUrl = null;
            core.Navigate(url);
        }
    }

    static void ApplySettings(CoreWebView2 core)
    {
        var settings = core.Settings;

        // The page is a document, not a browser: no zoom, no swipe navigation, no status bar, no
        // script dialogs, no autofill, and no host object — so even a failed link guard could only
        // post a scroll message.
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
        settings.IsWebMessageEnabled = true;
        settings.IsScriptEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;   // pruned in ContextMenuRequested
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif
    }

    void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = JsonScript.ScrollMessage(e.WebMessageAsJson);
        // An echo is our own scroll coming back; forwarding it would fight the editor.
        if (message is { Echo: false } scroll) PreviewDidScroll?.Invoke(scroll.Fraction);
    }

    void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
        {
            e.Cancel = true;
            return;
        }

        switch (LinkPolicy.Decide(uri, e.IsUserInitiated, AssetOrigin.IndexUri))
        {
            case LinkDecision.Allow:
                return;
            case LinkDecision.OpenExternally:
                e.Cancel = true;
                _ = Windows.System.Launcher.LaunchUriAsync(uri);
                return;
            default:
                e.Cancel = true;
                return;
        }
    }

    // target="_blank" — nothing the writer emits today, but authors write raw HTML.
    void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;

        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri)
            && LinkPolicy.Decide(uri, e.IsUserInitiated, AssetOrigin.IndexUri) == LinkDecision.OpenExternally)
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    static void OnContextMenuRequested(CoreWebView2 sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        var items = e.MenuItems;
        for (var i = items.Count - 1; i >= 0; i--)
            if (Array.IndexOf(KeptContextMenuItems, items[i].Name) < 0)
                items.RemoveAt(i);
    }

    void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        // A blank preview is otherwise silent: the page either never loaded or loaded empty, and
        // from the outside those look identical. The first few navigations and every failure go to
        // md.log — enough to tell them apart without following a reload on every keystroke.
        if (!e.IsSuccess || _logged < LoggedNavigations)
        {
            _logged++;
            App.Diagnostics.Write(
                $"preview navigation {(e.IsSuccess ? "ok" : "FAILED " + e.WebErrorStatus)}"
                + $" status={e.HttpStatusCode} html={_surface.Html.Length} bytes shown={_surface.IsShown}");
        }

        _surface.RaiseNavigationCompleted(e.IsSuccess);
    }

    /// <summary>How many successful navigations are worth a line before md.log would just repeat itself.</summary>
    const int LoggedNavigations = 3;
    int _logged;

    /// <summary>
    /// A process in the WebView2 group died or stopped answering. Only two kinds are ours to act on,
    /// and the difference matters:
    ///
    /// <list type="bullet">
    /// <item><c>BrowserProcessExited</c> — the CoreWebView2 is gone. Re-create it and re-attach the
    /// virtual host, the settings and the scripts (§4.7), then load the document again; the HTML is
    /// still in hand, so nothing is lost but the position.</item>
    /// <item><c>RenderProcessExited</c> — the core is still valid and the documented recovery is a
    /// reload, not a re-creation.</item>
    /// </list>
    ///
    /// Everything else is left alone. <c>RenderProcessUnresponsive</c> especially: it is documented
    /// to "run every few seconds until the process becomes responsive again", which is exactly what a
    /// heavy PlantUML or Mermaid render looks like (§4.10). Re-running the attach on it would leave a
    /// second WebResourceRequested handler, a second copy of both injected scripts and a second set
    /// of event handlers behind on every tick — every reader scroll reported twice, then three times
    /// — and the reload would kill the render that made the page slow in the first place.
    /// </summary>
    async void OnCoreProcessFailed(WebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                // The core survives, so the attach must not be repeated: just load the page again.
                _surface.Navigate(AssetOrigin.IndexUrl);
                return;

            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                break;

            default:
                return;
        }

        _core = null;
        _initialising = null;

        try
        {
            await EnsureInitialisedAsync();
            _surface.Navigate(AssetOrigin.IndexUrl);
        }
        catch (Exception)
        {
            // A second failure means the runtime is gone; the pane stays blank on its paper colour
            // rather than taking the window down with it.
            _initialising = null;
        }
    }

    async Task<string> EvalAsync(string script)
    {
        if (_core is null) return "null";
        try
        {
            return await _core.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // A page that went away mid-script is "no answer"; JsonScript reads that as null.
            return "null";
        }
    }

    /// <summary>
    /// <see cref="IPreviewSurface"/> over the control. The coordinator may drive it before the core
    /// exists (the first Update can beat EnsureCoreWebView2Async), so a navigation that arrives early
    /// is remembered and performed by <see cref="InitialiseAsync"/>.
    /// </summary>
    sealed class WebViewSurface(PreviewHost host) : IPreviewSurface
    {
        public string Html { get; set; } = "";

        // What "stale while collapsed" is decided on. It must ask the TREE, not this control: the
        // layout collapses the ContentControl the host sits in (ArticlePanes.Apply collapses
        // PreviewSlot), and a collapsed parent leaves the child's own Visibility untouched at
        // Visible. Reading host.Visibility therefore answered "shown" in Edit mode as well, so the
        // coordinator reloaded a page nobody could see and never recorded the pane as stale.
        public bool IsShown => IsEffectivelyVisible(host);

        public event Action<bool> NavigationCompleted = delegate { };

        public void Navigate(string url)
        {
            if (host._core is { } core) core.Navigate(url);
            else host._pendingUrl = url;
        }

        public void Reload()
        {
            // Nothing has loaded yet: a reload is the first load.
            if (host._core is { } core) core.Reload();
            else host._pendingUrl = AssetOrigin.IndexUrl;
        }

        public Task<string> EvalAsync(string script) => host.EvalAsync(script);

        public void RaiseNavigationCompleted(bool isSuccess) => NavigationCompleted(isSuccess);

        /// <summary>
        /// Visible, and every ancestor visible too. WinUI has no WPF-style IsVisible, and
        /// Visibility is not inherited — a collapsed parent hides its children without changing
        /// their property — so the chain has to be walked.
        /// </summary>
        static bool IsEffectivelyVisible(DependencyObject? node)
        {
            for (; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is UIElement { Visibility: Visibility.Collapsed }) return false;
            return true;
        }
    }
}
