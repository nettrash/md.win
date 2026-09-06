using Md.App.Logic.Preview;
using Md.App.Logic.View;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>shell-design.md §5.1 — the per-window state itself: defaults, the three-valued identity memo, and what counts as a change.</summary>
public sealed class DocumentWindowStateTests
{
    readonly DocumentWindowState _state = new();

    [Fact]
    public void AFreshWindowIsSplitWithNothingElseSet()
    {
        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.Equal(ViewModes.WindowDefault, _state.StoredMode);
        Assert.Equal(ViewMode.Split, _state.EffectiveMode);
        Assert.False(_state.ZenActive);
        Assert.False(_state.ZenReading);
        Assert.False(_state.IsBookArticle);
        Assert.Null(_state.NavigationMode);
        Assert.Null(_state.PreviewNavigation);
        Assert.Null(_state.EditorJump);
        Assert.Same(DerivedText.Empty, _state.Derived);
        Assert.False(_state.LastIdentity.Ran);
    }

    [Fact]
    public void ADesktopWindowIsAlwaysWideSoNothingIsEverCoerced()
    {
        Assert.True(DocumentWindowState.IsWide);

        _state.StoredMode = ViewMode.Split;

        Assert.Equal(ViewMode.Split, _state.EffectiveMode);
    }

    [Fact]
    public void TheNudgeIsWhatTheWindowShowsWhileItStands()
    {
        _state.StoredMode = ViewMode.Preview;
        _state.NavigationMode = ViewMode.Edit;
        Assert.Equal(ViewMode.Edit, _state.EffectiveMode);
        Assert.Equal(ViewMode.Preview, _state.StoredMode);

        _state.NavigationMode = null;
        Assert.Equal(ViewMode.Preview, _state.EffectiveMode);
    }

    [Fact]
    public void ChangedFiresOnceForARealChangeAndNotAtAllForARewrite()
    {
        var changes = 0;
        _state.Changed += () => changes++;

        _state.StoredMode = ViewMode.Edit;
        _state.StoredMode = ViewMode.Edit;

        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheDerivedGuardIsStructuralSoAnUnchangedRecomputationIsSilent()
    {
        // Two runs of the scheduler over the same text build fresh lists. Record equality compares
        // IReadOnlyList<T> members with EqualityComparer<T>.Default — REFERENCE equality — so
        // without DerivedText's own Equals every 250 ms tick would announce a change that is not
        // one, and the window would relay out and re-evaluate the Go menu for nothing.
        var changes = 0;
        _state.Changed += () => changes++;
        var first = new DerivedText(2, 7, [new OutlineEntry(1, "T", "t", 0)], [new NoteEntry("n", 3)], [new DiagramSvg.Diagram(0, "mermaid", "graph", "Diagram 1")]);
        var second = new DerivedText(2, 7, [new OutlineEntry(1, "T", "t", 0)], [new NoteEntry("n", 3)], [new DiagramSvg.Diagram(0, "mermaid", "graph", "Diagram 1")]);
        Assert.NotSame(first, second);
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());

        _state.Derived = first;
        _state.Derived = second;

        Assert.Equal(1, changes);
        Assert.Same(first, _state.Derived);
    }

    [Fact]
    public void ADerivedTextThatReallyDiffersIsStillAnnounced()
    {
        var changes = 0;
        _state.Changed += () => changes++;
        var outline = new OutlineEntry(1, "T", "t", 0);

        _state.Derived = new DerivedText(2, 7, [outline], [], []);
        _state.Derived = new DerivedText(2, 8, [outline], [], []);               // one more character
        _state.Derived = new DerivedText(2, 8, [], [], []);                      // the heading went away
        _state.Derived = new DerivedText(2, 8, [], [new NoteEntry("n", 1)], []); // a note appeared

        Assert.Equal(4, changes);
    }

    [Fact]
    public void TheBookExemptionIsOneWayAndAnnouncesItselfOnce()
    {
        var changes = 0;
        _state.Changed += () => changes++;

        _state.MarkBookArticle();
        _state.MarkBookArticle();

        Assert.True(_state.IsBookArticle);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void TheIdentityMemoHasThreeStatesAndTheMiddleOneIsWhatMakesTheFirstSaveAMigration()
    {
        Assert.False(IdentityMemo.NeverRan.Ran);
        Assert.False(IdentityMemo.NeverRan.RanWithNoIdentity);

        var untitled = IdentityMemo.Of(null);
        Assert.True(untitled.Ran);
        Assert.True(untitled.RanWithNoIdentity);
        Assert.Null(untitled.Value);

        var saved = IdentityMemo.Of("0123456789abcdef");
        Assert.True(saved.Ran);
        Assert.False(saved.RanWithNoIdentity);
        Assert.Equal("0123456789abcdef", saved.Value);
    }

    [Fact]
    public void AOneShotRequestSurvivesAStaleHandledCallback()
    {
        var first = new EditorJump(Guid.NewGuid(), 3);
        var second = new EditorJump(Guid.NewGuid(), 9);
        _state.EditorJump = first;
        _state.EditorJump = second;

        _state.EditorJumpHandled(first.Id);
        Assert.Equal(second, _state.EditorJump);

        _state.EditorJumpHandled(second.Id);
        Assert.Null(_state.EditorJump);

        var nav = new PreviewNavigation(Guid.NewGuid(), "slug");
        _state.PreviewNavigation = nav;
        _state.PreviewNavigationHandled(Guid.NewGuid());
        Assert.Equal(nav, _state.PreviewNavigation);
        _state.PreviewNavigationHandled(nav.Id);
        Assert.Null(_state.PreviewNavigation);
    }
}
