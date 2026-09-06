using Md.Core.Book;
using BookTree = Md.Core.Book.Book;

namespace Md.Core.Tests;

/// <summary>
/// The tree (mdTests: testReadingOrderIsRootArticlesThenChapters,
/// testReadingOrderOfEmptyBookIsEmpty) and the listing that builds it — through an
/// in-memory IBookListing, and once through System.IO.
/// </summary>
public class BookModelTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "Book"));

    /// <summary>A book snapshot straight from paths — the listing is not under test.</summary>
    private static BookTree MakeBook(string root, string[] articles, (string Name, string[] Files)[] chapters) =>
        new(root,
            articles.Select(a => new BookArticle(Path.Combine(root, a))).ToList(),
            chapters.Select(c =>
            {
                var folder = Path.Combine(root, c.Name);
                return new BookChapter(folder, c.Files.Select(f => new BookArticle(Path.Combine(folder, f))).ToList());
            }).ToList());

    [Fact]
    public void ReadingOrderIsRootArticlesThenChapters()
    {
        var book = MakeBook(Root, new[] { "01-Preface.md" },
            new[] { ("02-One", new[] { "01-a.md", "02-b.md" }), ("03-Two", new[] { "01-c.md" }) });
        Assert.Equal(new[] { "01-Preface", "01-a", "02-b", "01-c" },
            BookModel.ReadingOrder(book).Select(a => a.Name).ToArray());
    }

    [Fact]
    public void ReadingOrderOfEmptyBookIsEmpty()
    {
        var book = MakeBook(Root, Array.Empty<string>(), new[] { ("02-One", Array.Empty<string>()) });
        Assert.Empty(BookModel.ReadingOrder(book));
        Assert.Empty(BookModel.ReadingOrder(BookTree.Empty(Root)));
    }

    [Fact]
    public void NamesFollowTheSwiftModel()
    {
        // Article: last extension off, prefix kept. Chapter: full folder name. Book: raw root name.
        Assert.Equal("01-Preface", new BookArticle(Path.Combine(Root, "01-Preface.md")).Name);
        Assert.Equal("chapter.one", new BookArticle(Path.Combine(Root, "chapter.one.md")).Name);
        Assert.Equal("README", new BookArticle(Path.Combine(Root, "README")).Name);
        Assert.Equal("v1.2", new BookChapter(Path.Combine(Root, "v1.2"), Array.Empty<BookArticle>()).Name);
        Assert.Equal("02-One", new BookChapter(Path.Combine(Root, "02-One") + Path.DirectorySeparatorChar, Array.Empty<BookArticle>()).Name);
        Assert.Equal("Book", BookTree.Empty(Root).Name);
        Assert.Equal("Book", BookTree.Empty(Root + Path.DirectorySeparatorChar).Name);
    }

    private sealed class MemoryListing : IBookListing
    {
        public Dictionary<string, List<BookEntry>?> Folders { get; } =
            new(BookPaths.Comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<BookEntry>? List(string folder) =>
            Folders.TryGetValue(BookPaths.Standardize(folder), out var entries) ? entries : null;
    }

    private static MemoryListing SampleListing()
    {
        var listing = new MemoryListing();
        listing.Folders[Root] = new List<BookEntry>
        {
            new("10-deploy.md", false), new("01-intro.md", false), new("2. setup.markdown", false),
            new("notes.TXT", false), new("cover.png", false), new(".DS_Store", false),
            new("secret.md", false, IsHidden: true),
            new("03-Two", true), new("02-One", true), new("Empty", true), new(".git", true),
        };
        listing.Folders[Path.Combine(Root, "02-One")] = new List<BookEntry>
        {
            new("02-b.md", false), new("01-a.md", false), new("Foo.md", true), new("img.png", false),
        };
        listing.Folders[Path.Combine(Root, "03-Two")] = new List<BookEntry> { new("01-c.md", false) };
        listing.Folders[Path.Combine(Root, "Empty")] = new List<BookEntry>();
        return listing;
    }

    [Fact]
    public void LoadSeparatesArticlesFromChaptersAndOrdersBoth()
    {
        var book = BookModel.Load(Root, SampleListing());
        Assert.NotNull(book);
        Assert.Equal(Root, book!.Root);
        Assert.Equal(new[] { "01-intro", "2. setup", "10-deploy", "notes" }, book.Articles.Select(a => a.Name).ToArray());
        Assert.Equal(Path.Combine(Root, "notes.TXT"), book.Articles[3].Path);
        // Every subfolder is a chapter, the empty one included; hidden ones are not.
        Assert.Equal(new[] { "02-One", "03-Two", "Empty" }, book.Chapters.Select(c => c.Name).ToArray());
        // Inside a chapter: sorted, files only — a sub-subfolder named "Foo.md" is not an article.
        Assert.Equal(new[] { "01-a", "02-b" }, book.Chapters[0].Articles.Select(a => a.Name).ToArray());
        Assert.Equal(Path.Combine(Root, "02-One", "01-a.md"), book.Chapters[0].Articles[0].Path);
        Assert.Empty(book.Chapters[2].Articles);
        Assert.Equal(new[] { "01-intro", "2. setup", "10-deploy", "notes", "01-a", "02-b", "01-c" },
            BookModel.ReadingOrder(book).Select(a => a.Name).ToArray());
    }

    [Fact]
    public void LoadReturnsNullForAnUnlistableRootAndKeepsAnUnlistableChapter()
    {
        Assert.Null(BookModel.Load(Path.Combine(Root, "Nowhere"), new MemoryListing()));
        var listing = SampleListing();
        listing.Folders.Remove(Path.Combine(Root, "03-Two"));
        var book = BookModel.Load(Root, listing);
        Assert.NotNull(book);
        Assert.Equal(new[] { "02-One", "03-Two", "Empty" }, book!.Chapters.Select(c => c.Name).ToArray());
        Assert.Empty(book.Chapters[1].Articles);
    }

    [Fact]
    public void LoadFromDiskMatchesTheListingContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "md-book-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "01-Preface.md"), "# Preface\n");
            File.WriteAllText(Path.Combine(root, "cover.png"), "");
            File.WriteAllText(Path.Combine(root, ".md-reorder-LEFTOVER"), "");
            Directory.CreateDirectory(Path.Combine(root, "02-One"));
            File.WriteAllText(Path.Combine(root, "02-One", "02-b.md"), "");
            File.WriteAllText(Path.Combine(root, "02-One", "01-a.md"), "");
            Directory.CreateDirectory(Path.Combine(root, "02-One", "Nested.md"));
            Directory.CreateDirectory(Path.Combine(root, "Drafts"));

            var book = BookModel.Load(root);
            Assert.NotNull(book);
            Assert.Equal(new[] { "01-Preface" }, book!.Articles.Select(a => a.Name).ToArray());
            Assert.Equal(new[] { "02-One", "Drafts" }, book.Chapters.Select(c => c.Name).ToArray());
            Assert.Equal(new[] { "01-a", "02-b" }, book.Chapters[0].Articles.Select(a => a.Name).ToArray());
            Assert.Null(BookModel.Load(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
