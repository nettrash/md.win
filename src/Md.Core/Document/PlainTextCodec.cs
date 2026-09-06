using System.Text;

namespace Md.Core.Document;

/// <summary>The encodings the app reads and round-trips (Swift <c>String.Encoding</c> subset).</summary>
public enum TextEncoding
{
    Utf8,
    /// <summary>BOM'd UTF-16, either byte order on read; written little-endian with a BOM.</summary>
    Utf16,
    WindowsCP1251,
    IsoLatin1,
}

/// <summary>A decoded file: its text and the encoding that matched.</summary>
public sealed record DecodedText(string Text, TextEncoding Encoding);

/// <summary>
/// Bytes to save and the encoding actually used — not necessarily the one preferred.
/// Equality is structural over the bytes, like <c>ZipEntry</c> and <c>TextBundle.Asset</c>
/// (a plain record would compare the array by reference).
/// </summary>
public sealed record EncodedText(byte[] Data, TextEncoding Encoding)
{
    public bool Equals(EncodedText? other) =>
        other is not null && Encoding == other.Encoding && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Encoding);
        hash.AddBytes(Data);
        return hash.ToHashCode();
    }

    public override string ToString() => $"EncodedText({Encoding}, {Data.Length} bytes)";
}

/// <summary>
/// Decoding and encoding for the plain-text files the app edits — shared by the document
/// model, the book workspace's in-place article editor and TextBundle import, so every
/// path reads and round-trips a file's bytes identically (macOS <c>PlainTextCodec</c>).
/// </summary>
public static class PlainTextCodec
{
    // Every trial decoder throws instead of substituting U+FFFD: a lenient decode would
    // "succeed" on the wrong encoding and the next autosave would bake the mojibake in.
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf16LE = new UnicodeEncoding(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictUtf16BE = new UnicodeEncoding(bigEndian: true, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Encoding StrictCP1251;
    private static readonly Encoding StrictLatin1;
    // Swift's `Data(text.utf8)` cannot fail; a C# string may hold a lone surrogate, which
    // this encoder replaces rather than throwing on the one path that must always succeed.
    private static readonly Encoding LenientUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private static readonly Encoding LenientUtf16LE = new UnicodeEncoding(bigEndian: false, byteOrderMark: false);

    // Windows-1251 leaves exactly one byte undefined, 0x98, and Foundation refuses it in
    // both directions (measured: the only byte its decoder rejects, and U+0098 is the one
    // C1 control its encoder will not write). .NET's code-pages table maps 0x98 <-> U+0098
    // even with exception fallbacks, so the codec refuses it by hand — otherwise a file
    // carrying 0x98 would open as CP1251 here and as Latin-1 on the Mac, and a document
    // holding U+0098 would save as CP1251 here where the Mac upgrades it to UTF-8.
    private const byte UndefinedCp1251Byte = 0x98;

    // Windows-1251 is not in .NET's default encoding set; the provider ships in the shared
    // framework and registering it twice is harmless. The two code-page encodings are
    // assigned HERE, not in field initializers: C# runs every static initializer before the
    // static constructor's body, so an initializer would call GetEncoding(1251) before the
    // provider exists and the type would fail to load.
    static PlainTextCodec()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        StrictCP1251 = Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        StrictLatin1 = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    /// <summary>
    /// Decode bytes as text, returning the matched encoding. UTF-16 is considered only
    /// behind an explicit BOM (FF FE / FE FF): without one a strict UTF-16 decoder happily
    /// pairs the bytes of BOM-less CP1251 prose into CJK mojibake. The single-byte trials
    /// run most- to least-specific — strict UTF-8, strict Windows-1251 (only 0x98 is
    /// undefined), then ISO-8859-1, which maps every byte and so never fails. Null is
    /// therefore unreachable in practice and kept only for the Swift shape.
    /// </summary>
    public static DecodedText? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            // Foundation strips the BOM; the byte order comes from it, not the host.
            // Divergence (measured, unpinned by macOS): Foundation turns "BOM + one odd
            // byte" into "" with .utf16 — and the next autosave would write a two-byte
            // file over it. Here the strict decoder refuses an odd tail (and a lone
            // surrogate, as Foundation does) and the single-byte decoders take the file
            // losslessly instead.
            var utf16 = data[0] == 0xFF ? StrictUtf16LE : StrictUtf16BE;
            var text = TryDecode(utf16, data[2..]);
            if (text is not null) return new DecodedText(text, TextEncoding.Utf16);
        }

        var utf8 = TryDecode(StrictUtf8, data);
        if (utf8 is not null)
        {
            // Foundation's .utf8 decode drops a leading EF BB BF (measured), and its
            // encode writes none back — so a BOM'd UTF-8 file loses its BOM on save.
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) utf8 = utf8[1..];
            return new DecodedText(utf8, TextEncoding.Utf8);
        }
        if (data.IndexOf(UndefinedCp1251Byte) < 0)
        {
            var cp1251 = TryDecode(StrictCP1251, data);
            if (cp1251 is not null) return new DecodedText(cp1251, TextEncoding.WindowsCP1251);
        }
        var latin1 = TryDecode(StrictLatin1, data);
        if (latin1 is not null) return new DecodedText(latin1, TextEncoding.IsoLatin1);
        return null;
    }

    /// <summary>Array overload for callers holding file bytes.</summary>
    public static DecodedText? Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Decode(data.AsSpan());
    }

    /// <summary>
    /// Encode for saving in the encoding the file was read in, so a save round-trips
    /// instead of silently rewriting the file as UTF-8. If the text no longer fits (an
    /// emoji typed into a Windows-1251 file) it is upgraded to UTF-8 — and the encoding
    /// actually used is reported, so the caller remembers the upgrade instead of failing
    /// the same way on every autosave.
    /// </summary>
    public static EncodedText Encode(string text, TextEncoding preferred)
    {
        ArgumentNullException.ThrowIfNull(text);
        switch (preferred)
        {
            case TextEncoding.Utf8:
                return new EncodedText(LenientUtf8.GetBytes(text), TextEncoding.Utf8);
            case TextEncoding.Utf16:
            {
                // Foundation writes BOM + host order, little-endian on every Mac that
                // runs this app (measured FF FE, present even for an empty string).
                var body = LenientUtf16LE.GetBytes(text);
                var data = new byte[body.Length + 2];
                data[0] = 0xFF;
                data[1] = 0xFE;
                body.CopyTo(data, 2);
                return new EncodedText(data, TextEncoding.Utf16);
            }
            case TextEncoding.WindowsCP1251:
                return text.Contains((char)UndefinedCp1251Byte)
                    ? new EncodedText(LenientUtf8.GetBytes(text), TextEncoding.Utf8)
                    : TryEncode(StrictCP1251, text, TextEncoding.WindowsCP1251);
            case TextEncoding.IsoLatin1:
                return TryEncode(StrictLatin1, text, TextEncoding.IsoLatin1);
            default:
                throw new ArgumentOutOfRangeException(nameof(preferred), preferred, null);
        }
    }

    private static EncodedText TryEncode(Encoding strict, string text, TextEncoding reported)
    {
        try
        {
            return new EncodedText(strict.GetBytes(text), reported);
        }
        catch (EncoderFallbackException)
        {
            // Foundation's data(using:) returns nil here and never best-fits; upgrade.
            return new EncodedText(LenientUtf8.GetBytes(text), TextEncoding.Utf8);
        }
    }

    private static string? TryDecode(Encoding strict, ReadOnlySpan<byte> data)
    {
        try
        {
            return strict.GetString(data);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
