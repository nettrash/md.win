using System.Globalization;

namespace Md.Core.Book;

/// <summary>
/// Reading order for sibling names: a leading integer sorts first, numerically
/// ("2. setup" before "10-ending"); numbered before unnumbered; ties and the rest in
/// Finder-style natural order. Port of <c>BookLibrary.ordered / leadingNumber</c>.
///
/// Swift's fallback is <c>localizedStandardCompare</c>, which .NET does not have.
/// <see cref="NaturalCompare"/> reproduces it for ASCII names by hand — the same
/// algorithm as <c>ExampleLibrary.CompareNatural</c> (the File ▸ Examples menu), which a
/// 33,600-pair random differential against Foundation found exact; a 36,000-pair
/// differential over this comparator (random ASCII, mixed-script, and realistic book
/// names) agreed on every ASCII pair. Non-ASCII is outside the contract and compares by
/// code unit after every ASCII character (Foundation weighs "é" as "e" plus an accent,
/// "ß" as "ss"): the two Foundation-faithful ports and Android's <c>naturalCompare</c>
/// all differ there. The earlier Kotlin-style stand-in (per-unit lowercase, ordinal
/// punctuation) disagreed with Foundation on 8.6% of random ASCII pairs and 4.2% of
/// realistic names — "1_x" before "1-x", "1)" before "1." — and would have listed the
/// book sidebar and the Examples menu in two different orders inside one app. Never
/// <c>StrCmpLogicalW</c>: Explorer order is a third order again.
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

    /// <summary>
    /// Finder order (Swift <c>localizedStandardCompare</c>) for ASCII names, hand-written so no
    /// culture, ICU build or NLS table is consulted and every machine lists the book the same way:
    /// <list type="number">
    /// <item>Letters compare without regard to case, runs of ASCII digits by numeric value (no
    /// integer parse, so a 40-digit run cannot overflow), and everything else by the root-collation
    /// order of the ASCII punctuation — TAB, space, then
    /// <c>_ - , ; : ! ? . ' " ( ) [ ] { } @ * / \ &amp; # % ` ^ + &lt; = &gt; | ~ $</c> — with all
    /// punctuation before every digit and every digit before every letter (so <c>Tables (copy)</c>
    /// lists before <c>Tables</c> and <c>a_b</c> before <c>a.b</c>). A name that runs out first lists first.</item>
    /// <item>When that leaves two names equal, one more walk from the left, where a digit run is told
    /// apart by its leading zeros (fewer first) and a letter by its case (lower first); whichever
    /// difference comes first decides — <c>1-A</c> &lt; <c>01-a</c>, but <c>a01</c> &lt; <c>A1</c>.</item>
    /// </list>
    /// Zero only for identical strings, so the order is total and <c>OrderBy</c> needs no tie-break.
    /// </summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (IsAsciiDigit(a[i]) && IsAsciiDigit(b[j]))
            {
                var runA = DigitRunLength(a, i);
                var runB = DigitRunLength(b, j);
                var byValue = CompareValue(a.AsSpan(i, runA), b.AsSpan(j, runB));
                if (byValue != 0) return byValue;
                i += runA;
                j += runB;
                continue;
            }
            var wa = PrimaryWeight(a[i]);
            var wb = PrimaryWeight(b[j]);
            if (wa != wb) return wa < wb ? -1 : 1;
            i++;
            j++;
        }
        if (i < a.Length) return 1;
        if (j < b.Length) return -1;

        // Same letters, same numbers: the second walk. Equal primary weights mean the two
        // elements are either digit runs (compared here by their zeros) or the same letter
        // (compared by its case).
        i = 0;
        j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (IsAsciiDigit(a[i]))
            {
                var runA = DigitRunLength(a, i);
                var runB = DigitRunLength(b, j);
                var za = LeadingZeros(a.AsSpan(i, runA));
                var zb = LeadingZeros(b.AsSpan(j, runB));
                if (za != zb) return za < zb ? -1 : 1;
                i += runA;
                j += runB;
                continue;
            }
            if (a[i] != b[j]) return IsAsciiLower(a[i]) ? -1 : 1;
            i++;
            j++;
        }
        return 0;
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
    /// Distinct names never compare equal, so stability only matters for duplicates a
    /// file system cannot hold anyway.
    /// </summary>
    public static IComparer<string> Comparer { get; } = new NameComparer();

    // TAB and space first, then the punctuation in root-collation order — the sequence Foundation
    // returned for the 33 printable non-alphanumeric ASCII characters plus TAB, alone and between letters.
    private const string PunctuationOrder = "\t _-,;:!?.'\"()[]{}@*/\\&#%`^+<=>|~$";

    private static readonly int[] AsciiWeight = BuildAsciiWeights();

    private static int[] BuildAsciiWeights()
    {
        var weights = new int[128];
        // Other controls never appear in a file name; ordered by code unit ahead of everything so the order stays total.
        for (var c = 0; c < 128; c++) weights[c] = c;
        for (var k = 0; k < PunctuationOrder.Length; k++) weights[PunctuationOrder[k]] = 100 + k;
        for (var c = '0'; c <= '9'; c++) weights[c] = 200;
        for (var c = 'a'; c <= 'z'; c++)
        {
            weights[c] = 300 + (c - 'a');
            weights[c - 32] = 300 + (c - 'a');
        }
        return weights;
    }

    private static int PrimaryWeight(char c) => c < 128 ? AsciiWeight[c] : 1000 + c;

    private static int DigitRunLength(string s, int start)
    {
        var end = start;
        while (end < s.Length && IsAsciiDigit(s[end])) end++;
        return end - start;
    }

    /// <summary>Zeros ahead of the value — a run of nothing but zeros keeps its last digit as the value.</summary>
    private static int LeadingZeros(ReadOnlySpan<char> run)
    {
        var zeros = 0;
        while (zeros < run.Length - 1 && run[zeros] == '0') zeros++;
        return zeros;
    }

    /// <summary>Numeric order for runs of any length — no integer parse, so a 40-digit run cannot overflow.</summary>
    private static int CompareValue(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var sa = a[LeadingZeros(a)..];
        var sb = b[LeadingZeros(b)..];
        if (sa.Length != sb.Length) return sa.Length < sb.Length ? -1 : 1;
        var lexical = sa.SequenceCompareTo(sb);
        return lexical == 0 ? 0 : lexical < 0 ? -1 : 1;
    }

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsAsciiLower(char c) => c is >= 'a' and <= 'z';

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
