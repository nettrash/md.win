using Md.App.Logic.View;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-design.md §5.5 — the first fill is immediate, every later one waits 250 ms and is
/// cancelled by the next keystroke, and a result that a newer edit has overtaken is dropped.
/// </summary>
public sealed class DerivedTextSchedulerTests
{
    readonly FakeScheduler _scheduler = new();
    readonly FakeUiThread _ui = new();
    readonly FakeWordCounter _words = new();
    readonly List<DerivedText> _published = [];

    /// <summary>The computation runs where it is asked; the thread-pool hop is the App's business.</summary>
    DerivedTextScheduler Build(Func<string, IReadOnlyList<DiagramRef>>? diagrams = null, DerivedTextScheduler.RunOffThread? offThread = null)
    {
        var scheduler = new DerivedTextScheduler(_scheduler, _ui, _words, diagrams, offThread ?? (work => work()));
        scheduler.Changed += _published.Add;
        return scheduler;
    }

    [Fact]
    public void TheFirstComputationIsImmediate()
    {
        var derived = Build();

        derived.TextChanged("# Title\n\nHello world");

        Assert.Single(_published);
        Assert.Equal(0, _scheduler.PendingTimers);
        Assert.Equal(3, derived.Current.Words);
        Assert.Equal("Title", Assert.Single(derived.Current.Outline).Text);
    }

    [Fact]
    public void EveryLaterComputationWaitsTwoHundredAndFiftyMilliseconds()
    {
        var derived = Build();
        derived.TextChanged("one");
        _published.Clear();

        derived.TextChanged("one two");
        Assert.Empty(_published);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], _scheduler.PendingDelays);

        _scheduler.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Empty(_published);

        _scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, Assert.Single(_published).Words);
    }

    [Fact]
    public void TheNextKeystrokeCancelsThePendingComputation()
    {
        var derived = Build();
        derived.TextChanged("one");
        _published.Clear();

        derived.TextChanged("one two");
        _scheduler.Advance(TimeSpan.FromMilliseconds(200));
        derived.TextChanged("one two three");
        _scheduler.Advance(TimeSpan.FromMilliseconds(200));

        Assert.Empty(_published);
        Assert.Equal(1, _scheduler.PendingTimers);

        _scheduler.Advance(TimeSpan.FromMilliseconds(50));
        Assert.Equal(3, Assert.Single(_published).Words);
    }

    [Fact]
    public void ABurstBeforeTheFirstResultStillOnlyComputesOnce()
    {
        var derived = Build();

        derived.TextChanged("a");
        derived.TextChanged("ab");
        derived.TextChanged("abc");

        Assert.Single(_published);                       // the immediate one
        _scheduler.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(2, _published.Count);
        Assert.Equal(["a", "abc"], _words.Calls);
    }

    [Fact]
    public void AResultOvertakenByANewerEditIsDropped()
    {
        var queued = new List<Action>();
        var derived = Build(offThread: queued.Add);

        derived.TextChanged("two stale words");
        derived.TextChanged("fresh");                    // the second is debounced
        _scheduler.Advance(TimeSpan.FromMilliseconds(250));
        Assert.Equal(2, queued.Count);

        queued[1]();                                     // the newer computation lands first
        queued[0]();                                     // the older one comes back late

        Assert.Single(_published);
        Assert.Equal(["fresh", "two stale words"], _words.Calls);
        Assert.Equal(1, derived.Current.Words);
    }

    [Fact]
    public void CancelDropsBothThePendingTimerAndAnythingInFlight()
    {
        var queued = new List<Action>();
        var derived = Build(offThread: queued.Add);
        derived.TextChanged("one");
        _published.Clear();
        derived.TextChanged("one two");
        _scheduler.Advance(TimeSpan.FromMilliseconds(250));

        derived.Cancel();
        foreach (var work in queued) work();

        Assert.Empty(_published);
        Assert.Equal(0, _scheduler.PendingTimers);
    }

    [Fact]
    public void CharactersAreGraphemeClustersAndNotesAndDiagramsComeAlong()
    {
        var derived = Build(diagrams: _ => [new DiagramRef(0, "mermaid", "graph TD;", "Diagram 1")]);

        derived.TextChanged("e\u0301\U0001F469\u200D\U0001F467\n\n<!-- note: private -->\n");

        var current = derived.Current;
        // 32 UTF-16 units, 27 grapheme clusters: "e + U+0301" is one and the ZWJ family is one.
        Assert.Equal(27, current.Characters);
        Assert.Equal("Diagram 1", Assert.Single(current.Diagrams).MenuTitle);
        Assert.Equal("private", Assert.Single(current.Notes).Text);
    }

    [Fact]
    public void TheResultIsPublishedOnTheUiThread()
    {
        _ui.RunInline = false;
        var derived = Build();

        derived.TextChanged("one two");

        Assert.Empty(_published);
        Assert.Equal(1, _ui.PendingPosts);
        _ui.RunPosted();
        Assert.Single(_published);
    }

    [Fact]
    public void ComputeIsAvailableSynchronouslyForTheCallersThatCannotWait()
    {
        var derived = Build();

        var value = derived.Compute("# Heading\n\nfour more words here");

        Assert.Equal(5, value.Words);       // the fake counter counts "#" out and the five words in
        Assert.Equal("Heading", Assert.Single(value.Outline).Text);
        Assert.Same(DerivedText.Empty, derived.Current);
    }

    // ── The chain the window actually builds ──────────────────────────────────────────────────

    [Fact]
    public void TheWindowsWholeChainIsOnePublishPerDebounceWindow()
    {
        // What DocumentWindow wires: scheduler.Changed → state.Derived → state.Changed → footer.
        // A keystroke must not reach the footer, and one debounce window must not reach it twice.
        var state = new DocumentWindowState();
        var footer = new List<string>();
        var derived = Build();
        derived.Changed += value => state.Derived = value;
        state.Changed += () => footer.Add(Strings.Footer(state.Derived.Words, state.Derived.Characters));

        derived.TextChanged("one two");
        Assert.Equal(["2 words · 7 characters"], footer);        // the first fill is immediate

        derived.TextChanged("one two t");
        derived.TextChanged("one two th");
        derived.TextChanged("one two three");
        Assert.Single(footer);                                    // the debounce is still running

        _scheduler.Advance(DerivedTextScheduler.Delay);

        Assert.Equal(["2 words · 7 characters", "3 words · 13 characters"], footer);
    }

    [Fact]
    public void ARepeatOfTheSameTextDoesNotDisturbTheFooter()
    {
        // A window re-shown, or an external replace that changes nothing, runs the chain again with
        // identical numbers; DerivedText is a record, so the state's equality guard swallows it.
        var state = new DocumentWindowState();
        var updates = 0;
        var derived = Build();
        derived.Changed += value => state.Derived = value;
        state.Changed += () => updates++;

        derived.TextChanged("one two");
        derived.TextChanged("one two");
        _scheduler.Advance(DerivedTextScheduler.Delay);

        Assert.Equal(1, updates);
    }
}

/// <summary>
/// shell-design.md §5.5 fixes the footer's sentence verbatim: <c>$"{words} words · {characters} characters"</c>
/// — U+00B7 between two single spaces, and no pluralisation ("1 words"). It is the one string a
/// reader compares between their machines, and <c>FooterBar.Update</c> does nothing to it, so this is
/// the only place it can be pinned.
/// </summary>
public sealed class FooterTextTests
{
    [Fact]
    public void TheFooterSentenceIsTheMacsWordForWord()
    {
        Assert.Equal("0 words · 0 characters", Strings.Footer(0, 0));
        Assert.Equal("1 words · 1 characters", Strings.Footer(1, 1));      // no pluralisation, as on the Mac
        Assert.Equal("1234 words · 5678 characters", Strings.Footer(1234, 5678));
    }

    [Fact]
    public void TheSeparatorIsAMiddleDotAndNotAnyOtherPunctuation()
    {
        var text = Strings.Footer(2, 3);

        Assert.Contains(" · ", text, StringComparison.Ordinal);
        Assert.DoesNotContain(",", text, StringComparison.Ordinal);
        Assert.DoesNotContain("•", text, StringComparison.Ordinal);        // bullet
        Assert.DoesNotContain("··", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNumbersAreTheDerivedTextsAndNothingIsRecomputedForTheFooter()
    {
        // What FooterBar.Update does, minus the WinUI: the two fields, in this order.
        var derived = new DerivedText(7, 42, [], [], []);

        Assert.Equal("7 words · 42 characters", Strings.Footer(derived.Words, derived.Characters));
    }

    [Fact]
    public void TheNumbersAreFormattedTheSameWhateverTheMachinesCulture()
    {
        // A thousands separator here would be a visible divergence from the Mac, which uses none.
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12345 words · 67890 characters", Strings.Footer(12345, 67890));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }
}
