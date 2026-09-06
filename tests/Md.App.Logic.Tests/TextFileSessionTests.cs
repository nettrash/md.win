using System.Text;
using Md.App.Logic.Documents;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>
/// §6.3's table and the fourteen macOS <c>BookArticleSession</c> behaviours (book.md §10.10), against
/// the WP0 fakes. The article fixture is the Mac's: <c>01-Scene.md</c> holding <c>"# Scene\n"</c>.
/// </summary>
public class TextFileSessionTests
{
    const string Article = @"C:\Book\01-Scene.md";
    const string Other = @"C:\Book\02-Other.md";
    const string Document = @"C:\Docs\Chapter One.md";

    /// <summary>One session with every seam a test can reach into, plus a log of what it asked for.</summary>
    sealed class World : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public FakeFileSystem Fs { get; }
        public FakeFileWatcher Watcher { get; } = new();
        public FakeScheduler Scheduler { get; }
        public FakeDocumentRegistry Registry { get; } = new();
        public FakeFileIdentity Identity { get; } = new();
        public TextFileSession Session { get; }
        public Guid WindowId { get; }

        public List<(string Title, string Message)> Alerts { get; } = [];
        public List<string> Replacements { get; } = [];
        public List<string?> Identities { get; } = [];
        public int ChangedCount { get; private set; }

        public World(SessionRole role)
        {
            Fs = new FakeFileSystem(Clock);
            Scheduler = new FakeScheduler(Clock);
            WindowId = Guid.NewGuid();
            Session = new TextFileSession(Fs, Watcher, Scheduler, Registry, Identity, role,
                role == SessionRole.Document ? WindowId : null);
            Session.Changed += () => ChangedCount++;
            Session.AlertRequested += (t, m) => Alerts.Add((t, m));
            Session.TextReplacedExternally += t => Replacements.Add(t);
            Session.IdentityChanged += p => Identities.Add(p);
        }

        /// <summary>Both fakes normalise a path the way FakeFileIdentity does, so registry keys match.</summary>
        public static string Canonical(string path) => path.Replace('\\', '/');

        public void Dispose() => Session.Dispose();
    }

    static World Book()
    {
        var world = new World(SessionRole.BookArticle);
        world.Fs.AddFile(Article, "# Scene\n");
        world.Fs.AddFile(Other, "# Other\n");
        return world;
    }

    static World Doc()
    {
        var world = new World(SessionRole.Document);
        world.Fs.AddFile(Document, "# Title\nBody\n");
        return world;
    }

    // ---- the fourteen ----

    [Fact]
    public void SessionLoadsEditsAndFlushesToDisk()
    {
        using var w = Book();
        Assert.True(w.Session.Select(Article));
        Assert.Equal("# Scene\n", w.Session.Text);
        Assert.Equal(Article, w.Session.EditingPath);
        Assert.False(w.Session.IsDirty);

        w.Session.Edit("# Scene\nMore.\n");
        Assert.True(w.Session.IsDirty);

        Assert.True(w.Session.FlushNow(explicitSave: false));
        Assert.False(w.Session.IsDirty);
        Assert.Equal("# Scene\nMore.\n", w.Fs.Text(Article));
    }

    [Fact]
    public void SessionSelectionChangeSavesTheOutgoingArticle()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("edited A\n");

        Assert.True(w.Session.Select(Other));

        Assert.Equal("edited A\n", w.Fs.Text(Article));
        Assert.Equal("# Other\n", w.Session.Text);
        Assert.Equal(Other, w.Session.EditingPath);
    }

    [Fact]
    public void SessionRefusesToClobberAFileChangedUnderIt()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Article, "theirs, and longer\n");     // a different size: no coarse clock can mask it

        Assert.False(w.Session.FlushNow(explicitSave: true));
        Assert.True(w.Session.Conflicted);
        Assert.Equal("theirs, and longer\n", w.Fs.Text(Article));
        Assert.Equal("mine\n", w.Session.Text);

        w.Session.ResolveConflictKeepingMine();
        Assert.False(w.Session.Conflicted);
        Assert.Equal("mine\n", w.Fs.Text(Article));
    }

    [Fact]
    public void SessionReloadResolutionDiscardsTheBuffer()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Article, "theirs, and longer\n");
        Assert.False(w.Session.FlushNow(explicitSave: false));

        w.Session.ResolveConflictReloading();

        Assert.False(w.Session.Conflicted);
        Assert.Equal("theirs, and longer\n", w.Session.Text);
        var writesBefore = w.Fs.Writes.Count;
        w.Session.Detach(reportFailure: true);                     // Close Book writes nothing
        Assert.Equal(writesBefore, w.Fs.Writes.Count);
    }

    [Fact]
    public void SessionCloseBookFlushesPendingEdits()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("last words\n");

        w.Session.Detach(reportFailure: true);

        Assert.Equal("last words\n", w.Fs.Text(Article));
        Assert.Empty(w.Alerts);
    }

    [Fact]
    public void SessionCleanFlushLeavesTheFileUntouched()
    {
        using var w = Book();
        w.Session.Select(Article);
        var before = w.Fs.Stamp(Article);

        Assert.True(w.Session.FlushNow(explicitSave: false));

        Assert.Equal(before, w.Fs.Stamp(Article));
        Assert.Empty(w.Fs.Writes);
    }

    [Fact]
    public void SessionDeselectFlushesAndFullyDetaches()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("saved on the way out\n");

        Assert.True(w.Session.Select(null));

        Assert.Equal("saved on the way out\n", w.Fs.Text(Article));
        Assert.Null(w.Session.EditingPath);
        Assert.IsType<Stage.Empty>(w.Session.Stage);
        Assert.Equal("", w.Session.Text);
        Assert.False(w.Session.IsDirty);
    }

    [Fact]
    public void SessionFailedFlushAbortsTheSelectionChange()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("cannot be written\n");
        w.Fs.SetReadOnly(Article);                                 // FileAttributes.ReadOnly, the Windows chmod 444

        Assert.False(w.Session.Select(Other));
        Assert.Equal(Article, w.Session.EditingPath);
        Assert.Equal("cannot be written\n", w.Session.Text);
        Assert.True(w.Session.IsDirty);
        Assert.NotNull(w.Session.SaveErrorText);

        w.Fs.SetReadOnly(Article, false);
        Assert.True(w.Session.Select(Other));
        Assert.Equal("cannot be written\n", w.Fs.Text(Article));
    }

    [Fact]
    public void SessionHandOffForExternalOpenSavesThenStepsAside()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("handed off\n");

        Assert.True(w.Session.HandOffForExternalOpen());

        Assert.Equal("handed off\n", w.Fs.Text(Article));
        Assert.IsType<Stage.Handoff>(w.Session.Stage);
        Assert.Null(w.Session.EditingPath);
    }

    [Fact]
    public void SessionStepsAsideWhileADocumentOwnsTheArticle()
    {
        using var w = Book();
        var documentWindow = Guid.NewGuid();
        w.Registry.Register(documentWindow, World.Canonical(Article));

        w.Session.Select(Article);
        Assert.IsType<Stage.Handoff>(w.Session.Stage);
        Assert.Equal(documentWindow, w.Session.OwningWindow);

        w.Registry.Unregister(documentWindow);
        w.Session.RecheckOwnership();

        Assert.IsType<Stage.Editing>(w.Session.Stage);
        Assert.Equal("# Scene\n", w.Session.Text);
    }

    [Fact]
    public void RecheckOwnershipSavesThenHandsOffWhenADocumentOpensOverAnEditedArticle()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("saved before handing over\n");

        w.Registry.Register(Guid.NewGuid(), World.Canonical(Article));
        w.Session.RecheckOwnership();

        Assert.Equal("saved before handing over\n", w.Fs.Text(Article));
        Assert.IsType<Stage.Handoff>(w.Session.Stage);
    }

    [Fact]
    public void SessionRescueCopyParksTheBufferWithoutOverwriting()
    {
        using var w = Book();
        w.Fs.AddFile(@"C:\Book\01-Scene (rescued).md", "an earlier rescue");
        w.Session.Select(Article);
        w.Session.Edit("unsavable\n");
        w.Fs.SetReadOnly(Article);

        var rescued = w.Session.WriteRescueCopy();

        Assert.Equal(@"C:\Book\01-Scene (rescued 2).md", rescued);
        Assert.Equal("unsavable\n", w.Fs.Text(rescued!));
        Assert.Equal("an earlier rescue", w.Fs.Text(@"C:\Book\01-Scene (rescued).md"));
        Assert.Equal("# Scene\n", w.Fs.Text(Article));
        Assert.True(w.Session.IsDirty);                            // a rescue is a copy, not a save
    }

    [Fact]
    public void TheFlushGateSavesTheBufferSynchronouslyAndReportsItsVerdict()
    {
        // The gate itself is Core's static event (WP7 wires the book window to it); what the session
        // owes it is a synchronous flush whose bool is the veto.
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("compile me\n");

        Assert.True(w.Session.FlushNow(explicitSave: false));
        Assert.Equal("compile me\n", w.Fs.Text(Article));

        w.Session.Edit("and now refuse\n");
        w.Fs.SetReadOnly(Article);
        Assert.False(w.Session.FlushNow(explicitSave: false));
    }

    [Fact]
    public void SessionRoundTripsLegacyEncoding()
    {
        using var w = Book();
        const string original = "Привет, мир!";
        w.Fs.AddFile(@"C:\Book\03-Legacy.md", DocumentFixtures.Cp1251.GetBytes(original));

        w.Session.Select(@"C:\Book\03-Legacy.md");
        Assert.Equal(original, w.Session.Text);

        w.Session.Edit(original + " Ещё.");
        Assert.True(w.Session.FlushNow(explicitSave: false));

        Assert.Equal(DocumentFixtures.Cp1251.GetBytes(original + " Ещё."), w.Fs.Bytes(@"C:\Book\03-Legacy.md"));
    }

    // ---- the rest of the macOS session behaviours ----

    [Fact]
    public void SessionRoundTripsABomedUtf16Article()
    {
        using var w = Book();
        var bytes = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Wide\n")).ToArray();
        w.Fs.AddFile(@"C:\Book\04-Wide.md", bytes);

        w.Session.Select(@"C:\Book\04-Wide.md");
        Assert.Equal("Wide\n", w.Session.Text);
        w.Session.Edit("Wider\n");
        w.Session.FlushNow(explicitSave: false);

        Assert.Equal(
            new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Wider\n")).ToArray(),
            w.Fs.Bytes(@"C:\Book\04-Wide.md"));
    }

    [Fact]
    public void SessionReportsAnUnreadableArticleWithoutFailingTheSelection()
    {
        using var w = Book();
        Assert.True(w.Session.Select(@"C:\Book\99-Gone.md"));
        Assert.IsType<Stage.Unreadable>(w.Session.Stage);
        Assert.Equal("Gone", w.Session.Title);                     // the book strips the ordering prefix
    }

    [Fact]
    public void SelectingTheEditedArticleAgainIsANoOp()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("in progress\n");
        var changes = w.ChangedCount;

        Assert.True(w.Session.Select(Article));
        Assert.True(w.Session.Select(@"C:/Book/01-Scene.md"));     // and the other spelling is the same file

        Assert.Equal("in progress\n", w.Session.Text);
        Assert.True(w.Session.IsDirty);
        Assert.Empty(w.Fs.Writes);
        Assert.Equal(changes, w.ChangedCount);
    }

    [Fact]
    public void FileChangedReloadsACleanSessionAndConflictsADirtyOne()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Fs.WriteExternally(Article, "theirs\n");
        w.Watcher.RaiseChanged();
        Assert.False(w.Session.Conflicted);
        Assert.Equal("theirs\n", w.Session.Text);
        Assert.Equal(["# Scene\n", "theirs\n"], w.Replacements);

        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Article, "theirs again, longer\n");
        w.Watcher.RaiseChanged();
        Assert.True(w.Session.Conflicted);
        Assert.Equal("mine\n", w.Session.Text);
    }

    [Fact]
    public void OurOwnWriteAndAnAttributeOnlyTouchAreNotExternalChanges()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("ours\n");
        w.Session.FlushNow(explicitSave: false);
        var replacements = w.Replacements.Count;

        w.Watcher.RaiseChanged();                                  // the echo of our own write
        Assert.False(w.Session.Conflicted);
        Assert.Equal(replacements, w.Replacements.Count);

        w.Fs.Touch(Article);                                       // mtime moves, bytes do not
        w.Watcher.RaiseChanged();
        Assert.False(w.Session.Conflicted);
        Assert.Equal("ours\n", w.Session.Text);                    // reloaded, silently, with the same text
    }

    [Fact]
    public void FileDeletedDetachesACleanBookSessionAndConflictsADirtyOne()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("mine\n");
        w.Fs.Delete(Article);
        w.Watcher.RaiseDeleted();
        Assert.True(w.Session.Conflicted);
        Assert.Equal("mine\n", w.Session.Text);

        w.Session.ResolveConflictKeepingMine();                    // recreates the file
        Assert.Equal("mine\n", w.Fs.Text(Article));

        w.Fs.Delete(Article);
        w.Watcher.RaiseDeleted();
        Assert.IsType<Stage.Empty>(w.Session.Stage);
    }

    [Fact]
    public void FileMovedFollowsTheRename()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Fs.Move(Article, @"C:\Book\01-Scene Renamed.md");

        w.Watcher.RaiseRenamed(@"C:\Book\01-Scene Renamed.md");

        Assert.Equal(@"C:\Book\01-Scene Renamed.md", w.Session.EditingPath);
        Assert.Equal("Scene Renamed", w.Session.Title);
        Assert.Equal(@"C:\Book\01-Scene Renamed.md", w.Watcher.WatchedPath);
    }

    [Fact]
    public void AutosaveIsArmedPerKeystrokeAndDisarmedOnDetach()
    {
        using var w = Book();
        w.Session.Select(Article);

        w.Session.Edit("a");
        Assert.Equal(1, w.Scheduler.PendingTimers);
        w.Scheduler.Advance(TimeSpan.FromMilliseconds(900));
        w.Session.Edit("ab");                                      // restarts the debounce
        Assert.Equal([TextFileSession.AutosaveDelay], w.Scheduler.PendingDelays);
        Assert.Empty(w.Fs.Writes);

        w.Scheduler.Advance(TextFileSession.AutosaveDelay);
        Assert.Equal("ab", w.Fs.Text(Article));

        w.Session.Edit("abc");
        w.Session.Detach(reportFailure: false);
        Assert.Equal(0, w.Scheduler.PendingTimers);
    }

    [Fact]
    public void CloseBookRescuesAnUnsavableBufferAndSaysSo()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("unsavable\n");
        w.Fs.SetReadOnly(Article);

        w.Session.Detach(reportFailure: true);

        var alert = Assert.Single(w.Alerts);
        Assert.Equal(Strings.Documents.RescueFailedTitle("Scene"), alert.Title);
        Assert.Equal(Strings.Documents.RescueKeptMessage("01-Scene (rescued).md"), alert.Message);
        Assert.Equal("unsavable\n", w.Fs.Text(@"C:\Book\01-Scene (rescued).md"));
        Assert.IsType<Stage.Empty>(w.Session.Stage);               // detach always completes
    }

    [Fact]
    public void TerminateFlushWritesOrRescues()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("in time\n");
        w.Session.TerminateFlush();
        Assert.Equal("in time\n", w.Fs.Text(Article));

        w.Session.Edit("too late\n");
        w.Fs.SetReadOnly(Article);
        w.Session.TerminateFlush();
        Assert.Equal("too late\n", w.Fs.Text(@"C:\Book\01-Scene (rescued).md"));
        Assert.Empty(w.Alerts);                                    // no modal at exit
    }

    [Fact]
    public void UndoGenerationChangesOnEveryLoadAndDetach()
    {
        using var w = Book();
        var start = w.Session.UndoGeneration;
        w.Session.Select(Article);
        var afterLoad = w.Session.UndoGeneration;
        Assert.NotEqual(start, afterLoad);

        w.Session.Select(Other);
        Assert.NotEqual(afterLoad, w.Session.UndoGeneration);

        var beforeDetach = w.Session.UndoGeneration;
        w.Session.Detach(reportFailure: false);
        Assert.NotEqual(beforeDetach, w.Session.UndoGeneration);
    }

    [Fact]
    public void ChangedFiresOnObservableTransitions()
    {
        using var w = Book();
        Assert.Equal(0, w.ChangedCount);
        w.Session.Select(Article);
        Assert.True(w.ChangedCount > 0);

        var afterSelect = w.ChangedCount;
        w.Session.Edit("x");
        Assert.True(w.ChangedCount > afterSelect);

        var afterEdit = w.ChangedCount;
        w.Session.Edit("x");                                       // the same text is not a change
        Assert.Equal(afterEdit, w.ChangedCount);
    }

    // ---- the document-window rows of §6.3 ----

    [Fact]
    public void OpeningADocumentTakesItsStampWatchesItAndClaimsIt()
    {
        using var w = Doc();
        Assert.True(w.Session.Open(Document));

        Assert.Equal("# Title\nBody\n", w.Session.Text);
        Assert.Equal("Chapter One", w.Session.Title);
        Assert.False(w.Session.IsDirty);
        Assert.Equal(w.Session.Text, w.Session.LastSavedText);
        Assert.Equal(w.Fs.Stamp(Document), w.Session.DiskStamp);
        Assert.Equal(Document, w.Watcher.WatchedPath);
        Assert.Equal(w.WindowId, w.Registry.Owning(World.Canonical(Document)));
        Assert.Equal([Document], w.Identities);
    }

    [Fact]
    public void AFailedOpenAsksForTheAlertAndChangesNothing()
    {
        using var w = Doc();
        Assert.False(w.Session.Open(@"C:\Docs\missing.md"));
        Assert.Equal(Strings.Documents.CouldNotOpen, Assert.Single(w.Alerts).Title);
        Assert.IsType<Stage.Untitled>(w.Session.Stage);
    }

    [Fact]
    public void AnImportedBundleIsAnUntitledDocumentTitledAfterTheBundleAndOwnsNoFile()
    {
        using var w = Doc();
        w.Fs.AddFile(@"C:\Downloads\Trip.textpack", DocumentFixtures.TextPack("# Trip\n"));

        Assert.True(w.Session.Open(@"C:\Downloads\Trip.textpack"));

        Assert.IsType<Stage.Untitled>(w.Session.Stage);
        Assert.Equal("Trip", w.Session.Title);
        Assert.Equal(@"C:\Downloads\Trip.textpack", w.Session.ImportedFrom);
        Assert.False(w.Session.IsDirty);                           // closing an unedited import prompts nothing
        Assert.Null(w.Session.CanonicalPath);
        Assert.False(w.Watcher.IsWatching);
    }

    [Fact]
    public void AnAutosaveWritesTheFileButLeavesTheTitlesEditedMarkerAlone()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("# Title\nEdited\n");

        w.Scheduler.Advance(TextFileSession.AutosaveDelay);

        Assert.Equal("# Title\nEdited\n", w.Fs.Text(Document));
        Assert.True(w.Session.IsDirty);                            // "— Edited" survives an autosave-in-place
        Assert.False(w.Session.HasUnsavedChanges);
    }

    [Fact]
    public void OnlyAnExplicitSaveClearsTheEditedMarkerEvenWithNothingLeftToWrite()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("# Title\nEdited\n");
        w.Scheduler.Advance(TextFileSession.AutosaveDelay);
        Assert.True(w.Session.IsDirty);

        Assert.True(w.Session.FlushNow(explicitSave: true));

        Assert.False(w.Session.IsDirty);
        Assert.Equal("# Title\nEdited\n", w.Session.LastSavedText);
        Assert.Single(w.Fs.Writes);                                // the explicit save wrote nothing new
    }

    [Fact]
    public void SaveOnAnUntitledDocumentWritesNothingBecauseTheCallerRoutesToSaveAs()
    {
        using var w = Doc();
        w.Session.OpenUntitled("draft", dirty: true);
        Assert.True(w.Session.FlushNow(explicitSave: true));
        Assert.Empty(w.Fs.Writes);
        Assert.True(w.Session.IsDirty);
    }

    [Fact]
    public void SaveAsWritesTheNewPathKeepsTheDressingAndLeavesTheOldFileAlone()
    {
        using var w = Doc();
        w.Fs.AddFile(@"C:\Docs\legacy.md", DocumentFixtures.Cp1251.GetBytes("Привет\r\n"));
        w.Fs.AddDirectory(@"C:\Docs\Copies");
        w.Session.Open(@"C:\Docs\legacy.md");
        w.Session.Edit("Привет\nЕщё\n");

        Assert.True(w.Session.SaveAs(@"C:\Docs\Copies\copy.md"));

        Assert.Equal(DocumentFixtures.Cp1251.GetBytes("Привет\r\nЕщё\r\n"), w.Fs.Bytes(@"C:\Docs\Copies\copy.md"));
        Assert.Equal(DocumentFixtures.Cp1251.GetBytes("Привет\r\n"), w.Fs.Bytes(@"C:\Docs\legacy.md"));
        Assert.Equal(@"C:\Docs\Copies\copy.md", w.Session.EditingPath);
        Assert.Equal("copy", w.Session.Title);
        Assert.False(w.Session.IsDirty);
        Assert.Equal(@"C:\Docs\Copies\copy.md", w.Watcher.WatchedPath);
        Assert.Equal(w.WindowId, w.Registry.Owning(World.Canonical(@"C:\Docs\Copies\copy.md")));
        Assert.Null(w.Registry.Owning(World.Canonical(@"C:\Docs\legacy.md")));
    }

    [Fact]
    public void AFailedSaveAsLeavesTheSessionWhereItWasWithItsError()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Fs.NextWriteFailure = new IOException("the disk is full");

        Assert.False(w.Session.SaveAs(@"C:\Docs\elsewhere.md"));

        Assert.Equal(Document, w.Session.EditingPath);
        Assert.Equal("the disk is full", w.Session.SaveErrorText);
    }

    [Fact]
    public void RenameRetargetsEverythingAndReDecidesTheIdentity()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Fs.Move(Document, @"C:\Docs\Renamed.md");

        w.Session.Retarget(@"C:\Docs\Renamed.md");

        Assert.Equal(@"C:\Docs\Renamed.md", w.Session.EditingPath);
        Assert.Equal("Renamed", w.Session.Title);
        Assert.Equal(@"C:\Docs\Renamed.md", w.Watcher.WatchedPath);
        Assert.Equal(w.WindowId, w.Registry.Owning(World.Canonical(@"C:\Docs\Renamed.md")));
        Assert.Equal([Document, @"C:\Docs\Renamed.md"], w.Identities);
    }

    [Fact]
    public void RevertGoesBackToTheLastExplicitSaveAndReplacesTheEditor()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("first change\n");
        w.Session.FlushNow(explicitSave: true);
        w.Session.Edit("second change\n");
        w.Scheduler.Advance(TextFileSession.AutosaveDelay);        // the autosave put the second one on disk

        w.Session.RevertToSaved();

        Assert.Equal("first change\n", w.Session.Text);
        Assert.Equal("first change\n", w.Fs.Text(Document));
        Assert.False(w.Session.IsDirty);
        Assert.Equal("first change\n", w.Replacements[^1]);
    }

    [Fact]
    public void ADeletedFileKeepsADocumentWindowsBufferAndPathAndMarksItEdited()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Fs.Delete(Document);

        w.Watcher.RaiseDeleted();

        Assert.Equal(Document, w.Session.EditingPath);
        Assert.Equal("# Title\nBody\n", w.Session.Text);
        Assert.True(w.Session.IsDirty);
        Assert.False(w.Session.Conflicted);

        // …and a save recreates it, with no stale stamp in the way.
        w.Fs.AddDirectory(@"C:\Docs");
        Assert.True(w.Session.FlushNow(explicitSave: true));
        Assert.Equal("# Title\nBody\n", w.Fs.Text(Document));
    }

    [Fact]
    public void AReadOnlyFileOpensNormallyAndOnlyTheAutosaveFails()
    {
        using var w = Doc();
        w.Fs.SetReadOnly(Document);

        Assert.True(w.Session.Open(Document));
        w.Session.Edit("try me\n");
        w.Scheduler.Advance(TextFileSession.AutosaveDelay);

        Assert.NotNull(w.Session.SaveErrorText);
        Assert.True(w.Session.IsDirty);
        Assert.True(w.Session.HasUnsavedChanges);
        Assert.Empty(w.Alerts);                                    // an error bar, never a modal
    }

    [Fact]
    public void AConflictSuspendsTheAutosaveUntilTheWriterChooses()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Document, "theirs, and longer\n");

        w.Watcher.RaiseChanged();
        Assert.True(w.Session.Conflicted);
        Assert.Equal(0, w.Scheduler.PendingTimers);

        w.Session.Edit("mine, more\n");
        Assert.Equal(0, w.Scheduler.PendingTimers);                // still suspended
        w.Scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal("theirs, and longer\n", w.Fs.Text(Document));
    }

    [Fact]
    public void ADocumentWindowReloadingAConflictKeepsItsWindow()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Document, "theirs, and longer\n");
        w.Watcher.RaiseChanged();

        w.Session.ResolveConflictReloading();

        Assert.Equal(Document, w.Session.EditingPath);             // not Stage.Empty: NSDocument keeps the window
        Assert.Equal("theirs, and longer\n", w.Session.Text);
        Assert.False(w.Session.Conflicted);
        Assert.False(w.Session.IsDirty);
    }

    [Fact]
    public void ReloadingWhenTheFileIsGoneKeepsTheTextAndLetsASaveRecreateIt()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("mine\n");
        w.Fs.Delete(Document);
        w.Watcher.RaiseDeleted();
        Assert.True(w.Session.Conflicted);

        w.Session.ResolveConflictReloading();

        Assert.Equal("mine\n", w.Session.Text);
        Assert.False(w.Session.Conflicted);
        w.Fs.AddDirectory(@"C:\Docs");
        Assert.True(w.Session.FlushNow(explicitSave: true));
        Assert.Equal("mine\n", w.Fs.Text(Document));
    }

    [Fact]
    public void ADocumentWindowNeverHandsOffToItself()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("mine\n");

        w.Session.RecheckOwnership();

        Assert.IsType<Stage.Editing>(w.Session.Stage);
        Assert.Empty(w.Fs.Writes);
    }

    [Fact]
    public void AnExampleOpensUntitledAndEditedSoClosingOffersToSave()
    {
        using var w = Doc();
        w.Session.OpenUntitled("# Welcome\r\n", title: Strings.UntitledNumbered(2), dirty: true);

        Assert.IsType<Stage.Untitled>(w.Session.Stage);
        Assert.Equal("Untitled 2", w.Session.Title);
        Assert.Equal("# Welcome\n", w.Session.Text);               // the model is LF even for an example
        Assert.True(w.Session.IsDirty);
        Assert.Equal(TextFileDressing.Default, w.Session.Dressing);
    }

    [Fact]
    public void EditingIsIgnoredWhileAnotherWindowOwnsTheFile()
    {
        using var w = Book();
        w.Registry.Register(Guid.NewGuid(), World.Canonical(Article));
        w.Session.Select(Article);

        w.Session.Edit("cannot type here");

        Assert.IsType<Stage.Handoff>(w.Session.Stage);
        Assert.Equal("", w.Session.Text);
        Assert.False(w.Session.IsDirty);
    }

    [Fact]
    public void DisposingUnsubscribesFromTheWatcherAndReleasesTheRegistryEntry()
    {
        var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("pending\n");
        w.Session.Dispose();

        Assert.Null(w.Registry.Owning(World.Canonical(Document)));
        Assert.Equal(0, w.Scheduler.PendingTimers);
        w.Fs.WriteExternally(Document, "theirs\n");
        w.Watcher.RaiseChanged();
        Assert.False(w.Session.Conflicted);                        // no longer listening
    }
}
