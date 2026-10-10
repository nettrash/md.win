using System.Globalization;
using System.Text.RegularExpressions;
using Md.Core.Markdown;
using Md.Core.Text;

namespace Md.Core.Export;

/// <summary>
/// What a document's front matter adds to its EPUB package document.
/// </summary>
/// <remarks>
/// From 1.6 every md EPUB writer reads the same front-matter keys, by the same rules, into the same
/// lines in the same order (md.vscode's <c>src/export/epub-metadata.ts</c> is where the family
/// wrote them down; <c>EpubMetadataTests</c> carries its vectors):
/// <list type="table">
/// <item><term><c>author</c></term><description><c>&lt;dc:creator&gt;</c></description></item>
/// <item><term><c>language</c></term><description><c>&lt;dc:language&gt;</c> when it is a language tag, else <c>en</c></description></item>
/// <item><term><c>date</c></term><description><c>&lt;dc:date&gt;</c> when it is a real W3C date, else left out</description></item>
/// <item><term><c>description</c></term><description><c>&lt;dc:description&gt;</c></description></item>
/// <item><term><c>series</c>, <c>series_index</c></term><description>a <c>belongs-to-collection</c> of type series, with its position when the index is a number</description></item>
/// <item><term><c>cover</c></term><description>a <c>cover-image</c> item, when the image can be read</description></item>
/// </list>
/// A key matches as <c>title:</c> does — case-insensitively, the first non-empty value winning,
/// trimmed of whitespace and newlines. Only a single document's EPUB reads them; a book keeps
/// <see cref="Default"/> (book metadata is a later release's).
/// </remarks>
public sealed record EpubMetadata(
    string? Creator,
    string Language,
    string? Date,
    string? Description,
    string? Series,
    string? SeriesIndex)
{
    /// <summary>A document with no front matter: the package document exactly as 1.5 wrote it.</summary>
    public static readonly EpubMetadata Default = new(null, "en", null, null, null, null);
}

/// <summary>A cover image recognised by its own bytes.</summary>
/// <param name="Extension"><c>png</c>, <c>jpg</c>, <c>gif</c> or <c>webp</c>.</param>
public sealed record EpubCover(string Extension, string MediaType, byte[] Data)
{
    /// <summary>The cover's path inside <c>OEBPS/</c>.</summary>
    public string Href => "images/cover." + Extension;

    public bool Equals(EpubCover? other) =>
        other is not null
        && string.Equals(Extension, other.Extension, StringComparison.Ordinal)
        && string.Equals(MediaType, other.MediaType, StringComparison.Ordinal)
        && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode() => HashCode.Combine(Extension, MediaType, Data.Length);

    public override string ToString() => $"EpubCover({Href}, {MediaType}, {Data.Length} bytes)";
}

/// <summary>Where a <c>cover:</c> value says the image is.</summary>
public abstract record CoverReference
{
    private CoverReference() { }

    /// <summary>A <c>data:</c> image, already decoded.</summary>
    public sealed record Inline(byte[] Data) : CoverReference
    {
        public bool Equals(Inline? other) => other is not null && Data.AsSpan().SequenceEqual(other.Data);
        public override int GetHashCode() => Data.Length;
    }

    /// <summary>An <c>https:</c> image, to be fetched.</summary>
    public sealed record Https(string Url) : CoverReference;

    /// <summary>A path relative to the document's own folder.</summary>
    public sealed record Local(string Path) : CoverReference;
}

/// <summary>
/// The front-matter rules for <see cref="EpubMetadata"/> and the cover, and the package-document
/// lines they become. Pure: no file system, no network — reading a cover is the app's.
/// </summary>
public static class EpubMetadataReader
{
    /// <summary>A cover larger than this is skipped: a reader shows a thumbnail, not a poster.</summary>
    public const int MaxCoverBytes = 20 * 1024 * 1024;

    // Explicit ASCII classes, \z rather than $ (which also matches before a final \n in .NET), and
    // no \d (which matches every Unicode digit in .NET): the other ports' regexes are ASCII-only.
    private static readonly Regex LanguageTag =
        new(@"^[A-Za-z]{2,3}(?:-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant);

    private static readonly Regex W3CDate = new(
        @"^([0-9]{4})(?:-([0-9]{2})(?:-([0-9]{2})(?:T([0-9]{2}):([0-9]{2})(?::([0-9]{2}))?(?:Z|[+-]([0-9]{2}):([0-9]{2})))?)?)?\z",
        RegexOptions.CultureInvariant);

    private static readonly Regex SeriesIndexPattern = new(@"^[0-9]+(?:\.[0-9]+)?\z", RegexOptions.CultureInvariant);
    private static readonly Regex Base64Payload = new(@"^[A-Za-z0-9+/]*={0,2}\z", RegexOptions.CultureInvariant);
    private static readonly Regex Scheme = new(@"^[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant);

    /// <summary>The first non-empty value of <paramref name="key"/> — <see cref="EpubExport.DocumentTitle(IReadOnlyList{MetadataField}, string)"/>'s rule.</summary>
    public static string? FieldValue(IReadOnlyList<MetadataField> fields, string key)
    {
        foreach (var field in fields)
        {
            if (!string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = Whitespace.TrimWSNL(field.Value);
            if (value.Length > 0) return value;
        }
        return null;
    }

    /// <summary>The package document's metadata from a document's front matter.</summary>
    public static EpubMetadata FromFrontMatter(IReadOnlyList<MetadataField> fields)
    {
        var language = FieldValue(fields, "language");
        var date = FieldValue(fields, "date");
        var series = FieldValue(fields, "series");
        var seriesIndex = FieldValue(fields, "series_index");
        return new EpubMetadata(
            Creator: FieldValue(fields, "author"),
            Language: language is not null && LanguageTag.IsMatch(language) ? language : "en",
            Date: date is not null && IsW3CDate(date) ? date : null,
            Description: FieldValue(fields, "description"),
            Series: series,
            SeriesIndex: series is not null && seriesIndex is not null && SeriesIndexPattern.IsMatch(seriesIndex) ? seriesIndex : null);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a W3CDTF date EPUB accepts: the shape, and then real
    /// ranges — month 01–12, a day that month has (Gregorian leap years), hour 00–23, minute and
    /// second 00–59, a zone offset of 00–23 hours and 00–59 minutes.
    /// </summary>
    public static bool IsW3CDate(string value)
    {
        var match = W3CDate.Match(value);
        if (!match.Success) return false;
        static int Number(Group group) => int.Parse(group.Value, NumberStyles.None, CultureInfo.InvariantCulture);
        var year = Number(match.Groups[1]);
        if (match.Groups[2].Success)
        {
            var month = Number(match.Groups[2]);
            if (month is < 1 or > 12) return false;
            if (match.Groups[3].Success)
            {
                var day = Number(match.Groups[3]);
                var leap = (year % 4 == 0 && year % 100 != 0) || year % 400 == 0;
                int[] days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
                if (day < 1 || day > days[month - 1]) return false;
            }
        }
        if (match.Groups[4].Success && Number(match.Groups[4]) > 23) return false;
        if (match.Groups[5].Success && Number(match.Groups[5]) > 59) return false;
        if (match.Groups[6].Success && Number(match.Groups[6]) > 59) return false;
        if (match.Groups[7].Success && Number(match.Groups[7]) > 23) return false;
        if (match.Groups[8].Success && Number(match.Groups[8]) > 59) return false;
        return true;
    }

    private static bool StartsWithAsciiIgnoringCase(string text, string prefix)
    {
        if (text.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            var c = text[i];
            if (c is >= 'A' and <= 'Z') c = (char)(c + 32);
            if (c != prefix[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Classify a <c>cover:</c> value, or null when it can never be read: a <c>data:</c> URI that is
    /// not strict base64, <c>http:</c> or any other scheme, an absolute path, a fragment.
    /// </summary>
    public static CoverReference? Reference(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (StartsWithAsciiIgnoringCase(value, "data:"))
        {
            var comma = value.IndexOf(',', StringComparison.Ordinal);
            if (comma < 0) return null;
            var header = value[5..comma];
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return null;
            var payload = value[(comma + 1)..];
            if (payload.Length == 0 || payload.Length % 4 != 0 || !Base64Payload.IsMatch(payload)) return null;
            return new CoverReference.Inline(Convert.FromBase64String(payload));
        }
        if (StartsWithAsciiIgnoringCase(value, "https://")) return new CoverReference.Https(value);
        if (value.Contains("://", StringComparison.Ordinal) || Scheme.IsMatch(value)) return null;
        if (value[0] is '/' or '\\' or '#') return null;
        return new CoverReference.Local(value);
    }

    /// <summary>A cover from its bytes: PNG, JPEG, GIF or WebP by magic number, no larger than <see cref="MaxCoverBytes"/>.</summary>
    public static EpubCover? Recognise(byte[]? data)
    {
        if (data is null || data.Length == 0 || data.Length > MaxCoverBytes) return null;
        bool At(int offset, ReadOnlySpan<byte> bytes) =>
            data.Length >= offset + bytes.Length && data.AsSpan(offset, bytes.Length).SequenceEqual(bytes);
        if (At(0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return new EpubCover("png", "image/png", data);
        if (At(0, [0xFF, 0xD8, 0xFF])) return new EpubCover("jpg", "image/jpeg", data);
        if (At(0, "GIF87a"u8) || At(0, "GIF89a"u8)) return new EpubCover("gif", "image/gif", data);
        if (At(0, "RIFF"u8) && At(8, "WEBP"u8)) return new EpubCover("webp", "image/webp", data);
        return null;
    }

    /// <summary>
    /// The lines between <c>dc:title</c> and <c>dcterms:modified</c> — identical text in all five
    /// md EPUB writers. With <see cref="EpubMetadata.Default"/> and no cover this is exactly the one
    /// <c>&lt;dc:language&gt;en&lt;/dc:language&gt;</c> line every md EPUB has always carried.
    /// </summary>
    public static IReadOnlyList<string> Lines(EpubMetadata metadata, EpubCover? cover)
    {
        var lines = new List<string>();
        if (metadata.Creator is not null) lines.Add("<dc:creator>" + EpubExport.XmlEscape(metadata.Creator) + "</dc:creator>");
        lines.Add("<dc:language>" + EpubExport.XmlEscape(metadata.Language) + "</dc:language>");
        if (metadata.Date is not null) lines.Add("<dc:date>" + EpubExport.XmlEscape(metadata.Date) + "</dc:date>");
        if (metadata.Description is not null) lines.Add("<dc:description>" + EpubExport.XmlEscape(metadata.Description) + "</dc:description>");
        if (metadata.Series is not null)
        {
            lines.Add("<meta property=\"belongs-to-collection\" id=\"series\">" + EpubExport.XmlEscape(metadata.Series) + "</meta>");
            lines.Add("<meta refines=\"#series\" property=\"collection-type\">series</meta>");
            if (metadata.SeriesIndex is not null)
            {
                lines.Add("<meta refines=\"#series\" property=\"group-position\">" + EpubExport.XmlEscape(metadata.SeriesIndex) + "</meta>");
            }
        }
        // EPUB 3 finds the cover by the manifest property; this EPUB 2 meta is for the readers —
        // Kindle's converter among them — that still look for it.
        if (cover is not null) lines.Add("<meta name=\"cover\" content=\"cover-image\"/>");
        return lines;
    }

    /// <summary>The cover's manifest item — the last item of every md EPUB that has one.</summary>
    public static string ManifestItem(EpubCover cover) =>
        "<item id=\"cover-image\" href=\"" + cover.Href + "\" media-type=\"" + cover.MediaType + "\" properties=\"cover-image\"/>";
}
