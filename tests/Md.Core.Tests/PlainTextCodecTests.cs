using System.Text;
using Md.Core.Document;
using static Md.Core.Tests.ZipFixtures;

namespace Md.Core.Tests;

/// <summary>
/// The plain-text codec shared by documents, the book editor and TextBundle import
/// (macOS "Plain-text codec" tests plus the Kotlin TextCodecTest cases and the measured
/// Foundation behaviours the port pins).
/// </summary>
public class PlainTextCodecTests
{
    [Fact]
    public void DecodesUtf8()
    {
        var decoded = PlainTextCodec.Decode(Utf8("# Привет\n"));
        Assert.Equal("# Привет\n", decoded?.Text);
        Assert.Equal(TextEncoding.Utf8, decoded?.Encoding);
        // The empty file is UTF-8 too.
        Assert.Equal(new DecodedText("", TextEncoding.Utf8), PlainTextCodec.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void DoesNotMistakeBomlessCp1251ForUtf16()
    {
        // Cyrillic prose in Windows-1251 — even-length and BOM-less, the shape a naive
        // UTF-16 trial happily (and wrongly) accepts as CJK mojibake. It must decode as
        // CP1251 and round-trip byte-exactly.
        var original = "Привет, мир!";
        var data = Cp1251(original);
        Assert.Equal(12, data.Length);
        Assert.Equal(new byte[] { 0xCF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0x2C, 0x20, 0xEC, 0xE8, 0xF0, 0x21 }, data);
        var decoded = PlainTextCodec.Decode(data);
        Assert.NotNull(decoded);
        Assert.Equal(original, decoded.Text);
        Assert.Equal(TextEncoding.WindowsCP1251, decoded.Encoding);
        Assert.Equal(data, PlainTextCodec.Encode(decoded.Text, decoded.Encoding).Data);
    }

    [Fact]
    public void RoundTripsBomedUtf16()
    {
        var original = "# Chapter\n";
        // Foundation's data(using: .utf16) writes a BOM and host (little-endian) order.
        var data = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(original)).ToArray();
        var decoded = PlainTextCodec.Decode(data);
        Assert.Equal(original, decoded?.Text);
        Assert.Equal(TextEncoding.Utf16, decoded?.Encoding);
        // Re-encoding restores a BOM'd UTF-16 file, not a silently rewritten one.
        var encoded = PlainTextCodec.Encode(original, TextEncoding.Utf16);
        Assert.Equal(TextEncoding.Utf16, encoded.Encoding);
        Assert.Equal(data, encoded.Data);
        Assert.Equal(original, PlainTextCodec.Decode(encoded.Data)?.Text);
        // Measured: Foundation writes FF FE even for the empty string.
        Assert.Equal(new byte[] { 0xFF, 0xFE }, PlainTextCodec.Encode("", TextEncoding.Utf16).Data);
    }

    [Fact]
    public void HonoursABigEndianBomAndStripsIt()
    {
        var decoded = PlainTextCodec.Decode(new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x04, 0x1F });
        Assert.Equal("AП", decoded?.Text);
        Assert.Equal(TextEncoding.Utf16, decoded?.Encoding);
        // Written back little-endian: the byte order is the codec's, the BOM says which.
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x41, 0x00, 0x1F, 0x04 }, PlainTextCodec.Encode("AП", TextEncoding.Utf16).Data);
    }

    [Fact]
    public void UpgradesToUtf8AndSaysSo()
    {
        // An emoji cannot live in CP1251: the encode must fall back to UTF-8 *and report
        // it*, so an autosaving caller updates its remembered encoding instead of failing
        // the same way every save.
        var encoded = PlainTextCodec.Encode("Привет 🙂", TextEncoding.WindowsCP1251);
        Assert.Equal(TextEncoding.Utf8, encoded.Encoding);
        Assert.Equal("Привет 🙂", Utf8(encoded.Data));
        // Latin-1 upgrades the same way, and never best-fits a Cyrillic letter.
        var latin = PlainTextCodec.Encode("Привет", TextEncoding.IsoLatin1);
        Assert.Equal(TextEncoding.Utf8, latin.Encoding);
        Assert.Equal(Utf8("Привет"), latin.Data);
    }

    [Fact]
    public void LegacyEncodingRoundTripsThroughAnEdit()
    {
        // The book editor's session test at codec level: a Windows-1251 article is edited
        // and flushed, and the file keeps its own encoding.
        var original = "Привет, мир!";
        var decoded = PlainTextCodec.Decode(Cp1251(original));
        Assert.NotNull(decoded);
        Assert.Equal(original, decoded.Text);
        var edited = decoded.Text + " Ещё.";
        var encoded = PlainTextCodec.Encode(edited, decoded.Encoding);
        Assert.Equal(TextEncoding.WindowsCP1251, encoded.Encoding);
        Assert.Equal(Cp1251(edited), encoded.Data);
    }

    [Fact]
    public void FallsBackLosslesslyForArbitraryBytes()
    {
        // Every byte is defined in Windows-1251 except 0x98, so binary-ish input decodes as
        // CP1251 one char per byte…
        var cp = PlainTextCodec.Decode(new byte[] { 0x41, 0xFF, 0x42 });
        Assert.Equal("AяB", cp?.Text);
        Assert.Equal(TextEncoding.WindowsCP1251, cp?.Encoding);
        // …and 0x98 — the one byte Foundation's CP1251 rejects, which .NET's table would
        // happily map — falls through to ISO-8859-1, which maps everything and round-trips.
        var latin = PlainTextCodec.Decode(new byte[] { 0x41, 0x98, 0x42 });
        Assert.Equal("A" + (char)0x0098 + "B", latin?.Text);
        Assert.Equal(TextEncoding.IsoLatin1, latin?.Encoding);
        Assert.Equal(new byte[] { 0x41, 0x98, 0x42 }, PlainTextCodec.Encode(latin!.Text, latin.Encoding).Data);
        // The mirror image: U+0098 does not fit CP1251 on the Mac, so it upgrades here too.
        var upgraded = PlainTextCodec.Encode("A" + (char)0x0098 + "B", TextEncoding.WindowsCP1251);
        Assert.Equal(TextEncoding.Utf8, upgraded.Encoding);
        Assert.Equal(new byte[] { 0x41, 0xC2, 0x98, 0x42 }, upgraded.Data);
        // Every other C1 byte is a defined CP1251 character.
        Assert.Equal(TextEncoding.WindowsCP1251, PlainTextCodec.Decode(new byte[] { 0x80, 0x99, 0x9F })?.Encoding);
    }

    [Fact]
    public void StripsAUtf8BomOnDecodeAndWritesNoneBack()
    {
        // Measured: Foundation's String(data:encoding:.utf8) drops a leading EF BB BF and
        // Data(text.utf8) never writes one, so a BOM'd UTF-8 file loses its BOM on save.
        var decoded = PlainTextCodec.Decode(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 });
        Assert.Equal("A", decoded?.Text);
        Assert.Equal(TextEncoding.Utf8, decoded?.Encoding);
        Assert.Equal(new byte[] { 0x41 }, PlainTextCodec.Encode("A", TextEncoding.Utf8).Data);
    }

    [Fact]
    public void RejectsInvalidUtf8StrictlyInsteadOfSubstituting()
    {
        // A lenient decode would "succeed" with U+FFFD and the next autosave would bake it
        // in; the trial must fail and hand the bytes to the single-byte decoders.
        var decoded = PlainTextCodec.Decode(new byte[] { 0xD0, 0x9F, 0xE0 });   // "П" then a truncated sequence
        Assert.Equal(TextEncoding.WindowsCP1251, decoded?.Encoding);
        Assert.DoesNotContain((char)0xFFFD, decoded!.Text);
        Assert.Equal(new byte[] { 0xD0, 0x9F, 0xE0 }, PlainTextCodec.Encode(decoded.Text, decoded.Encoding).Data);
    }

    [Fact]
    public void AnOddTailAfterAUtf16BomIsNotSwallowed()
    {
        // Divergence, pinned: Foundation turns FF FE 41 into "" (and would then write FF FE
        // over the file); here the UTF-16 trial fails and the bytes survive as CP1251.
        var decoded = PlainTextCodec.Decode(new byte[] { 0xFF, 0xFE, 0x41 });
        Assert.NotNull(decoded);
        Assert.Equal(TextEncoding.WindowsCP1251, decoded.Encoding);
        Assert.Equal("яюA", decoded.Text);
        Assert.Equal(new byte[] { 0xFF, 0xFE, 0x41 }, PlainTextCodec.Encode(decoded.Text, decoded.Encoding).Data);
    }

    [Fact]
    public void EncodingUtf8NeverFails()
    {
        // Swift's Data(text.utf8) cannot fail; a lone surrogate in a C# string is replaced
        // rather than thrown on the one path every save falls back to.
        var encoded = PlainTextCodec.Encode("a" + (char)0xD800 + "b", TextEncoding.Utf8);
        Assert.Equal(TextEncoding.Utf8, encoded.Encoding);
        Assert.Equal(new byte[] { 0x61, 0xEF, 0xBF, 0xBD, 0x62 }, encoded.Data);
        Assert.Equal(TextEncoding.Utf8, PlainTextCodec.Encode("a" + (char)0xD800 + "b", TextEncoding.WindowsCP1251).Encoding);
    }
}
