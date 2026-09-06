using System.Text;
using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// The in-place editing state machine (mdTests: the fourteen testSession* /
/// testBookFlushGate* tests) against a scratch book folder, plus the watcher events
/// and the autosave / alert seams the Swift tests reached through AppKit.
/// </summary>
public class BookArticleSessionTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    private static string Read(string path) => Utf8.GetString(File.ReadAllBytes(path));

    private static void SetReadOnly(string path, bool readOnly)
    {
        var attributes = File.GetAttributes(path);
        File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
    }

    /// <summary>A scratch book folder on disk with "01-Scene.md" = "# Scene\n".</summary>
    private sealed class SessionBook : IDisposable
    {
        public string Root { get; }
        public string Article { get; }
        public BookArticleSession Session { get; }

        public SessionBook(IDocumentOwnership? ownership = null, IBookAlerts? alerts = null, IAutosaveScheduler? autosave = null)
        {
            Root = Path.Combine(Path.GetTempPath(), "md-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Article = Path.Combine(Root, "01-Scene.md");
            File.WriteAllBytes(Article, Utf8.GetBytes("# Scene\n"));
            Session = new BookArticleSession(ownership: ownership, alerts: alerts, autosave: autosave);
            Session.OpenBook(Root);
        }

        public string Write(string name, string text)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllBytes(path, Utf8.GetBytes(text));
            return path;
        }

        public void Dispose()
        {
            Session.Dispose();
            foreach (var file in Directory.EnumerateFiles(Root))
            {
                try { SetReadOnly(file, false); } catch (IOException) { }
            }
            try { Directory.Delete(Root, true); } catch (IOException) { }
        }
    }

    private sealed class FakeOwnership : IDocumentOwnership
    {
        public HashSet<string> Owned { get; } = new(StringComparer.Ordinal);
        public List<string> Shown { get; } = new();
        public bool Owns(string path) => Owned.Any(o => BookPaths.Same(o, path));
        public void ShowOwner(string path) => Shown.Add(path);
    }

    private sealed class FakeAlerts : IBookAlerts
    {
        public List<(string Message, string Informative)> Presented { get; } = new();
        public void PresentError(string message, string informative) => Presented.Add((message, informative));
    }

    private sealed class ManualAutosave : IAutosaveScheduler
    {
        public Action? Pending { get; private set; }
        public int Scheduled { get; private set; }
        public void Schedule(Action flush) { Pending = flush; Scheduled++; }
        public void Cancel() => Pending = null;
        public void Fire() { var pending = Pending; Pending = null; pending?.Invoke(); }
    }

    [Fact]
    public void SessionLoadsEditsAndFlushesToDisk()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        Assert.Equal("# Scene\n", session.Text);
        Assert.Equal(BookPaths.Standardize(book.Article), session.EditingPath);
        Assert.Equal("Scene", session.Title);

        session.Edit("# Scene\n\nIt was a dark and stormy night.\n");
        Assert.True(session.Dirty);
        Assert.True(session.FlushNow());
        Assert.False(session.Dirty);
        Assert.Equal("# Scene\n\nIt was a dark and stormy night.\n", Read(book.Article));
        session.CloseBook();
    }

    [Fact]
    public void SessionSelectionChangeSavesTheOutgoingArticle()
    {
        using var book = new SessionBook();
        var session = book.Session;
        var second = book.Write("02-Scene.md", "# Second\n");

        Assert.True(session.Select(book.Article));
        session.Edit("# Scene, revised\n");
        Assert.True(session.Select(second));
        // Moving on flushed the first article and loaded the second.
        Assert.Equal("# Scene, revised\n", Read(book.Article));
        Assert.Equal("# Second\n", session.Text);
        session.CloseBook();
    }

    [Fact]
    public void SessionRefusesToClobberAFileChangedUnderIt()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Mine\n");
        // Someone else rewrites the file (different size, so the staleness check
        // cannot be fooled by coarse timestamps).
        book.Write("01-Scene.md", "# Theirs, and much longer than before\n");

        Assert.False(session.FlushNow());
        Assert.True(session.Conflicted);
        // Neither side was lost: theirs is on disk, mine is in the buffer.
        Assert.Equal("# Theirs, and much longer than before\n", Read(book.Article));
        Assert.Equal("# Mine\n", session.Text);

        // The writer decides: Keep My Version writes the buffer out.
        session.ResolveConflictKeepingMine();
        Assert.False(session.Conflicted);
        Assert.False(session.Dirty);
        Assert.Equal("# Mine\n", Read(book.Article));
        session.CloseBook();
    }

    [Fact]
    public void SessionReloadResolutionDiscardsTheBuffer()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Mine\n");
        book.Write("01-Scene.md", "# Theirs, and much longer than before\n");
        Assert.False(session.FlushNow());
        Assert.True(session.Conflicted);

        session.ResolveConflictReloading();
        Assert.False(session.Conflicted);
        Assert.Equal("# Theirs, and much longer than before\n", session.Text);
        Assert.Equal(BookPaths.Standardize(book.Article), session.EditingPath);
        // Nothing dirty remains, so closing writes nothing.
        session.CloseBook();
        Assert.Equal("# Theirs, and much longer than before\n", Read(book.Article));
    }

    [Fact]
    public void SessionCloseBookFlushesPendingEdits()
    {
        using var book = new SessionBook();
        Assert.True(book.Session.Select(book.Article));
        book.Session.Edit("# Closing time\n");
        book.Session.CloseBook();
        Assert.Equal("# Closing time\n", Read(book.Article));
        Assert.Equal(BookStage.Empty, book.Session.Stage);
        Assert.Null(book.Session.Root);
    }

    [Fact]
    public void SessionCleanFlushLeavesTheFileUntouched()
    {
        // flushNow runs on every selection change and window focus; a clean session
        // must not rewrite (and re-stamp) the file each time.
        using var book = new SessionBook();
        Assert.True(book.Session.Select(book.Article));
        var before = File.GetLastWriteTimeUtc(book.Article);
        Thread.Sleep(50);

        Assert.True(book.Session.FlushNow());

        Assert.Equal(before, File.GetLastWriteTimeUtc(book.Article));
        book.Session.CloseBook();
    }

    [Fact]
    public void SessionDeselectFlushesAndFullyDetaches()
    {
        // select(nil) is the prelude of every managed file operation: it must flush
        // the buffer and fully let go of the file before anything moves on disk.
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Detached\n");

        Assert.True(session.Select(null));
        Assert.Equal("# Detached\n", Read(book.Article));
        Assert.Null(session.EditingPath);
        Assert.Equal(BookStage.Empty, session.Stage);
        Assert.Equal("", session.Text);
        Assert.Equal("", session.Title);
        Assert.False(session.Dirty);
        session.CloseBook();
    }

    [Fact]
    public void SessionFailedFlushAbortsTheSelectionChange()
    {
        // The sidebar reverts its selection when Select returns false, on the
        // contract that the session still holds the unsaved article.
        using var book = new SessionBook();
        var session = book.Session;
        var second = book.Write("02-Scene.md", "# Second\n");
        Assert.True(session.Select(book.Article));
        session.Edit("# Unsaved\n");
        SetReadOnly(book.Article, true);

        Assert.False(session.Select(second));
        Assert.Equal(BookPaths.Standardize(book.Article), session.EditingPath);
        Assert.Equal("# Unsaved\n", session.Text);
        Assert.True(session.Dirty);
        Assert.NotNull(session.SaveErrorText);

        // Once the file is writable again the same move succeeds.
        SetReadOnly(book.Article, false);
        Assert.True(session.Select(second));
        Assert.Equal("# Unsaved\n", Read(book.Article));
        Assert.Null(session.SaveErrorText);
        session.CloseBook();
    }

    [Fact]
    public void SessionHandOffForExternalOpenSavesThenStepsAside()
    {
        // Opening the edited article in its own window must ship the buffer to disk
        // first and leave the session in handoff so there is never a second writer.
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# For the window\n");

        Assert.True(session.HandOffForExternalOpen());
        Assert.Equal("# For the window\n", Read(book.Article));
        Assert.Equal(BookStage.Handoff(BookPaths.Standardize(book.Article)), session.Stage);
        Assert.Null(session.EditingPath);
        Assert.Equal("Scene", session.Title);
        session.CloseBook();
    }

    [Fact]
    public void SessionStepsAsideWhileADocumentOwnsTheArticle()
    {
        // The two-writers guard: while a document window has the file, selecting it
        // yields a handoff, and the session reclaims it once the document goes away.
        var ownership = new FakeOwnership();
        using var book = new SessionBook(ownership);
        var session = book.Session;
        ownership.Owned.Add(book.Article);

        Assert.True(session.Select(book.Article));
        Assert.Equal(BookStage.Handoff(BookPaths.Standardize(book.Article)), session.Stage);
        Assert.Null(session.EditingPath);
        session.ShowOwningWindow();
        Assert.Single(ownership.Shown);

        ownership.Owned.Clear();
        session.RecheckOwnership();
        Assert.Equal(BookPaths.Standardize(book.Article), session.EditingPath);
        Assert.Equal("# Scene\n", session.Text);
        session.CloseBook();
    }

    [Fact]
    public void RecheckOwnershipSavesThenHandsOffWhenADocumentOpensOverAnEditedArticle()
    {
        var ownership = new FakeOwnership();
        using var book = new SessionBook(ownership);
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Edited here first\n");

        ownership.Owned.Add(book.Article);
        session.RecheckOwnership();
        Assert.Equal(BookStage.Handoff(BookPaths.Standardize(book.Article)), session.Stage);
        Assert.Equal("# Edited here first\n", Read(book.Article));
        Assert.False(session.Dirty);

        // A failed flush stays put: the buffer must remain visible with its error.
        ownership.Owned.Clear();
        session.RecheckOwnership();
        Assert.Equal(BookStageKind.Editing, session.Stage.Kind);
        session.Edit("# Unsavable\n");
        SetReadOnly(book.Article, true);
        ownership.Owned.Add(book.Article);
        session.RecheckOwnership();
        Assert.Equal(BookStageKind.Editing, session.Stage.Kind);
        Assert.NotNull(session.SaveErrorText);
        SetReadOnly(book.Article, false);
        session.CloseBook();
    }

    [Fact]
    public void SessionRescueCopyParksTheBufferWithoutOverwriting()
    {
        // The quit-time last resort: when the regular write cannot land, the buffer
        // goes to a fresh "(rescued)" sibling, clobbering nothing.
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Mine\n");
        // Occupy the first rescue name to prove the copy never overwrites.
        var taken = book.Write("01-Scene (rescued).md", "occupied");

        var rescued = session.WriteRescueCopy(book.Article);
        Assert.NotNull(rescued);
        Assert.Equal("01-Scene (rescued 2).md", Path.GetFileName(rescued));
        Assert.Equal("# Mine\n", Read(rescued!));
        Assert.Equal("occupied", Read(taken));
        // The rescue is a copy, not a save: the buffer is still dirty.
        Assert.True(session.Dirty);
        session.CloseBook();
    }

    [Fact]
    public void BookFlushGateSavesTheBufferBeforeCompile()
    {
        // The book output actions can't reach the session directly — the flush
        // travels through the gate, and the buffer must be on disk when Post returns.
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Through the gate\n");

        var gate = BookFlushGate.Post();
        Assert.False(gate.Vetoed);
        Assert.Equal("# Through the gate\n", Read(book.Article));
        Assert.False(session.Dirty);
        Assert.True(BookFlushGate.FlushEditor());
        session.CloseBook();
    }

    [Fact]
    public void BookFlushGateVetoesWhenTheSaveFails()
    {
        // A compile must never ship a stale page: when the flush cannot land, the
        // gate is vetoed and the output action aborts.
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Unsavable\n");
        SetReadOnly(book.Article, true);

        var gate = BookFlushGate.Post();
        Assert.True(gate.Vetoed);
        Assert.False(BookFlushGate.FlushEditor());
        // Nothing was lost: the buffer is still the session's to save.
        Assert.Equal("# Unsavable\n", session.Text);
        Assert.True(session.Dirty);

        SetReadOnly(book.Article, false);
        session.CloseBook();
        Assert.Equal("# Unsavable\n", Read(book.Article));
    }

    [Fact]
    public void SessionRoundTripsLegacyEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        using var book = new SessionBook();
        var session = book.Session;
        var legacy = Path.Combine(book.Root, "03-Legacy.md");
        var original = "Привет, мир!";
        File.WriteAllBytes(legacy, cp1251.GetBytes(original));

        Assert.True(session.Select(legacy));
        Assert.Equal(original, session.Text);
        Assert.Equal(1251, session.Encoding.CodePage);
        session.Edit(original + " Ещё.");
        Assert.True(session.FlushNow());
        // The save stayed in the file's own encoding.
        Assert.Equal(cp1251.GetBytes(original + " Ещё."), File.ReadAllBytes(legacy));
        Assert.Equal(1251, session.Encoding.CodePage);

        // Text the encoding cannot hold upgrades the file to UTF-8 and says so.
        session.Edit(original + " \U0001F642");
        Assert.True(session.FlushNow());
        Assert.Equal(65001, session.Encoding.CodePage);
        Assert.Equal(original + " \U0001F642", Read(legacy));
        session.CloseBook();
    }

    [Fact]
    public void SessionRoundTripsABomedUtf16Article()
    {
        // UTF-16 is recognised only behind its BOM; the save writes the BOM back so
        // the file stays what it was, instead of being silently rewritten as UTF-8.
        using var book = new SessionBook();
        var session = book.Session;
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        var wide = Path.Combine(book.Root, "04-Wide.md");
        File.WriteAllBytes(wide, utf16.GetPreamble().Concat(utf16.GetBytes("# Chapter\n")).ToArray());

        Assert.True(session.Select(wide));
        Assert.Equal("# Chapter\n", session.Text);
        Assert.Equal(1200, session.Encoding.CodePage);
        session.Edit("# Chapter\n\nWide.\n");
        Assert.True(session.FlushNow());
        Assert.Equal(utf16.GetPreamble().Concat(utf16.GetBytes("# Chapter\n\nWide.\n")).ToArray(), File.ReadAllBytes(wide));
        session.CloseBook();
    }

    [Fact]
    public void SessionReportsAnUnreadableArticleWithoutFailingTheSelection()
    {
        using var book = new SessionBook();
        var session = book.Session;
        var missing = Path.Combine(book.Root, "02-Gone.md");
        Assert.True(session.Select(missing));
        Assert.Equal(BookStage.Unreadable(BookPaths.Standardize(missing)), session.Stage);
        Assert.Null(session.EditingPath);
        Assert.Equal("Gone", session.Title);
        Assert.Equal("", session.Text);
        // A real article afterwards loads normally.
        Assert.True(session.Select(book.Article));
        Assert.Equal("# Scene\n", session.Text);
        session.CloseBook();
    }

    [Fact]
    public void SelectingTheEditedArticleAgainIsANoOp()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Still typing\n");
        var generation = session.UndoGeneration;
        Assert.True(session.Select(book.Article));
        Assert.True(session.Dirty);
        Assert.Equal("# Still typing\n", session.Text);
        Assert.Equal(generation, session.UndoGeneration);
        Assert.Equal("# Scene\n", Read(book.Article));
        session.CloseBook();
    }

    [Fact]
    public void FileChangedReloadsACleanSessionAndConflictsADirtyOne()
    {
        var autosave = new ManualAutosave();
        using var book = new SessionBook(autosave: autosave);
        var session = book.Session;
        Assert.True(session.Select(book.Article));

        // Our own write leaves the stamp we recorded: nothing happens.
        session.Edit("# Ours\n");
        Assert.True(session.FlushNow());
        session.FileChanged();
        Assert.False(session.Conflicted);
        Assert.Equal("# Ours\n", session.Text);

        // Clean session, external change: follow the disk silently.
        book.Write("01-Scene.md", "# Theirs, rewritten outside\n");
        session.FileChanged();
        Assert.False(session.Conflicted);
        Assert.Equal("# Theirs, rewritten outside\n", session.Text);
        Assert.False(session.Dirty);

        // Dirty session, external change: conflict, autosave disarmed.
        session.Edit("# Mine again\n");
        Assert.NotNull(autosave.Pending);
        book.Write("01-Scene.md", "# Theirs once more, longer still\n");
        session.FileChanged();
        Assert.True(session.Conflicted);
        Assert.Null(autosave.Pending);
        Assert.Equal("# Mine again\n", session.Text);
        // Typing while conflicted does not re-arm the autosave.
        session.Edit("# Mine again, more\n");
        Assert.Null(autosave.Pending);
        session.ResolveConflictReloading();
        session.CloseBook();
    }

    [Fact]
    public void FileDeletedDetachesACleanSessionAndConflictsADirtyOne()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        File.Delete(book.Article);
        session.FileDeleted();
        Assert.Equal(BookStage.Empty, session.Stage);

        var again = book.Write("01-Scene.md", "# Scene\n");
        Assert.True(session.Select(again));
        session.Edit("# Keep me\n");
        File.Delete(again);
        session.FileDeleted();
        Assert.True(session.Conflicted);
        Assert.Equal(BookStageKind.Editing, session.Stage.Kind);
        // A vanished file is stale to the regular flush; Keep My Version recreates it.
        Assert.False(session.FlushNow());
        session.ResolveConflictKeepingMine();
        Assert.Equal("# Keep me\n", Read(again));
        Assert.False(session.Dirty);
        session.CloseBook();
    }

    [Fact]
    public void FileMovedFollowsTheRename()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Renamed under us\n");
        var moved = Path.Combine(book.Root, "01-Renamed.md");
        // The stamp travels with the file, so the pending save lands at the new name.
        File.Move(book.Article, moved);
        session.FileMoved(moved);
        Assert.Equal(BookPaths.Standardize(moved), session.EditingPath);
        Assert.Equal("Renamed", session.Title);
        Assert.True(session.FlushNow());
        Assert.Equal("# Renamed under us\n", Read(moved));
        session.CloseBook();
    }

    [Fact]
    public void AutosaveIsArmedPerKeystrokeAndDisarmedOnDetach()
    {
        var autosave = new ManualAutosave();
        using var book = new SessionBook(autosave: autosave);
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        Assert.Null(autosave.Pending);

        session.Edit("# One\n");
        session.Edit("# One two\n");
        Assert.Equal(2, autosave.Scheduled);
        Assert.NotNull(autosave.Pending);
        autosave.Fire();
        Assert.False(session.Dirty);
        Assert.Equal("# One two\n", Read(book.Article));

        session.Edit("# One two three\n");
        Assert.True(session.Select(null));
        Assert.Null(autosave.Pending);
        Assert.Equal("# One two three\n", Read(book.Article));
        session.CloseBook();
    }

    [Fact]
    public void CloseBookRescuesAnUnsavableBufferAndSaysSo()
    {
        var alerts = new FakeAlerts();
        using var book = new SessionBook(alerts: alerts);
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Cannot land\n");
        SetReadOnly(book.Article, true);

        session.CloseBook();
        Assert.Equal(BookStage.Empty, session.Stage);
        var rescued = Path.Combine(book.Root, "01-Scene (rescued).md");
        Assert.True(File.Exists(rescued));
        Assert.Equal("# Cannot land\n", Read(rescued));
        Assert.Equal("# Scene\n", Read(book.Article));
        var alert = Assert.Single(alerts.Presented);
        Assert.Equal("Could not save “Scene”", alert.Message);
        Assert.Equal("Your text was kept as “01-Scene (rescued).md” in the same folder.", alert.Informative);
    }

    [Fact]
    public void TerminateFlushWritesOrRescues()
    {
        using var book = new SessionBook();
        var session = book.Session;
        Assert.True(session.Select(book.Article));
        session.Edit("# Quitting\n");
        session.TerminateFlush();
        Assert.Equal("# Quitting\n", Read(book.Article));

        session.Edit("# Quitting, conflicted\n");
        book.Write("01-Scene.md", "# Someone else, at the last moment\n");
        session.TerminateFlush();
        Assert.True(session.Conflicted);
        Assert.Equal("# Quitting, conflicted\n", Read(Path.Combine(book.Root, "01-Scene (rescued).md")));
        session.ResolveConflictReloading();
        session.CloseBook();
    }

    [Fact]
    public void UndoGenerationChangesOnEveryLoadAndDetach()
    {
        using var book = new SessionBook();
        var session = book.Session;
        var second = book.Write("02-Scene.md", "# Second\n");
        var g0 = session.UndoGeneration;
        Assert.True(session.Select(book.Article));
        var g1 = session.UndoGeneration;
        Assert.NotEqual(g0, g1);
        Assert.True(session.Select(second));
        var g2 = session.UndoGeneration;
        Assert.NotEqual(g1, g2);
        Assert.True(session.Select(null));
        Assert.NotEqual(g2, session.UndoGeneration);
        session.CloseBook();
    }

    [Fact]
    public void ChangedFiresOnObservableTransitions()
    {
        using var book = new SessionBook();
        var session = book.Session;
        var changes = 0;
        session.Changed += () => changes++;
        Assert.True(session.Select(book.Article));
        Assert.True(changes > 0);
        var afterSelect = changes;
        session.Edit("# Typed\n");
        Assert.True(changes > afterSelect);
        session.CloseBook();
    }
}
