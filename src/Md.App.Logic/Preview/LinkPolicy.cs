namespace Md.App.Logic.Preview;

/// <summary>What the host does with a navigation the page asks for (§4.7).</summary>
public enum LinkDecision
{
    /// <summary>Let it happen: it is our own load, reload or a fragment hop inside the document.</summary>
    Allow,

    /// <summary><c>e.Cancel = true</c> and nothing else — the Mac's silent drop.</summary>
    Cancel,

    /// <summary><c>e.Cancel = true</c> then <c>Launcher.LaunchUriAsync(uri)</c>.</summary>
    OpenExternally,
}

/// <summary>
/// The whole navigation policy, pure (§4.7). Applied in <c>NavigationStarting</c> and
/// <c>NewWindowRequested</c>.
///
/// The order of the tests is the point: the <em>host</em> is checked before the http(s) branch, so a
/// relative link the page resolved against our own origin (<c>[x](other.md)</c> →
/// <c>https://md.assets/other.md</c>) is cancelled like the Mac cancels a non-fragment <c>mdassets</c>
/// URL, instead of launching the browser at a made-up address.
///
/// <c>javascript:</c> hrefs never reach here — Chromium runs a clicked <c>javascript:</c> URL in-page
/// with no navigation at all — which is why <see cref="Scripts.LinkGuard"/> exists as well.
/// </summary>
public static class LinkPolicy
{
    /// <param name="uri">The navigation target (<c>NavigationStarting.Uri</c>).</param>
    /// <param name="isUserInitiated">
    /// <c>NavigationStarting.IsUserInitiated</c>. Only ever narrows the answer: a cross-host
    /// http(s) navigation the user did not ask for (a script assigning <c>location</c>, a
    /// <c>meta refresh</c>) is cancelled rather than handed to the browser. The Mac allows those to
    /// navigate the preview away, which is worse; this is the one recorded divergence in this table.
    /// </param>
    /// <param name="indexUrl">Our document URL, <see cref="AssetOrigin.IndexUri"/>.</param>
    public static LinkDecision Decide(Uri uri, bool isUserInitiated, Uri indexUrl)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(indexUrl);

        // Our own Navigate / Reload, and a fragment hop that surfaces as a navigation.
        if (WithoutFragment(uri).Equals(WithoutFragment(indexUrl), StringComparison.Ordinal)) return LinkDecision.Allow;

        // Host and Scheme throw on a relative Uri, and nothing relative can be one of ours.
        if (!uri.IsAbsoluteUri) return LinkDecision.Cancel;

        // Anything else on our own host: a relative link that resolved against the origin. Nothing
        // but index.html is a document here, so it can only be a 404 or an asset — never a page.
        if (indexUrl.IsAbsoluteUri && string.Equals(uri.Host, indexUrl.Host, StringComparison.OrdinalIgnoreCase))
            return LinkDecision.Cancel;

        var web = uri.Scheme is "http" or "https";
        if (web && isUserInitiated) return LinkDecision.OpenExternally;

        // file:, data:, mailto:, dropped files — and a cross-host page the user never clicked.
        return LinkDecision.Cancel;
    }

    /// <summary>Scheme, host, path and query — everything but the fragment, which never changes the decision.</summary>
    static string WithoutFragment(Uri uri) =>
        uri.IsAbsoluteUri ? uri.GetLeftPart(UriPartial.Query) : uri.OriginalString;
}
