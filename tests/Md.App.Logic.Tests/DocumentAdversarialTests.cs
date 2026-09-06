using System.Text;
using Md.App.Logic.Documents;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>
/// The rows of §6 that the package's own suite left to a constant's own name, and the traps the
/// porting reports call out for this package: a conflicted save, a case-only rename, a bundle that
/// arrives with someone else's line endings, an asset reference that is not a file path at all.
///
/// Every number here is written as a literal on purpose. A test that asserts
/// <c>PendingDelays == [TextFileSession.AutosaveDelay]</c> passes for any delay, and a rescue loop
/// bounded by <c>RescueCopy.MaxAttempts</c> passes for any cap — both were shown to survive the
/// mutation that changes the value the design fixes.
/// </summary>
public class DocumentAdversarialTests
{
    const string Article = @"C:\Book\01-Scene.md";
    const string Document = @"C:\Docs\Chapter One.md";

    sealed class World : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public FakeFileSystem Fs { get; }
        public FakeFileWatcher Watcher { get; } = new();
        public FakeScheduler Scheduler { get; }
        public FakeDocumentRegistry Registry { get; } = new();
        public FakeFileIdentity Identity { get; } = new();
        public TextFileSession Session { get; }
        public Guid WindowId { get; } = Guid.NewGuid();
        public List<string?> Identities { get; } = [];

        public World(SessionRole role)
        {
            Fs = new FakeFileSystem(Clock);
            Scheduler = new FakeScheduler(Clock);
            Session = new TextFileSession(Fs, Watcher, Scheduler, Registry, Identity, role,
                role == SessionRole.Document ? WindowId : null);
            Session.IdentityChanged += p => Identities.Add(p);
        }

        public void Dispose() => Session.Dispose();

        /// <summary>FakeFileIdentity's normalisation, so a registry key built here matches the session's.</summary>
        public static string Canonical(string path) => path.Replace('\\', '/');
    }

    static World Doc()
    {
        var world = new World(SessionRole.Document);
        world.Fs.AddFile(Document, "# Title\nBody\n");
        return world;
    }

    static World Book()
    {
        var world = new World(SessionRole.BookArticle);
        world.Fs.AddFile(Article, "# Scene\n");
        return world;
    }

    // ---- the debounce the design fixes at 1.0 s (§6.3) ----

    [Fact]
    public void TheAutosaveDebounceIsExactlyOneSecondNotWhateverTheConstantSays()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), TextFileSession.AutosaveDelay);

        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("typed\n");

        // 999 ms after the last keystroke the file is still the one that was opened: the whole
        // point of a debounce is that a fast typist's disk is not written on every character.
        w.Scheduler.Advance(TimeSpan.FromMilliseconds(999));
        Assert.Empty(w.Fs.Writes);
        Assert.Equal("# Title\nBody\n", w.Fs.Text(Document));

        w.Scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal("typed\n", w.Fs.Text(Document));
    }

    [Fact]
    public void EveryKeystrokeRestartsTheFullSecondRatherThanLettingTheFirstOneFire()
    {
        using var w = Doc();
        w.Session.Open(Document);

        for (var i = 0; i < 5; i++)
        {
            w.Session.Edit(new string('x', i + 1));
            w.Scheduler.Advance(TimeSpan.FromMilliseconds(900));
            Assert.Empty(w.Fs.Writes);                    // 4.5 s of typing, not one write
        }
        w.Scheduler.Advance(TimeSpan.FromMilliseconds(100));
        Assert.Equal("xxxxx", w.Fs.Text(Document));
    }

    // ---- "a vanished file counts as stale" (§6.3, autosave-tick row) ----

    [Fact]
    public void AFileThatVanishedBehindTheSessionIsStaleSoNoWriteRecreatesItSilently()
    {
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("mine\n");
        w.Fs.Delete(Document);                            // deleted with no watcher event: a sync client, say

        // Not "there is nothing to clobber, so write": the file the stamp was taken from is gone,
        // and recreating it would resurrect a document the writer may have deliberately deleted.
        Assert.False(w.Session.FlushNow(explicitSave: true));
        Assert.True(w.Session.Conflicted);
        Assert.False(w.Fs.FileExists(Document));
        Assert.Empty(w.Fs.Writes);

        // The autosave is suspended too, not merely refused once.
        w.Session.Edit("mine, more\n");
        w.Scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(w.Fs.Writes);

        // Keep My Version is the one route that writes, and it recreates the file.
        w.Session.ResolveConflictKeepingMine();
        Assert.Equal("mine, more\n", w.Fs.Text(Document));
        Assert.False(w.Session.Conflicted);
        Assert.False(w.Session.IsDirty);
    }

    [Fact]
    public void AConflictedSessionWritesNothingOnAnyRouteAndTheCloseParksTheTextInstead()
    {
        using var w = Book();
        w.Session.Select(Article);
        w.Session.Edit("mine\n");
        w.Fs.WriteExternally(Article, "theirs, and a different length\n");
        Assert.False(w.Session.FlushNow(explicitSave: false));
        Assert.True(w.Session.Conflicted);

        // Neither an explicit Ctrl+S, nor a fresh keystroke's autosave, nor the flush inside a
        // selection change may overwrite what the other writer put there.
        Assert.False(w.Session.FlushNow(explicitSave: true));
        w.Session.Edit("mine, edited again\n");
        w.Scheduler.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(w.Fs.Writes);
        Assert.Equal("theirs, and a different length\n", w.Fs.Text(Article));

        // Closing on top of an unresolved conflict does not discard the keystrokes: they land in
        // the rescue sibling, and the other writer's file is still untouched.
        w.Session.CloseFlush();
        Assert.Equal("mine, edited again\n", w.Fs.Text(@"C:\Book\01-Scene (rescued).md"));
        Assert.Equal("theirs, and a different length\n", w.Fs.Text(Article));
    }

    // ---- a case-only rename (§13.4 stage 5; §5.2's "one identity") ----

    [Fact]
    public void ACaseOnlyRenameIsTheSameFileSoOneWindowStillOwnsOneEntry()
    {
        const string lowered = @"C:\Docs\chapter one.md";
        using var w = Doc();
        w.Session.Open(Document);
        w.Session.Edit("edited\n");
        w.Session.FlushNow(explicitSave: true);

        // NTFS renames in place — one file, a new spelling, the same bytes and the same stamp,
        // which is exactly what the case-insensitive fake already models — and the watcher reports
        // old → new. (FakeFileSystem.Move cannot be used here: it reads the destination as
        // occupied, where MoveFileEx on NTFS performs the case-only rename.)
        w.Watcher.RaiseRenamed(lowered, Document);

        Assert.Equal(lowered, w.Session.EditingPath);
        Assert.Equal(lowered, w.Watcher.WatchedPath);
        Assert.True(FileNames.SamePath(Document, lowered));

        // One registry entry, not two: the second spelling must not leave the old one claiming the
        // file, or the next window to open it would think a document already owns it.
        Assert.Single(w.Registry.Windows);
        Assert.Equal(w.WindowId, w.Registry.Owning(World.Canonical(lowered)));
        Assert.Null(w.Registry.Owning(@"C:/Docs/Nowhere.md"));

        // The view-mode memory is told, so §5.2 re-decides for the new spelling.
        Assert.Contains(lowered, w.Identities);

        // And a save after the rename still writes one file, under the new spelling.
        w.Session.Edit("edited twice\n");
        Assert.True(w.Session.FlushNow(explicitSave: true));
        Assert.Equal("edited twice\n", w.Fs.Text(lowered));
    }

    // ---- a bundle brings its own line endings (§6.5) ----

    [Fact]
    public void AnImportedCrLfBundleStillWritesCrLfWhenItIsFirstSavedAs()
    {
        using var w = Doc();
        w.Fs.AddFile(@"C:\In\Notes.textpack", DocumentFixtures.TextPack("one\r\ntwo\r\n"));

        Assert.True(w.Session.Open(@"C:\In\Notes.textpack"));
        Assert.Equal("one\ntwo\n", w.Session.Text);                 // LF in the model, as always
        Assert.Equal(NewLine.CrLf, w.Session.Dressing.NewLine);     // the file's own convention survives
        Assert.IsType<Stage.Untitled>(w.Session.Stage);
        Assert.Equal("Notes", w.Session.Title);

        w.Fs.AddDirectory(@"C:\Out");
        Assert.True(w.Session.SaveAs(@"C:\Out\Notes.md"));
        Assert.Equal("one\r\ntwo\r\n", Encoding.UTF8.GetString(w.Fs.Bytes(@"C:\Out\Notes.md")!));
    }

    [Fact]
    public void AnLfBundleIsNotGivenWindowsLineEndingsJustBecauseItLandsOnWindows()
    {
        using var w = Doc();
        w.Fs.AddFile(@"C:\In\Notes.textpack", DocumentFixtures.TextPack("one\ntwo\n"));
        w.Session.Open(@"C:\In\Notes.textpack");

        Assert.Equal(NewLine.Lf, w.Session.Dressing.NewLine);
        w.Fs.AddDirectory(@"C:\Out");
        w.Session.SaveAs(@"C:\Out\Notes.md");
        Assert.Equal("one\ntwo\n", Encoding.UTF8.GetString(w.Fs.Bytes(@"C:\Out\Notes.md")!));
    }

    // ---- the identity is the file's own spelling, never a folded one (§5.2) ----

    [Fact]
    public void TheCanonicalPathIsNeverCaseFoldedBecauseThatWouldHideWhichSpellingIsReal()
    {
        var root = Path.Combine(Path.GetTempPath(), "md-win-case-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "MixedCase");
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "Chapter One.md");
            File.WriteAllText(file, "x");

            // Both legs: the Win32 final name and the lexical fallback. Neither may upper-case the
            // way Md.Core's ViewModeMemory.CanonicalPath does on Windows — the two are not
            // interchangeable, and folding here would make every title and every InfoBar shout.
            foreach (var canonical in new[] { FileIdentity.Instance.Canonical(file), FileIdentity.Lexical(file) })
            {
                Assert.Contains("MixedCase", canonical, StringComparison.Ordinal);
                Assert.EndsWith("Chapter One.md", canonical, StringComparison.Ordinal);
                Assert.DoesNotContain("MIXEDCASE", canonical, StringComparison.Ordinal);
            }

            // Core's own helper is the folded one, and it is deliberately a different answer.
            Assert.NotEqual(
                ViewModeMemory.CanonicalPath(file, foldCase: true),
                FileIdentity.Lexical(file),
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // ---- the picker filter and the manifest are one list (§6.1) ----

    [Fact]
    public void EveryAssociatedExtensionIsAlsoOfferedByTheOpenPickerAndTheTwoUnclaimedOnesStayUnclaimed()
    {
        var manifest = System.Xml.Linq.XDocument.Load(RepoFiles.At("src", "Md.App", "Package.appxmanifest"));
        var associations = manifest.Descendants()
            .Where(e => e.Name.LocalName == "FileType")
            .Select(e => e.Value.Trim())
            .ToList();

        // §6.1's opening sentence, as an invariant rather than a claim: a type Windows will hand us
        // through activation that the Open dialog then cannot show is a double-click that works and
        // a File ▸ Open… that does not.
        Assert.NotEmpty(associations);
        Assert.All(associations, e => Assert.Contains(e, DocumentLoader.OpenExtensions));

        // md is the Markdown owner: all five, and the manifest declares them under one association.
        Assert.All(DocumentLoader.MarkdownExtensions, e => Assert.Contains(e, associations));

        // ".dot" stays unclaimed (it is Word's template) and ".textbundle" is a folder, which
        // Windows cannot associate at all.
        Assert.DoesNotContain(".dot", associations);
        Assert.DoesNotContain(".textbundle", associations);
        Assert.DoesNotContain(".textbundle", DocumentLoader.OpenExtensions);
    }

    // ---- an asset reference is not a URL, and never leaves the folder (§7.7) ----

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://example.com/logo.png")]
    [InlineData("https://example.com/logo.png")]
    [InlineData("file:///C:/Windows/System32/config/SAM")]
    [InlineData("data:image/png;base64,AAAA")]
    [InlineData(@"C:\Windows\System32\config\SAM")]
    [InlineData(@"\\server\share\secret.png")]
    [InlineData(@"..\..\Windows\System32\config\SAM")]
    [InlineData("../../etc/passwd")]
    [InlineData("/etc/passwd")]
    public void AnAssetReferenceThatIsNotAPlainFileBesideTheDocumentReadsNothing(string reference)
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\a.md", "# a\n");
        fs.AddFile(@"C:\Docs\logo.png", "inside");
        fs.AddFile(@"C:\Windows\System32\config\SAM", "secret");
        fs.AddFile(@"C:\etc\passwd", "secret");

        var read = AssetReader.Beside(@"C:\Docs\a.md", fs, new FakeFileIdentity());

        Assert.Null(read(reference));
        Assert.Equal("inside", Encoding.UTF8.GetString(read("logo.png")!));   // the control: this one is ours
    }

    [Fact]
    public void TheTwoContainmentGuardsCoverDisjointHalvesSoNeitherCanBeDroppedQuietly()
    {
        // Which layer refuses what, pinned so a later edit to either one is caught. Core screens
        // schemes and POSIX-absolute refs before resolveAsset is ever called; §7.7's "documented
        // Windows deviation" — a drive letter and a UNC head — is this package's alone, and Core
        // lets both straight through. "javascript:" is in neither screen; it is refused only
        // because no file of that name is beside the document, which is enough and is why the
        // theory above keeps it.
        Assert.False(Md.Core.Export.TextBundle.IsLocalRelativeReference("http://example.com/logo.png"));
        Assert.False(Md.Core.Export.TextBundle.IsLocalRelativeReference("data:image/png;base64,AAAA"));
        Assert.False(Md.Core.Export.TextBundle.IsLocalRelativeReference("/etc/passwd"));

        Assert.True(Md.Core.Export.TextBundle.IsLocalRelativeReference(@"C:\Windows\System32\config\SAM"));
        Assert.True(Md.Core.Export.TextBundle.IsLocalRelativeReference(@"\\server\share\secret.png"));
        Assert.True(Md.Core.Export.TextBundle.IsLocalRelativeReference("javascript:alert(1)"));

        Assert.True(FileNames.IsRooted(@"C:\Windows\System32\config\SAM"));
        Assert.True(FileNames.IsRooted(@"\\server\share\secret.png"));

        // And the one that survives both screens still reads nothing, because the containment
        // check runs after the resolve rather than instead of it.
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\a.md", "# a\n");
        var read = AssetReader.Beside(@"C:\Docs\a.md", fs, new FakeFileIdentity());
        Assert.Null(read("javascript:alert(1)"));
    }
}
