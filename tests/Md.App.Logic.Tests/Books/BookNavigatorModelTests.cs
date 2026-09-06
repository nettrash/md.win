using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Settings;
using Md.Core.Book;
using Md.Core.Document;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// The workspace's selection rules (§8.5, book.md §13.7) against a real book folder: what a click,
/// a rename, a move, a delete and a failed save each do to the selection, what gets remembered, and
/// the handshake that keeps a document window and the book pane from both writing one file.
/// </summary>
/// <remarks>
/// In the "BookArticleOpens" collection because <see cref="OpenInWindow"/> marks a process-wide
/// static (the sticky book exemption WP4's controller reads).
/// </remarks>
[Collection("BookArticleOpens")]
public class BookNavigatorModelTests
{
    static (BookNavigatorModel Model, FakeArticleSession Session, FakeSettingsStore Settings) Open(BookFixture fixture)
    {
        var session = new FakeArticleSession();
        var settings = new FakeSettingsStore();
        var model = new BookNavigatorModel(session, settings);
        model.OpenBook(fixture.Root);
        return (model, session, settings);
    }

    static BookFixture SmallBook()
    {
        var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        fixture.Article("02-One/01-a.md");
        fixture.Article("02-One/02-b.md");
        fixture.Article("03-Two/01-c.md");
        return fixture;
    }

    // MARK: Opening and remembering

    [Fact]
    public void OpeningABookSelectsTheFirstArticleAndHandsItToTheSession()
    {
        using var fixture = SmallBook();

        var (model, session, _) = Open(fixture);

        Assert.Equal("01-Preface.md", Path.GetFileName(model.Selection));
        Assert.Equal(model.Selection, session.EditingPath);
        Assert.Equal([model.Selection], session.Selected);
    }

    [Fact]
    public void TheLastArticleWrittenInIsRememberedRelativeToTheRootWithForwardSlashes()
    {
        using var fixture = SmallBook();
        var (model, _, settings) = Open(fixture);

        model.Select(fixture.At("02-One", "02-b.md"));

        Assert.Equal("02-One/02-b.md", settings.GetString(SettingsKeys.BookLastArticle));
        Assert.DoesNotContain('\\', model.RelativePath(model.Selection!));
    }

    [Fact]
    public void ARememberedArticleWinsOverTheFirstOneWhenTheBookIsReopened()
    {
        using var fixture = SmallBook();
        var session = new FakeArticleSession();
        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.BookLastArticle, "02-One/02-b.md");

        var model = new BookNavigatorModel(session, settings);
        model.OpenBook(fixture.Root);

        Assert.Equal("02-b.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void ARememberedArticleThatIsGoneFallsBackToTheFirstOne()
    {
        using var fixture = SmallBook();
        var session = new FakeArticleSession();
        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.BookLastArticle, "02-One/99-vanished.md");

        var model = new BookNavigatorModel(session, settings);
        model.OpenBook(fixture.Root);

        Assert.Equal("01-Preface.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void ARememberedPathIsNeverFollowedOutOfTheBook()
    {
        using var fixture = SmallBook();
        var session = new FakeArticleSession();
        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.BookLastArticle, "../elsewhere.md");

        var model = new BookNavigatorModel(session, settings);
        model.OpenBook(fixture.Root);

        Assert.Equal("01-Preface.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void AnEmptyBookSelectsNothingAndTheSessionIsLeftEmpty()
    {
        using var fixture = new BookFixture();
        fixture.Chapter("01-Empty");

        var (model, session, settings) = Open(fixture);

        Assert.Null(model.Selection);
        Assert.Null(session.EditingPath);
        Assert.DoesNotContain(settings.Writes, write => write.Key == SettingsKeys.BookLastArticle);
    }

    [Fact]
    public void ClosingTheBookDeselectsAndForgetsTheListing()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);

        model.CloseBook();

        Assert.Null(model.Selection);
        Assert.Null(model.Book);
        Assert.Null(model.Root);
        Assert.Null(session.EditingPath);
        // A detach, not a deselect: the final save is reported, never silently abandoned.
        Assert.Equal([true], session.Detaches);
    }

    [Fact]
    public void ClosingABookLetsGoEvenWhenTheFinalSaveFails()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);

        session.FlushFails = true;
        model.CloseBook();

        Assert.Null(model.Book);
        Assert.Null(session.EditingPath);
        Assert.Equal([true], session.Detaches);
    }

    // MARK: A failed flush

    [Fact]
    public void AFailedFlushLeavesTheSelectionOnTheArticleThatWillNotSave()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        var stuck = model.Selection;

        session.FlushFails = true;
        model.Select(fixture.At("02-One", "01-a.md"));

        Assert.Equal(stuck, model.Selection);
        Assert.Equal(stuck, session.EditingPath);
    }

    [Fact]
    public void AFailedFlushAbortsAWholeManagedOperationBeforeAnythingIsTouched()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        var before = fixture.Names();

        session.FlushFails = true;
        var ran = false;
        var performed = model.PerformManaged(previous =>
        {
            ran = true;
            return previous;
        });

        Assert.False(performed);
        Assert.False(ran);
        Assert.Equal(before, fixture.Names());
    }

    [Fact]
    public void AFailedFlushKeepsADeleteFromHappening()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);

        session.FlushFails = true;
        model.PerformDelete(fixture.At("01-Preface.md"));

        Assert.True(File.Exists(fixture.At("01-Preface.md")));
    }

    // MARK: Creating

    [Fact]
    public void ANewArticleIsCreatedSeededAndSelected()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);

        model.CreateArticle("  Scene Two  ", fixture.At("02-One"));

        var created = fixture.At("02-One", "Scene Two.md");
        Assert.True(File.Exists(created));
        Assert.Equal("# Scene Two\n", File.ReadAllText(created));
        Assert.Equal(BookPaths.Standardize(created), model.Selection);
    }

    [Fact]
    public void AnEmptyOrWhitespaceOnlyNameCreatesNothing()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var before = fixture.Names();

        model.CreateArticle("   ", fixture.Root);
        model.CreateChapter(" ");

        Assert.Equal(before, fixture.Names());
    }

    [Fact]
    public void ANewChapterAppearsWithoutDisturbingTheSelection()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        var selected = model.Selection;
        var selections = session.Selected.Count;

        model.CreateChapter("04-Three");

        Assert.True(Directory.Exists(fixture.At("04-Three")));
        Assert.Equal(selected, model.Selection);
        Assert.Equal(selections, session.Selected.Count);          // a folder creation never detaches the editor
    }

    // MARK: Renaming

    [Fact]
    public void RenamingAnArticleKeepsItsNumberAndExtensionAndTheSelectionFollows()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("02-One", "01-a.md"));

        model.PerformRename(fixture.At("02-One", "01-a.md"), "Arrival");

        Assert.Equal(["01-Arrival.md", "02-b.md"], fixture.Names(fixture.At("02-One")));
        Assert.Equal("01-Arrival.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void RenamingAChapterCarriesTheSelectedArticleWithIt()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("02-One", "02-b.md"));

        model.PerformRename(fixture.At("02-One"), "Beginnings");

        Assert.Equal(BookPaths.Standardize(fixture.At("02-Beginnings", "02-b.md")), model.Selection);
    }

    [Fact]
    public void AnUnusableNameIsRefusedWithTheRenameAlertAndChangesNothing()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));
        var before = fixture.Names();

        model.PerformRename(fixture.At("01-Preface.md"), "a/b");

        Assert.Equal(before, fixture.Names());
        Assert.Equal((Strings.Documents.CouldNotRename, Strings.Documents.InvalidNameMessage), alerts.Single());
    }

    [Fact]
    public void ACollidingRenameIsReportedWithTheFileSystemsOwnMessage()
    {
        // Two articles under the same ordering number: renaming one onto the other's name is the
        // collision the Mac surfaces through the OS error, not through a check of its own.
        using var fixture = new BookFixture();
        fixture.Article("01-a.md");
        fixture.Article("01-b.md");
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));

        model.PerformRename(fixture.At("01-a.md"), "b");

        Assert.Equal(["01-a.md", "01-b.md"], fixture.Names());
        Assert.Equal(Strings.Documents.CouldNotRename, alerts.Single().Title);
        Assert.NotEmpty(alerts.Single().Message);
    }

    [Fact]
    public void ARenameToTheNameItAlreadyHasChangesNothingAndSaysNothing()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));
        var selected = model.Selection;

        model.PerformRename(fixture.At("01-Preface.md"), "Preface");

        Assert.Empty(alerts);
        Assert.Contains("01-Preface.md", fixture.Names());
        Assert.Equal(selected, model.Selection);
    }

    // MARK: Moving

    [Fact]
    public void MovingDownSwapsTheNumbersAndTheSelectionFollowsItsArticle()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-a.md");
        fixture.Article("02-b.md");
        fixture.Article("03-c.md");
        var (model, _, _) = Open(fixture);
        Assert.Equal("01-a.md", Path.GetFileName(model.Selection));

        model.Move(fixture.At("01-a.md"), +1);

        Assert.Equal(["01-b.md", "02-a.md", "03-c.md"], fixture.Names());
        Assert.Equal("02-a.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void MovingUpNumbersEveryUnprefixedSiblingOnTheWay()
    {
        using var fixture = new BookFixture();
        fixture.Article("1. intro.md");
        fixture.Article("notes.md");
        fixture.Article("02-end.md");
        var (model, _, _) = Open(fixture);

        // Reading order is 1. intro, 02-end, notes → move "notes" (index 2) up one.
        model.Move(fixture.At("notes.md"), -1);

        Assert.Equal(["01-intro.md", "02-notes.md", "03-end.md"], fixture.Names());
    }

    [Fact]
    public void MovingAChapterRenumbersTheChaptersAndKeepsTheArticleSelected()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("03-Two", "01-c.md"));

        model.Move(fixture.At("03-Two"), -1);

        Assert.Contains("01-Two", fixture.Names());
        Assert.Contains("02-One", fixture.Names());
        Assert.Equal(BookPaths.Standardize(fixture.At("01-Two", "01-c.md")), model.Selection);
    }

    [Fact]
    public void AMoveOffTheEndOfTheGroupDoesNothing()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-a.md");
        fixture.Article("02-b.md");
        var (model, _, _) = Open(fixture);
        var before = fixture.Names();

        model.Move(fixture.At("01-a.md"), -1);

        Assert.Equal(before, fixture.Names());
        Assert.Equal("01-a.md", Path.GetFileName(model.Selection));
    }

    // MARK: Deleting

    [Fact]
    public void DeletingTheSelectedArticleFallsToTheNextOneInReadingOrder()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("02-One", "01-a.md"));

        model.PerformDelete(fixture.At("02-One", "01-a.md"));

        Assert.False(File.Exists(fixture.At("02-One", "01-a.md")));
        Assert.Equal("02-b.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void DeletingTheLastArticleFallsBackToTheOneBeforeIt()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("03-Two", "01-c.md"));

        model.PerformDelete(fixture.At("03-Two", "01-c.md"));

        Assert.Equal("02-b.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void DeletingTheChapterTheSelectionLivesInFallsToTheFirstSurvivorAfterIt()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("02-One", "02-b.md"));

        model.PerformDelete(fixture.At("02-One"));

        Assert.False(Directory.Exists(fixture.At("02-One")));
        Assert.Equal(BookPaths.Standardize(fixture.At("03-Two", "01-c.md")), model.Selection);
    }

    [Fact]
    public void DeletingSomethingElseLeavesTheSelectionAlone()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        model.Select(fixture.At("02-One", "01-a.md"));
        var selected = model.Selection;

        model.PerformDelete(fixture.At("01-Preface.md"));

        Assert.Equal(selected, model.Selection);
    }

    [Fact]
    public void DeletingTheOnlyArticleLeavesNothingSelected()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-a.md");
        var (model, session, _) = Open(fixture);

        model.PerformDelete(fixture.At("01-a.md"));

        Assert.Null(model.Selection);
        Assert.Null(session.EditingPath);
    }

    [Fact]
    public void DeletingSomethingAlreadyGoneIsReportedAndNothingMoves()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));

        model.PerformDelete(fixture.At("99-never.md"));

        Assert.Equal(Strings.Books.CouldNotDelete, alerts.Single().Title);
        Assert.Equal("01-Preface.md", Path.GetFileName(model.Selection));
    }

    // MARK: Re-listing

    [Fact]
    public void AReloadRePointsTheSelectionAtTheListingsOwnSpellingOfThePath()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);

        // The same file, spelled with a dot segment: the reload hands back the listing's path.
        model.Select(Path.Combine(fixture.Root, ".", "02-One", "01-a.md"));
        model.Reload();

        Assert.Equal(BookPaths.Standardize(fixture.At("02-One", "01-a.md")), model.Selection);
    }

    [Fact]
    public void AReloadPrefersThePathItIsGivenWhenThatStillExists()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);

        model.Reload(preferring: fixture.At("03-Two", "01-c.md"));

        Assert.Equal("01-c.md", Path.GetFileName(model.Selection));
    }

    [Fact]
    public void AFolderThatCannotBeListedShowsAsNoBook()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);

        model.Reload(preferring: null);
        Assert.NotNull(model.Book);

        var session = new FakeArticleSession();
        var gone = new BookNavigatorModel(session, new FakeSettingsStore());
        gone.OpenBook(Path.Combine(fixture.Root, "no-such-folder"));

        Assert.Null(gone.Book);
        Assert.Null(gone.Selection);
    }

    // MARK: Separate windows

    [Fact]
    public void WhileArticlesOpenInTheirOwnWindowsTheSidebarHoldsNoSelection()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var opened = new List<string>();
        model.OpenRequested += opened.Add;

        model.OpensInSeparateWindows = true;

        Assert.Null(model.Selection);

        model.Select(fixture.At("02-One", "01-a.md"));

        Assert.Null(model.Selection);
        Assert.Equal("01-a.md", Path.GetFileName(opened.Single()));
    }

    [Fact]
    public void OpeningTheArticleBeingEditedHandsTheFileOverBeforeTheWindowIsAskedFor()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        model.Select(fixture.At("02-One", "01-a.md"));
        var opened = new List<string>();
        model.OpenRequested += opened.Add;

        Assert.True(model.OpenInWindow(fixture.At("02-One", "01-a.md")));

        Assert.Equal(1, session.HandOffs);
        Assert.Null(session.EditingPath);
        Assert.Single(opened);
        Assert.True(BookArticleOpens.ClaimOpen(fixture.At("02-One", "01-a.md")));
    }

    [Fact]
    public void OpeningAnArticleThatIsNotBeingEditedNeedsNoHandoff()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        var opened = new List<string>();
        model.OpenRequested += opened.Add;

        Assert.True(model.OpenInWindow(fixture.At("03-Two", "01-c.md")));

        Assert.Equal(0, session.HandOffs);
        Assert.Single(opened);
        BookArticleOpens.Reset();
    }

    [Fact]
    public void AFailedHandoffAbandonsTheOpenSoTwoWritersNeverExist()
    {
        using var fixture = SmallBook();
        var (model, session, _) = Open(fixture);
        var selected = model.Selection!;
        var opened = new List<string>();
        model.OpenRequested += opened.Add;

        session.FlushFails = true;
        Assert.False(model.OpenInWindow(selected));

        Assert.Empty(opened);
        Assert.Equal(1, session.HandOffs);
        Assert.False(BookArticleOpens.ClaimOpen(selected));
    }

    // MARK: The jump reset

    [Fact]
    public void EverySelectionChangeAnnouncesItselfSoOneShotJumpsAreCleared()
    {
        using var fixture = SmallBook();
        var (model, _, _) = Open(fixture);
        var announcements = 0;
        model.SelectionChanging += () => announcements++;

        model.Select(fixture.At("02-One", "01-a.md"));
        model.Select(null);

        Assert.Equal(2, announcements);
    }
}
