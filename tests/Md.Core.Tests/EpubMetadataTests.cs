using System.Text;
using Md.Core.Export;
using Md.Core.Markdown;
using static Md.Core.Tests.ZipFixtures;

namespace Md.Core.Tests;

/// <summary>
/// EPUB metadata and cover from front matter (1.6, #2). These are the family's vectors — md.vscode's
/// <c>test/epub-metadata.test.ts</c>, and the same ones md for iPhone, Mac and Android carry — so the
/// five package documents agree on which keys count, which values are accepted, and the exact
/// lines they become.
/// </summary>
public class EpubMetadataTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16];
    private static readonly byte[] Gif = Encoding.Latin1.GetBytes("GIF89a\x01\x00");
    private static readonly byte[] Webp = Encoding.Latin1.GetBytes("RIFF\x24\x00\x00\x00WEBPVP8 ");

    private static IReadOnlyList<MetadataField> Fields(string matter) =>
        MarkdownParser.FrontMatter("---\n" + matter + "\n---\n\n# Body\n");

    private static IReadOnlyList<string> Lines(string matter, EpubCover? cover = null) =>
        EpubMetadataReader.Lines(EpubMetadataReader.FromFrontMatter(Fields(matter)), cover);

    // MARK: - Metadata lines

    [Fact]
    public void NoFrontMatterIsTheOneLanguageLine()
    {
        Assert.Equal(["<dc:language>en</dc:language>"], EpubMetadataReader.Lines(EpubMetadataReader.FromFrontMatter([]), null));
        Assert.Equal(["<dc:language>en</dc:language>"], EpubMetadataReader.Lines(EpubMetadata.Default, null));
        Assert.Equal(["<dc:language>en</dc:language>"], Lines("title: Only a title"));
    }

    [Fact]
    public void WritesEveryKeyInTheFamilyOrder()
    {
        var lines = Lines(string.Join("\n",
            "title: The Book",
            "author: Ada Lovelace",
            "language: ru",
            "date: 2026-10-10",
            "description: Notes on the \"Engine\" & more",
            "series: Analytical",
            "series_index: 2"), EpubMetadataReader.Recognise(Png));
        Assert.Equal(
        [
            "<dc:creator>Ada Lovelace</dc:creator>",
            "<dc:language>ru</dc:language>",
            "<dc:date>2026-10-10</dc:date>",
            "<dc:description>Notes on the &quot;Engine&quot; &amp; more</dc:description>",
            "<meta property=\"belongs-to-collection\" id=\"series\">Analytical</meta>",
            "<meta refines=\"#series\" property=\"collection-type\">series</meta>",
            "<meta refines=\"#series\" property=\"group-position\">2</meta>",
            "<meta name=\"cover\" content=\"cover-image\"/>",
        ], lines);
    }

    [Fact]
    public void MatchesKeysCaseInsensitivelyFirstNonEmptyWinsQuotesStripped()
    {
        Assert.Equal(
            ["<dc:creator>Grace Hopper</dc:creator>", "<dc:language>en</dc:language>"],
            Lines("Author:\nAUTHOR: \"Grace Hopper\"\nauthor: Someone Else"));
    }

    [Theory]
    [InlineData("en", "en")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("zh-Hant-TW", "zh-Hant-TW")]
    [InlineData("ast", "ast")]
    [InlineData("English", "en")]
    [InlineData("e", "en")]
    [InlineData("en_US", "en")]
    [InlineData("en-", "en")]
    [InlineData("ру", "en")]
    [InlineData("en-toolongsubtag", "en")]
    public void TakesALanguageTagElseEn(string value, string expected) =>
        Assert.Equal(expected, EpubMetadataReader.FromFrontMatter(Fields("language: " + value)).Language);

    [Theory]
    [InlineData("2026")]
    [InlineData("2026-10")]
    [InlineData("2026-10-10")]
    [InlineData("2026-10-10T09:30Z")]
    [InlineData("2026-10-10T09:30:15+03:00")]
    [InlineData("2024-02-29")]
    [InlineData("2000-02-29")]
    [InlineData("2026-12-31T23:59:59-11:30")]
    public void KeepsAW3CDate(string value) =>
        Assert.Equal(value, EpubMetadataReader.FromFrontMatter(Fields("date: " + value)).Date);

    [Theory]
    [InlineData("10/10/2026")]
    [InlineData("October 2026")]
    [InlineData("2026-10-10 09:30")]
    [InlineData("2026-10-10T09:30")]
    [InlineData("26-10-10")]
    [InlineData("\uFF12\uFF10\uFF12\uFF16")]
    [InlineData("\u0662\u0660\u0662\u0666-10-10")]      // Arabic-Indic digits: \d would take them
    // Ranges, after the shape matches.
    [InlineData("2026-13")]
    [InlineData("2026-00")]
    [InlineData("2026-10-00")]
    [InlineData("2026-02-29")]
    [InlineData("1900-02-29")]
    [InlineData("2026-04-31")]
    [InlineData("2026-10-10T24:00Z")]
    [InlineData("2026-10-10T23:60Z")]
    [InlineData("2026-10-10T23:59:60Z")]
    [InlineData("2026-10-10T10:00+24:00")]
    [InlineData("2026-10-10T10:00+05:60")]
    public void LeavesAnythingElseOut(string value) =>
        Assert.Null(EpubMetadataReader.FromFrontMatter(Fields("date: " + value)).Date);

    [Fact]
    public void DateRegexDoesNotMatchBeforeATrailingNewline()
    {
        // .NET's $ matches before a final \n; the pattern ends in \z so the ports agree.
        Assert.False(EpubMetadataReader.IsW3CDate("2026-10-10\n"));
    }

    [Fact]
    public void GivesASeriesItsPositionOnlyForANumberAndIgnoresAnIndexWithoutASeries()
    {
        Assert.Contains("<meta refines=\"#series\" property=\"group-position\">2.5</meta>", Lines("series: S\nseries_index: 2.5"));
        Assert.Equal(3, Lines("series: S\nseries_index: two").Count);
        Assert.Equal(["<dc:language>en</dc:language>"], Lines("series_index: 3"));
    }

    [Fact]
    public void EscapesAsTheRestOfThePackageDocumentApostropheLeftAlone() =>
        Assert.Equal("<dc:creator>O'Brien &lt;ob@example.com&gt;</dc:creator>", Lines("author: O'Brien <ob@example.com>")[0]);

    // MARK: - The cover reference

    [Fact]
    public void DecodesABase64DataImageAndRefusesAnyOtherData()
    {
        var b64 = Convert.ToBase64String(Png);
        Assert.Equal(new CoverReference.Inline(Png), EpubMetadataReader.Reference("data:image/png;base64," + b64));
        Assert.Equal(new CoverReference.Inline(Png), EpubMetadataReader.Reference("DATA:image/png;BASE64," + b64));
        Assert.Null(EpubMetadataReader.Reference("data:image/svg+xml,<svg/>"));
        Assert.Null(EpubMetadataReader.Reference("data:image/png;base64,"));
        Assert.Null(EpubMetadataReader.Reference("data:image/png;base64,abc"));
        Assert.Null(EpubMetadataReader.Reference("data:image/png;base64,ab!d"));
    }

    [Fact]
    public void FetchesHttpsOnly()
    {
        Assert.Equal(new CoverReference.Https("https://example.com/c.png"), EpubMetadataReader.Reference("https://example.com/c.png"));
        Assert.Equal(new CoverReference.Https("HTTPS://example.com/c.png"), EpubMetadataReader.Reference("HTTPS://example.com/c.png"));
        Assert.Null(EpubMetadataReader.Reference("http://example.com/c.png"));
        Assert.Null(EpubMetadataReader.Reference("ftp://example.com/c.png"));
        Assert.Null(EpubMetadataReader.Reference("file:///tmp/c.png"));
    }

    [Fact]
    public void ReadsARelativePathFromBesideTheDocumentAndNothingAbsolute()
    {
        Assert.Equal(new CoverReference.Local("cover.png"), EpubMetadataReader.Reference("cover.png"));
        Assert.Equal(new CoverReference.Local("images/cover.jpg"), EpubMetadataReader.Reference("images/cover.jpg"));
        Assert.Equal(new CoverReference.Local("../cover.png"), EpubMetadataReader.Reference("../cover.png"));
        foreach (var value in new[] { "/tmp/cover.png", "\\\\server\\c.png", "C:\\c.png", "c:/c.png", "#cover", "mailto:x", "" })
        {
            Assert.Null(EpubMetadataReader.Reference(value));
        }
        Assert.Null(EpubMetadataReader.Reference(null));
    }

    // MARK: - Recognising a cover by its bytes

    [Fact]
    public void KnowsPngJpegGifAndWebp()
    {
        Assert.Equal(("png", "image/png"), Kind(EpubMetadataReader.Recognise(Png)));
        Assert.Equal(("jpg", "image/jpeg"), Kind(EpubMetadataReader.Recognise(Jpeg)));
        Assert.Equal(("gif", "image/gif"), Kind(EpubMetadataReader.Recognise(Gif)));
        Assert.Equal(("webp", "image/webp"), Kind(EpubMetadataReader.Recognise(Webp)));

        static (string, string) Kind(EpubCover? cover) => (cover!.Extension, cover.MediaType);
    }

    [Fact]
    public void SkipsAnythingElseAnEmptyFileAndAnOversizedOne()
    {
        Assert.Null(EpubMetadataReader.Recognise(Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"/>")));
        Assert.Null(EpubMetadataReader.Recognise(Encoding.Latin1.GetBytes("RIFF\x24\x00\x00\x00WAVE")));
        Assert.Null(EpubMetadataReader.Recognise([0x89, 0x50, 0x4E, 0x47]));
        Assert.Null(EpubMetadataReader.Recognise([]));
        Assert.Null(EpubMetadataReader.Recognise(null));
        var big = new byte[EpubMetadataReader.MaxCoverBytes + 1];
        Png.CopyTo(big, 0);
        Assert.Null(EpubMetadataReader.Recognise(big));
    }

    // MARK: - The package document

    private static readonly string[] Units = ["content.xhtml"];
    private static readonly string[] Images = ["images/unit-000-rich-0.png"];

    [Fact]
    public void IsByteIdenticalTo15WithoutFrontMatter()
    {
        var expected = string.Join("\n",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">",
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">",
            "<dc:identifier id=\"bookid\">urn:uuid:x</dc:identifier>",
            "<dc:title>T</dc:title>",
            "<dc:language>en</dc:language>",
            "<meta property=\"dcterms:modified\">2026-10-10T00:00:00Z</meta>",
            "</metadata>",
            "<manifest>",
            "<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>",
            "<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>",
            "<item id=\"u0\" href=\"content.xhtml\" media-type=\"application/xhtml+xml\"/>",
            "<item id=\"i0\" href=\"images/unit-000-rich-0.png\" media-type=\"image/png\"/>",
            "</manifest>",
            "<spine>",
            "<itemref idref=\"u0\"/>",
            "</spine>",
            "</package>");
        Assert.Equal(expected, EpubExport.Opf("T", "urn:uuid:x", "2026-10-10T00:00:00Z", Units, Images));
        Assert.Equal(expected, EpubExport.Opf("T", "urn:uuid:x", "2026-10-10T00:00:00Z", Units, Images, null, EpubMetadata.Default, null));
    }

    [Fact]
    public void ListsTheCoverLastInTheManifestAndKeepsItOutOfTheSpine()
    {
        var opf = EpubExport.Opf("T", "urn:uuid:x", "2026-10-10T00:00:00Z", Units, Images, null,
            EpubMetadata.Default, EpubMetadataReader.Recognise(Jpeg));
        Assert.Contains(
            "<item id=\"i0\" href=\"images/unit-000-rich-0.png\" media-type=\"image/png\"/>\n"
            + "<item id=\"cover-image\" href=\"images/cover.jpg\" media-type=\"image/jpeg\" properties=\"cover-image\"/>\n</manifest>",
            opf, StringComparison.Ordinal);
        Assert.Contains("<dc:language>en</dc:language>\n<meta name=\"cover\" content=\"cover-image\"/>\n<meta property=\"dcterms:modified\">",
            opf, StringComparison.Ordinal);
        Assert.DoesNotContain("<itemref idref=\"cover-image\"/>", opf, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanDocumentReadsTheFrontMatterAndABookKeepsTheDefaults()
    {
        var source = "---\nauthor: A\nlanguage: de\ncover: images/c.png\n---\n\n# Body\n";
        var plan = EpubExport.PlanDocument(source, "T");
        Assert.Equal("A", plan.Metadata!.Creator);
        Assert.Equal("de", plan.Metadata.Language);
        Assert.Equal(new CoverReference.Local("images/c.png"), plan.Cover);

        var plain = EpubExport.PlanDocument("# Body\n", "T");
        Assert.Equal(EpubMetadata.Default, plain.Metadata);
        Assert.Null(plain.Cover);
    }

    [Fact]
    public void PacksTheCoverAfterTheSnapshotsWithTheMetadataInTheOpf()
    {
        var source = "---\nauthor: A\nlanguage: de\n---\n\n# Body\n";
        var epub = EpubExport.BuildDocument(source, "T", modified: "2026-10-10T00:00:00Z", cover: EpubMetadataReader.Recognise(Png));
        var entries = ZipReader.Entries(epub);
        Assert.NotNull(entries);
        Assert.Equal("OEBPS/images/cover.png", entries![^1].Name);
        Assert.Equal(Png, entries[^1].Data);
        var opf = Encoding.UTF8.GetString(Named(entries, "OEBPS/content.opf")!.Data);
        Assert.Contains("<dc:creator>A</dc:creator>\n<dc:language>de</dc:language>\n<meta name=\"cover\" content=\"cover-image\"/>",
            opf, StringComparison.Ordinal);
    }

    [Fact]
    public void NoCoverWritesNoCoverEntry()
    {
        var epub = EpubExport.BuildDocument("# Body\n", "T", modified: "2026-10-10T00:00:00Z");
        Assert.DoesNotContain(ZipReader.Entries(epub)!, entry => entry.Name.Contains("cover", StringComparison.Ordinal));
    }
}
