using System.IO.Compression;
using System.Text;
using Md.Core.Export;
using static Md.Core.Tests.ZipFixtures;

namespace Md.Core.Tests;

/// <summary>
/// The STORED writer (macOS <c>EPUBZipWriter</c>) and the central-directory reader
/// (<c>ZipReader</c>): the byte layout the EPUB and TextPack exports depend on, and the
/// import path's defences — inflate-on-demand, the size cap, refusal of anything that is
/// not a zip it can parse.
/// </summary>
public class ZipTests
{
    // MARK: Writer

    [Fact]
    public void WriterProducesValidStoredArchive()
    {
        var zip = new ZipWriter();
        zip.Add("mimetype", Utf8("application/epub+zip"));
        zip.Add("OEBPS/a.txt", Utf8("hello"));
        var data = zip.Finish();
        // Local file header signature "PK\3\4" at byte 0.
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, data[..4]);
        // Compression method (bytes 8–9) is 0: stored, as EPUB requires of the mimetype.
        Assert.Equal(0, data[8]);
        Assert.Equal(0, data[9]);
        // The FIRST entry is the mimetype, its bytes verbatim right after the 30-byte
        // header and 8-byte name.
        Assert.Equal("mimetype", Utf8(data[30..38]));
        Assert.Equal("application/epub+zip", Utf8(data[38..58]));
        // End-of-central-directory record closes the archive.
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x05, 0x06 }, data[^22..^18]);
    }

    [Fact]
    public void WriterCrc32MatchesKnownVector()
    {
        // The canonical CRC-32 check value.
        Assert.Equal(0xCBF43926u, ZipWriter.Crc32(Utf8("123456789")));
        Assert.Equal(0u, ZipWriter.Crc32(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void WriterLayoutIsExactAndDeterministic()
    {
        // Two entries: 30-byte local headers, 46-byte central records, 22-byte end record,
        // no extra fields, no comment, no data descriptors — so the size is arithmetic.
        var data = Stored(("mimetype", Utf8("application/epub+zip")), ("OEBPS/a.txt", Utf8("hello")));
        var body = (30 + 8 + 20) + (30 + 11 + 5);
        var directory = (46 + 8) + (46 + 11);
        Assert.Equal(body + directory + 22, data.Length);

        // The fixed DOS time 0 and date 0x0021 (1980-01-01), bytes 10..14 of the local header.
        Assert.Equal(new byte[] { 0x00, 0x00, 0x21, 0x00 }, data[10..14]);
        // Central directory: record count twice, its length, and its offset (= body length).
        var end = data.Length - 22;
        Assert.Equal(2, BitConverter.ToUInt16(data, end + 8));
        Assert.Equal(2, BitConverter.ToUInt16(data, end + 10));
        Assert.Equal((uint)directory, BitConverter.ToUInt32(data, end + 12));
        Assert.Equal((uint)body, BitConverter.ToUInt32(data, end + 16));
        // The second central record points at the second local header.
        Assert.Equal((uint)(30 + 8 + 20), BitConverter.ToUInt32(data, body + 46 + 8 + 42));
        // Flag bit 11 (UTF-8 names) deliberately clear, as on every port.
        Assert.Equal(0, BitConverter.ToUInt16(data, 6));

        // A pure function of its payloads: the same input is the same archive.
        Assert.Equal(data, Stored(("mimetype", Utf8("application/epub+zip")), ("OEBPS/a.txt", Utf8("hello"))));
    }

    [Fact]
    public void WriterOutputOpensInTheBclReader()
    {
        // The unzip -t of this port: a foreign reader must accept the archive and hand back
        // every payload (System.IO.Compression validates the CRC when an entry is read).
        var data = Stored(("mimetype", Utf8("application/epub+zip")), ("sub/b.txt", Utf8("hello")));
        using var archive = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        Assert.Equal(new[] { "mimetype", "sub/b.txt" }, archive.Entries.Select(e => e.FullName).ToArray());
        using var reader = new StreamReader(archive.GetEntry("mimetype")!.Open(), Encoding.UTF8);
        Assert.Equal("application/epub+zip", reader.ReadToEnd());
        Assert.Equal(20, archive.GetEntry("mimetype")!.Length);
    }

    // MARK: Reader

    [Fact]
    public void ReaderParsesStoredArchive()
    {
        // A stored archive built by the app's own writer: the reader must walk its central
        // directory and return each payload, including a nested path.
        var a = new byte[] { 1, 2, 3, 4, 5 };
        var b = Utf8("hello");
        var entries = ZipReader.Entries(Stored(("a.bin", a), ("sub/b.txt", b)));
        Assert.NotNull(entries);
        Assert.Equal(2, entries.Count);
        Assert.Equal(a, Named(entries, "a.bin")?.Data);
        Assert.Equal(b, Named(entries, "sub/b.txt")?.Data);
    }

    [Fact]
    public void ReaderInflatesDeflatedEntry()
    {
        // Real TextPacks deflate their entries; prove the inflate path against a hand-built
        // method-8 archive (a stored-only reader would miss it).
        var payload = Utf8(string.Concat(Enumerable.Repeat("abcABC123 the quick brown fox ", 40)));
        var entries = ZipReader.Entries(Deflated("x/text.md", payload));
        Assert.NotNull(entries);
        Assert.Equal("x/text.md", entries[0].Name);
        Assert.Equal(payload, entries[0].Data);
    }

    [Fact]
    public void ReaderRejectsNonZip()
    {
        Assert.Null(ZipReader.Entries(Utf8("not a zip at all")));
        Assert.Null(ZipReader.Entries(Array.Empty<byte>()));
    }

    [Fact]
    public void ReaderInflatesOnlyTheEntriesTheCallerAsksFor()
    {
        // The memory-exhaustion defence: an entry the caller doesn't want is listed by name
        // but never inflated (nor allocated), so a hostile pack full of huge deflate-bomb
        // assets costs nothing to open — import reads only text.md.
        var archive = Deflated("assets/big.bin", new byte[50_000]);
        var inflated = ZipReader.Entries(archive);
        Assert.Equal(50_000, inflated?[0].Data.Length);

        var skipped = ZipReader.Entries(archive, _ => false);
        Assert.Equal("assets/big.bin", skipped?[0].Name);
        Assert.Equal(0, skipped?[0].Data.Length);

        // And end to end: a pack whose only inflated entry is text.md imports, while its
        // (skipped) assets are irrelevant to the outcome.
        var big = new byte[10_000];
        Array.Fill(big, (byte)0xFF);
        var pack = Stored(("Doc.textbundle/text.md", Utf8("# Real\n")), ("Doc.textbundle/assets/x.png", big));
        Assert.Equal("# Real\n", TextBundle.TextFromPack(pack)?.Text);
    }

    [Fact]
    public void ReaderDropsDirectoryEntries()
    {
        // A name ending in "/" is a folder marker with no payload and no local header
        // worth chasing — exactly what an empty assets/ in a pack looks like.
        var entries = ZipReader.Entries(Stored(("Doc.textbundle/assets/", Array.Empty<byte>()), ("Doc.textbundle/text.md", Utf8("x"))));
        Assert.NotNull(entries);
        Assert.Equal(new[] { "Doc.textbundle/text.md" }, entries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void ReaderRefusesAnOversizedClaimBeforeAllocating()
    {
        // The central directory's uncompressed size is attacker-controlled: one byte past
        // the 128 MiB cap is refused outright, whatever the stream would really inflate to.
        var tooBig = Deflated("text.md", Utf8("tiny"), claimedSize: (uint)ZipReader.MaxEntrySize + 1);
        Assert.Null(ZipReader.Entries(tooBig));
        // A stored entry of that size has no allocation to refuse, but its bytes must exist.
        Assert.Equal(128 * 1024 * 1024, ZipReader.MaxEntrySize);
    }

    [Fact]
    public void ReaderRejectsAStreamThatDisagreesWithItsDeclaredSize()
    {
        // The inflate is "exactly the declared size or nothing": a stream that stops short
        // is truncated; one that carries more than it declared is just as corrupt.
        var payload = Utf8(string.Concat(Enumerable.Repeat("0123456789", 10)));
        Assert.Null(ZipReader.Entries(Deflated("text.md", payload, claimedSize: 200)));
        Assert.Null(ZipReader.Entries(Deflated("text.md", payload, claimedSize: 50)));
        // A zero claim with a non-empty stream is the one declared size that skips the
        // decoder entirely (Swift's `guard expectedSize > 0 else { return Data() }`).
        var zero = ZipReader.Entries(Deflated("text.md", payload, claimedSize: 0));
        Assert.NotNull(zero);
        Assert.Empty(zero[0].Data);
    }

    [Fact]
    public void ReaderRejectsUnknownMethodsAndCorruptDirectories()
    {
        var payload = Utf8("# hi\n");
        // Method 12 (bzip2) is neither STORED nor DEFLATE.
        Assert.Null(ZipReader.Entries(Deflated("text.md", payload, method: 12)));

        // An end record whose central-directory offset points past the buffer.
        var broken = Stored(("text.md", payload));
        BitConverter.GetBytes((uint)broken.Length).CopyTo(broken, broken.Length - 22 + 16);
        Assert.Null(ZipReader.Entries(broken));

        // A truncated archive has lost its end record.
        var whole = Stored(("text.md", payload));
        Assert.Null(ZipReader.Entries(whole[..^1]));
    }

    [Fact]
    public void ReaderReadsAnArchiveWrittenByTheBcl()
    {
        // A pack another tool wrote: DEFLATE, a directory entry, CRC and sizes wherever
        // that writer chose to put them — the central directory is what is trusted.
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            archive.CreateEntry("Doc.textbundle/assets/");
            var text = archive.CreateEntry("Doc.textbundle/text.md", CompressionLevel.Optimal);
            using var writer = text.Open();
            writer.Write(Utf8(string.Concat(Enumerable.Repeat("# Deflated\n\nBody text that compresses.\n", 8))));
        }
        var entries = ZipReader.Entries(stream.ToArray());
        Assert.NotNull(entries);
        Assert.Equal(new[] { "Doc.textbundle/text.md" }, entries.Select(e => e.Name).ToArray());
        Assert.StartsWith("# Deflated\n\nBody", Utf8(entries[0].Data), StringComparison.Ordinal);
        Assert.Equal(string.Concat(Enumerable.Repeat("# Deflated\n\nBody text that compresses.\n", 8)),
            TextBundle.TextFromPack(stream.ToArray())?.Text);
    }

    [Fact]
    public void ReaderDecodesNamesAsUtf8()
    {
        // Names are UTF-8 on every port regardless of flag bit 11; a Cyrillic bundle folder
        // round-trips through the writer and back.
        var entries = ZipReader.Entries(Stored(("Документ.textbundle/text.md", Utf8("# Привет\n"))));
        Assert.Equal("Документ.textbundle/text.md", entries?[0].Name);
        Assert.Equal("# Привет\n", TextBundle.TextFromPack(Stored(("Документ.textbundle/text.md", Utf8("# Привет\n"))))?.Text);
    }

    [Fact]
    public void EntryEqualityIsStructural()
    {
        // A plain record would compare the byte[] by reference and every payload assertion
        // above would be meaningless.
        Assert.Equal(new ZipEntry("a", new byte[] { 1, 2 }), new ZipEntry("a", new byte[] { 1, 2 }));
        Assert.NotEqual(new ZipEntry("a", new byte[] { 1, 2 }), new ZipEntry("a", new byte[] { 1, 3 }));
        Assert.NotEqual(new ZipEntry("a", new byte[] { 1, 2 }), new ZipEntry("A", new byte[] { 1, 2 }));
    }
}
