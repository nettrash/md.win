using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>§3.5: the find bar wraps, ignores case, and hands back a range the TextBox can select as is.</summary>
public class TextSearchTests
{
    const string Text = "alpha beta Alpha gamma alpha";
    //                   0          11          23

    [Fact]
    public void NextFindsTheFirstHitAtOrAfterTheCaret()
    {
        Assert.Equal(new TextSearch.Match(0, 5), TextSearch.Next(Text, "alpha", 0));
        Assert.Equal(new TextSearch.Match(11, 5), TextSearch.Next(Text, "alpha", 1));
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Next(Text, "alpha", 12));
    }

    [Fact]
    public void NextWrapsToTheTopWhenNothingFollows()
    {
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Next(Text, "alpha", 23));   // "at or after"
        Assert.Equal(new TextSearch.Match(0, 5), TextSearch.Next(Text, "alpha", 24));
        Assert.Equal(new TextSearch.Match(0, 5), TextSearch.Next(Text, "alpha", Text.Length));
        Assert.Equal(new TextSearch.Match(0, 5), TextSearch.Next(Text, "alpha", 9999));
    }

    [Fact]
    public void PreviousFindsTheLastHitStartingBeforeTheCaret()
    {
        Assert.Equal(new TextSearch.Match(11, 5), TextSearch.Previous(Text, "alpha", 22));
        Assert.Equal(new TextSearch.Match(0, 5), TextSearch.Previous(Text, "alpha", 11));
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Previous(Text, "alpha", Text.Length));
    }

    [Fact]
    public void PreviousWrapsToTheBottomWhenNothingPrecedes()
    {
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Previous(Text, "alpha", 0));
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Previous(Text, "alpha", -5));
    }

    [Fact]
    public void MatchingIsOrdinalAndCaseInsensitive()
    {
        Assert.Equal(new TextSearch.Match(11, 5), TextSearch.Next(Text, "ALPHA", 1));
        // Ordinal, so no culture folds these together and the length always equals the query's.
        Assert.Null(TextSearch.Next("straße", "strasse", 0));
        Assert.Equal(6, TextSearch.Next("Straße x", "straße", 0)!.Value.Length);
    }

    [Fact]
    public void AnEmptyOrOversizedQueryFindsNothing()
    {
        Assert.Null(TextSearch.Next(Text, "", 0));
        Assert.Null(TextSearch.Previous(Text, "", 0));
        Assert.Null(TextSearch.Next("ab", "abc", 0));
        Assert.Null(TextSearch.Next(Text, "zeta", 0));
        Assert.Null(TextSearch.Previous(Text, "zeta", 0));
    }

    [Fact]
    public void ASingleHitIsFoundFromAnywhereInBothDirections()
    {
        const string one = "xxxNEEDLExxx";
        Assert.Equal(new TextSearch.Match(3, 6), TextSearch.Next(one, "needle", 0));
        Assert.Equal(new TextSearch.Match(3, 6), TextSearch.Next(one, "needle", 5));   // wraps onto itself
        Assert.Equal(new TextSearch.Match(3, 6), TextSearch.Previous(one, "needle", 0));
        Assert.Equal(new TextSearch.Match(3, 6), TextSearch.Previous(one, "needle", one.Length));
    }

    [Fact]
    public void OverlappingHitsAreEachReachable()
    {
        const string aaa = "aaaa";
        Assert.Equal(new TextSearch.Match(0, 2), TextSearch.Next(aaa, "aa", 0));
        Assert.Equal(new TextSearch.Match(1, 2), TextSearch.Next(aaa, "aa", 1));
        Assert.Equal(new TextSearch.Match(2, 2), TextSearch.Next(aaa, "aa", 2));
        Assert.Equal(new TextSearch.Match(2, 2), TextSearch.Previous(aaa, "aa", 3));
    }
}
