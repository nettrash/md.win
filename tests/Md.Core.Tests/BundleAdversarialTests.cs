using System.IO.Compression;
using Md.Core.Document;
using Md.Core.Export;
using static Md.Core.Tests.ZipFixtures;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial inputs for the TextBundle / zip / codec module. Every expected value here
/// was derived from the macOS behaviour (NSRegularExpression ranges, Foundation's
/// String(data:encoding:) and data(using:), EPUBZipWriter / ZipReader), measured with a
/// Swift probe on this Mac — never from the C# output — so a passing test is parity, not
/// self-agreement.
/// </summary>
public class BundleAdversarialTests
{
    private const string Mark = "\u0301";            // COMBINING ACUTE ACCENT
    private static readonly string Frak = char.ConvertFromUtf32(0x1D504);   // 𝔄, two UTF-16 units
    private static readonly string FrakB = char.ConvertFromUtf32(0x1D505);  // 𝔅
    private static readonly string Grin = char.ConvertFromUtf32(0x1F600);   // 😀

    [Fact]
    public void CombiningMarksOnDelimitersAreTheirOwnUnits()
    {
        // ICU (measured): `![a](pic.png)` + U+0301 matches with the URL at range {5, 7};
        // the mark after ')' is outside the match and survives the splice untouched.
        var after = TextBundle.ExportRewriting("![a](pic.png)" + Mark, _ => new byte[] { 1 });
        Assert.Equal("![a](assets/pic.png)" + Mark, after.Text);
        Assert.Equal(new[] { "pic.png" }, after.Assets.Select(a => a.Name).ToArray());

        // A mark right after '[' is alt text ({6, 5} for the URL); the ref still rewrites.
        Assert.Equal("![" + Mark + "a](assets/x.png)", TextBundle.ExportRewriting("![" + Mark + "a](x.png)", _ => new byte[] { 1 }).Text);

        // A mark right after '(' is the FIRST unit of the destination (ICU range {5, 8}):
        // the loader is asked for the marked path and the asset is named by it verbatim.
        string? asked = null;
        var lead = TextBundle.ExportRewriting("![a](" + Mark + "pic.png)", p => { asked = p; return new byte[] { 1 }; });
        Assert.Equal(Mark + "pic.png", asked);
        Assert.Equal("![a](assets/" + Mark + "pic.png)", lead.Text);
        Assert.Equal(Mark + "pic.png", lead.Assets[0].Name);

        // Classification is scalar-exact around the delimiters (Swift ScalarText):
        // ':' + mark + "//" is not a scheme; "://" anywhere is; a bare ':' is local.
        Assert.True(TextBundle.IsLocalRelativeReference("weird:" + Mark + "//name.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("a/b://c.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("://x.png"));
        Assert.True(TextBundle.IsLocalRelativeReference("x:y.png"));
        Assert.True(TextBundle.IsLocalRelativeReference("./photo.png"));
        Assert.True(TextBundle.IsLocalRelativeReference("../photo.png"));
        Assert.True(TextBundle.IsLocalRelativeReference("mailto"));
        Assert.False(TextBundle.IsLocalRelativeReference("data:"));
        // Swift's lowercased() maps the Kelvin sign to 'k' but no letter of these schemes
        // is a k; nothing non-ASCII can ever spell them, so U+212A in a scheme stays local.
        Assert.True(TextBundle.IsLocalRelativeReference("dat\u212A:x.png"));

        // Zip names are bytes: a decomposed name is not normalised on the way through.
        var name = "Docu" + Mark + "ment.textbundle/text.md";
        var entries = ZipReader.Entries(Stored((name, Utf8("x"))));
        Assert.Equal(name, entries?[0].Name);
        Assert.Equal("x", TextBundle.TextFromPack(Stored((name, Utf8("x"))))?.Text);
    }

    [Fact]
    public void SupplementaryPlaneCharactersKeepUtf16OffsetsAndScalarSplits()
    {
        // ICU (measured): in "𝔄𝔄 ![😀](𝔄/𝔅.png) 𝔅" the URL is NSRange {11, 9} — UTF-16
        // units, which is exactly what .NET's Regex reports — and the splice must land on
        // those units so the astral text on both sides is untouched.
        var source = Frak + Frak + " ![" + Grin + "](" + Frak + "/" + FrakB + ".png) " + FrakB;
        var result = TextBundle.ExportRewriting(source, _ => new byte[] { 1 });
        Assert.Equal(Frak + Frak + " ![" + Grin + "](assets/" + FrakB + ".png) " + FrakB, result.Text);
        Assert.Equal(FrakB + ".png", result.Assets[0].Name);

        // disambiguated() splits at the last '.' with a SCALAR index > 0: 𝔅.png has the
        // dot at scalar 1 (UTF-16 unit 2), so the stem is 𝔅 and the twin is 𝔅-2.png.
        var twins = TextBundle.ExportRewriting("![a](x/" + FrakB + ".png) ![b](y/" + FrakB + ".png)", _ => new byte[] { 1 });
        Assert.Equal(new[] { FrakB + ".png", FrakB + "-2.png" }, twins.Assets.Select(a => a.Name).ToArray());
        // An extensionless astral name (no dot at all) gets the counter appended whole.
        var extless = TextBundle.ExportRewriting("![a](x/" + Grin + ") ![b](y/" + Grin + ")", _ => new byte[] { 1 });
        Assert.Equal(new[] { Grin, Grin + "-2" }, extless.Assets.Select(a => a.Name).ToArray());

        // Codec: a surrogate pair behind a UTF-16 BOM decodes to one scalar and re-encodes
        // to the same six bytes (Foundation: BOM + host LE order); an astral scalar cannot
        // live in CP1251 or Latin-1, so both upgrade to UTF-8 and say so.
        var laugh = char.ConvertFromUtf32(0x1F602);
        var utf16 = new byte[] { 0xFF, 0xFE, 0x3D, 0xD8, 0x02, 0xDE };
        var decoded = PlainTextCodec.Decode(utf16);
        Assert.Equal(new DecodedText(laugh, TextEncoding.Utf16), decoded);
        Assert.Equal(utf16, PlainTextCodec.Encode(laugh, TextEncoding.Utf16).Data);
        Assert.Equal(new DecodedText(laugh, TextEncoding.Utf8), PlainTextCodec.Decode(new byte[] { 0xF0, 0x9F, 0x98, 0x82 }));
        Assert.Equal(new EncodedText(new byte[] { 0xF0, 0x9F, 0x98, 0x82 }, TextEncoding.Utf8), PlainTextCodec.Encode(laugh, TextEncoding.WindowsCP1251));
        Assert.Equal(TextEncoding.Utf8, PlainTextCodec.Encode(laugh, TextEncoding.IsoLatin1).Encoding);

        // Zip names are UTF-8 on the wire, so an astral folder name round-trips.
        var pack = TextBundle.BundleWrapper("# " + Grin + "\n", Array.Empty<TextBundle.Asset>()).ToTextPack(Frak);
        Assert.Equal(Frak + ".textbundle/text.md", ZipReader.Entries(pack)?.Select(e => e.Name).ElementAt(1));
        Assert.Equal("# " + Grin + "\n", TextBundle.TextFromPack(pack)?.Text);
    }

    [Fact]
    public void CrlfIsPreservedEverywhereAndSeparatesATitle()
    {
        // CR and LF are both ICU whitespace: a destination stops at either, and the
        // rewrite keeps every CRLF byte-exact between the edits.
        var source = "![a](p.png)\r\n![b](q.png \"t\")\r\n";
        Assert.Equal("![a](assets/p.png)\r\n![b](assets/q.png \"t\")\r\n", TextBundle.ExportRewriting(source, _ => new byte[] { 1 }).Text);
        // A CRLF (and, measured, a U+2028) between destination and title is a `\s+` run.
        Assert.Equal("![a](assets/p.png\r\n\"t\")", TextBundle.ExportRewriting("![a](p.png\r\n\"t\")", _ => new byte[] { 1 }).Text);
        Assert.Equal("![a](assets/p.png\u2028\"t\")", TextBundle.ExportRewriting("![a](p.png\u2028\"t\")", _ => new byte[] { 1 }).Text);
        // A destination cannot span a line: the ref stays exactly as written, no asset.
        var broken = TextBundle.ExportRewriting("![a](p\r\n.png)", _ => new byte[] { 1 });
        Assert.Equal("![a](p\r\n.png)", broken.Text);
        Assert.Empty(broken.Assets);

        // Foundation never normalises line endings (measured "U+0041 U+000D U+000A U+0042"),
        // in any of the four encodings, on either side of the round trip.
        Assert.Equal("A\r\nB", PlainTextCodec.Decode(new byte[] { 0x41, 0x0D, 0x0A, 0x42 })?.Text);
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A, 0x42 }, PlainTextCodec.Encode("A\r\nB", TextEncoding.Utf8).Data);
        var cyr = Cp1251("Привет\r\nмир");
        var cp = PlainTextCodec.Decode(cyr);
        Assert.Equal(new DecodedText("Привет\r\nмир", TextEncoding.WindowsCP1251), cp);
        Assert.Equal(cyr, PlainTextCodec.Encode(cp!.Text, cp.Encoding).Data);
        var utf16 = new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x0D, 0x00, 0x0A, 0x00, 0x42, 0x00 };
        Assert.Equal(new DecodedText("A\r\nB", TextEncoding.Utf16), PlainTextCodec.Decode(utf16));
        Assert.Equal(utf16, PlainTextCodec.Encode("A\r\nB", TextEncoding.Utf16).Data);
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A, 0x42 }, PlainTextCodec.Encode("A\r\nB", TextEncoding.IsoLatin1).Data);
        // text.md carries the author's line endings verbatim.
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A, 0x42 }, TextBundle.BundleWrapper("A\r\nB", Array.Empty<TextBundle.Asset>()).TextBytes);
    }

    [Fact]
    public void EmptyInputsHaveTheSwiftShape()
    {
        // Nothing to rewrite: the source comes back as is and the loader is never asked.
        var calls = 0;
        var nothing = TextBundle.ExportRewriting("", _ => { calls++; return new byte[] { 1 }; });
        Assert.Equal("", nothing.Text);
        Assert.Empty(nothing.Assets);
        Assert.Equal(0, calls);

        // EPUBZipWriter().finish() with no entries is exactly the 22-byte end record.
        var empty = new ZipWriter().Finish();
        Assert.Equal(22, empty.Length);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x05, 0x06 }, empty[..4]);
        Assert.All(empty[4..], b => Assert.Equal(0, b));
        // ZipReader.entries finds that record at index 0 and returns [] — not nil.
        var entries = ZipReader.Entries(empty);
        Assert.NotNull(entries);
        Assert.Empty(entries);
        // …while textFromPack of it is nil (no text file), and a 21-byte tail is not a zip.
        Assert.Null(TextBundle.TextFromPack(empty));
        Assert.Null(ZipReader.Entries(empty[..21]));
        Assert.False(TextBundle.LooksLikePack("", ReadOnlySpan<byte>.Empty));

        // An empty document exports as an empty text.md — and, faithfully to Swift's
        // `!data.isEmpty` guard, that pack does NOT import (nil), while the same bundle
        // as a folder imports as "" (FileWrapper hands back empty Data, the codec decodes it).
        var wrapper = TextBundle.BundleWrapper("", Array.Empty<TextBundle.Asset>());
        Assert.Empty(wrapper.TextBytes);
        Assert.Equal(3, wrapper.PackEntries("Doc").Count);
        Assert.Null(TextBundle.TextFromPack(wrapper.ToTextPack("Doc")));
        Assert.Equal(new DecodedText("", TextEncoding.Utf8), TextBundle.TextFromBundle(new Dictionary<string, byte[]> { ["text.md"] = Array.Empty<byte>() }));

        // Codec edges measured on Foundation: FF FE alone is "" as UTF-16; EF BB BF alone
        // is "" as UTF-8; the empty string encodes to nothing in every single-byte encoding
        // and to a bare BOM in UTF-16, each reporting the preferred encoding unchanged.
        Assert.Equal(new DecodedText("", TextEncoding.Utf16), PlainTextCodec.Decode(new byte[] { 0xFF, 0xFE }));
        Assert.Equal(new DecodedText("", TextEncoding.Utf16), PlainTextCodec.Decode(new byte[] { 0xFE, 0xFF }));
        Assert.Equal(new DecodedText("", TextEncoding.Utf8), PlainTextCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.Equal(new EncodedText(Array.Empty<byte>(), TextEncoding.Utf8), PlainTextCodec.Encode("", TextEncoding.Utf8));
        Assert.Equal(new EncodedText(Array.Empty<byte>(), TextEncoding.WindowsCP1251), PlainTextCodec.Encode("", TextEncoding.WindowsCP1251));
        Assert.Equal(new EncodedText(Array.Empty<byte>(), TextEncoding.IsoLatin1), PlainTextCodec.Encode("", TextEncoding.IsoLatin1));
        Assert.Equal(new EncodedText(new byte[] { 0xFF, 0xFE }, TextEncoding.Utf16), PlainTextCodec.Encode("", TextEncoding.Utf16));
        // (Those comparisons mean something only because EncodedText compares its bytes
        // structurally, like ZipEntry and Asset — a plain record would compare references.)
        Assert.Equal(new EncodedText(new byte[] { 1 }, TextEncoding.Utf8), new EncodedText(new byte[] { 1 }, TextEncoding.Utf8));
        Assert.NotEqual(new EncodedText(new byte[] { 1 }, TextEncoding.Utf8), new EncodedText(new byte[] { 1 }, TextEncoding.IsoLatin1));
        Assert.NotEqual(new EncodedText(new byte[] { 1 }, TextEncoding.Utf8), new EncodedText(new byte[] { 2 }, TextEncoding.Utf8));
        // Foundation strips exactly ONE UTF-8 BOM (measured: EF BB BF EF BB BF 41 → U+FEFF A).
        Assert.Equal("\uFEFFA", PlainTextCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0xEF, 0xBB, 0xBF, 0x41 })?.Text);
        // …and keeps a second UTF-16 BOM in the body (measured: A U+FEFF B).
        Assert.Equal("A\uFEFFB", PlainTextCodec.Decode(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0xFF, 0xFE, 0x42, 0x00 })?.Text);
    }

    [Fact]
    public void PathologicalArchivesAreReadOrRefusedLikeTheSwift()
    {
        var payload = Utf8("# comment\n");

        // 1. A zip comment after the end record — up to 65535 bytes, the Swift scans back
        //    exactly that far — must not hide the record; one byte further and it does.
        foreach (var commentLength in new[] { 1, 0xFFFF })
        {
            var commented = WithComment(Stored(("Doc.textbundle/text.md", payload)), commentLength);
            Assert.Equal("# comment\n", TextBundle.TextFromPack(commented)?.Text);
        }
        Assert.Null(ZipReader.Entries(WithComment(Stored(("Doc.textbundle/text.md", payload)), 0x10000)));

        // 2. The LOCAL header carries an extra field the central record lacks: the data
        //    offset must come from the local lengths (Swift reads u16 at local+26/+28).
        var withExtra = WithLocalExtraField(Stored(("text.md", payload)), new byte[] { 0x55, 0x54, 0x00, 0x00 });
        var entries = ZipReader.Entries(withExtra);
        Assert.NotNull(entries);
        Assert.Equal(payload, entries[0].Data);
        // The BCL agrees the archive is well formed.
        using (var archive = new ZipArchive(new MemoryStream(withExtra), ZipArchiveMode.Read))
        {
            using var reader = new StreamReader(archive.GetEntry("text.md")!.Open());
            Assert.Equal("# comment\n", reader.ReadToEnd());
        }

        // 3. The central directory's count is authoritative: fewer records than entries
        //    reads that many; more than exist runs off the directory and is refused.
        var two = Stored(("a.txt", Utf8("a")), ("b.txt", Utf8("b")));
        var one = (byte[])two.Clone();
        one[^12] = 1; one[^14] = 1;                        // both u16 counts → 1
        Assert.Equal(new[] { "a.txt" }, ZipReader.Entries(one)?.Select(e => e.Name).ToArray());
        var three = (byte[])two.Clone();
        three[^12] = 3; three[^14] = 3;
        Assert.Null(ZipReader.Entries(three));

        // 4. A hostile size claim: the full u32 (4 GiB) is refused without an allocation
        //    attempt or an exception, whatever the stream holds.
        Assert.Null(ZipReader.Entries(Deflated("text.md", payload, claimedSize: 0xFFFFFFFF)));
        Assert.Null(TextBundle.TextFromPack(Deflated("Doc.textbundle/text.md", payload, claimedSize: 0xFFFFFFFF)));

        // 5. The cap is a boundary, not a fig leaf: a stream that GENUINELY inflates to
        //    128 MiB + 1 zero bytes, declared honestly, is refused (only the cap can refuse
        //    it — the stream itself is consistent), while exactly 128 MiB is accepted.
        var overCompressed = DeflateZeros((long)ZipReader.MaxEntrySize + 1);
        Assert.Null(ZipReader.Entries(DeflatedPrecompressed("text.md", overCompressed, (uint)ZipReader.MaxEntrySize + 1)));
        var atCapCompressed = DeflateZeros(ZipReader.MaxEntrySize);
        var atCap = ZipReader.Entries(DeflatedPrecompressed("text.md", atCapCompressed, (uint)ZipReader.MaxEntrySize));
        Assert.Equal(ZipReader.MaxEntrySize, atCap?[0].Data.Length);

        // 6. A name that runs past the end of the buffer, and a local header whose
        //    signature is wrong, are each refused rather than guessed.
        var longName = Stored(("text.md", payload));
        longName[longName.Length - 22 - 7 - 46 + 28] = 0xFF;   // central nameLength 7 → 255, past the buffer
        Assert.Null(ZipReader.Entries(longName));
        var badLocal = Stored(("text.md", payload));
        badLocal[0] = 0x51;
        Assert.Null(ZipReader.Entries(badLocal));
    }

    // MARK: helpers

    /// <summary>Append a zip comment of <paramref name="length"/> bytes and record its length in the end record.</summary>
    private static byte[] WithComment(byte[] archive, int length)
    {
        var output = new byte[archive.Length + length];
        archive.CopyTo(output, 0);
        output[archive.Length - 2] = (byte)(length & 0xFF);
        output[archive.Length - 1] = (byte)((length >> 8) & 0xFF);
        Array.Fill(output, (byte)'#', archive.Length, length);
        return output;
    }

    /// <summary>
    /// Insert an extra field into the LOCAL header of a one-entry stored archive (leaving the
    /// central record's extra length 0) and shift the end record's directory offset.
    /// </summary>
    private static byte[] WithLocalExtraField(byte[] archive, byte[] extra)
    {
        var nameLength = BitConverter.ToUInt16(archive, 26);
        var insertAt = 30 + nameLength;
        var output = new byte[archive.Length + extra.Length];
        Array.Copy(archive, 0, output, 0, insertAt);
        extra.CopyTo(output, insertAt);
        Array.Copy(archive, insertAt, output, insertAt + extra.Length, archive.Length - insertAt);
        output[28] = (byte)(extra.Length & 0xFF);
        output[29] = (byte)((extra.Length >> 8) & 0xFF);
        var end = output.Length - 22;
        var centralOffset = BitConverter.ToUInt32(output, end + 16) + (uint)extra.Length;
        BitConverter.GetBytes(centralOffset).CopyTo(output, end + 16);
        return output;
    }

    /// <summary>Raw DEFLATE of <paramref name="count"/> zero bytes, streamed so the test never holds the plaintext.</summary>
    private static byte[] DeflateZeros(long count)
    {
        using var output = new MemoryStream();
        using (var deflater = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            var chunk = new byte[1 << 20];
            for (var remaining = count; remaining > 0; remaining -= chunk.Length)
            {
                deflater.Write(chunk, 0, (int)Math.Min(chunk.Length, remaining));
            }
        }
        return output.ToArray();
    }
}
