using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// The reading-order comparator (mdTests: testOrderedPutsNumberedNamesFirstInNumericOrder,
/// testOrderedFallsBackToFinderAlphabetical, testOrderedTreatsOverflowingDigitRunsAsUnnumbered)
/// and the Kotlin BookOrderTest vectors for leadingNumber, naturalCompare and bookNameOrder,
/// which this port reproduces instead of localizedStandardCompare.
/// </summary>
public class BookOrderTests
{
    [Fact]
    public void OrderedPutsNumberedNamesFirstInNumericOrder()
    {
        // Numeric, not lexicographic: 2 before 10; numbered before not.
        Assert.True(BookOrder.Ordered("2. setup", "10-ending"));
        Assert.False(BookOrder.Ordered("10-ending", "2. setup"));
        Assert.True(BookOrder.Ordered("01-intro", "appendix"));
        Assert.False(BookOrder.Ordered("appendix", "01-intro"));
    }

    [Fact]
    public void OrderedFallsBackToFinderAlphabetical()
    {
        Assert.True(BookOrder.Ordered("apple", "Banana"));
        Assert.False(BookOrder.Ordered("Banana", "apple"));
        // Equal leading numbers tie-break alphabetically too.
        Assert.True(BookOrder.Ordered("01-a", "01-b"));
    }

    [Fact]
    public void OrderedTreatsOverflowingDigitRunsAsUnnumbered()
    {
        // A digit run too long for Int64 must not throw — it sorts as unnumbered.
        Assert.True(BookOrder.Ordered("1-x", "99999999999999999999-y"));
        Assert.False(BookOrder.Ordered("99999999999999999999-y", "1-x"));
    }

    [Fact]
    public void OrderedIsStrict()
    {
        Assert.False(BookOrder.Ordered("draft", "draft"));
        Assert.Equal(0, BookOrder.Compare("draft", "draft"));
    }

    [Fact]
    public void LeadingDigitsParse()
    {
        Assert.Equal(1L, BookOrder.LeadingNumber("01-intro"));
        Assert.Equal(2L, BookOrder.LeadingNumber("2. setup"));
        Assert.Equal(10L, BookOrder.LeadingNumber("10"));
        Assert.Equal(7L, BookOrder.LeadingNumber("007"));
    }

    [Fact]
    public void UnnumberedNamesDoNot()
    {
        Assert.Null(BookOrder.LeadingNumber("appendix"));
        Assert.Null(BookOrder.LeadingNumber("-3 degrees"));   // a sign is not a digit
        Assert.Null(BookOrder.LeadingNumber(""));
        Assert.Null(BookOrder.LeadingNumber("99999999999999999999-overflow"));
        Assert.Null(BookOrder.LeadingNumber("\u0663-arabic"));   // only ASCII '0'..'9'
    }

    [Fact]
    public void EmbeddedNumbersCompareNumerically()
    {
        Assert.True(BookOrder.NaturalCompare("part2", "part10") < 0);
        Assert.True(BookOrder.NaturalCompare("part10", "part2") > 0);
        Assert.True(BookOrder.NaturalCompare("v1.9", "v1.10") < 0);
    }

    [Fact]
    public void NaturalCompareIsCaseInsensitiveWithDeterministicTies()
    {
        Assert.True(BookOrder.NaturalCompare("Apple", "banana") < 0);
        Assert.Equal(0, BookOrder.NaturalCompare("draft", "draft"));
        // Same value, different padding / case: ordered, never "equal".
        Assert.NotEqual(0, BookOrder.NaturalCompare("part01", "part1"));
        Assert.NotEqual(0, BookOrder.NaturalCompare("Draft", "draft"));
    }

    [Fact]
    public void NumbersSortNumericallyNotLexicographically()
    {
        Assert.Equal(new[] { "01-intro", "2. setup", "10-deploy" }, Sorted("10-deploy", "01-intro", "2. setup"));
    }

    [Fact]
    public void NumberedComeBeforeUnnumbered()
    {
        Assert.Equal(new[] { "01-intro", "2-body", "appendix", "Zebra" }, Sorted("appendix", "01-intro", "Zebra", "2-body"));
    }

    [Fact]
    public void UnnumberedSortNaturally()
    {
        Assert.Equal(new[] { "Apple", "banana", "cherry" }, Sorted("banana", "Apple", "cherry"));
        // The Finder rule reaches embedded numbers too: part2 before part10.
        Assert.Equal(new[] { "epilogue", "part2", "part10" }, Sorted("part10", "part2", "epilogue"));
    }

    [Fact]
    public void EqualNumbersFallBackToNaturalOrder()
    {
        Assert.Equal(new[] { "01-a", "1-b" }, Sorted("1-b", "01-a"));
    }

    private static string[] Sorted(params string[] names) => names.OrderBy(n => n, BookOrder.Comparer).ToArray();
}
