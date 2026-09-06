using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>The Mac scheme handler's table, row for row (§4.2), plus what <c>rich/</c> actually holds.</summary>
public class AssetMimeTests
{
    [Theory]
    [InlineData("js", "text/javascript")]
    [InlineData("mjs", "text/javascript")]
    [InlineData("css", "text/css")]
    [InlineData("html", "text/html")]
    [InlineData("json", "application/json")]
    [InlineData("svg", "image/svg+xml")]
    [InlineData("woff2", "font/woff2")]
    [InlineData("woff", "font/woff")]
    [InlineData("ttf", "font/ttf")]
    public void EveryRowOfTheMacTable(string extension, string mime)
    {
        Assert.Equal(mime, AssetMime.For(extension));
        Assert.Equal(mime, AssetMime.For("." + extension));
        Assert.Equal(mime, AssetMime.For("rich/thing." + extension));
        Assert.Equal(mime, AssetMime.For(@"C:\Program Files\md\web\rich\thing." + extension));
    }

    [Theory]
    [InlineData("rich/md-init.js", "text/javascript")]
    [InlineData("rich/plantuml.js", "text/javascript")]
    [InlineData("rich/katex.min.css", "text/css")]
    [InlineData("rich/fonts/KaTeX_Main-Regular.woff2", "font/woff2")]
    [InlineData("rich/fonts/KaTeX_Main-Regular.woff", "font/woff")]
    [InlineData("rich/fonts/KaTeX_Main-Regular.ttf", "font/ttf")]
    public void EveryKindOfFileRichActuallyHolds(string path, string mime) => Assert.Equal(mime, AssetMime.For(path));

    [Fact]
    public void ModulesMustNotFallToOctetStream()
    {
        // A module fetched as application/octet-stream is rejected outright, which would take
        // md-init.js's dynamic import('./plantuml.js') with it.
        Assert.Equal("text/javascript", AssetMime.For("rich/md-init.js"));
    }

    [Theory]
    [InlineData("JS")]
    [InlineData(".Woff2")]
    [InlineData("rich/KATEX.MIN.CSS")]
    public void ExtensionsAreMatchedCaseInsensitively(string path) =>
        Assert.NotEqual(AssetMime.Fallback, AssetMime.For(path));

    [Fact]
    public void EveryFileTheAppActuallyShipsInRichGetsARealType()
    {
        // The InlineData rows above are the Mac's table; this one is the package. If someone vendors
        // an engine with an extension the table has no row for, the contingency would serve it as
        // application/octet-stream — and a module or a font served that way is rejected outright by
        // Chromium, with the failure only visible on Windows at run time.
        var rich = RepoFiles.At("src", "Md.App", AssetOrigin.RichFolderName);
        Assert.True(Directory.Exists(rich), rich + " is missing: the engines are the preview");

        var unmapped = Directory.EnumerateFiles(rich, "*", SearchOption.AllDirectories)
            .Where(f => AssetMime.For(f) == AssetMime.Fallback)
            .Select(f => Path.GetRelativePath(rich, f))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(unmapped);
    }

    [Theory]
    [InlineData("rich/logo.png")]
    [InlineData("rich/engine.wasm")]
    [InlineData("rich/data.bin")]
    [InlineData("rich/LICENSE")]
    [InlineData("")]
    [InlineData(null)]
    public void EverythingElseIsOctetStreamExactlyAsOnTheMac(string? path) =>
        Assert.Equal("application/octet-stream", AssetMime.For(path));
}
