using System.Text.RegularExpressions;
using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The origin constants (§4.2). They are not decoration: <see cref="AssetOrigin.WebFolderName"/> is
/// one half of a contract with <c>Md.App.csproj</c>'s
/// <c>&lt;Content Include="rich\**\*" Link="web\rich\…" /&gt;</c> (§11.3) — the csproj decides where
/// the engines land in the package and <c>AssetHost.WebRoot</c> decides where the virtual host looks
/// for them. If the two ever disagree the preview is a blank page with 404s for every engine, and no
/// other test in this repo notices, because the whole failure lives on Windows at run time.
/// </summary>
public class AssetOriginTests
{
    [Fact]
    public void TheConstantsAreTheOnesTheDesignFixes()
    {
        Assert.Equal("md.assets", AssetOrigin.Host);
        Assert.Equal("web", AssetOrigin.WebFolderName);
        Assert.Equal("rich", AssetOrigin.RichFolderName);
        Assert.Equal("https://md.assets/index.html", AssetOrigin.IndexUrl);
    }

    [Fact]
    public void TheIndexUrlIsBuiltOnTheHostAndIsHttps()
    {
        // https, so the page is a secure context — the engines' WASM is happier there and it matches
        // Android's appassets.androidplatform.net.
        Assert.Equal("https://" + AssetOrigin.Host + "/index.html", AssetOrigin.IndexUrl);
        Assert.Equal(AssetOrigin.IndexUrl, AssetOrigin.IndexUri.ToString());
        Assert.Equal("https", AssetOrigin.IndexUri.Scheme);
        Assert.Equal(AssetOrigin.Host, AssetOrigin.IndexUri.Host);
    }

    [Fact]
    public void TheCsprojPackagesTheEnginesWhereTheVirtualHostLooksForThem()
    {
        var csproj = File.ReadAllText(RepoFiles.At("src", "Md.App", "Md.App.csproj"));

        var link = Regex.Match(csproj, @"<Content\s+Include=""rich\\\*\*\\\*""\s+Link=""([^""]+)""");
        Assert.True(link.Success, "Md.App.csproj no longer carries the <Content Include=\"rich\\**\\*\" Link=\"…\"/> item of §11.3");

        // web\rich\%(RecursiveDir)%(Filename)%(Extension) — the first two segments are the contract.
        var segments = link.Groups[1].Value.Split('\\');
        Assert.Equal(AssetOrigin.WebFolderName, segments[0]);
        Assert.Equal(AssetOrigin.RichFolderName, segments[1]);
    }

    [Fact]
    public void TheMappedFolderIsNotTheInstallRoot()
    {
        // Mapping the install root — as two of the candidate designs did — would put md.dll,
        // Examples\ and every assembly one fetch away from a rendered document.
        Assert.NotEmpty(AssetOrigin.WebFolderName);
        Assert.DoesNotContain('/', AssetOrigin.WebFolderName);
        Assert.DoesNotContain('\\', AssetOrigin.WebFolderName);
        Assert.DoesNotContain('.', AssetOrigin.WebFolderName);
    }
}
