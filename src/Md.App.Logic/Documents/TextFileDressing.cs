using Md.Core.Document;

namespace Md.App.Logic.Documents;

/// <summary>
/// Everything about a text file except its text: the encoding it was read in, whether it carried a
/// UTF-8 BOM, and its line-break convention (§6.3). The session keeps one and re-applies it at every
/// save, so a file that arrived as CRLF Windows-1251 with a BOM leaves the same way — nothing about
/// a document silently changes because it was opened in md.
///
/// The BOM is a decision this design records: Foundation's <c>.utf8</c> decode drops a leading
/// EF BB BF and its encode writes none back, so on the Mac a BOM'd file loses its BOM the first time
/// it is saved. Here it is remembered and re-emitted — the file the writer gets back is the file they
/// had. A UTF-16 BOM needs no flag: <c>PlainTextCodec.Encode</c> always writes one for that encoding.
/// </summary>
/// <param name="Encoding">What <c>PlainTextCodec.Decode</c> matched; a save that outgrows it upgrades to UTF-8 and the session remembers the upgrade.</param>
/// <param name="HadUtf8Bom">Only meaningful for <see cref="TextEncoding.Utf8"/>; a CP1251 file upgraded to UTF-8 gains no BOM.</param>
/// <param name="NewLine">The first line break in the file; none, and a lone CR, mean <see cref="Documents.NewLine.Lf"/>.</param>
public sealed record TextFileDressing(TextEncoding Encoding, bool HadUtf8Bom, NewLine NewLine)
{
    /// <summary>What a new, untitled document writes: UTF-8, no BOM, LF (the Mac writes <c>\n</c>).</summary>
    public static TextFileDressing Default { get; } = new(TextEncoding.Utf8, false, NewLine.Lf);

    /// <summary>
    /// Read a file's bytes: LF text plus the dressing to write it back with. Null when no encoding
    /// matched — unreachable in practice (ISO-8859-1 maps every byte) and kept only because
    /// <c>PlainTextCodec.Decode</c> has the shape.
    /// </summary>
    public static (string Text, TextFileDressing Dressing)? Undress(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (PlainTextCodec.Decode(bytes) is not { } decoded) return null;
        var hadBom = decoded.Encoding == TextEncoding.Utf8
            && bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        var dressing = new TextFileDressing(decoded.Encoding, hadBom, LineEndings.Detect(decoded.Text));
        return (LineEndings.Normalize(decoded.Text), dressing);
    }

    /// <summary>
    /// The bytes to write for LF <paramref name="text"/>: line breaks restored, encoded in
    /// <see cref="Encoding"/>, BOM re-attached. <paramref name="used"/> is the encoding actually
    /// written — a CP1251 file that has grown an emoji is upgraded to UTF-8, and the caller must
    /// remember that or every later autosave retries the encoding that cannot hold the text.
    /// </summary>
    public byte[] Dress(string text, out TextEncoding used)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoded = PlainTextCodec.Encode(LineEndings.Apply(text, NewLine), Encoding);
        used = encoded.Encoding;
        // Both halves matter: the flag means "this UTF-8 file had a BOM", so a file read in
        // another encoding never gains one just because the save upgraded it to UTF-8.
        if (!HadUtf8Bom || Encoding != TextEncoding.Utf8 || used != TextEncoding.Utf8) return encoded.Data;
        var withBom = new byte[encoded.Data.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        encoded.Data.CopyTo(withBom, 3);
        return withBom;
    }

    /// <summary>
    /// The dressing after a save that upgraded the encoding (§6.3: the upgrade is remembered). The
    /// BOM flag does not survive: the only encoding change there is is an upgrade to UTF-8 from
    /// something that had no UTF-8 BOM to begin with.
    /// </summary>
    public TextFileDressing WithEncoding(TextEncoding encoding) =>
        encoding == Encoding ? this : new TextFileDressing(encoding, false, NewLine);
}
