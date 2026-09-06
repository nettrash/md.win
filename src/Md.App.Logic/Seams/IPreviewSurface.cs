namespace Md.App.Logic.Seams;

/// <summary>
/// The live preview as the coordinator sees it (§4.5): the HTML the virtual host serves, whether
/// the control is shown, navigate / reload / eval, and the navigation outcome. App:
/// <c>WebView2Surface</c> inside <c>PreviewHost</c> (WP5); tests: <c>FakePreviewSurface</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IPreviewSurface
{
    /// <summary>What <c>https://md.assets/index.html</c> returns on the next request.</summary>
    string Html { get; set; }

    /// <summary>False while the pane is collapsed (Edit mode): the coordinator only records "stale".</summary>
    bool IsShown { get; }

    void Navigate(string url);
    void Reload();

    /// <summary>Raw JSON result of <c>ExecuteScriptAsync</c> ("1" with quotes, the four characters null).</summary>
    Task<string> EvalAsync(string script);

    /// <summary>Args: IsSuccess.</summary>
    event Action<bool> NavigationCompleted;
}
