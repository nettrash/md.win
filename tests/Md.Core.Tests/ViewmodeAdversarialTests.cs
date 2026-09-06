using System.Globalization;
using Md.Core.Document;

namespace Md.Core.Tests;

// Adversarial review pins for the view-mode / examples / page-size module (2026-09-06). Every expected
// value below was derived from a sibling port, never from this C#: the Swift codec and `Example.name`
// logic run verbatim on macOS, Foundation's localizedStandardCompare and CharacterSet.whitespaces on the
// same machine (en_GB), Python hashlib for SHA-256, and the Kotlin suite where it pins a literal.
// The last case pins three guards a mutation campaign found no other test killing. Non-ASCII is spelled
// as escapes on purpose, so the fused delimiters stay visible in a diff.
public class ViewmodeAdversarialTests
{
    private const string Id1 = "53ba23f60734adf1";
    private const string Id2 = "427542472354b900";

    private static ViewModeMemory.Entry E(string identity, ViewMode mode) => new(identity, mode);

    private static string Hex16(int i) => i.ToString("x16", CultureInfo.InvariantCulture);

    [Fact]
    public void DecodeTreatsFusedDelimitersAndLineEndingsLikeSwiftCharacters()
    {
        // A combining mark after LF: UAX #29 breaks after a control, so the LF still splits; the mark then rides
        // on the next identity (17 units, first one not hex) and only that line is lost.
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n" + Id1 + " edit\n\u0301" + Id2 + " split"));
        // A mark on the token, on the header, or on the identity's last digit; a ZWJ in the identity: nothing readable.
        Assert.Empty(ViewModeMemory.Decode("v1\n" + Id1 + " edit\u0301"));
        Assert.Empty(ViewModeMemory.Decode("v1\u0301\n" + Id1 + " edit"));
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf\u0301 edit"));
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf\u200D edit"));
        // NBSP is not the field separator; around the token it is Foundation whitespace and is forgiven, as are ZWSP and TAB.
        Assert.Empty(ViewModeMemory.Decode("v1\n" + Id1 + "\u00A0edit"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n" + Id1 + " \u00A0edit\u00A0"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n" + Id1 + " \u200Bedit\u200B"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n" + Id1 + " \tedit\t"));

        // CRLF: "\r\n" is one Character in Swift, so a trailing CRLF rides on the token and the line is dropped
        // (Kotlin's trim() would read one entry here; the Mac is the source of truth). A bare trailing LF is fine.
        Assert.Empty(ViewModeMemory.Decode("v1\n" + Id1 + " edit\r\n"));
        Assert.Empty(ViewModeMemory.Decode("v1\r\n"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n" + Id1 + " edit\n"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Decode("v1\n\n\n" + Id1 + " edit\n\n"));
        Assert.Null(ViewModeMemory.ModeForToken("edit\r\n"));
        Assert.Null(ViewModeMemory.ModeForToken("\r\nedit"));
        Assert.Null(ViewModeMemory.ModeForToken("edit\u0085"));
        Assert.Null(ViewModeMemory.ModeForToken("edit\uFEFF"));
        // U+2028, NEL and a BOM are not line structure for this codec: line 0 is then not "v1".
        Assert.Empty(ViewModeMemory.Decode("v1\u2028" + Id1 + " edit"));
        Assert.Empty(ViewModeMemory.Decode("v1\u0085" + Id1 + " edit"));
        Assert.Empty(ViewModeMemory.Decode("\uFEFFv1\n" + Id1 + " edit"));
        // Full Unicode lowercasing never spells a token from a non-ASCII letter: fullwidth E, long s, Kelvin sign.
        Assert.Empty(ViewModeMemory.Decode("v1\n" + Id1 + " \uFF25DIT"));
        Assert.Empty(ViewModeMemory.Decode("v1\n" + Id1 + " \u017Fplit"));
        Assert.Null(ViewModeMemory.ModeForToken("\u212A"));
    }

    [Fact]
    public void SupplementaryPlaneAndEmptyInputsMatchTheSiblings()
    {
        // An astral identity is 16 graphemes to Swift and 17 UTF-16 units here; both reject it, and only its line is lost.
        Assert.False(ViewModeMemory.IsIdentity("0123456789abcde\U0001F600"));
        Assert.Equal(new[] { E(Id1, ViewMode.Split) }, ViewModeMemory.Decode("v1\n0123456789abcde\U0001F600 edit\n" + Id1 + " split"));
        // SHA-256 over UTF-8: four bytes for U+1F600, not two surrogates (Python hashlib).
        Assert.Equal("d95eee229ad0f4a5", ViewModeMemory.Sha256Prefix("file:/tmp/\U0001F600.md"));
        Assert.Equal("e3b0c44298fc1c14", ViewModeMemory.Sha256Prefix(""));
        // NFC and NFD spellings are different bytes and different identities on every port: nothing normalises.
        Assert.Equal("b3dc02924f7ab070", ViewModeMemory.Sha256Prefix("file:/tmp/\u00E9.md"));
        Assert.Equal("570313911bc0885e", ViewModeMemory.Sha256Prefix("file:/tmp/e\u0301.md"));
        // Example names (Swift `Example.name` run verbatim): an astral stem is a stem; an emoji modifier fused onto the
        // dash makes it not the Character "-"; a lone dash or space after the prefix is a non-empty rest.
        Assert.Equal("\U0001F600", ExampleLibrary.DisplayName("01-\U0001F600.md"));
        Assert.Equal("01-\U0001F3FBx", ExampleLibrary.DisplayName("01-\U0001F3FBx.md"));
        Assert.Equal("-", ExampleLibrary.DisplayName("5--.md"));
        Assert.Equal(" ", ExampleLibrary.DisplayName("12- .md"));
        Assert.Equal("9-\u0308", ExampleLibrary.DisplayName("9-\u0308.md"));
        Assert.Equal("00-", ExampleLibrary.DisplayName("00-.md"));
        Assert.Equal("foo.", ExampleLibrary.DisplayName("foo..md"));
        Assert.Equal("foo ", ExampleLibrary.DisplayName("foo .md"));

        // Empty everything.
        Assert.Empty(ViewModeMemory.Decode(""));
        Assert.Equal("v1", ViewModeMemory.Encode([]));
        Assert.False(ViewModeMemory.IsIdentity(""));
        Assert.Null(ViewModeMemory.ModeForToken("   \u3000"));
        Assert.Equal(new[] { E(Id1, ViewMode.Edit) }, ViewModeMemory.Touched([], Id1, ViewMode.Edit));
        var store = new InMemoryViewModeStore();
        Assert.Null(ViewModeMemory.Lookup(Id1, store));
        Assert.Equal(1, store.Reads);
        Assert.Equal(0, store.Writes);
        ViewModeMemory.Remember(ViewMode.Edit, Id1, store);
        Assert.Equal("v1\n" + Id1 + " edit", store.Value);
        Assert.Equal(1, store.Writes);
        Assert.Equal(0, ExampleLibrary.CompareNatural("", ""));
        Assert.True(ExampleLibrary.CompareNatural("", "a") < 0);
        Assert.True(ExampleLibrary.CompareNatural("", "\t") < 0);
        Assert.Equal("", ExampleLibrary.DisplayName(""));
        Assert.False(ExampleLibrary.IsExampleFile(""));
        Assert.Empty(ExampleLibrary.FromListing([]));
        Assert.Empty(ExampleLibrary.FromListing(["", ".md", "md", "Example Book"]));
    }

    [Fact]
    public void PathologicalSizesNeitherCapDecodeNorOverflowCompare()
    {
        // Decode has no cap on any port, only Touched does: 5,000 distinct identities all come back, in order.
        var ids = Enumerable.Range(0, 5000).Select(Hex16).ToArray();
        var big = "v1\n" + string.Join("\n", ids.Select(id => id + " preview"));
        var decoded = ViewModeMemory.Decode(big);
        Assert.Equal(5000, decoded.Count);
        Assert.Equal(ids, decoded.Select(e => e.Identity));

        // One touch collapses it to the newest 200 with the new entry first; the encoding has exactly 200 line
        // breaks and the length the format dictates (2 + 200 x 18 + one "edit" + 199 x "preview").
        var touched = ViewModeMemory.Touched(decoded, "ffffffffffffffff", ViewMode.Edit);
        Assert.Equal(200, touched.Count);
        Assert.Equal("ffffffffffffffff", touched[0].Identity);
        Assert.Equal(ids[198], touched[^1].Identity);
        var encoded = ViewModeMemory.Encode(touched);
        Assert.Equal(200, encoded.Count(c => c == '\n'));
        Assert.Equal(2 + 200 * 18 + 4 + 199 * 7, encoded.Length);
        Assert.Equal(touched, ViewModeMemory.Decode(encoded));
        // Through the store, one Remember rewrites a 5,000-entry value as 200.
        var store = new InMemoryViewModeStore { Value = big };
        ViewModeMemory.Remember(ViewMode.Split, Id1, store);
        Assert.Equal(200, ViewModeMemory.Entries(store).Count);
        Assert.Equal(ViewMode.Split, ViewModeMemory.Lookup(Id1, store));

        // Tens of thousands of CRLF lines, or a value that is nothing but separators, read as absent without incident.
        Assert.Empty(ViewModeMemory.Decode("v1\r\n" + string.Concat(Enumerable.Repeat(Id1 + " edit\r\n", 20000))));
        Assert.Empty(ViewModeMemory.Decode("v1" + new string('\n', 100000)));
        Assert.Empty(ViewModeMemory.Decode("v1\n" + new string(' ', 100000)));

        // 100,000-digit runs compare by value with no integer parse; a huge ordering prefix is still stripped.
        var zeros = new string('0', 100000);
        var nines = new string('9', 100000);
        Assert.True(ExampleLibrary.CompareNatural(nines + ".md", "1" + zeros + ".md") < 0);
        Assert.Equal(0, ExampleLibrary.CompareNatural("1" + zeros, "1" + zeros));
        Assert.True(ExampleLibrary.CompareNatural(zeros + "1", "1") > 0);
        Assert.Equal("x", ExampleLibrary.DisplayName("1" + zeros + "-x.md"));
        Assert.Equal("1" + zeros + "-", ExampleLibrary.DisplayName("1" + zeros + "-.md"));
        Assert.Null(ViewModeMemory.ModeForToken(new string(' ', 100000) + "editx"));
        Assert.Equal(ViewMode.Edit, ViewModeMemory.ModeForToken(new string('\u3000', 100000) + "EDIT"));
    }

    /// <summary>
    /// Rows Foundation's <c>localizedStandardCompare</c> returned on macOS for pairs the main theory does not
    /// reach (TAB against space, zeros against zeros, punctuation soup), drawn from a 33,600-pair random
    /// differential that produced no mismatch.
    /// </summary>
    public static TheoryData<string, int, string> FoundationAdversarialPairs => new()
    {
        { "a\t", -1, "a " },
        { "\t", -1, " " },
        { "0", -1, "00" },
        { "01", -1, "001" },
        { "a 1", -1, "a1" },
        { "x", -1, "X" },
        { "d", -1, "D" },
        { " -a", -1, " -A" },
        { "a1", -1, "A1" },
        { "00.", 1, "." },
        { "\t\"\\4", -1, "+)?cd2" },
        { " 0a ", -1, "1..aA" },
        { "c7c", 1, "<*e" },
        { "0 -0A", -1, "10.-10 " },
        { "A.A.- 1 ", 1, "-a 0AA0" },
        { "A A", -1, "AA0Aaa-" },
        { "aa1 A", 1, "0  11" },
        { ">$@%a:", 1, "\\#,3;2" },
        { "]._y", -1, "9}6" },
        { "Aaa", 1, "-Aa.1A-" },
        { "- a0.1-0", -1, ".. 0a1 " },
        { "C9", 1, "87@:=4" },
        { ".-A0", -1, "A." },
        { "3*[*!C", -1, "Z\t9" },
        { ".aAa ", 1, "-110" },
        { "  ", -1, " 0A1A 0" },
        { "10a-", -1, "A.0A-" },
        { "-a-", -1, "A.a1-" },
        { ")+ ", -1, "a2ezz" },
        { ")>", -1, "ZD=AX\t" },
        { "0 A0 0a1", -1, "11.0" },
        { "a. ...- ", 1, "0 0aa-a" },
        { " --A", -1, "11..a" },
        { " a", -1, "-.0." },
        { "--.aA-1", -1, "A " },
        { "a{7e", 1, "';({(7" },
        { "A.aa0-.", 1, " a" },
        { "4:&>,,]", -1, "a] ]^>9" },
    };

    [Theory]
    [MemberData(nameof(FoundationAdversarialPairs))]
    public void CompareNaturalAgreesWithFoundationOnAdversarialAscii(string a, int sign, string b)
    {
        Assert.Equal(sign, Math.Sign(ExampleLibrary.CompareNatural(a, b)));
        Assert.Equal(-sign, Math.Sign(ExampleLibrary.CompareNatural(b, a)));
    }

    [Fact]
    public void GuardsNoOtherTestKilled()
    {
        // 200 is an on-disk contract shared by three apps, so it is pinned as a LITERAL (as the Kotlin suite does),
        // not through the constant the implementation reads: MaxEntries = 201 passed every other test.
        Assert.Equal(200, ViewModeMemory.MaxEntries);
        IReadOnlyList<ViewModeMemory.Entry> list = [];
        for (var i = 0; i < 250; i++) list = ViewModeMemory.Touched(list, Hex16(i), ViewMode.Edit);
        Assert.Equal(200, list.Count);
        Assert.Equal(Hex16(249), list[0].Identity);
        Assert.Equal(Hex16(50), list[^1].Identity);

        // Half away from zero, as Swift's .rounded() and Kotlin's roundToInt: none of the seven shipped sizes lands
        // on .5, so banker's rounding passed every other test. 48 x 43.84375 / 841.8 is exactly 2.5 in IEEE doubles
        // and 56 x 15.942857142857145 / 595.2 exactly 1.5: "3px 2px", where ToEven would print "2px 2px".
        var half = new PageSize("half", "Half", 15.942857142857145, 43.84375);
        Assert.Equal(2.5, 48 * half.Height / PageSize.A4.Height);
        Assert.Equal(1.5, 56 * half.Width / PageSize.A4.Width);
        Assert.Equal("3px 2px", half.CssPadding);

        // Named is exact and case-sensitive. Named("A4") == A4 cannot tell a case fold from the fallback (the Kotlin
        // pin has the same blind spot); a wrong-case or padded id of a NON-default size must fall back to A4.
        Assert.Equal(PageSize.UsLetter, PageSize.Named("letter"));
        Assert.Equal(PageSize.A4, PageSize.Named("LETTER"));
        Assert.Equal(PageSize.A4, PageSize.Named("Legal"));
        Assert.Equal(PageSize.A4, PageSize.Named("6X9"));
        Assert.Equal(PageSize.A4, PageSize.Named(" letter"));
        Assert.Equal(PageSize.A4, PageSize.Named("letter "));
    }
}
