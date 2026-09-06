using System.Text;

namespace Md.Core.Book;

/// <summary>
/// STAND-IN — replace with <c>Md.Core.Text.PlainTextCodec</c> once the text module
/// lands (one call site each in <see cref="BookArticleSession"/>: Load, FlushNow,
/// ResolveConflictKeepingMine, WriteRescueCopy, FileChanged). Internal on purpose so
/// nothing else grows a dependency on it.
///
/// Mirrors Swift <c>PlainTextCodec</c> (md.macOS MarkdownDocument.swift) closely
/// enough for the session's contract: UTF-16 only behind a BOM (BOM stripped on read,
/// written back on save, little-endian as Foundation writes it); then strict UTF-8,
/// Windows-1251, Latin-1 (which maps every byte, so a read never fails). Encode
/// prefers the encoding the file was read in and upgrades to UTF-8 — reporting it —
/// when the text no longer fits. Known gaps versus Foundation: .NET's CP1251 table
/// maps the undefined byte 0x98 to U+0098 instead of failing, and a UTF-8 BOM stays
/// in the text as U+FEFF. Both belong to the real codec's tests, not this file's.
/// </summary>
internal static class ArticleTextCodec
{
    static ArticleTextCodec()
    {
        // Windows-1251 is not in the default encoding set; the provider ships in the
        // shared framework (no package), it just has to be registered once.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>Strict UTF-8 without a BOM — the encoding a new or upgraded file is written in.</summary>
    internal static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>BOM'd little-endian UTF-16, what Foundation's <c>.utf16</c> writes regardless of the BOM it read.</summary>
    internal static readonly Encoding Utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true, throwOnInvalidBytes: true);

    private static readonly Lazy<Encoding> Cp1251Lazy = new(() =>
        Encoding.GetEncoding(1251, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));

    private static readonly Lazy<Encoding> Latin1Lazy = new(() =>
        Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));

    internal static Encoding Cp1251 => Cp1251Lazy.Value;

    /// <summary>ISO-8859-1 with an exception fallback: <c>Encoding.Latin1</c> would write "?" for a Cyrillic letter instead of failing over to UTF-8.</summary>
    internal static Encoding Latin1 => Latin1Lazy.Value;

    internal static (string Text, Encoding Encoding)? Decode(byte[] data)
    {
        if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
        {
            try
            {
                var bigEndian = data[0] == 0xFE;
                var text = new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true)
                    .GetString(data, 2, data.Length - 2);
                return (text, Utf16);
            }
            catch (DecoderFallbackException)
            {
                // Not UTF-16 after all — fall through to the single-byte trials, as Swift does.
            }
        }
        foreach (var encoding in new[] { Utf8, Cp1251, Latin1 })
        {
            try { return (encoding.GetString(data), encoding); }
            catch (DecoderFallbackException) { }
        }
        return null;
    }

    internal static (byte[] Data, Encoding Encoding) Encode(string text, Encoding preferred)
    {
        try
        {
            var body = preferred.GetBytes(text);
            var preamble = preferred.GetPreamble();
            if (preamble.Length == 0) return (body, preferred);
            var withBom = new byte[preamble.Length + body.Length];
            preamble.CopyTo(withBom, 0);
            body.CopyTo(withBom, preamble.Length);
            return (withBom, preferred);
        }
        catch (EncoderFallbackException)
        {
            // Swift's Data(text.utf8) cannot fail; a C# string can hold a lone
            // surrogate, so the fallback writer replaces rather than throws.
            return (new UTF8Encoding(false, false).GetBytes(text), Utf8);
        }
    }
}
