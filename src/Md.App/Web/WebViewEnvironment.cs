// One CoreWebView2Environment for the whole process (shell-final.md §4.1): the live previews and
// every export renderer share it, because WebView2s that share a user-data folder must also share
// browser arguments — asking for a second environment with different ones fails at creation.
using Microsoft.Web.WebView2.Core;

namespace Md.App.Web;

internal static class WebViewEnvironment
{
    /// <summary>
    /// Chromium throttles timers on pages it considers hidden, and page visibility follows the
    /// controller's IsVisible, which the WinUI control derives from XAML Visibility — not from screen
    /// position. PlantUML's TeaVM scheduler and its waitForSvg poll are both setTimeout-driven, so a
    /// throttled page turns a 2 s render into a 20 s timeout with the source restored. This is what
    /// keeps the off-canvas export renderer and a minimised preview honest.
    /// </summary>
    public const string BrowserArguments = "--disable-background-timer-throttling";

    static Task<CoreWebView2Environment>? _creating;

    /// <summary>The shared environment; created on first use, awaited by everyone after that.</summary>
    public static Task<CoreWebView2Environment> GetAsync() => _creating ??= CreateAsync();

    static async Task<CoreWebView2Environment> CreateAsync()
    {
        var options = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = BrowserArguments,
            // Nothing here loads an extension or a third-party origin, so both machineries are dead
            // weight — and tracking prevention would only ever look at our own virtual host.
            AreBrowserExtensionsEnabled = false,
            EnableTrackingPrevention = false,
        };

        // browserExecutableFolder null = the Evergreen runtime, which Windows 11 ships in the box.
        return await CoreWebView2Environment.CreateWithOptionsAsync(null, UserDataFolder(), options);
    }

    /// <summary>
    /// Under LocalCacheFolder, not LocalFolder: it is a browser cache, it is rebuilt on demand, and
    /// it must never reach a backup or roam. Without package identity ApplicationData.Current throws,
    /// so an unpackaged run falls back the same way the crash log does.
    /// </summary>
    static string UserDataFolder()
    {
        string root;
        try
        {
            root = Windows.Storage.ApplicationData.Current.LocalCacheFolder.Path;
        }
        catch (Exception)
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "md");
        }

        var folder = Path.Combine(root, "WebView2");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
