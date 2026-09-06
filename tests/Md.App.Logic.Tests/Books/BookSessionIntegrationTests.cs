using Md.App.Logic.Books;
using Md.App.Logic.Documents;
using Md.Core.Book;
using Md.Core.Document;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// The navigator over the real <see cref="TextFileSession"/> in article mode — the seam WP2
/// published and the Book window uses. Real files, real bytes: what a fake session cannot prove is
/// that a selection change actually writes the outgoing article, that the flush gate saves through
/// the same path an output reads from, and that a document window taking the file puts the pane
/// into handoff.
/// </summary>
[Collection("BookFlushGate")]
public class BookSessionIntegrationTests : IDisposable
{
    readonly BookFixture fixture = new("session-" + Guid.NewGuid().ToString("N"));
    readonly FakeFileWatcher watcher = new();
    readonly FakeScheduler scheduler = new();
    readonly FakeDocumentRegistry registry = new();
    readonly FakeFileIdentity identity = new();
    readonly FakeSettingsStore settings = new();
    readonly TextFileSession session;
    readonly BookNavigatorModel navigator;

    public BookSessionIntegrationTests()
    {
        fixture.Article("01-Preface.md", "# Preface\n");
        fixture.Article("02-One/01-a.md", "# A\n");
        fixture.Article("02-One/02-b.md", "# B\n");
        session = new TextFileSession(
            SystemIoFileSystem.Instance, watcher, scheduler, registry, identity, SessionRole.BookArticle);
        navigator = new BookNavigatorModel(new TextFileSessionArticles(session), settings);
        navigator.OpenBook(fixture.Root);
    }

    public void Dispose()
    {
        session.Dispose();
        fixture.Dispose();
    }

    [Fact]
    public void TheFirstArticleIsLoadedWithItsBytes()
    {
        Assert.Equal("# Preface\n", session.Text);
        Assert.Equal("Preface", session.Title);                       // the ordering prefix is not a title
        Assert.IsType<Stage.Editing>(session.Stage);
    }

    [Fact]
    public void MovingToAnotherArticleWritesTheOutgoingOne()
    {
        session.Edit("# Preface\n\nRewritten.\n");

        navigator.Select(fixture.At("02-One", "01-a.md"));

        Assert.Equal("# Preface\n\nRewritten.\n", File.ReadAllText(fixture.At("01-Preface.md")));
        Assert.Equal("# A\n", session.Text);
        Assert.False(session.IsDirty);
    }

    [Fact]
    public void SteppingThroughTheBookSavesOnEveryHop()
    {
        navigator.StepNext();
        session.Edit("# A edited\n");
        navigator.StepNext();

        Assert.Equal("# A edited\n", File.ReadAllText(fixture.At("02-One", "01-a.md")));
        Assert.Equal("# B\n", session.Text);
    }

    [Fact]
    public void TheFlushGateSavesTheBufferBeforeAnOutputReadsIt()
    {
        session.Edit("# Preface\n\nUnsaved.\n");
        using var binding = new BookFlushGateBinding(() => session.FlushNow(explicitSave: false));

        Assert.True(BookFlushGate.FlushEditor());

        Assert.Equal("# Preface\n\nUnsaved.\n", File.ReadAllText(fixture.At("01-Preface.md")));
        Assert.Equal(1, binding.Requests);
    }

    [Fact]
    public void AnOutputCompilesWhatTheGateJustSaved()
    {
        session.Edit("Front matter, unsaved.");
        using var binding = new BookFlushGateBinding(() => session.FlushNow(explicitSave: false));
        var outputs = new FakeBookOutputs();
        var output = new BookOutput(outputs, settings, () => navigator.Book);

        output.ExportPdfAsync().GetAwaiter().GetResult();

        Assert.Contains("Front matter, unsaved.", outputs.Pdfs.Single().Source, StringComparison.Ordinal);
    }

    [Fact]
    public void AVetoStopsTheOutputAndTheBufferSurvives()
    {
        session.Edit("Not going anywhere.");
        using var binding = new BookFlushGateBinding(() => false);
        var outputs = new FakeBookOutputs();
        var output = new BookOutput(outputs, settings, () => navigator.Book);

        output.ExportPdfAsync().GetAwaiter().GetResult();

        Assert.Empty(outputs.Pdfs);
        Assert.Equal("Not going anywhere.", session.Text);
        Assert.True(session.IsDirty);
    }

    [Fact]
    public void WhileADocumentWindowOwnsTheArticleThePaneStepsAsideAndReclaimsItAfterwards()
    {
        var article = fixture.At("02-One", "01-a.md");
        var owner = Guid.NewGuid();
        registry.Register(owner, identity.Canonical(article));

        navigator.Select(article);

        Assert.IsType<Stage.Handoff>(session.Stage);
        Assert.Null(session.EditingPath);
        Assert.Equal(owner, session.OwningWindow);

        registry.Unregister(owner);
        session.RecheckOwnership();

        Assert.IsType<Stage.Editing>(session.Stage);
        Assert.Equal("# A\n", session.Text);
    }

    [Fact]
    public void OpeningAnArticleInItsOwnWindowSavesFirstAndOnlyThenAsksForTheWindow()
    {
        var opened = new List<string>();
        navigator.OpenRequested += opened.Add;
        session.Edit("# Preface\n\nSaved by the handoff.\n");

        Assert.True(navigator.OpenInWindow(fixture.At("01-Preface.md")));

        Assert.Equal("# Preface\n\nSaved by the handoff.\n", File.ReadAllText(fixture.At("01-Preface.md")));
        Assert.IsType<Stage.Handoff>(session.Stage);
        Assert.Single(opened);
        BookArticleOpens.Reset();
    }

    [Fact]
    public void AnExternalRewriteUnderUnsavedEditsIsAConflictAndNothingIsClobbered()
    {
        session.Edit("Mine, longer than theirs.");
        File.WriteAllText(fixture.At("01-Preface.md"), "Theirs.");

        Assert.False(session.FlushNow(explicitSave: false));

        Assert.True(session.Conflicted);
        Assert.Equal("Theirs.", File.ReadAllText(fixture.At("01-Preface.md")));
        Assert.Equal("Mine, longer than theirs.", session.Text);

        session.ResolveConflictKeepingMine();

        Assert.False(session.Conflicted);
        Assert.Equal("Mine, longer than theirs.", File.ReadAllText(fixture.At("01-Preface.md")));
    }

    [Fact]
    public void AWatcherEventOverOurOwnBytesIsNotAConflict()
    {
        session.Edit("Ours.");
        Assert.True(session.FlushNow(explicitSave: false));

        // The watcher fires for our own write too; the stamp — not the event — decides.
        watcher.RaiseChanged();

        Assert.False(session.Conflicted);
        Assert.Equal("Ours.", session.Text);
    }

    [Fact]
    public void ARenameFollowsTheSelectionThroughTheRealSessionAndTheFileKeepsItsBytes()
    {
        navigator.Select(fixture.At("02-One", "02-b.md"));
        session.Edit("# B\n\nStill B.\n");

        navigator.PerformRename(fixture.At("02-One", "02-b.md"), "Bravo");

        var renamed = fixture.At("02-One", "02-Bravo.md");
        Assert.True(File.Exists(renamed));
        Assert.Equal("# B\n\nStill B.\n", File.ReadAllText(renamed));
        Assert.Equal(BookPaths.Standardize(renamed), navigator.Selection);
        Assert.Equal("# B\n\nStill B.\n", session.Text);
    }
}
