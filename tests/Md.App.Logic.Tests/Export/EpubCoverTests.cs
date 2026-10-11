using System.Text;
using Md.App.Logic.Export;
using Md.Core.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The EPUB's <c>cover:</c> (1.6, #2) read for real: beside the document with the TextBundle
/// export's containment, fetched over https through the pipeline's seam, or decoded from a
/// <c>data:</c> image — and, whatever goes wrong, a book without a cover rather than a failed export.
/// </summary>
public class EpubCoverTests
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];

    static string Source(string cover) => "---\ntitle: Book\nauthor: A\ncover: " + cover + "\n---\n\nText.\n";

    static IReadOnlyList<ZipEntry> Written(ExportHarness h, string path) => ZipReader.Entries(h.WrittenBytes(path)!)!;

    static string Opf(ExportHarness h, string path) =>
        Encoding.UTF8.GetString(Written(h, path).Single(e => e.Name == "OEBPS/content.opf").Data);

    [Fact]
    public void ACoverBesideTheDocumentGoesOnTheBook()
    {
        using var h = new ExportHarness();
        h.Files.AddFile(ExportHarness.Docs + @"\cover.png", Png);
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("cover.png"), "notes.md", ExportHarness.Docs + @"\notes.md"));

        var cover = Written(h, path)[^1];
        Assert.Equal("OEBPS/images/cover.png", cover.Name);
        Assert.Equal(Png, cover.Data);
        Assert.Contains("<dc:creator>A</dc:creator>", Opf(h, path), StringComparison.Ordinal);
        Assert.Contains("properties=\"cover-image\"", Opf(h, path), StringComparison.Ordinal);
        Assert.Empty(h.Fetched);
    }

    [Fact]
    public void ACoverFilePastTheCapIsNeverRead()
    {
        using var h = new ExportHarness();
        var big = new byte[EpubMetadataReader.MaxCoverBytes + 1];
        Png.CopyTo(big, 0);
        h.Files.AddFile(ExportHarness.Docs + @"ig.png", big);
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("big.png"), "notes.md", ExportHarness.Docs + @"
otes.md"));

        Assert.DoesNotContain("properties=\"cover-image\"", Opf(h, path), StringComparison.Ordinal);
        Assert.DoesNotContain(h.Files.Reads, r => r.EndsWith("big.png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("../outside.png")]        // climbs out of the document's folder
    [InlineData("missing.png")]           // not there
    [InlineData("notes.txt")]             // there, but not an image
    public void ACoverThatCannotBeReadIsLeftOutSilently(string reference)
    {
        using var h = new ExportHarness();
        h.Files.AddFile(@"C:\outside.png", Png);
        h.Files.AddFile(ExportHarness.Docs + @"\notes.txt", "not a picture");
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source(reference), "notes.md", ExportHarness.Docs + @"\notes.md"));

        Assert.DoesNotContain(Written(h, path), e => e.Name.Contains("cover", StringComparison.Ordinal));
        Assert.DoesNotContain("cover-image", Opf(h, path), StringComparison.Ordinal);
        Assert.Empty(h.Alerts.Warnings);
    }

    [Fact]
    public void AnUnsavedDocumentFindsNoLocalCover()
    {
        using var h = new ExportHarness();
        h.Files.AddFile(ExportHarness.Docs + @"\cover.png", Png);
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("cover.png"), "Untitled"));

        Assert.DoesNotContain(Written(h, path), e => e.Name.Contains("cover", StringComparison.Ordinal));
    }

    [Fact]
    public void AnHttpsCoverIsFetchedOnce()
    {
        using var h = new ExportHarness { Fetch = _ => Jpeg };
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("https://example.com/c.jpg"), "notes.md"));

        Assert.Equal(["https://example.com/c.jpg"], h.Fetched);
        Assert.Equal("OEBPS/images/cover.jpg", Written(h, path)[^1].Name);
    }

    [Fact]
    public void AFailedFetchIsABookWithoutACover()
    {
        using var h = new ExportHarness { Fetch = _ => throw new HttpRequestException("offline") };
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("https://example.com/c.jpg"), "notes.md"));

        Assert.DoesNotContain(Written(h, path), e => e.Name.Contains("cover", StringComparison.Ordinal));
        Assert.Empty(h.Alerts.Warnings);
    }

    [Theory]
    [InlineData("http://example.com/c.jpg")]
    [InlineData("C:\\pictures\\c.jpg")]
    public void NothingIsFetchedForAReferenceThatCanNeverBeRead(string reference)
    {
        using var h = new ExportHarness { Fetch = _ => Jpeg };
        h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source(reference), "notes.md", ExportHarness.Docs + @"\notes.md"));

        Assert.Empty(h.Fetched);
    }

    [Fact]
    public void ADataCoverNeedsNoFileAndNoFetch()
    {
        using var h = new ExportHarness();
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Source("data:image/png;base64," + Convert.ToBase64String(Png)), "Untitled"));

        Assert.Equal(Png, Written(h, path)[^1].Data);
        Assert.Empty(h.Fetched);
    }

    [Theory]
    [InlineData("https://a.example/c.png", "https://b.example/d.png", "https://b.example/d.png")]
    [InlineData("https://a.example/x/c.png", "/d.png", "https://a.example/d.png")]
    [InlineData("https://a.example/x/c.png", "d.png", "https://a.example/x/d.png")]
    [InlineData("https://a.example/c.png", "//b.example/d.png", "https://b.example/d.png")]
    [InlineData("https://a.example/c.png", "http://b.example/d.png", null)]
    [InlineData("https://a.example/c.png", "ftp://b.example/d.png", null)]
    public void ARedirectIsFollowedOnlyToHttps(string from, string location, string? expected)
    {
        var next = CoverReader.FollowedRedirect(new Uri(from), new Uri(location, UriKind.RelativeOrAbsolute));
        Assert.Equal(expected, next?.AbsoluteUri);
        Assert.Null(CoverReader.FollowedRedirect(new Uri(from), null));
    }

    [Fact]
    public async Task ANonHttpsUrlIsNeverRequested() =>
        Assert.Null(await CoverReader.FetchAsync("http://example.com/c.png", CancellationToken.None));

    [Fact]
    public async Task ABoundedReadStopsPastTheLimit()
    {
        using var small = new MemoryStream(new byte[10]);
        Assert.Equal(10, (await CoverReader.ReadBoundedAsync(small, 10, CancellationToken.None))!.Length);
        using var big = new MemoryStream(new byte[11]);
        Assert.Null(await CoverReader.ReadBoundedAsync(big, 10, CancellationToken.None));
    }
}
