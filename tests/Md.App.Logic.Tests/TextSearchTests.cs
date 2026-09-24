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

    // ── §3.5 Replace: the same one matching rule, and nothing decided in the shell ────────────

    /// <summary>The whole of Replace's decision: is the selection the hit we are standing on?</summary>
    [Fact]
    public void TheSelectionIsTheMatchOnlyWhenItSpansOneHitExactly()
    {
        Assert.True(TextSearch.SelectionIsMatch(Text, "alpha", 0, 5));
        Assert.True(TextSearch.SelectionIsMatch(Text, "alpha", 11, 5));    // "Alpha" — case-insensitive
        Assert.True(TextSearch.SelectionIsMatch(Text, "ALPHA", 23, 5));
        Assert.True(TextSearch.SelectionIsMatch(Text, "alpha", 23, 5));    // the hit at the very end

        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", 0, 0));    // a caret is not a match
        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", 0, 4));    // short of the query
        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", 0, 6));    // past it
        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", 1, 5));    // the nearest hit is elsewhere
        Assert.False(TextSearch.SelectionIsMatch(Text, "", 0, 0));         // an empty query matches nothing
        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", -1, 5));   // off either end, rather than a throw
        Assert.False(TextSearch.SelectionIsMatch(Text, "alpha", 24, 5));
        Assert.False(TextSearch.SelectionIsMatch("straße", "strasse", 0, 7));   // ordinal, like the search
    }

    [Fact]
    public void ReplaceEditsTheSelectionWhenItIsTheHitAndSearchesOnFromAfterTheReplacement()
    {
        var step = TextSearch.Replace(Text, "alpha", "omega", 11, 5);
        Assert.Equal(new TextSearch.Edit(11, 5, "omega"), step.Apply);
        Assert.Equal(16, step.SearchFrom);

        // What the window then does: apply, re-read, Next from SearchFrom.
        var after = Applied(Text, step.Apply!.Value);
        Assert.Equal("alpha beta omega gamma alpha", after);
        Assert.Equal(new TextSearch.Match(23, 5), TextSearch.Next(after, "alpha", step.SearchFrom));
    }

    [Fact]
    public void ReplaceIsAPlainFindNextWhenTheSelectionIsNotTheHit()
    {
        // A caret inside the word, a selection of the wrong length, and one over other text.
        foreach (var (start, length) in new[] { (2, 0), (0, 4), (6, 4) })
        {
            var step = TextSearch.Replace(Text, "alpha", "omega", start, length);
            Assert.Null(step.Apply);
            Assert.Equal(start + length, step.SearchFrom);
        }

        Assert.Null(TextSearch.Replace(Text, "", "omega", 0, 0).Apply);
        Assert.Null(TextSearch.Replace(Text, "zeta", "omega", 0, 4).Apply);
    }

    [Fact]
    public void ReplaceClampsASelectionTheControlCouldNotHaveHad()
    {
        var step = TextSearch.Replace(Text, "alpha", "omega", 9999, 9999);
        Assert.Null(step.Apply);
        Assert.Equal(Text.Length, step.SearchFrom);

        var negative = TextSearch.Replace(Text, "alpha", "omega", -4, -4);
        Assert.Null(negative.Apply);
        Assert.Equal(0, negative.SearchFrom);
    }

    [Fact]
    public void ReplaceAtTheVeryStartAndTheVeryEndAreBothTheHitTheyStandOn()
    {
        var first = TextSearch.Replace(Text, "alpha", "x", 0, 5);
        Assert.Equal(new TextSearch.Edit(0, 5, "x"), first.Apply);
        Assert.Equal(1, first.SearchFrom);
        Assert.Equal("x beta Alpha gamma alpha", Applied(Text, first.Apply!.Value));

        var last = TextSearch.Replace(Text, "alpha", "x", 23, 5);
        Assert.Equal(new TextSearch.Edit(23, 5, "x"), last.Apply);
        Assert.Equal(24, last.SearchFrom);
        Assert.Equal("alpha beta Alpha gamma x", Applied(Text, last.Apply!.Value));
    }

    /// <summary>
    /// The loop this rule exists to prevent: "a" → "aa" is a replacement that contains the query,
    /// and SearchFrom is past what went in, so the very next Next cannot land inside it.
    /// </summary>
    [Fact]
    public void AReplacementThatContainsTheQueryIsNotSearchedAgain()
    {
        var step = TextSearch.Replace("aaa", "a", "aa", 0, 1);
        Assert.Equal(new TextSearch.Edit(0, 1, "aa"), step.Apply);
        Assert.Equal(2, step.SearchFrom);
        Assert.Equal("aaaa", Applied("aaa", step.Apply!.Value));
        Assert.Equal(new TextSearch.Match(2, 1), TextSearch.Next("aaaa", "a", step.SearchFrom));
    }

    [Fact]
    public void AReplacementEqualToTheQueryStillCountsAndStillMoves()
    {
        var step = TextSearch.Replace(Text, "alpha", "alpha", 11, 5);
        Assert.Equal(new TextSearch.Edit(11, 5, "alpha"), step.Apply);
        Assert.Equal(16, step.SearchFrom);
        // "Alpha" became "alpha": the text DID change, which is the point of a case-insensitive find.
        Assert.Equal("alpha beta alpha gamma alpha", Applied(Text, step.Apply!.Value));
    }

    [Fact]
    public void AnEmptyReplacementIsADeletion()
    {
        var step = TextSearch.Replace(Text, "alpha ", "", 0, 6);
        Assert.Equal(new TextSearch.Edit(0, 6, ""), step.Apply);
        Assert.Equal(0, step.SearchFrom);
        Assert.Equal("beta Alpha gamma alpha", Applied(Text, step.Apply!.Value));
    }

    // ── Replace All ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAllReplacesEveryHitCaseInsensitivelyAndCountsThem()
    {
        var plan = TextSearch.ReplaceAll(Text, "alpha", "omega");
        Assert.Equal(3, plan.Count);
        Assert.Equal("omega beta omega gamma omega", plan.Text);
        AssertPlanIsOneEdit(Text, plan);
    }

    /// <summary>
    /// The edit is the span from the first hit to the last, not the whole document: that is what
    /// lets the shell put it in with one <c>SelectedText</c> assignment (one undo unit) without
    /// rewriting text nobody asked it to touch.
    /// </summary>
    [Fact]
    public void TheReplaceAllPlanIsTheOneSpanFromTheFirstHitToTheLast()
    {
        const string text = "head alpha middle alpha tail";
        var plan = TextSearch.ReplaceAll(text, "alpha", "X");
        Assert.Equal(2, plan.Count);
        Assert.Equal("head X middle X tail", plan.Text);
        Assert.Equal(new TextSearch.Edit(5, 18, "X middle X"), plan.Apply);
        AssertPlanIsOneEdit(text, plan);
    }

    [Fact]
    public void ReplaceAllWithNothingToDoLeavesTheTextAndPlansAnEmptyEdit()
    {
        foreach (var query in new[] { "", "zeta", Text + " and more" })
        {
            var plan = TextSearch.ReplaceAll(Text, query, "X");
            Assert.Equal(0, plan.Count);
            Assert.Equal(Text, plan.Text);
            Assert.Equal(new TextSearch.Edit(0, 0, ""), plan.Apply);
            AssertPlanIsOneEdit(Text, plan);
        }
    }

    [Fact]
    public void ReplaceAllTakesHitsAtTheVeryStartAndTheVeryEnd()
    {
        var plan = TextSearch.ReplaceAll("xx middle xx", "xx", "Y");
        Assert.Equal(2, plan.Count);
        Assert.Equal("Y middle Y", plan.Text);
        Assert.Equal(new TextSearch.Edit(0, 12, "Y middle Y"), plan.Apply);
        AssertPlanIsOneEdit("xx middle xx", plan);
    }

    /// <summary>Hits are taken left to right and never overlap: "aa" over "aaaa" is two, over "aaaaa" two with an "a" left over.</summary>
    [Fact]
    public void OverlappingCandidatesAreTakenLeftToRightWithoutOverlapping()
    {
        var four = TextSearch.ReplaceAll("aaaa", "aa", "b");
        Assert.Equal(2, four.Count);
        Assert.Equal("bb", four.Text);
        AssertPlanIsOneEdit("aaaa", four);

        var five = TextSearch.ReplaceAll("aaaaa", "aa", "b");
        Assert.Equal(2, five.Count);
        Assert.Equal("bba", five.Text);
        Assert.Equal(new TextSearch.Edit(0, 4, "bb"), five.Apply);
        AssertPlanIsOneEdit("aaaaa", five);
    }

    /// <summary>One pass, left to right, never rescanning what it inserted — or this test never returns.</summary>
    [Fact]
    public void AReplacementContainingTheQueryReplacesEachHitExactlyOnce()
    {
        var plan = TextSearch.ReplaceAll("aaa", "a", "aa");
        Assert.Equal(3, plan.Count);
        Assert.Equal("aaaaaa", plan.Text);
        AssertPlanIsOneEdit("aaa", plan);

        var longer = TextSearch.ReplaceAll("one two", "o", "foo");
        Assert.Equal(2, longer.Count);
        Assert.Equal("foone twfoo", longer.Text);
        AssertPlanIsOneEdit("one two", longer);
    }

    [Fact]
    public void AReplacementEqualToTheQueryOnlyChangesTheCasingThatDiffered()
    {
        var plan = TextSearch.ReplaceAll(Text, "alpha", "alpha");
        Assert.Equal(3, plan.Count);
        Assert.Equal("alpha beta alpha gamma alpha", plan.Text);
        AssertPlanIsOneEdit(Text, plan);

        // Nothing differed: the text comes back byte for byte, and the count still says what happened.
        var same = TextSearch.ReplaceAll("alpha alpha", "alpha", "alpha");
        Assert.Equal(2, same.Count);
        Assert.Equal("alpha alpha", same.Text);
        AssertPlanIsOneEdit("alpha alpha", same);
    }

    /// <summary>
    /// The TextBox reports lone CRs and a file may hold CRLF; the engine is ordinal over UTF-16
    /// units and never treats a line end as anything else — including a query that spans one.
    /// </summary>
    [Fact]
    public void LineEndsAreOrdinaryCharactersOnBothSidesOfTheReplace()
    {
        const string crlf = "one\r\ntwo\r\nthree";
        var plan = TextSearch.ReplaceAll(crlf, "\r\n", "\n");
        Assert.Equal(2, plan.Count);
        Assert.Equal("one\ntwo\nthree", plan.Text);
        AssertPlanIsOneEdit(crlf, plan);

        // A query with a line end inside it, replaced by text with one inside it.
        var joined = TextSearch.ReplaceAll(crlf, "two\r\n", "TWO\r");
        Assert.Equal(1, joined.Count);
        Assert.Equal("one\r\nTWO\rthree", joined.Text);
        AssertPlanIsOneEdit(crlf, joined);

        // And the step: a CR is a unit like any other, so the offsets stay the box's own.
        var step = TextSearch.Replace("a\rb\rc", "\r", "\r\r", 1, 1);
        Assert.Equal(new TextSearch.Edit(1, 1, "\r\r"), step.Apply);
        Assert.Equal(3, step.SearchFrom);
        Assert.Equal("a\r\rb\rc", Applied("a\rb\rc", step.Apply!.Value));
    }

    /// <summary>
    /// Offsets are UTF-16 units (§0.1), so an emoji on either side of the hit moves it by two and
    /// the edit must still land on the hit and never inside a pair.
    /// </summary>
    [Fact]
    public void SurrogatePairsOnBothSidesOfTheMatchAreCountedAsTwoUnitsEach()
    {
        const string text = "\U0001F600needle\U0001F600needle\U0001F600";
        Assert.Equal(2, TextSearch.Next(text, "needle", 0)!.Value.Index);

        var step = TextSearch.Replace(text, "needle", "\U0001F680", 2, 6);
        Assert.Equal(new TextSearch.Edit(2, 6, "\U0001F680"), step.Apply);
        Assert.Equal(4, step.SearchFrom);
        Assert.Equal("\U0001F600\U0001F680\U0001F600needle\U0001F600", Applied(text, step.Apply!.Value));

        var plan = TextSearch.ReplaceAll(text, "needle", "\U0001F680");
        Assert.Equal(2, plan.Count);
        Assert.Equal("\U0001F600\U0001F680\U0001F600\U0001F680\U0001F600", plan.Text);
        Assert.Equal(new TextSearch.Edit(2, 14, "\U0001F680\U0001F600\U0001F680"), plan.Apply);
        AssertPlanIsOneEdit(text, plan);

        // A pair as the query itself: two units in, whatever goes out.
        var emoji = TextSearch.ReplaceAll(text, "\U0001F600", "!");
        Assert.Equal(3, emoji.Count);
        Assert.Equal("!needle!needle!", emoji.Text);
        AssertPlanIsOneEdit(text, emoji);
    }

    [Fact]
    public void ReplaceAndReplaceAllRefuseANullArgumentRatherThanInventOne()
    {
        Assert.Throws<ArgumentNullException>(() => TextSearch.Replace(null!, "a", "b", 0, 1));
        Assert.Throws<ArgumentNullException>(() => TextSearch.Replace("a", null!, "b", 0, 1));
        Assert.Throws<ArgumentNullException>(() => TextSearch.Replace("a", "a", null!, 0, 1));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll(null!, "a", "b"));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll("a", null!, "b"));
        Assert.Throws<ArgumentNullException>(() => TextSearch.ReplaceAll("a", "a", null!));
    }

    /// <summary>What the shell does with one edit: that span replaced, nothing else touched.</summary>
    static string Applied(string text, TextSearch.Edit edit) =>
        string.Concat(text.AsSpan(0, edit.Start), edit.Text, text.AsSpan(edit.Start + edit.Length));

    /// <summary>
    /// The plan's two halves agree: applying the single edit gives the whole new text. This is the
    /// invariant the one-undo-step apply path rests on — the shell assigns <c>Apply.Text</c> over
    /// <c>Apply.Start</c> / <c>Apply.Length</c> and must get <c>Text</c>.
    /// </summary>
    static void AssertPlanIsOneEdit(string text, TextSearch.ReplaceAllPlan plan) =>
        Assert.Equal(plan.Text, Applied(text, plan.Apply));
}
