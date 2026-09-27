using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>2026-09-27, the find panel: the count beside the query and find as you type.</summary>
public class FindPanelTests
{
    const string Text = "alpha beta Alpha gamma alpha";
    //                   0          11          23

    [Fact]
    public void TheCountPlacesTheSelectedHitAmongAllOfThem()
    {
        Assert.Equal(new TextSearch.Tally(1, 3), TextSearch.Count(Text, "alpha", 0, 5));
        Assert.Equal(new TextSearch.Tally(2, 3), TextSearch.Count(Text, "ALPHA", 11, 5));
        Assert.Equal(new TextSearch.Tally(3, 3), TextSearch.Count(Text, "alpha", 23, 5));
    }

    [Fact]
    public void OffAHitTheCountHasNoCurrent()
    {
        Assert.Equal(new TextSearch.Tally(0, 3), TextSearch.Count(Text, "alpha", 6, 0));
        Assert.Equal(new TextSearch.Tally(0, 3), TextSearch.Count(Text, "alpha", 0, 4));
        Assert.Equal(new TextSearch.Tally(0, 0), TextSearch.Count(Text, "delta", 0, 0));
        Assert.Equal(new TextSearch.Tally(0, 0), TextSearch.Count(Text, "", 0, 0));
        Assert.Equal(new TextSearch.Tally(0, 0), TextSearch.Count("ab", "abc", 0, 0));
    }

    [Fact]
    public void TheTotalIsWhatReplaceAllWouldReplace()
    {
        foreach (var (text, query) in new[] { ("aaaa", "aa"), ("aaa", "aa"), (Text, "a"), ("x", "x"), ("", "x") })
            Assert.Equal(TextSearch.ReplaceAll(text, query, "-").Count, TextSearch.Count(text, query, 0, 0).Total);
    }

    [Fact]
    public void AnOverlappingHitIsPlacedAfterTheCountedOnesBeforeIt()
    {
        // "aaaa" / "aa" counts 0 and 2; the hit at 1 overlaps the first and is the second.
        Assert.Equal(new TextSearch.Tally(2, 2), TextSearch.Count("aaaa", "aa", 1, 2));
        // …and never past the total: "aaa" counts only the hit at 0, so the one at 1 is "1 of 1", not "2 of 1".
        Assert.Equal(new TextSearch.Tally(1, 1), TextSearch.Count("aaa", "aa", 1, 2));
    }

    [Fact]
    public void TypingSearchesFromWhereTheCaretWas()
    {
        var find = new FindAsYouType();
        find.Focused(12);
        Assert.Equal(new TextSearch.Match(15, 1), find.Search(Text, "a"));             // the last letter of Alpha
        Assert.Equal(new TextSearch.Match(23, 5), find.Search(Text, "alpha"));
        Assert.Equal(new TextSearch.Match(23, 4), find.Search(Text, "alph"));     // Backspace: still from the caret
    }

    [Fact]
    public void ALongerQueryKeepsTheHitItAlreadyHas()
    {
        var find = new FindAsYouType();
        find.Focused(0);
        var hit = TextSearch.Next(Text, "al", 1)!.Value;      // Next: the Alpha at 11
        find.Stepped(hit);
        Assert.Equal(new TextSearch.Match(11, 3), find.Search(Text, "alp"));
        Assert.Equal(new TextSearch.Match(11, 5), find.Search(Text, "alpha"));
    }

    [Fact]
    public void TypingWrapsAndAnEmptyOrAbsentQueryFindsNothing()
    {
        var find = new FindAsYouType();
        find.Focused(24);
        Assert.Equal(new TextSearch.Match(6, 4), find.Search(Text, "beta"));
        Assert.Null(find.Search(Text, ""));
        Assert.Null(find.Search(Text, "delta"));
        find.Focused(-3);
        Assert.Equal(0, find.Anchor);
    }

    [Fact]
    public void TheCountReadsLikeWindows()
    {
        Assert.Equal("", Strings.Find.Tally(0, 0, hasQuery: false));
        Assert.Equal("No results", Strings.Find.Tally(0, 0, hasQuery: true));
        Assert.Equal("3 of 12", Strings.Find.Tally(3, 12, hasQuery: true));
        Assert.Equal("12 results", Strings.Find.Tally(0, 12, hasQuery: true));
        Assert.Equal("1 result", Strings.Find.Tally(0, 1, hasQuery: true));
        Assert.Equal("1 of 1", Strings.Find.Tally(1, 1, hasQuery: true));
    }
}
