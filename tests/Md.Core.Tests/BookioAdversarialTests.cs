using System.Text;
using Md.Core.Book;
using BookTree = Md.Core.Book.Book;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial cover for the book-I/O module, written against oracles rather than against
/// the C# under test: every expectation here was produced by compiling the real md.macOS
/// functions with <c>swiftc</c> (BookNavigator.swift's <c>splitExtension</c> /
/// <c>splitPrefix</c> / <c>displayName</c> / <c>compile</c> and the compile reader, plus
/// Foundation's <c>trimmingCharacters(in: .whitespaces)</c> and <c>FileManager</c>) and
/// reading the answer off the probe, or from the Win32 naming rule the port declares.
/// Each test closes a hole a mutant walked through: the case-only rename staging, the
/// "which article could not be read" name in three of its four branches, the order side of
/// the selection compare, and the create prompts' name rule.
/// </summary>
public class BookioAdversarialTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    private static readonly string FakeRoot =
        Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "03-My Book"));

    private static BookTree MakeBook(
        (string File, string Text)[] articles,
        (string Name, (string File, string Text)[] Files)[] chapters,
        out Dictionary<string, string> sources)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        var roots = articles.Select(a =>
        {
            var path = Path.Combine(FakeRoot, a.File);
            texts[path] = a.Text;
            return new BookArticle(path);
        }).ToList();
        var sections = chapters.Select(c =>
        {
            var folder = Path.Combine(FakeRoot, c.Name);
            return new BookChapter(folder, c.Files.Select(f =>
            {
                var path = Path.Combine(folder, f.File);
                texts[path] = f.Text;
                return new BookArticle(path);
            }).ToList());
        }).ToList();
        sources = texts;
        return new BookTree(FakeRoot, roots, sections);
    }

    /// <summary>
    /// A Windows-shaped file system: names match case-insensitively, and a move whose
    /// destination differs from its source only by case is refused — the
    /// <c>Directory.Move</c> behaviour report §16.4 records and that no Mac can produce.
    /// </summary>
    private sealed class WindowsShapedFolder : IBookFolderFileSystem
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => Files.ContainsKey(path) || Directories.Contains(path);
        public bool DirectoryExists(string path) => Directories.Contains(path);
        public byte[] ReadAllBytes(string path) => Files.TryGetValue(path, out var d) ? d : throw new FileNotFoundException(path);
        public void WriteAllBytes(string path, byte[] data) => Files[path] = data;
        public FileStamp? Stamp(string path) => Files.ContainsKey(path) ? new FileStamp(DateTime.UnixEpoch, Files[path].Length) : null;
        public void CreateDirectory(string path) => Directories.Add(path);
        public void Delete(string path) { Files.Remove(path); Directories.Remove(path); }

        public void Move(string from, string to)
        {
            if (!string.Equals(from, to, StringComparison.Ordinal)
                && string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Source and destination path must be different.");
            }
            if (FileExists(to)) throw new IOException("Cannot create a file when that file already exists.");
            if (Files.Remove(from, out var data)) Files[to] = data;
            else if (Directories.Remove(from)) Directories.Add(to);
            else throw new FileNotFoundException(from);
        }

        /// <summary>Everything the folder holds, the folder itself excluded.</summary>
        public IReadOnlyList<string> Names(string root) =>
            Files.Keys.Concat(Directories)
                .Where(p => !string.Equals(p, root, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .OrderBy(n => n, StringComparer.Ordinal).ToList()!;
    }

    /// <summary>
    /// The staging route is the whole point of the case-only branch, and a Mac cannot show
    /// it: APFS takes "01-draft.md" to "01-Draft.md" as a direct move (measured), so
    /// deleting the branch leaves every disk-backed test green. Against a volume that
    /// refuses the direct move — Windows, per report §16.4 — the rename must still land, in
    /// the exact case asked for, with no hidden staging name left behind.
    /// </summary>
    [Fact]
    public void CaseOnlyRenameSurvivesAVolumeThatRefusesTheDirectMove()
    {
        var root = BookPaths.Standardize(Path.Combine(Path.GetTempPath(), "md-winshape"));
        var files = new WindowsShapedFolder();
        files.Directories.Add(root);
        var article = Path.Combine(root, "01-draft.md");
        files.Files[article] = Utf8.GetBytes("# Draft\n");
        files.Directories.Add(Path.Combine(root, "02-one"));

        Assert.Equal(RenameOutcome.Renamed, BookFolder.RenameItem(article, "Draft", out var failure, files));
        Assert.Null(failure);
        Assert.Equal(RenameOutcome.Renamed,
            BookFolder.RenameItem(Path.Combine(root, "02-one"), "One", out _, files));

        Assert.Equal(new[] { "01-Draft.md", "02-One" }, files.Names(root));
        // The case actually stored, not just a case-insensitive hit.
        Assert.Contains(Path.Combine(root, "01-Draft.md"), files.Files.Keys.ToArray(), StringComparer.Ordinal);
        Assert.Contains(Path.Combine(root, "02-One"), files.Directories.ToArray(), StringComparer.Ordinal);
        Assert.DoesNotContain(files.Names(root), n => n.StartsWith(".md-reorder-", StringComparison.Ordinal));
        Assert.Equal(Utf8.GetBytes("# Draft\n"), files.Files[Path.Combine(root, "01-Draft.md")]);
    }

    /// <summary>
    /// Swift alerts with <c>article.name</c> — the sidebar name, ordering prefix kept,
    /// extension gone — from the one <c>append</c> / <c>read</c> closure both loops call, so
    /// a root article and a chapter article are named the same way by both readers. Four
    /// branches; the shipped tests exercised one each.
    /// </summary>
    [Fact]
    public void EveryUnreadableArticleIsNamedByItsSidebarNameInBothLoopsOfBothReaders()
    {
        var book = MakeBook(
            new[] { ("01-Preface.md", "Front matter.") },
            new[] { ("02-One", new[] { ("01-a.markdown", "First.") }) },
            out var sources);
        Func<string, string?> Refusing(string name) =>
            path => string.Equals(Path.GetFileName(path), name, StringComparison.Ordinal) ? null : sources[path];

        BookCompiler.CompileBookSource(book, Refusing("01-Preface.md"), out var rootCompile);
        Assert.Equal("01-Preface", rootCompile);
        BookCompiler.CompileBookSource(book, Refusing("01-a.markdown"), out var chapterCompile);
        Assert.Equal("01-a", chapterCompile);

        BookCompiler.ReadStructuredBook(book, Refusing("01-Preface.md"), out var rootStructured);
        Assert.Equal("01-Preface", rootStructured);
        BookCompiler.ReadStructuredBook(book, Refusing("01-a.markdown"), out var chapterStructured);
        Assert.Equal("01-a", chapterStructured);

        // ...and a readable run leaves the out parameter alone.
        Assert.NotNull(BookCompiler.CompileBookSource(book, path => sources[path], out var none));
        Assert.Null(none);
    }

    /// <summary>
    /// Swift's <c>orderIndex</c> puts <em>both</em> sides through
    /// <c>standardizedFileURL.path</c>; a compare that only normalises the needle finds
    /// nothing when the haystack carries the odd spelling.
    /// </summary>
    [Fact]
    public void OrderIndexStandardizesTheOrderSideAsWellAsTheTarget()
    {
        var order = new[]
        {
            new BookArticle(Path.Combine(FakeRoot, "01-Preface.md")),
            new BookArticle(Path.Combine(FakeRoot, "02-One", ".", "01-a.md")),
            new BookArticle(Path.Combine(FakeRoot, "02-One", "sub", "..", "02-b.md")),
        };
        Assert.Equal(1, BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "02-One", "01-a.md")));
        Assert.Equal(2, BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "02-One", "02-b.md")));
        Assert.Equal(0, BookFolder.OrderIndex(order, Path.Combine(FakeRoot, "01-Preface.md")));
    }

    /// <summary>
    /// The three prompts trim with Foundation's <c>.whitespaces</c>, whose frozen tables
    /// still hold U+200B and have never held U+180E. Probed with swiftc: U+200B goes at
    /// both ends; U+180E, U+0085, U+000B and U+FEFF all survive, and so does a newline.
    /// <c>string.Trim()</c> gets four of these the other way round.
    /// </summary>
    [Fact]
    public void ThePromptTrimIsFoundationsWhitespaceSetNotTheBcls()
    {
        // U+200B is in Foundation's frozen .whitespaces table (Unicode has moved it to Cf).
        Assert.Equal("Draft", BookFolder.TrimmedPromptName("\u200BDraft\u200B"));
        // U+0020, U+3000 IDEOGRAPHIC SPACE, U+00A0, U+2003, U+1680, U+202F, U+205F, TAB.
        Assert.Equal("Draft", BookFolder.TrimmedPromptName("\u0020Draft\u3000"));
        Assert.Equal("D", BookFolder.TrimmedPromptName("\u00A0\u2003\u1680D\u202F\u205F\u0009"));
        // U+180E left the set in Unicode 6.3 and Foundation agrees: it stays.
        Assert.Equal("\u180EDraft\u180E", BookFolder.TrimmedPromptName("\u180EDraft\u180E"));
        // Line terminators are .newlines, never .whitespaces: NEL, VT and LF all stay.
        Assert.Equal("\u0085Draft\u0085", BookFolder.TrimmedPromptName("\u0085Draft\u0085"));
        Assert.Equal("\u000BDraft\n", BookFolder.TrimmedPromptName("\u000BDraft\n"));
        // A BOM is content, not space.
        Assert.Equal("\uFEFFDraft", BookFolder.TrimmedPromptName("\uFEFFDraft"));
        Assert.Equal("", BookFolder.TrimmedPromptName("   "));
    }

    /// <summary>
    /// <c>compileBookSource</c> appends the chapter part before it walks that chapter's
    /// articles, unconditionally — so a chapter with nothing in it still spends a page on
    /// its heading, and the structured read still emits its section. The expected string
    /// came out of the real <c>compile</c> run under swiftc with that part list.
    /// </summary>
    [Fact]
    public void AnEmptyChapterStillSpendsAPageOnItsHeadingAndStillGetsASection()
    {
        var book = MakeBook(
            new[] { ("01-Preface.md", "Front matter.") },
            new[] { ("02-Empty", Array.Empty<(string, string)>()), ("03-Two", new[] { ("01-c.md", "Third.") }) },
            out var sources);

        Assert.Equal(
            "# My Book\n\n\\newpage\n\nFront matter.\n\n\\newpage\n\n# Empty\n\n\\newpage\n\n"
            + "# Two\n\n\\newpage\n\nThird.",
            BookCompiler.CompileBookSource(book, path => sources[path]));

        var structured = BookCompiler.ReadStructuredBook(book, path => sources[path]);
        Assert.Equal(new[] { "Empty", "Two" }, structured!.Sections.Select(s => s.Title).ToArray());
        Assert.Empty(structured.Sections[0].Units);
    }

    /// <summary>
    /// <c>displayName</c> runs <c>splitExtension</c> before <c>splitPrefix</c> and does not
    /// care that the thing it names is a folder, so a chapter folder called "02-Notes.md" is
    /// the chapter "Notes" in the compile, the EPUB and the .tex — while the sidebar
    /// (<c>BookChapter.name</c>) still shows the folder's whole name. Probed with swiftc:
    /// 02-Notes.md gives Notes, x.TXT gives x, "..md" gives one dot.
    /// </summary>
    [Fact]
    public void AChapterFolderCarryingAnArticleExtensionLosesItInEveryTitle()
    {
        var book = MakeBook(
            Array.Empty<(string, string)>(),
            new[] { ("02-Notes.md", new[] { ("01-a.md", "A.") }), ("x.TXT", new[] { ("01-b.md", "B.") }) },
            out var sources);

        Assert.Equal(
            "# My Book\n\n\\newpage\n\n# Notes\n\n\\newpage\n\nA.\n\n\\newpage\n\n# x\n\n\\newpage\n\nB.",
            BookCompiler.CompileBookSource(book, path => sources[path]));
        var structured = BookCompiler.ReadStructuredBook(book, path => sources[path]);
        Assert.Equal(new[] { "Notes", "x" }, structured!.Sections.Select(s => s.Title).ToArray());
        // The sidebar keeps the folder name whole; only the export titles are display names.
        Assert.Equal(new[] { "02-Notes.md", "x.TXT" }, book.Chapters.Select(c => c.Name).ToArray());
        Assert.Equal(".", BookNaming.DisplayName("..md"));
    }

    /// <summary>
    /// The compile reader, on the two BOMs. A UTF-8 BOM is dropped by both sides —
    /// Foundation's <c>String(data:encoding:.utf8)</c> swallows EF BB BF (probed) and so
    /// does <c>PlainTextCodec</c>. A UTF-16 file is where the declared §7.2 divergence
    /// reaches past CP1251: the Mac's compile reader has no UTF-16 step, so it falls to
    /// Latin-1 and the probe prints U+00FF U+00FE '#' U+0000 and so on. This port reads it
    /// as the text it is.
    /// </summary>
    [Fact]
    public void TheCompileReaderDropsAUtf8BomAndReadsTheUtf16FileTheMacTurnsIntoMojibake()
    {
        var container = Path.Combine(Path.GetTempPath(), "md-bookio-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(container, "03-My Book");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllBytes(Path.Combine(root, "01-Bom.md"),
                new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8.GetBytes("# Scene\n")).ToArray());
            var utf16 = new byte[] { 0xFF, 0xFE }
                .Concat(new UnicodeEncoding(false, false).GetBytes("# S")).ToArray();
            File.WriteAllBytes(Path.Combine(root, "02-Wide.md"), utf16);

            Assert.Equal("# Scene\n", BookFolder.ReadArticle(Path.Combine(root, "01-Bom.md")));
            Assert.Equal("# S", BookFolder.ReadArticle(Path.Combine(root, "02-Wide.md")));
            // What the Mac's compile would have put in the PDF instead.
            Assert.NotEqual(Encoding.Latin1.GetString(utf16), BookFolder.ReadArticle(Path.Combine(root, "02-Wide.md")));

            var tree = BookModel.Load(root);
            Assert.Equal(
                "# My Book\n\n\\newpage\n\n# Scene\n\n\n\\newpage\n\n# S",
                BookCompiler.CompileBookSource(tree!, path => BookFolder.ReadArticle(path)));
        }
        finally
        {
            Directory.Delete(container, recursive: true);
        }
    }

    /// <summary>
    /// The create prompts vet the name the OS will actually hold. "CON.md" is Win32's
    /// console device in every folder — <c>File.WriteAllBytes</c> reports success and
    /// creates nothing — so the article must be refused rather than handed back as a path to
    /// a file that is not there; likewise a chapter folder. A trailing space is NOT refused
    /// for an article: ".md" follows it, the name is legal, and Foundation creates
    /// "Draft .md" (probed).
    /// </summary>
    [Fact]
    public void TheCreatePromptsRefuseAWindowsDeviceNameAndOnlyThat()
    {
        var container = Path.Combine(Path.GetTempPath(), "md-bookio-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(container, "03-My Book");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Null(BookFolder.CreateArticle(root, "CON"));
            Assert.Null(BookFolder.CreateArticle(root, "con"));
            Assert.Null(BookFolder.CreateArticle(root, "LPT1"));
            Assert.False(BookFolder.CreateChapter(root, "CON"));
            Assert.False(BookFolder.CreateChapter(root, "NUL"));

            // Everything a book actually holds still goes through, trailing space included.
            Assert.NotNull(BookFolder.CreateArticle(root, "Draft "));
            Assert.NotNull(BookFolder.CreateArticle(root, "CONS"));
            Assert.NotNull(BookFolder.CreateArticle(root, "Scene.md"));
            Assert.True(BookFolder.CreateChapter(root, "v1.2"));
            Assert.Equal(
                new[] { "CONS.md", "Draft .md", "Scene.md.md", "v1.2" },
                Directory.GetFileSystemEntries(root).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        }
        finally
        {
            Directory.Delete(container, recursive: true);
        }
    }
}
