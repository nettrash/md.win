using System.Globalization;

namespace Md.Core.Document;

/// <summary>One bundled example: the file name as shipped and the menu label derived from it.</summary>
public sealed record Example(string FileName, string Name);

/// <summary>
/// The File ▸ Examples model (md.macOS/md/mdApp.swift <c>ExampleLibrary</c>) as a pure function over a
/// folder listing. The caller lists the TOP LEVEL of the bundled <c>Examples/</c> folder only — the
/// <c>Example Book/</c> subfolder is deliberately not a menu item; it unpacks whole via Example Book….
/// Opening an example is the app's job: read as UTF-8, create an untitled document with the text, mark
/// it dirty so closing offers to save.
/// </summary>
public static class ExampleLibrary
{
    /// <summary>
    /// The examples the menu lists, in menu order, from the file names of one folder. Keeps only
    /// <c>.md</c> files (ordinal, as Bundle's resource lookup and Kotlin's <c>extension == "md"</c>;
    /// a bare <c>.md</c> has no extension in Foundation and is not listed) and sorts them with
    /// <see cref="CompareNatural"/>.
    /// </summary>
    public static IReadOnlyList<Example> FromListing(IEnumerable<string> fileNames)
    {
        var names = new List<string>();
        foreach (var name in fileNames)
        {
            if (IsExampleFile(name)) names.Add(name);
        }
        names.Sort(CompareNatural);
        var examples = new List<Example>(names.Count);
        foreach (var name in names) examples.Add(new Example(name, DisplayName(name)));
        return examples;
    }

    public static bool IsExampleFile(string fileName)
        => fileName.Length > 3 && fileName.EndsWith(".md", StringComparison.Ordinal);

    /// <summary>
    /// Menu label: the stem without its ordering prefix — <c>"01-Welcome.md"</c> → <c>"Welcome"</c>. The
    /// prefix is ASCII digits then <c>-</c> (Swift <c>isASCII &amp;&amp; isNumber</c>; never <c>\d</c>, which
    /// is Unicode in .NET), and it is stripped ONLY if something remains: <c>"01-"</c> stays <c>"01-"</c>.
    /// Android's regex has no such guard and yields <c>""</c> there; the Mac is the source of truth.
    /// Defined for the names <see cref="FromListing"/> feeds it — only a <c>.md</c> suffix is dropped.
    /// </summary>
    public static string DisplayName(string fileName)
    {
        // deletingPathExtension: ".md" alone has no extension in Foundation, so only a non-empty stem loses it.
        var stem = IsExampleFile(fileName) ? fileName[..^3] : fileName;
        var digits = 0;
        while (digits < stem.Length && stem[digits] is >= '0' and <= '9') digits++;
        if (digits == 0 || digits >= stem.Length || stem[digits] != '-') return stem;
        // Swift compares Characters: a dash wearing a combining mark, a ZWJ or a variation selector is one
        // cluster that is not "-", and the prefix stays. StringInfo walks the same UAX #29 clusters.
        if (StringInfo.GetNextTextElementLength(stem.AsSpan(digits)) != 1) return stem;
        var rest = stem[(digits + 1)..];
        return rest.Length == 0 ? stem : rest;
    }

    /// <summary>
    /// Finder order (Swift <c>localizedStandardCompare</c>) for the ASCII names the bundle ships, hand-written
    /// so no culture, ICU build or NLS table is consulted and every machine lists the menu the same way.
    /// Checked against Foundation on macOS (2026-09-06) for every vector the tests pin:
    /// <list type="number">
    /// <item>Letters compare without regard to case, runs of ASCII digits by numeric value, and everything
    /// else by the root-collation order of the ASCII punctuation — TAB, space, then
    /// <c>_ - , ; : ! ? . ' " ( ) [ ] { } @ * / \ &amp; # % ` ^ + &lt; = &gt; | ~ $</c> — with all punctuation
    /// before every digit and every digit before every letter (so <c>Tables (copy).md</c> lists before
    /// <c>Tables.md</c> and <c>a_b</c> before <c>a.b</c>). A name that runs out first lists first.</item>
    /// <item>When that leaves two names equal, one more walk from the left, where a digit run is told apart
    /// by its leading zeros (fewer first) and a letter by its case (lower first); whichever difference
    /// comes first decides — <c>1-A</c> &lt; <c>01-a</c>, but <c>a01</c> &lt; <c>A1</c>.</item>
    /// </list>
    /// Zero only for identical strings, so the order is total. Non-ASCII is outside the contract: it
    /// compares by code unit after every ASCII character (Foundation weighs <c>é</c> as <c>e</c> plus an
    /// accent); the shipped names are ASCII and <see cref="FromListing"/> is the only caller.
    /// </summary>
    public static int CompareNatural(string a, string b)
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

        // Same letters, same numbers: the second walk. Equal primary weights mean the two elements are
        // either digit runs (compared here by their zeros) or the same letter (compared by its case).
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

    // TAB and space first, then the punctuation in root-collation order — the sequence Foundation returned
    // for the 33 printable non-alphanumeric ASCII characters plus TAB, and the same with letters around them.
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
}
