using System.Globalization;

namespace Md.Core.Document;

/// <summary>
/// A named PDF page ("trim") size in PostScript points, 1 in = 72 pt, portrait. Port of the table in
/// md.macOS/md/DocumentExport.swift, copied verbatim by every port — a drift here paginates the same
/// document differently on one platform. A4 keeps the historical 595.2 × 841.8 (210 × 297 mm to a
/// tenth of a point, what the app paginated to before trim sizes existed) so the A4 export is
/// byte-for-byte what it always was; do not "fix" it to 595.28 × 841.89.
/// </summary>
public sealed record PageSize(string Id, string Label, double Width, double Height)
{
    // Labels carry the real multiplication sign U+00D7 and a straight inch mark.
    public static readonly PageSize A4 = new("a4", "A4", 595.2, 841.8);
    public static readonly PageSize A5 = new("a5", "A5", 419.5, 595.3);
    public static readonly PageSize UsLetter = new("letter", "US Letter", 612, 792);
    public static readonly PageSize UsLegal = new("legal", "US Legal", 612, 1008);
    public static readonly PageSize SixByNine = new("6x9", "6 \u00D7 9\"", 432, 648);
    public static readonly PageSize FiveByEight = new("5x8", "5 \u00D7 8\"", 360, 576);
    public static readonly PageSize Digest = new("5.5x8.5", "5.5 \u00D7 8.5\"", 396, 612);

    /// <summary>Every offered size, in menu order — A4 first, since it is the default.</summary>
    public static readonly IReadOnlyList<PageSize> All = [A4, A5, UsLetter, UsLegal, SixByNine, FiveByEight, Digest];

    /// <summary>The app-wide setting (<c>@AppStorage("md.pdfPageSize")</c>; Windows: <c>ApplicationData.Current.LocalSettings</c>). Holds an <see cref="Id"/>.</summary>
    public const string PreferenceKey = "md.pdfPageSize";

    /// <summary>First-launch value: <c>"a4"</c>.</summary>
    public static string DefaultId => A4.Id;

    /// <summary>
    /// The size stored under <paramref name="id"/>, A4 for an empty, unknown or absent key (first launch,
    /// or a preference from a future build). Exact, case-sensitive match like the Swift — <c>"A4"</c> is
    /// unknown (the Kotlin suite pins that too).
    /// </summary>
    public static PageSize Named(string? id)
    {
        foreach (var size in All)
        {
            if (size.Id == id) return size;
        }
        return A4;
    }

    /// <summary>
    /// The export body margin (CSS <c>padding</c>) scaled per axis from A4's <c>48px 56px</c>, so a 6 × 9"
    /// booklet does not wear A4 margins and Letter, wider than A4, grows horizontally. Whole pixels,
    /// rounded half away from zero (Swift <c>.rounded()</c>; .NET's default is banker's) — the integer
    /// string is what keeps the A4 case byte-identical to the historical CSS.
    /// </summary>
    public string CssPadding
    {
        get
        {
            var vertical = (int)Math.Round(48 * Height / A4.Height, MidpointRounding.AwayFromZero);
            var horizontal = (int)Math.Round(56 * Width / A4.Width, MidpointRounding.AwayFromZero);
            return vertical.ToString(CultureInfo.InvariantCulture) + "px " + horizontal.ToString(CultureInfo.InvariantCulture) + "px";
        }
    }
}
