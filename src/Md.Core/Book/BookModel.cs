namespace Md.Core.Book;

/// <summary>One article — a Markdown file in the book. Identity is the file path.</summary>
public sealed record BookArticle(string Path)
{
    /// <summary>
    /// The sidebar name: file name without its last extension, ordering prefix kept
    /// ("01-Preface.md" → "01-Preface", "chapter.one.md" → "chapter.one"). Swift's
    /// <c>deletingPathExtension</c>; a leading or trailing dot is not an extension.
    /// </summary>
    public string Name => StripLastExtension(BookPaths.Name(Path));

    internal static string StripLastExtension(string file)
    {
        var dot = file.LastIndexOf('.');
        return dot > 0 && dot < file.Length - 1 ? file[..dot] : file;
    }
}

/// <summary>One chapter — a direct subfolder of the book — with its articles already in reading order.</summary>
public sealed record BookChapter(string Path, IReadOnlyList<BookArticle> Articles)
{
    /// <summary>Folders show their full name: a dot in a folder name is part of the name, not a format to hide.</summary>
    public string Name => BookPaths.Name(Path);
}

/// <summary>
/// A snapshot of the book as listed from disk, rebuilt wholesale after every change.
/// Root articles are the front matter and come before every chapter.
/// </summary>
public sealed record Book(string Root, IReadOnlyList<BookArticle> Articles, IReadOnlyList<BookChapter> Chapters)
{
    /// <summary>The window title: the raw root folder name. The compile title is <c>BookNaming.DisplayName</c> of it instead.</summary>
    public string Name => BookPaths.Name(Root);

    /// <summary>A book with nothing in it — what an empty folder lists as.</summary>
    public static Book Empty(string root) =>
        new(BookPaths.Standardize(root), Array.Empty<BookArticle>(), Array.Empty<BookChapter>());
}

/// <summary>One directory entry as the listing saw it.</summary>
/// <param name="IsHidden">The OS hidden flag (Windows attribute). Dot-names are hidden regardless.</param>
public sealed record BookEntry(string Name, bool IsDirectory, bool IsHidden = false);

/// <summary>
/// The directory-listing seam <c>BookModel.Load</c> reads through, so the tree logic
/// is testable against an in-memory folder and the app can route through whatever
/// storage API granted the folder.
/// </summary>
public interface IBookListing
{
    /// <summary>The entries of <paramref name="folder"/>, or null when it cannot be listed.</summary>
    IReadOnlyList<BookEntry>? List(string folder);
}

/// <summary><see cref="IBookListing"/> over System.IO.</summary>
public sealed class DirectoryBookListing : IBookListing
{
    public static DirectoryBookListing Instance { get; } = new();

    public IReadOnlyList<BookEntry>? List(string folder)
    {
        try
        {
            var entries = new List<BookEntry>();
            foreach (var info in new DirectoryInfo(folder).EnumerateFileSystemInfos())
            {
                // Windows hides by attribute, not by dot; the System flag marks
                // desktop.ini-style clutter that is never an article.
                var hidden = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
                entries.Add(new BookEntry(info.Name, (info.Attributes & FileAttributes.Directory) != 0, hidden));
            }
            return entries;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}

/// <summary>
/// The book tree from a listing. Port of <c>BookLibrary.loadBook / readingOrder</c>:
/// every direct subfolder is a chapter (empty or not — it still gets a section and a
/// "New Article…" row), every article-extension file is an article, one level deep,
/// hidden items skipped. Swift lists a chapter's entries without checking that they
/// are files, so a sub-subfolder named "Foo.md" shows up as an article there; Kotlin
/// filters on <c>isFile</c>. This port filters — a folder is not an article.
/// </summary>
public static class BookModel
{
    /// <summary>List the folder at <paramref name="root"/> through System.IO. Null when the root cannot be listed.</summary>
    public static Book? Load(string root) => Load(root, DirectoryBookListing.Instance);

    /// <summary>List <paramref name="root"/> through <paramref name="listing"/>. Null when the root cannot be listed.</summary>
    public static Book? Load(string root, IBookListing listing)
    {
        var rootPath = BookPaths.Standardize(root);
        var top = listing.List(rootPath);
        if (top is null) return null;
        var articles = new List<BookArticle>();
        var chapters = new List<BookChapter>();
        foreach (var entry in top)
        {
            if (IsSkipped(entry)) continue;
            var path = Path.Combine(rootPath, entry.Name);
            if (entry.IsDirectory)
            {
                // An unlistable chapter is still a chapter, just an empty one — the
                // section stays visible instead of vanishing from the sidebar.
                var inner = listing.List(path) ?? Array.Empty<BookEntry>();
                var chapterArticles = inner
                    .Where(e => !IsSkipped(e) && !e.IsDirectory && BookNaming.IsArticleName(e.Name))
                    .Select(e => new BookArticle(Path.Combine(path, e.Name)))
                    .OrderBy(a => a.Name, BookOrder.Comparer)
                    .ToList();
                chapters.Add(new BookChapter(path, chapterArticles));
            }
            else if (BookNaming.IsArticleName(entry.Name))
            {
                articles.Add(new BookArticle(path));
            }
        }
        return new Book(
            rootPath,
            articles.OrderBy(a => a.Name, BookOrder.Comparer).ToList(),
            chapters.OrderBy(c => c.Name, BookOrder.Comparer).ToList());
    }

    /// <summary>
    /// The whole book flattened: root articles (front matter), then each chapter's
    /// articles, chapters already in order. What Previous / Next step through and
    /// the order the compile, EPUB and LaTeX walk.
    /// </summary>
    public static IReadOnlyList<BookArticle> ReadingOrder(Book book) =>
        book.Articles.Concat(book.Chapters.SelectMany(c => c.Articles)).ToList();

    /// <summary>
    /// Swift's <c>.skipsHiddenFiles</c>: dot-names and flagged-hidden items. The
    /// reorder executor's staging names (".md-reorder-…") are dot-names on purpose.
    /// </summary>
    private static bool IsSkipped(BookEntry entry) =>
        entry.IsHidden || entry.Name.StartsWith('.');
}
