using System.Globalization;

namespace Md.Core.Book;

/// <summary>
/// Reading order for sibling names: a leading integer sorts first, numerically
/// ("2. setup" before "10-ending"); numbered before unnumbered; ties and the rest in
/// Finder-style natural order. Port of <c>BookLibrary.ordered / leadingNumber</c>.
///
/// Swift's fallback is <c>localizedStandardCompare</c>, which .NET does not have. The
/// stand-in is the Kotlin port's <c>naturalCompare</c> (md.Android Book.kt), already
/// parity-tested there: embedded ASCII-digit runs compare numerically, other code
/// units case-insensitively, and equal-looking names ("part01" / "part1", "Draft" /
/// "draft") fall back to ordinal so the order is total and deterministic. Never
/// <c>StrCmpLogicalW</c>: Explorer order would put the Windows listing out of step
/// with the Android one.
/// </summary>
public static class BookOrder
{
    /// <summary>
    /// The integer prefix of a name ("01-intro" → 1), or null when there is none. A
    /// run too long for Int64 ("99999999999999999999-y") is unnumbered rather than an
    /// overflow — Swift's <c>Int(digits)</c> returns nil there too. No sign handling:
    /// "-3 degrees" is unnumbered.
    /// </summary>
    public static long? LeadingNumber(string name)
    {
        var count = BookNaming.LeadingAsciiDigits(name);
        if (count == 0) return null;
        return long.TryParse(name.AsSpan(0, count), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    /// <summary>Finder-style natural comparison — the Kotlin stand-in for <c>localizedStandardCompare</c>.</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (IsAsciiDigit(a[i]) && IsAsciiDigit(b[j]))
            {
                // Bound both digit runs, skip leading zeros (keeping one digit), then
                // compare by significant length and digit by digit — never parsed, so
                // run length cannot overflow anything.
                var ea = i; while (ea < a.Length && IsAsciiDigit(a[ea])) ea++;
                var eb = j; while (eb < b.Length && IsAsciiDigit(b[eb])) eb++;
                var sa = i; while (sa < ea - 1 && a[sa] == '0') sa++;
                var sb = j; while (sb < eb - 1 && b[sb] == '0') sb++;
                var byLength = (ea - sa).CompareTo(eb - sb);
                if (byLength != 0) return byLength;
                while (sa < ea)
                {
                    var byDigit = a[sa].CompareTo(b[sb]);
                    if (byDigit != 0) return byDigit;
                    sa++; sb++;
                }
                i = ea; j = eb;
            }
            else
            {
                // Per UTF-16 unit, like Kotlin's lowercaseChar(): no locale tailoring,
                // no diacritic folding, surrogate halves compare as units.
                var byChar = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (byChar != 0) return byChar;
                i++; j++;
            }
        }
        var byRemainder = (a.Length - i).CompareTo(b.Length - j);
        return byRemainder != 0 ? byRemainder : string.CompareOrdinal(a, b);
    }

    /// <summary>The full book ordering: numbered first by number, natural order for ties and the rest.</summary>
    public static int Compare(string a, string b)
    {
        var na = LeadingNumber(a);
        var nb = LeadingNumber(b);
        if (na is { } x && nb is { } y)
        {
            var byNumber = x.CompareTo(y);
            return byNumber != 0 ? byNumber : NaturalCompare(a, b);
        }
        if (na is not null) return -1;
        if (nb is not null) return 1;
        return NaturalCompare(a, b);
    }

    /// <summary>Swift's strict <c>ordered(a, b)</c>: a sorts before b.</summary>
    public static bool Ordered(string a, string b) => Compare(a, b) < 0;

    /// <summary>
    /// For <c>OrderBy</c> (stable, like Swift's <c>sorted</c>; <c>List.Sort</c> is not).
    /// The ordinal tie-break means distinct names never compare equal, so stability
    /// only matters for duplicates a file system cannot hold anyway.
    /// </summary>
    public static IComparer<string> Comparer { get; } = new NameComparer();

    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    private sealed class NameComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x is null) return y is null ? 0 : -1;
            if (y is null) return 1;
            return BookOrder.Compare(x, y);
        }
    }
}
