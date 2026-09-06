using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The §4.7 matrix, row by row. The row that matters most is the second: two of the three candidate
/// designs tested http(s) before the host and so opened the user's browser at
/// <c>https://md.assets/other.md</c> for an ordinary relative link.
/// </summary>
public class LinkPolicyTests
{
    static readonly Uri Index = AssetOrigin.IndexUri;

    static LinkDecision Decide(string uri, bool userInitiated = true) =>
        LinkPolicy.Decide(new Uri(uri, UriKind.RelativeOrAbsolute), userInitiated, Index);

    [Theory]
    [InlineData("https://md.assets/index.html")]                 // our own Navigate / Reload
    [InlineData("https://md.assets/index.html#a-heading")]        // [x](#slug), if it ever surfaces
    [InlineData("https://md.assets/index.html#")]
    public void OurOwnDocumentIsAllowed(string uri) => Assert.Equal(LinkDecision.Allow, Decide(uri));

    [Theory]
    [InlineData("https://md.assets/other.md")]                    // [x](other.md) resolved against the host
    [InlineData("https://md.assets/")]
    [InlineData("https://md.assets/rich/md-init.js")]
    [InlineData("https://md.assets/index.html?v=2")]              // same path, different resource
    [InlineData("http://md.assets/index.html")]                   // same host, wrong scheme
    public void EveryOtherUrlOnOurHostIsCancelled(string uri) => Assert.Equal(LinkDecision.Cancel, Decide(uri));

    [Theory]
    [InlineData("https://nettrash.me/")]
    [InlineData("http://example.com/a?b=c#d")]
    public void HttpOnAnotherHostGoesToTheBrowser(string uri) =>
        Assert.Equal(LinkDecision.OpenExternally, Decide(uri));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<b>x</b>")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("mailto:nettrash@nettrash.me")]
    [InlineData("ftp://example.com/x")]
    [InlineData("about:blank")]
    public void EveryOtherSchemeDies(string uri) => Assert.Equal(LinkDecision.Cancel, Decide(uri));

    [Fact]
    public void ARelativeUriIsCancelled()
    {
        // Nothing produces one (WebView2 hands over absolute URIs), and Host / Scheme throw on it.
        Assert.Equal(LinkDecision.Cancel, Decide("other.md"));
    }

    [Fact]
    public void TheHostIsMatchedCaseInsensitivelyLikeDns()
    {
        Assert.Equal(LinkDecision.Cancel, Decide("https://MD.ASSETS/other.md"));
    }

    [Fact]
    public void ANavigationTheUserDidNotStartNeverOpensTheBrowser()
    {
        // A script assigning location, or a meta refresh, inside the rendered document. The Mac lets
        // those navigate the preview away; here they are dropped (§4.7, the recorded divergence).
        Assert.Equal(LinkDecision.Cancel, Decide("https://nettrash.me/", userInitiated: false));
    }

    [Fact]
    public void OurOwnLoadsAreNotUserInitiatedAndStillPass()
    {
        Assert.Equal(LinkDecision.Allow, Decide(AssetOrigin.IndexUrl, userInitiated: false));
    }

    [Fact]
    public void TheIndexUrlIsTheOneTheHostServes()
    {
        Assert.Equal("https://md.assets/index.html", AssetOrigin.IndexUrl);
        Assert.Equal("md.assets", AssetOrigin.Host);
        Assert.Equal(AssetOrigin.IndexUrl, AssetOrigin.IndexUri.ToString());
    }
}
