using Md.Core.Document;

namespace Md.Core.Tests;

// The Examples menu model (shell.md §4): the nine bundled names in order, the prefix-stripping rule, the
// two behaviours the Swift pins that Android does not ("01-" stays; digits are ASCII only), and the
// natural order — every vector below was produced by Foundation's localizedStandardCompare on macOS
// (2026-09-06), which is what the Mac sorts the menu with.
public class ExampleLibraryTests
{
    private static readonly string[] MenuOrder =
    [
        "01-Welcome.md", "02-Formatting.md", "03-Tables.md", "04-Code.md", "05-Images.md",
        "06-Math.md", "07-Diagrams.md", "08-Plots.md", "09-Writer Tools.md",
    ];

    [Fact]
    public void BundledExamplesListInMenuOrderWithPrefixesStripped()
    {
        // The fixture folder is the shipped Examples/ top level; the listing arrives in whatever order the
        // file system gives, so shuffle before asking.
        var listing = Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples"), "*.md")
            .Select(Path.GetFileName)
            .Select(name => name!)
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(9, listing.Length);

        var examples = ExampleLibrary.FromListing(listing);
        Assert.Equal(MenuOrder, examples.Select(e => e.FileName));
        Assert.Equal(
            new[] { "Welcome", "Formatting", "Tables", "Code", "Images", "Math", "Diagrams", "Plots", "Writer Tools" },
            examples.Select(e => e.Name));
    }

    [Fact]
    public void DisplayNameStripsTheOrderingPrefixOnlyWhenSomethingRemains()
    {
        Assert.Equal("Welcome", ExampleLibrary.DisplayName("01-Welcome.md"));
        Assert.Equal("Writer Tools", ExampleLibrary.DisplayName("09-Writer Tools.md"));
        Assert.Equal("Bond", ExampleLibrary.DisplayName("007-Bond.md"));
        Assert.Equal("2-3", ExampleLibrary.DisplayName("1-2-3.md"));
        // No prefix, no change.
        Assert.Equal("Welcome", ExampleLibrary.DisplayName("Welcome.md"));
        Assert.Equal("01Welcome", ExampleLibrary.DisplayName("01Welcome.md"));
        Assert.Equal("-Welcome", ExampleLibrary.DisplayName("-Welcome.md"));
        // The Swift guard Android lacks: "01-" stays "01-" rather than becoming "".
        Assert.Equal("01-", ExampleLibrary.DisplayName("01-.md"));
        // Digits are ASCII only (isASCII && isNumber) — full-width and Arabic-Indic digits are not a prefix.
        Assert.Equal("０１-Fullwidth", ExampleLibrary.DisplayName("０１-Fullwidth.md"));
        Assert.Equal("١-Arabic", ExampleLibrary.DisplayName("١-Arabic.md"));
        // A dash fused with a combining mark, a ZWJ or a variation selector is not the Character "-" in
        // Swift, so the prefix stays.
        Assert.Equal("01-́Acute", ExampleLibrary.DisplayName("01-́Acute.md"));
        Assert.Equal("01-‍Joined", ExampleLibrary.DisplayName("01-‍Joined.md"));
        Assert.Equal("01-️Selected", ExampleLibrary.DisplayName("01-️Selected.md"));
        // A digit fused with a mark is not an ASCII digit Character either.
        Assert.Equal("0́-Acute", ExampleLibrary.DisplayName("0́-Acute.md"));
        // Only a real extension is dropped: a bare ".md" has none in Foundation.
        Assert.Equal(".md", ExampleLibrary.DisplayName(".md"));
        Assert.Equal("Plain", ExampleLibrary.DisplayName("Plain"));
    }

    [Fact]
    public void ListingKeepsOnlyMarkdownFilesAndSortsNaturally()
    {
        var examples = ExampleLibrary.FromListing(
            ["10-Ten.md", "2-Two.md", "notes.txt", "Example Book", "README.MD", ".md", "b.md", "A.md", "1-One.md"]);
        Assert.Equal(new[] { "1-One.md", "2-Two.md", "10-Ten.md", "A.md", "b.md" }, examples.Select(e => e.FileName));
        Assert.Equal(new[] { "One", "Two", "Ten", "A", "b" }, examples.Select(e => e.Name));

        Assert.True(ExampleLibrary.IsExampleFile("x.md"));
        Assert.False(ExampleLibrary.IsExampleFile(".md"));
        Assert.False(ExampleLibrary.IsExampleFile("x.MD"));
        Assert.False(ExampleLibrary.IsExampleFile("x.markdown"));
    }

    // MARK: The natural order, against Foundation

    /// <summary>
    /// Each row is <c>a</c>, the sign <c>localizedStandardCompare</c> returned, <c>b</c>. Numeric runs by
    /// value; letters case-blind; punctuation before digits before letters; a shorter prefix first; then
    /// one left-to-right walk over leading zeros (fewer first) and case (lower first), whichever comes first.
    /// </summary>
    public static TheoryData<string, int, string> FoundationPairs => new()
    {
        { "2-Two.md", -1, "10-Ten.md" },
        { "02-b.md", 1, "2-a.md" },
        { "a.md", -1, "B.md" },
        { "Tables.md", 1, "Tables (copy).md" },
        { "x.md", 1, "x (2).md" },
        { "1-x.md", -1, "01-x.md" },
        { "1.md", -1, "01.md" },
        { "00", 1, "0" },
        { "A.md", 1, "a.md" },
        { "b.md", -1, "B.md" },
        { "a.md", -1, "a.MD" },
        { "01-Welcome.md", 1, "01-welcome.md" },
        { "a.md", 1, "a-b.md" },
        { "a b.md", -1, "a.md" },
        { "a_b.md", -1, "a.md" },
        { "a-1.md", -1, "a.1.md" },
        { "a(1).md", 1, "a-1.md" },
        { "a1.md", 1, "a.md" },
        { "ab.md", 1, "A.md" },
        { "a", -1, "a.md" },
        { "A.md", -1, "b.md" },
        { "Z.md", 1, "a.md" },
        { "z.md", 1, "A.md" },
        { "09-Writer Tools.md", -1, "10-X.md" },
        { "Writer Tools.md", -1, "Writer-Tools.md" },
        { "x 1.md", -1, "x1.md" },
        { "x2.md", -1, "x10.md" },
        { "x02.md", 1, "x2.md" },
        { "a9.md", -1, "a10.md" },
        { "a10b2.md", -1, "a10b10.md" },
        // The second walk is one walk: whichever of zeros and case differs first from the left decides.
        { "01-a.md", 1, "1-A.md" },
        { "A1.md", 1, "a01.md" },
        { "x1y.md", -1, "x01Y.md" },
        { "aB.md", -1, "Ab.md" },
        { "1-01.md", -1, "01-1.md" },
        { "01-x.md", -1, "001-x.md" },
        { "1-2.md", -1, "1-02.md" },
        { "01-2.md", 1, "1-02.md" },
        // A letter difference outranks any number of zeros.
        { "1-b.md", 1, "01-a.md" },
        // Runs of any length compare by value — no integer parse to overflow.
        { "9999999999999999999999.md", -1, "10000000000000000000000.md" },
        { "1234567890123456789012345678901234567890.md", 1, "999.md" },
        { "1-x.md", 0, "1-x.md" },
        { "a.md", 0, "a.md" },
    };

    [Theory]
    [MemberData(nameof(FoundationPairs))]
    public void CompareNaturalAgreesWithFoundation(string a, int sign, string b)
    {
        Assert.Equal(sign, Math.Sign(ExampleLibrary.CompareNatural(a, b)));
        Assert.Equal(-sign, Math.Sign(ExampleLibrary.CompareNatural(b, a)));
    }

    [Fact]
    public void CompareNaturalOrdersAsciiPunctuationLikeFoundation()
    {
        // TAB, space, then the 33 printable non-alphanumerics in root-collation order — not ASCII order —
        // then any digit, then any letter. Exactly Foundation's sequence, on its own and between letters.
        string[] order =
        [
            "\t", " ", "_", "-", ",", ";", ":", "!", "?", ".", "'", "\"", "(", ")", "[", "]", "{", "}",
            "@", "*", "/", "\\", "&", "#", "%", "`", "^", "+", "<", "=", ">", "|", "~", "$",
        ];
        Assert.Equal(34, order.Length);
        Assert.Equal(34, order.Distinct(StringComparer.Ordinal).Count());
        for (var i = 1; i < order.Length; i++)
        {
            Assert.True(ExampleLibrary.CompareNatural(order[i - 1], order[i]) < 0, order[i - 1] + " < " + order[i]);
            Assert.True(ExampleLibrary.CompareNatural("a" + order[i - 1] + "b", "a" + order[i] + "b") < 0);
        }
        foreach (var punctuation in order)
        {
            Assert.True(ExampleLibrary.CompareNatural(punctuation, "0") < 0);
            Assert.True(ExampleLibrary.CompareNatural(punctuation, "a") < 0);
        }
        Assert.True(ExampleLibrary.CompareNatural("1", "a") < 0);
        Assert.True(ExampleLibrary.CompareNatural("9", "A") < 0);

        var shuffled = order.OrderByDescending(s => s, StringComparer.Ordinal).ToList();
        shuffled.Sort(ExampleLibrary.CompareNatural);
        Assert.Equal(order, shuffled);
    }

    [Fact]
    public void CompareNaturalSortsWholeListsLikeFoundation()
    {
        static string[] Sorted(params string[] names)
        {
            var list = names.OrderByDescending(s => s, StringComparer.Ordinal).ToList();
            list.Sort(ExampleLibrary.CompareNatural);
            return list.ToArray();
        }

        Assert.Equal(
            new[] { "1-One.md", "1-x.md", "01-x.md", "2-Two.md", "10-Ten.md", "a.md", "A.md", "b.md", "Tables (copy).md", "Tables.md" },
            Sorted("10-Ten.md", "2-Two.md", "b.md", "A.md", "1-One.md", "Tables.md", "Tables (copy).md", "a.md", "01-x.md", "1-x.md"));
        Assert.Equal(
            new[] { "1-1.md", "1-01.md", "01-1.md", "01-01.md", "001-1.md" },
            Sorted("1-01.md", "01-1.md", "1-1.md", "01-01.md", "001-1.md"));
        Assert.Equal(new[] { "a.md", "a.MD", "A.md", "A.MD" }, Sorted("a.md", "A.md", "a.MD", "A.MD"));
        Assert.Equal(new[] { "ab.md", "aB.md", "Ab.md", "AB.md" }, Sorted("aB.md", "Ab.md", "ab.md", "AB.md"));
        Assert.Equal(new[] { "1-a.md", "1-A.md", "01-a.md", "01-A.md" }, Sorted("01-a.md", "1-A.md", "1-a.md", "01-A.md"));
    }
}
