namespace Md.App.Logic.Preview;

/// <summary>
/// The one origin the preview ever loads (§4.2). The virtual host is mapped to the install's
/// <c>web\</c> folder and the page itself is served from memory on the <em>same</em> host, which is
/// what lets the generated HTML keep its relative <c>rich/…</c> URLs unchanged: <c>md-init.js</c>'s
/// <c>import('./plantuml.js')</c> and <c>katex.min.css</c>'s <c>url(fonts/…)</c> resolve because the
/// document and the engines share an origin. <c>https</c> so the page is a secure context.
/// Shared by <c>AssetHost</c>, <c>PreviewHost</c>, <see cref="PreviewCoordinator"/> and
/// <see cref="LinkPolicy"/> so the string is spelled once.
/// </summary>
public static class AssetOrigin
{
    public const string Host = "md.assets";

    /// <summary>The document URL; <c>Reload()</c> re-requests it and picks up the new HTML.</summary>
    public const string IndexUrl = "https://md.assets/index.html";

    /// <summary>Same value as a <see cref="Uri"/>, for <see cref="LinkPolicy.Decide"/>.</summary>
    public static readonly Uri IndexUri = new(IndexUrl);

    /// <summary>The mapped folder under the install directory — never the install root, so no assembly is fetchable.</summary>
    public const string WebFolderName = "web";

    /// <summary>The engines' folder inside it; the only prefix the page ever asks for.</summary>
    public const string RichFolderName = "rich";
}
