using System.Text;
using Md.Core.Book;
using BookTree = Md.Core.Book.Book;

namespace Md.Core.Tests;

/// <summary>
/// The book folder's file operations against real temp folders — creation, rename
/// (including the case-only rename this case-insensitive volume makes interesting),
/// deletion and the selection helpers around them — plus the two compile readers and the
/// decoder they use. macOS has no unit tests for this half (BookLibrary's I/O is only
/// exercised through the UI), so these are the port's own, written off the Swift line by
/// line: BookNavigator.swift's createChapter / createArticle / renameItem / deleteItem /
/// compileBookSource / readStructuredBook and the navigator's relativePath /
/// deletionNeighbor / articleContext.
/// </summary>
public class BookFolderTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    /// <summary>A scratch container with the book root inside it, so the compile title is a fixed name and not a GUID.</summary>
    private sealed class TempBook : IDisposable
    {
        private readonly string container;

        public string Root { get; }

        public TempBook()
        {
            container = Path.Combine(Path.GetTempPath(), "md-folder-" + Guid.NewGuid().ToString("N"));
            Root = Path.Combine(container, "03-My Book");
            Directory.CreateDirectory(Root);
        }

        public string Write(string relative, string text) => Write(relative, Utf8.GetBytes(text));

        public string Write(string relative, byte[] data)
        {
            var path = Path.Combine(Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, data);
            return path;
        }

        public IReadOnlyList<string> Names(string relative = "") =>
            Directory.GetFileSystemEntries(Path.Combine(Root, relative))
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList()!;

        public void Dispose() => Directory.Delete(container, recursive: true);
    }

    /// <summary>A book root that never touches disk — the pure helpers only compare paths.</summary>
    private static readonly string FakeRoot = Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "Book"));

    /// <summary>A book snapshot straight from paths, as BookModelTests builds one — the listing is not under test here.</summary>
    private static BookTree MakeBook(string[] articles, (string Name, string[] Files)[] chapters) =>
        new(FakeRoot,
            articles.Select(a => new BookArticle(Path.Combine(FakeRoot, a))).ToList(),
            chapters.Select(c =>
            {
                var folder = Path.Combine(FakeRoot, c.Name);
                return new BookChapter(folder, c.Files.Select(f => new BookArticle(Path.Combine(folder, f))).ToList());
            }).ToList());

    // MARK: Names

    [Theory]
    // Ordinary names, the ones the prompts are for.
    [InlineData("Preface", true)]
    [InlineData("01-Preface", true)]
    [InlineData("v1.2", true)]
    [InlineData("Ideas (draft)", true)]
    [InlineData(" leading space", true)]
    [InlineData(".hidden", true)]
    [InlineData("CONS", true)]
    [InlineData("COM10", true)]
    // The nine Win32 characters. Swift rejects only "/" and ":".
    [InlineData("", false)]
    [InlineData("a/b", false)]
    [InlineData("a:b", false)]
    [InlineData("a\\b", false)]
    [InlineData("a*b", false)]
    [InlineData("a?b", false)]
    [InlineData("a\"b", false)]
    [InlineData("a<b", false)]
    [InlineData("a>b", false)]
    [InlineData("a|b", false)]
    // Trailing dot or space, control characters, device names — with or without extension.
    [InlineData("trailing.", false)]
    [InlineData("trailing ", false)]
    [InlineData("tab\there", false)]
    [InlineData("CON", false)]
    [InlineData("con.md", false)]
    [InlineData("NUL", false)]
    [InlineData("lpt1.txt", false)]
    public void IsValidNameFollowsTheWindowsRuleNotJustSwiftsTwoCharacters(string name, bool valid)
    {
        Assert.Equal(valid, BookFolder.IsValidName(name));
    }

    [Fact]
    public void PromptNamesAreTrimmedWithFoundationsWhitespaceSet()
    {
        Assert.Equal("Draft", BookFolder.TrimmedPromptName("  Draft\t"));
        // .whitespaces is Zs plus tab: a no-break space goes, a newline stays (it is not in the set).
        Assert.Equal("Draft", BookFolder.TrimmedPromptName("\u00A0Draft\u2003"));
        Assert.Equal("Draft\n", BookFolder.TrimmedPromptName(" Draft\n"));
        Assert.Equal("", BookFolder.TrimmedPromptName("   "));
    }

    // MARK: Creation

    [Fact]
    public void CreateChapterMakesOneFolderAndRefusesToReplaceAnything()
    {
        using var book = new TempBook();
        Assert.True(BookFolder.CreateChapter(book.Root, "02-One"));
        Assert.True(Directory.Exists(Path.Combine(book.Root, "02-One")));

        // Foundation's withIntermediateDirectories: false fails on an existing folder…
        Assert.False(BookFolder.CreateChapter(book.Root, "02-One"));
        // …on a name a file already holds…
        book.Write("notes.md", "# Notes\n");
        Assert.False(BookFolder.CreateChapter(book.Root, "notes.md"));
        // …and on a missing intermediate, where Directory.CreateDirectory would have built the chain.
        Assert.False(BookFolder.CreateChapter(book.Root, Path.Combine("Missing", "Deep")));
        Assert.False(Directory.Exists(Path.Combine(book.Root, "Missing")));
        Assert.False(BookFolder.CreateChapter(book.Root, ""));
        // Path.Combine hands back a rooted second argument whole, where appendingPathComponent
        // cannot escape: the book folder is the boundary for both prompts.
        var outside = Path.Combine(Path.GetTempPath(), "md-escape-" + Guid.NewGuid().ToString("N"));
        Assert.False(BookFolder.CreateChapter(book.Root, outside));
        Assert.False(Directory.Exists(outside));
        Assert.Null(BookFolder.CreateArticle(book.Root, outside));
        Assert.False(File.Exists(outside + ".md"));
        Assert.Equal(new[] { "02-One", "notes.md" }, book.Names());
    }

    [Fact]
    public void CreateArticleSeedsAMatchingHeadingAndNeverOverwrites()
    {
        using var book = new TempBook();
        var created = BookFolder.CreateArticle(book.Root, "01-Scene");
        Assert.NotNull(created);
        Assert.Equal(Path.Combine(book.Root, "01-Scene.md"), created);
        Assert.Equal(Utf8.GetBytes("# 01-Scene\n"), File.ReadAllBytes(created!));

        // Swift appends the extension unconditionally, so a typed ".md" doubles it.
        Assert.Equal(Path.Combine(book.Root, "Scene.md.md"), BookFolder.CreateArticle(book.Root, "Scene.md"));
        // Never twice, and never over a folder squatting on the name.
        Assert.Null(BookFolder.CreateArticle(book.Root, "01-Scene"));
        Assert.Equal(Utf8.GetBytes("# 01-Scene\n"), File.ReadAllBytes(created!));
        Directory.CreateDirectory(Path.Combine(book.Root, "Folder.md"));
        Assert.Null(BookFolder.CreateArticle(book.Root, "Folder"));
        Assert.Null(BookFolder.CreateArticle(book.Root, ""));
        // Into a chapter, which is the other folder the prompt offers.
        Assert.True(BookFolder.CreateChapter(book.Root, "02-One"));
        var inChapter = BookFolder.CreateArticle(Path.Combine(book.Root, "02-One"), "01-a");
        Assert.Equal(Path.Combine(book.Root, "02-One", "01-a.md"), inChapter);
    }

    // MARK: Rename

    [Fact]
    public void RenameKeepsTheOrderingPrefixAndTheExtension()
    {
        using var book = new TempBook();
        var article = book.Write("01-Draft.md", "# Draft\n");
        Assert.Equal(RenameOutcome.Renamed, BookFolder.RenameItem(article, "Final", out var failure));
        Assert.Null(failure);
        Assert.Equal(new[] { "01-Final.md" }, book.Names());
        Assert.Equal("# Draft\n", File.ReadAllText(Path.Combine(book.Root, "01-Final.md")));

        // A chapter is a folder: no extension logic, prefix kept.
        Directory.CreateDirectory(Path.Combine(book.Root, "02-Getting Started"));
        Assert.Equal(RenameOutcome.Renamed,
            BookFolder.RenameItem(Path.Combine(book.Root, "02-Getting Started"), "Basics", out _));
        Assert.True(Directory.Exists(Path.Combine(book.Root, "02-Basics")));
    }

    [Fact]
    public void RenameToTheNameItAlreadyHasIsANoOp()
    {
        using var book = new TempBook();
        var article = book.Write("01-Draft.md", "# Draft\n");
        var stamp = File.GetLastWriteTimeUtc(article);
        // Swift returns true here without touching the disk.
        Assert.Equal(RenameOutcome.Unchanged, BookFolder.RenameItem(article, "Draft", out var failure));
        Assert.Null(failure);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(article));
        Assert.Equal(new[] { "01-Draft.md" }, book.Names());
    }

    [Fact]
    public void RenameRefusesAnUnusableNameAndReportsACollision()
    {
        using var book = new TempBook();
        var article = book.Write("01-Draft.md", "# Draft\n");
        book.Write("01-Final.md", "# Final\n");

        Assert.Equal(RenameOutcome.InvalidName, BookFolder.RenameItem(article, "", out var failure));
        Assert.Null(failure);
        Assert.Equal(RenameOutcome.InvalidName, BookFolder.RenameItem(article, "a/b", out _));
        Assert.Equal(RenameOutcome.InvalidName, BookFolder.RenameItem(article, "a:b", out _));
        // Windows-only members of the set, rejected here so the port behaves the same on both.
        Assert.Equal(RenameOutcome.InvalidName, BookFolder.RenameItem(article, "a?b", out _));

        // A collision is the OS's answer, not a validation failure — Swift surfaces it the same way.
        Assert.Equal(RenameOutcome.Failed, BookFolder.RenameItem(article, "Final", out var collision));
        Assert.False(string.IsNullOrEmpty(collision));
        Assert.Equal(new[] { "01-Draft.md", "01-Final.md" }, book.Names());
        Assert.Equal("# Final\n", File.ReadAllText(Path.Combine(book.Root, "01-Final.md")));
    }

    [Fact]
    public void CaseOnlyRenameGoesThroughAStagingNameOnACaseInsensitiveVolume()
    {
        // APFS here, NTFS there: the destination "already exists" because it IS the source,
        // and .NET refuses the move on that check alone. The two-phase route is what makes
        // "01-draft.md" -> "01-Draft.md" work, and it must leave nothing staged behind.
        using var book = new TempBook();
        var article = book.Write("01-draft.md", "# Draft\n");
        Assert.Equal(RenameOutcome.Renamed, BookFolder.RenameItem(article, "Draft", out var failure));
        Assert.Null(failure);
        Assert.Equal(new[] { "01-Draft.md" }, book.Names());
        Assert.Equal("# Draft\n", File.ReadAllText(Path.Combine(book.Root, "01-Draft.md")));

        // A chapter folder too — Directory.Move is the half that throws for a case-only change.
        Directory.CreateDirectory(Path.Combine(book.Root, "02-one"));
        Assert.Equal(RenameOutcome.Renamed,
            BookFolder.RenameItem(Path.Combine(book.Root, "02-one"), "One", out _));
        Assert.Equal(new[] { "01-Draft.md", "02-One" }, book.Names());
        Assert.DoesNotContain(book.Names(), name => name.StartsWith(".md-reorder-", StringComparison.Ordinal));
    }

    // MARK: Delete

    [Fact]
    public void DeleteTakesAnArticleOrAWholeChapterAndFailsOnWhatIsAlreadyGone()
    {
        using var book = new TempBook();
        var article = book.Write("01-Preface.md", "# Preface\n");
        book.Write(Path.Combine("02-One", "01-a.md"), "a");
        book.Write(Path.Combine("02-One", "02-b.md"), "b");

        Assert.True(BookFolder.DeleteItem(article, out var failure));
        Assert.Null(failure);
        Assert.False(File.Exists(article));

        // A chapter goes with everything in it — permanent, no Trash and no Recycle Bin.
        Assert.True(BookFolder.DeleteItem(Path.Combine(book.Root, "02-One"), out _));
        Assert.Empty(book.Names());

        // File.Delete is a no-op for a missing file; Foundation throws, and so does this.
        Assert.False(BookFolder.DeleteItem(article, out var missing));
        Assert.False(string.IsNullOrEmpty(missing));
    }

    // MARK: Selection helpers (pure)

    [Fact]
    public void DeletionNeighborIsTheFirstSurvivorAfterTheBlockElseTheLastBefore()
    {
        var book = MakeBook(new[] { "01-Preface.md" },
            new[] { ("02-One", new[] { "01-a.md", "02-b.md" }), ("03-Two", new[] { "01-c.md" }) });
        string In(params string[] parts) => Path.Combine(new[] { FakeRoot }.Concat(parts).ToArray());

        // Reading order is Preface, a, b, c.
        Assert.Equal(In("02-One", "01-a.md"), BookFolder.DeletionNeighbor(book, In("01-Preface.md")));
        Assert.Equal(In("02-One", "02-b.md"), BookFolder.DeletionNeighbor(book, In("02-One", "01-a.md")));
        // A chapter takes its whole block with it; the survivor after it wins.
        Assert.Equal(In("03-Two", "01-c.md"), BookFolder.DeletionNeighbor(book, In("02-One")));
        // Nothing after the last block: fall back to the last article before it.
        Assert.Equal(In("02-One", "02-b.md"), BookFolder.DeletionNeighbor(book, In("03-Two")));
        Assert.Equal(In("02-One", "02-b.md"), BookFolder.DeletionNeighbor(book, In("03-Two", "01-c.md")));
        // Nothing in the book dies with it.
        Assert.Null(BookFolder.DeletionNeighbor(book, In("04-Empty")));
        Assert.Null(BookFolder.DeletionNeighbor(book, Path.Combine(Path.GetDirectoryName(FakeRoot)!, "Elsewhere", "01-a.md")));
        // The whole book: every candidate dies, and there is no survivor either side.
        Assert.Null(BookFolder.DeletionNeighbor(book, FakeRoot));
    }

    [Fact]
    public void OrderIndexComparesStandardizedPathsNotSpellings()
    {
        var book = MakeBook(new[] { "01-Preface.md" }, new[] { ("02-One", new[] { "01-a.md", "02-b.md" }) });
        var order = BookModel.ReadingOrder(book);
        Assert.Equal(0, BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "01-Preface.md")));
        // The same article spelled with a dot segment is the same article.
        Assert.Equal(2, BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "02-One", ".", "02-b.md")));
        Assert.Null(BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "nothing.md")));
        Assert.Null(BookFolder.OrderIndex(order, null));
    }

    [Fact]
    public void RelativePathAndPathInRoundTripAndRefuseToLeaveTheBook()
    {
        var article = Path.Combine(FakeRoot, "02-One", "01-a.md");
        var relative = BookFolder.RelativePath(FakeRoot, article);
        Assert.Equal(Path.Combine("02-One", "01-a.md"), relative);
        Assert.Equal(article, BookFolder.PathIn(FakeRoot, relative));
        Assert.Equal("01-Preface.md", BookFolder.RelativePath(FakeRoot, Path.Combine(FakeRoot, "01-Preface.md")));
        // Outside, or the root itself, is "" — Swift's "no book / not inside" answer.
        Assert.Equal("", BookFolder.RelativePath(FakeRoot, Path.Combine(Path.GetDirectoryName(FakeRoot)!, "Elsewhere", "a.md")));
        Assert.Equal("", BookFolder.RelativePath(FakeRoot, FakeRoot));
        // A remembered value from another book must not walk out of the folder we hold rights to.
        Assert.Null(BookFolder.PathIn(FakeRoot, ""));
        Assert.Null(BookFolder.PathIn(FakeRoot, ".."));
        Assert.Null(BookFolder.PathIn(FakeRoot, Path.Combine("..", "Elsewhere", "a.md")));
        Assert.Null(BookFolder.PathIn(FakeRoot, Path.Combine(Path.GetDirectoryName(FakeRoot)!, "Elsewhere", "a.md")));
    }

    [Fact]
    public void SiblingsOfFindsTheGroupARenameOrAMoveActsOn()
    {
        var book = MakeBook(new[] { "01-Preface.md", "02-Notes.md" },
            new[] { ("02-One", new[] { "01-a.md", "02-b.md" }), ("03-Two", new[] { "01-c.md" }) });

        var root = BookFolder.SiblingsOf(book, Path.Combine(FakeRoot, "02-Notes.md"));
        Assert.NotNull(root);
        Assert.Equal("02-Notes", root!.Name);
        Assert.Equal(FakeRoot, root.Folder);
        Assert.Equal(new[] { "01-Preface.md", "02-Notes.md" }, root.Names);
        Assert.Equal(1, root.Index);

        var inChapter = BookFolder.SiblingsOf(book, Path.Combine(FakeRoot, "02-One", "01-a.md"));
        Assert.NotNull(inChapter);
        Assert.Equal(Path.Combine(FakeRoot, "02-One"), inChapter!.Folder);
        Assert.Equal(new[] { "01-a.md", "02-b.md" }, inChapter.Names);
        Assert.Equal(0, inChapter.Index);

        // A chapter's siblings are the chapter list, renumbered inside the root.
        var chapter = BookFolder.SiblingsOf(book, Path.Combine(FakeRoot, "03-Two"));
        Assert.NotNull(chapter);
        Assert.Equal("03-Two", chapter!.Name);
        Assert.Equal(FakeRoot, chapter.Folder);
        Assert.Equal(new[] { "02-One", "03-Two" }, chapter.Names);
        Assert.Equal(1, chapter.Index);

        Assert.Null(BookFolder.SiblingsOf(book, Path.Combine(FakeRoot, "nothing.md")));
    }

    // MARK: The seam

    /// <summary>Everything BookFolder asks a file system for, in memory — Swift reached FileManager directly.</summary>
    private sealed class RecordingFolder : IBookFolderFileSystem
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Directories { get; } = new(StringComparer.Ordinal);
        public List<string> Log { get; } = new();

        public bool FileExists(string path) => Files.ContainsKey(path) || Directories.Contains(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public byte[] ReadAllBytes(string path) => Files.TryGetValue(path, out var data) ? data : throw new FileNotFoundException(path);
        public void WriteAllBytes(string path, byte[] data) { Log.Add("write " + path); Files[path] = data; }
        public FileStamp? Stamp(string path) => Files.ContainsKey(path) ? new FileStamp(DateTime.UnixEpoch, Files[path].Length) : null;
        public void CreateDirectory(string path) { Log.Add("mkdir " + path); Directories.Add(path); }
        public void Delete(string path) { Log.Add("delete " + path); Files.Remove(path); Directories.Remove(path); }

        public void Move(string from, string to)
        {
            Log.Add("move " + Path.GetFileName(from) + " -> " + Path.GetFileName(to));
            if (Files.Remove(from, out var data)) Files[to] = data;
            else if (Directories.Remove(from)) Directories.Add(to);
            else throw new FileNotFoundException(from);
        }
    }

    [Fact]
    public void EveryOperationGoesThroughTheInjectedFileSystem()
    {
        var root = BookPaths.Standardize(Path.Combine(Path.GetTempPath(), "md-seam-book"));
        var files = new RecordingFolder();
        files.Directories.Add(root);

        Assert.True(BookFolder.CreateChapter(root, "02-One", files));
        var article = BookFolder.CreateArticle(root, "01-Scene", files);
        Assert.Equal(Path.Combine(root, "01-Scene.md"), article);
        Assert.Equal(Utf8.GetBytes("# 01-Scene\n"), files.Files[article!]);

        Assert.Equal(RenameOutcome.Renamed, BookFolder.RenameItem(article!, "Opening", out _, files));
        Assert.True(files.Files.ContainsKey(Path.Combine(root, "01-Opening.md")));
        Assert.Equal("# 01-Scene\n", BookFolder.ReadArticle(Path.Combine(root, "01-Opening.md"), files));
        Assert.True(BookFolder.DeleteItem(Path.Combine(root, "01-Opening.md"), out _, files));
        Assert.Null(BookFolder.ReadArticle(Path.Combine(root, "01-Opening.md"), files));

        Assert.Equal(
            new[]
            {
                "mkdir " + Path.Combine(root, "02-One"),
                "write " + Path.Combine(root, "01-Scene.md"),
                "move 01-Scene.md -> 01-Opening.md",
                "delete " + Path.Combine(root, "01-Opening.md"),
            },
            files.Log);
        // Nothing reached the disk.
        Assert.False(Directory.Exists(root));
    }

    // MARK: The compile readers

    private static TempBook SampleBook()
    {
        var book = new TempBook();
        book.Write("01-Preface.md", "Front matter.");
        book.Write(Path.Combine("02-One", "01-a.md"), "First.\n\n---\n\nStill first.");
        book.Write(Path.Combine("02-One", "02-b.md"), "Second.");
        book.Write(Path.Combine("03-Two", "01-c.md"), "Third.");
        return book;
    }

    [Fact]
    public void CompileBookSourceReadsTheWholeBookInReadingOrder()
    {
        using var book = SampleBook();
        var tree = BookModel.Load(book.Root);
        Assert.NotNull(tree);
        var compiled = BookCompiler.CompileBookSource(tree!, path => BookFolder.ReadArticle(path), out var unreadable);
        Assert.Null(unreadable);
        // Title page, then every part on a page of its own; the article's "---" is not a page break.
        Assert.Equal(
            "# My Book\n\n\\newpage\n\nFront matter.\n\n\\newpage\n\n# One\n\n\\newpage\n\n"
            + "First.\n\n---\n\nStill first.\n\n\\newpage\n\nSecond.\n\n\\newpage\n\n# Two\n\n\\newpage\n\nThird.",
            compiled);
        Assert.Equal("My Book", BookCompiler.Title(tree!));
    }

    [Fact]
    public void CompileBookSourceAbortsAndNamesTheArticleItCouldNotRead()
    {
        using var book = SampleBook();
        var tree = BookModel.Load(book.Root);
        var refused = Path.Combine(book.Root, "02-One", "02-b.md");
        var compiled = BookCompiler.CompileBookSource(
            tree!,
            path => string.Equals(path, refused, StringComparison.Ordinal) ? null : BookFolder.ReadArticle(path),
            out var unreadable);
        Assert.Null(compiled);
        // The alert names the sidebar name, ordering prefix kept.
        Assert.Equal("02-b", unreadable);
        Assert.Null(BookFolder.ReadArticle(Path.Combine(book.Root, "missing.md")));
    }

    [Fact]
    public void CompileDecodesALegacyArticleAsCyrillicNotMojibake()
    {
        // DIVERGENCE, deliberate (report §7.2): Swift's compile reader is UTF-8 then
        // Latin-1, so this article compiles into the Mac's PDF as mojibake while the same
        // file edits correctly in the book window. The port reads both through
        // PlainTextCodec, so the compile says what the file says.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp1251 = Encoding.GetEncoding(1251);
        var cyrillic = "Привет, мир!";
        using var book = new TempBook();
        var bytes = cp1251.GetBytes(cyrillic);
        book.Write("01-Legacy.md", bytes);

        var tree = BookModel.Load(book.Root);
        var compiled = BookCompiler.CompileBookSource(tree!, path => BookFolder.ReadArticle(path), out _);
        Assert.Equal("# My Book\n\n\\newpage\n\n" + cyrillic, compiled);
        Assert.DoesNotContain(Encoding.Latin1.GetString(bytes), compiled, StringComparison.Ordinal);
        Assert.Equal(cyrillic, BookFolder.ReadArticle(Path.Combine(book.Root, "01-Legacy.md")));
    }

    [Fact]
    public void ReadStructuredBookKeepsTheChapterBoundariesTheCompileFlattens()
    {
        using var book = SampleBook();
        var tree = BookModel.Load(book.Root);
        var structured = BookCompiler.ReadStructuredBook(tree!, path => BookFolder.ReadArticle(path), out var unreadable);
        Assert.Null(unreadable);
        Assert.NotNull(structured);
        // Every title is a display name; the sources are verbatim.
        Assert.Equal("My Book", structured!.Title);
        Assert.Equal(new[] { "Preface" }, structured.FrontUnits.Select(unit => unit.Title).ToArray());
        Assert.Equal("Front matter.", structured.FrontUnits[0].Source);
        Assert.Equal(new[] { "One", "Two" }, structured.Sections.Select(section => section.Title).ToArray());
        Assert.Equal(new[] { "a", "b" }, structured.Sections[0].Units.Select(unit => unit.Title).ToArray());
        Assert.Equal("First.\n\n---\n\nStill first.", structured.Sections[0].Units[0].Source);
        Assert.Equal(new[] { "c" }, structured.Sections[1].Units.Select(unit => unit.Title).ToArray());
    }

    [Fact]
    public void ReadStructuredBookAbortsAndNamesTheArticleItCouldNotRead()
    {
        using var book = SampleBook();
        var tree = BookModel.Load(book.Root);
        var refused = Path.Combine(book.Root, "01-Preface.md");
        var structured = BookCompiler.ReadStructuredBook(
            tree!,
            path => string.Equals(path, refused, StringComparison.Ordinal) ? null : BookFolder.ReadArticle(path),
            out var unreadable);
        Assert.Null(structured);
        Assert.Equal("01-Preface", unreadable);
    }

    [Fact]
    public void AnEmptyBookStillCompilesToItsTitlePage()
    {
        using var book = new TempBook();
        var tree = BookModel.Load(book.Root);
        Assert.Equal("# My Book", BookCompiler.CompileBookSource(tree!, path => BookFolder.ReadArticle(path)));
        var structured = BookCompiler.ReadStructuredBook(tree!, path => BookFolder.ReadArticle(path));
        Assert.NotNull(structured);
        Assert.Empty(structured!.FrontUnits);
        Assert.Empty(structured.Sections);
    }
}
