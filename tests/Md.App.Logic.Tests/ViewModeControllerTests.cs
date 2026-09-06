using Md.App.Logic.Settings;
using Md.App.Logic.View;
using Md.Core.Document;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>
/// <c>BookArticleOpens</c> is a process-wide static (a dictionary plus an injectable clock), so the
/// classes that mark and claim run one at a time — the same reason Md.Core.Tests puts its book
/// session tests in a collection.
/// </summary>
[CollectionDefinition("BookArticleOpens", DisableParallelization = true)]
public sealed class BookArticleOpensCollection;

/// <summary>
/// shell-design.md §5.2 and §5.6 — the consequences the Mac's <c>setMode</c> /
/// <c>applyViewModeMemory</c> produce, each named after the sentence in the design it pins.
/// (The rules themselves — the open truth table, the codec, the MRU, the nudge — are Core's
/// <c>ViewModeTests</c>; these are the window's use of them.)
/// </summary>
[Collection("BookArticleOpens")]
public sealed class ViewModeControllerTests : IDisposable
{
    readonly FakeSettingsStore _settings = new();
    readonly DocumentWindowState _state = new();
    readonly ViewModeController _controller;

    public ViewModeControllerTests()
    {
        BookArticleOpens.Reset();
        _controller = new ViewModeController(_state, new SettingsViewModeStore(_settings));
    }

    public void Dispose() => BookArticleOpens.Reset();

    // Paths that do not exist: ViewModeMemory.CanonicalPath resolves links only for a path that
    // names something on disk, so these canonicalise to Path.GetFullPath and nothing else.
    static string Path1 => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md.win.tests", "one.md");
    static string Path2 => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md.win.tests", "two.md");

    string Remembered(string path) => ViewModeMemory.Lookup(ViewModeMemory.IdentityFor(path), new SettingsViewModeStore(_settings))?.RawValue() ?? "-";

    void Seed(string path, ViewMode mode) => ViewModeMemory.Remember(mode, ViewModeMemory.IdentityFor(path), new SettingsViewModeStore(_settings));

    // ── 1. First appearance decides and stores ────────────────────────────────────────────────

    [Fact]
    public void FirstAppearanceOfAFileWithNothingRememberedDecidesAndStoresTheResult()
    {
        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.Equal("split", Remembered(Path1));
    }

    [Fact]
    public void AnEmptyFileOpensInEditAndThatIsWhatGetsStored()
    {
        _controller.ApplyMemory(Path1, "");

        Assert.Equal(ViewMode.Edit, _state.StoredMode);
        Assert.Equal("edit", Remembered(Path1));
    }

    [Fact]
    public void ARememberedModeWinsOverTheOpenRule()
    {
        Seed(Path1, ViewMode.Preview);

        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
    }

    // ── 2. Documents with no file ─────────────────────────────────────────────────────────────

    [Fact]
    public void AnExampleHasContentAndNoFileSoItOpensInSplitAndStoresNothing()
    {
        _controller.ApplyMemory(null, "# Welcome");

        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    [Fact]
    public void AnEmptyUntitledDocumentOpensInEditAndStoresNothing()
    {
        _controller.ApplyMemory(null, "");

        Assert.Equal(ViewMode.Edit, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    [Fact]
    public void PickingAModeWhileUntitledStoresNothing()
    {
        _controller.ApplyMemory(null, "");

        _controller.Select(ViewMode.Preview);

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    // ── 3. The first save migrates; it does not re-decide ─────────────────────────────────────

    [Fact]
    public void TheFirstSaveOfAnUntitledDocumentMigratesTheRawPreferenceInsteadOfReDeciding()
    {
        _controller.ApplyMemory(null, "");
        _controller.Select(ViewMode.Preview);      // the writer is reading; the open rule would say Split

        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Equal("preview", Remembered(Path1));
    }

    [Fact]
    public void TheMigrateBranchLeavesAStandingNudgeAlone()
    {
        _controller.ApplyMemory(null, "# Something");
        _controller.Select(ViewMode.Preview);
        _controller.JumpToNote(new NoteEntry("a note", 3));
        Assert.Equal(ViewMode.Edit, _state.NavigationMode);

        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal(ViewMode.Edit, _state.NavigationMode);
        Assert.Equal(ViewMode.Preview, _state.StoredMode);
    }

    // ── 4. Save As / Rename / Move To re-decide for the new identity ──────────────────────────

    [Fact]
    public void SaveAsOfAnAlreadySavedDocumentReRunsTheOpenRuleForTheNewIdentity()
    {
        Seed(Path2, ViewMode.Preview);
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Edit);

        _controller.ApplyMemory(Path2, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Equal("edit", Remembered(Path1));      // the old file keeps what it was left in
    }

    [Fact]
    public void SaveAsClearsAStandingNudge()
    {
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        _controller.JumpToNote(new NoteEntry("a note", 1));
        Assert.Equal(ViewMode.Edit, _state.NavigationMode);

        _controller.ApplyMemory(Path2, "# Something");

        Assert.Null(_state.NavigationMode);
    }

    // ── 5. Saving back over the same path is a no-op ──────────────────────────────────────────

    [Fact]
    public void SavingOverTheSamePathNeitherReDecidesNorWrites()
    {
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        Seed(Path1, ViewMode.Edit);                    // someone else's window remembered Edit
        _settings.Writes.Clear();

        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    [Fact]
    [Trait("Platform", "Windows")]
    public void TwoSpellingsOfOnePathAreOneIdentity()
    {
        // NTFS compares names case-insensitively and the shell, the MRU and a drop hand the same
        // file over in either spelling; Core folds case on Windows so both reach one entry.
        if (!OperatingSystem.IsWindows()) return;

        var lower = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md.win.tests", "case.md");
        var upper = System.IO.Path.Combine(System.IO.Path.GetTempPath().ToUpperInvariant(), "MD.WIN.TESTS", "CASE.MD");
        Assert.Equal(ViewModeMemory.IdentityFor(lower), ViewModeMemory.IdentityFor(upper));

        _controller.ApplyMemory(lower, "# Something");
        _controller.Select(ViewMode.Preview);
        _settings.Writes.Clear();

        _controller.ApplyMemory(upper, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    // ── 6. The book exemption is claimed once and is sticky ───────────────────────────────────

    [Fact]
    public void AnArticleTheBookOpenedIsExemptFromTheMemoryBothWays()
    {
        Seed(Path1, ViewMode.Preview);
        BookArticleOpens.Mark(Path1);

        _controller.ApplyMemory(Path1, "# Something");

        Assert.True(_state.IsBookArticle);
        Assert.Equal(ViewMode.Split, _state.StoredMode);        // the window default, not the remembered Preview
        _settings.Writes.Clear();

        _controller.Select(ViewMode.Edit);

        Assert.Equal(ViewMode.Edit, _state.StoredMode);
        Assert.Empty(_settings.Writes);
        Assert.Equal("preview", Remembered(Path1));             // untouched
    }

    [Fact]
    public void TheBookExemptionIsStickyThroughSaveAs()
    {
        Seed(Path2, ViewMode.Preview);
        BookArticleOpens.Mark(Path1);
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Edit);
        _settings.Writes.Clear();

        _controller.ApplyMemory(Path2, "# Something");

        Assert.True(_state.IsBookArticle);
        Assert.Equal(ViewMode.Edit, _state.StoredMode);         // no re-decision to Path2's Preview
        Assert.Empty(_settings.Writes);
    }

    [Fact]
    public void TheMarkIsClaimedByExactlyOneWindow()
    {
        BookArticleOpens.Mark(Path1);
        _controller.ApplyMemory(Path1, "# Something");
        Assert.True(_state.IsBookArticle);

        var second = new DocumentWindowState();
        new ViewModeController(second, new SettingsViewModeStore(_settings)).ApplyMemory(Path1, "# Something");

        Assert.False(second.IsBookArticle);
    }

    // ── 7. Nudges are transient; Zen stores nothing ───────────────────────────────────────────

    [Fact]
    public void ANoteJumpNudgesPreviewToEditAndStoresNothing()
    {
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        _settings.Writes.Clear();

        _controller.JumpToNote(new NoteEntry("a note", 7));

        Assert.Equal(ViewMode.Edit, _state.NavigationMode);
        Assert.Equal(ViewMode.Edit, _state.EffectiveMode);
        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Empty(_settings.Writes);
        Assert.Equal("preview", Remembered(Path1));
    }

    [Fact]
    public void ANudgeIsClearedByThePickThatFollowsIt()
    {
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        _controller.JumpToNote(new NoteEntry("a note", 7));

        _controller.Select(ViewMode.Split);

        Assert.Null(_state.NavigationMode);
        Assert.Equal(ViewMode.Split, _state.EffectiveMode);
    }

    [Fact]
    public void ASecondNoteJumpDoesNotCancelTheNudgeTheFirstOneSet()
    {
        // macOS §6.9: "The nudge is assigned only when non-nil — writing nil back would cancel a
        // nudge from the previous note jump." Once the first jump has nudged Preview → Edit the
        // displayed mode IS Edit, so the second jump's nudge is null; assigning that null would
        // throw the reader straight back into Preview with a caret they cannot see.
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        _controller.JumpToNote(new NoteEntry("first", 3));
        Assert.Equal(ViewMode.Edit, _state.NavigationMode);

        _controller.JumpToNote(new NoteEntry("second", 9));

        Assert.Equal(ViewMode.Edit, _state.NavigationMode);
        Assert.Equal(ViewMode.Edit, _state.EffectiveMode);
        Assert.Equal(ViewMode.Preview, _state.StoredMode);      // the preference is still untouched
        Assert.Equal(9, _state.EditorJump!.Value.Line);
    }

    [Fact]
    public void AStaleBookMarkDoesNotExemptTheNextOrdinaryOpen()
    {
        // BookArticleOpens.MarkLifetime is 10 s precisely so that a mark nobody claimed — the
        // navigator opened the article, the user closed the window, the file is opened again from
        // the shell an hour later — cannot silently exempt that ordinary open from the memory.
        var now = FakeClock.Epoch.UtcDateTime;
        BookArticleOpens.Now = () => now;
        try
        {
            Seed(Path1, ViewMode.Preview);
            BookArticleOpens.Mark(Path1);
            now += BookArticleOpens.MarkLifetime + TimeSpan.FromSeconds(1);

            _controller.ApplyMemory(Path1, "# Something");

            Assert.False(_state.IsBookArticle);
            Assert.Equal(ViewMode.Preview, _state.StoredMode);   // the remembered mode, not the default
        }
        finally
        {
            BookArticleOpens.Now = () => DateTime.UtcNow;
        }
    }

    [Fact]
    public void ANoteJumpFromSplitOrEditDoesNotNudge()
    {
        _controller.ApplyMemory(Path1, "# Something");    // Split

        _controller.JumpToNote(new NoteEntry("a note", 2));
        Assert.Null(_state.NavigationMode);

        _controller.Select(ViewMode.Edit);
        _controller.JumpToNote(new NoteEntry("a note", 2));
        Assert.Null(_state.NavigationMode);
    }

    [Fact]
    public void AHeadingJumpNeverNudgesAndMovesOnlyThePanesThatAreShowing()
    {
        var heading = new OutlineEntry(1, "Title", "title", 4);

        _controller.ApplyMemory(Path1, "# Title");         // Split: both panes
        _controller.JumpToHeading(heading);
        Assert.NotNull(_state.PreviewNavigation);
        Assert.NotNull(_state.EditorJump);
        Assert.Null(_state.NavigationMode);

        _state.PreviewNavigation = null;
        _state.EditorJump = null;
        _controller.Select(ViewMode.Preview);
        _controller.JumpToHeading(heading);
        Assert.NotNull(_state.PreviewNavigation);
        Assert.Null(_state.EditorJump);                    // a reader is never dropped into the source

        _state.PreviewNavigation = null;
        _controller.Select(ViewMode.Edit);
        _controller.JumpToHeading(heading);
        Assert.Null(_state.PreviewNavigation);
        Assert.NotNull(_state.EditorJump);
    }

    [Fact]
    public void AOneShotRequestIsClearedOnlyByItsOwnId()
    {
        var ids = new Queue<Guid>([new Guid("00000000-0000-0000-0000-000000000001"), new Guid("00000000-0000-0000-0000-000000000002")]);
        var controller = new ViewModeController(_state, new SettingsViewModeStore(_settings), ids.Dequeue);
        _state.StoredMode = ViewMode.Edit;

        controller.JumpToNote(new NoteEntry("first", 1));
        var first = _state.EditorJump!.Value.Id;
        controller.JumpToNote(new NoteEntry("second", 9));

        _state.EditorJumpHandled(first);                   // the older pane reports back late
        Assert.Equal(9, _state.EditorJump!.Value.Line);

        _state.EditorJumpHandled(_state.EditorJump!.Value.Id);
        Assert.Null(_state.EditorJump);
    }

    [Fact]
    public void ZenSwallowsTheViewModeCommandsAndStoresNothing()
    {
        _controller.ApplyMemory(Path1, "# Something");     // Split, stored
        var zen = new ZenController(_state);
        zen.SetActive(true);
        _settings.Writes.Clear();

        _controller.Select(ViewMode.Preview);
        Assert.True(_state.ZenReading);
        _controller.Select(ViewMode.Edit);
        Assert.False(_state.ZenReading);
        _controller.Select(ViewMode.Split);
        Assert.False(_state.ZenReading);

        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.Empty(_settings.Writes);
        Assert.Equal("split", Remembered(Path1));
    }

    // ── 8. The traps the porting reports name ─────────────────────────────────────────────────

    [Fact]
    public void AViewModeCommandThatFiresTwiceLeavesOneEntryAndOneMode()
    {
        // §13.4 stage 2's day-1 check is "Ctrl+1 with the preview focused fires ONCE" — because a
        // WebView2 that has focus can let an accelerator through to the window as well as handling
        // it. If that guard ever slips, the damage must stop at a duplicated write: not two entries
        // for one file, not a mode that lands somewhere else on the second pass.
        _controller.ApplyMemory(Path1, "# Something");
        Seed(Path2, ViewMode.Preview);                       // a neighbour, so "one entry" is a real count
        _settings.Writes.Clear();

        _controller.Select(ViewMode.Edit);
        _controller.Select(ViewMode.Edit);

        Assert.Equal(ViewMode.Edit, _state.StoredMode);
        Assert.Equal(ViewMode.Edit, _state.EffectiveMode);
        Assert.Equal("edit", Remembered(Path1));
        Assert.Equal("preview", Remembered(Path2));
        var entries = ViewModeMemory.Entries(new SettingsViewModeStore(_settings));
        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Identity == ViewModeMemory.IdentityFor(Path1));
    }

    [Fact]
    public void ADoubledZenSwitchIsAlsoIdempotentAndStillStoresNothing()
    {
        _controller.ApplyMemory(Path1, "# Something");
        var zen = new ZenController(_state);
        zen.SetActive(true);
        _settings.Writes.Clear();

        _controller.Select(ViewMode.Preview);
        _controller.Select(ViewMode.Preview);

        Assert.True(_state.ZenReading);
        Assert.Equal(ViewMode.Split, _state.StoredMode);
        Assert.Empty(_settings.Writes);
    }

    [Fact]
    public void AZenRoundTripLeavesThePreferenceTheMemoryAndAStandingNudgeExactlyAsTheyWere()
    {
        // The window wires ViewModeController and ZenController to one state, and nothing else does;
        // this is the only test that drives both. Zen is view state — it must not be able to reach
        // md.viewModeMemory, md.viewMode or a navigation nudge, on the way in or on the way out.
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Preview);
        _controller.JumpToNote(new NoteEntry("a note", 4));   // nudge Preview -> Edit
        var zen = new ZenController(_state);
        _settings.Writes.Clear();

        zen.Toggle();
        _controller.Select(ViewMode.Preview);                 // Ctrl+3 in Zen: read
        _controller.Select(ViewMode.Edit);                    // Ctrl+1 in Zen: write
        zen.PresenterChanged(isFullScreen: false);            // the user hit F11

        Assert.False(_state.ZenActive);
        Assert.False(_state.ZenReading);
        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Equal(ViewMode.Edit, _state.NavigationMode);   // the nudge survived the round trip
        Assert.Equal(ViewMode.Edit, _state.EffectiveMode);
        Assert.Empty(_settings.Writes);
        Assert.Equal("preview", Remembered(Path1));
    }

    [Fact]
    [Trait("Platform", "Windows")]
    public void ACaseOnlyRenameKeepsTheRememberedModeAndAddsNoSecondEntry()
    {
        // §13.4 stage 5's day-1 check. On Windows a rename that only changes case is a rename the
        // file system does not consider a new name; Core folds case so the identity is unchanged,
        // and the window must NOT re-decide (it would drop the writer into whatever the open rule
        // says) nor leave a second, orphaned entry behind.
        if (!OperatingSystem.IsWindows()) return;

        var before = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md.win.tests", "chapter one.md");
        var after = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "md.win.tests", "Chapter One.md");

        _controller.ApplyMemory(before, "# Something");
        _controller.Select(ViewMode.Edit);
        _settings.Writes.Clear();

        _controller.ApplyMemory(after, "# Something");        // Rename ran; the file is the same file

        Assert.Equal(ViewMode.Edit, _state.StoredMode);
        Assert.Empty(_settings.Writes);
        Assert.Single(ViewModeMemory.Entries(new SettingsViewModeStore(_settings)));
        Assert.Equal("edit", Remembered(after));
    }

    [Fact]
    public void ARenameToADifferentNameIsADifferentFileOnEveryPlatform()
    {
        // The other half of the case-only test, and the one that runs everywhere: a real rename
        // changes the identity, so the open rule re-runs for the new name (§5.2, "Save As / Rename
        // / Move re-run the open rule").
        Seed(Path2, ViewMode.Preview);
        _controller.ApplyMemory(Path1, "# Something");
        _controller.Select(ViewMode.Edit);

        _controller.ApplyMemory(Path2, "# Something");

        Assert.Equal(ViewMode.Preview, _state.StoredMode);
        Assert.Equal("edit", Remembered(Path1));              // the old name keeps what it had
    }

    [Fact]
    public void TheMemoryStoreWritesTheOneKeyTheSettingsInventoryNames()
    {
        Assert.Equal(SettingsKeys.ViewModeMemory, ViewModeMemory.SettingsKey);

        _controller.ApplyMemory(Path1, "# Something");

        Assert.Equal([SettingsKeys.ViewModeMemory], _settings.Writes.Select(w => w.Key).Distinct());
        Assert.StartsWith(ViewModeMemory.Header, _settings.GetString(SettingsKeys.ViewModeMemory), StringComparison.Ordinal);
    }
}
