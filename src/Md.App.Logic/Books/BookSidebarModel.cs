// The sidebar as a list of rows (shell-design.md §8.3, book.md §13.2). Which rows exist, in which
// order, what each is called and which of Move Up / Move Down is live — all decided here, so the
// WinUI ListView builder has nothing left to decide.
using Md.Core.Book;

namespace Md.App.Logic.Books;

/// <summary>What one sidebar row is. Only the two article kinds are selectable.</summary>
public enum BookRowKind
{
    /// <summary>An article in the book root — the front matter, before every chapter.</summary>
    RootArticle,

    /// <summary>A chapter's header row: the full folder name, not selectable, carrying the chapter's own management menu.</summary>
    ChapterHeader,

    /// <summary>An article inside a chapter.</summary>
    ChapterArticle,

    /// <summary>The "New Article…" row that closes the root section and every chapter section.</summary>
    NewArticle,
}

/// <summary>
/// One row of the sidebar.
/// </summary>
/// <param name="Kind">Which of the four shapes this is.</param>
/// <param name="Title">
/// The text shown. Articles use <see cref="BookArticle.Name"/> and chapters
/// <see cref="BookChapter.Name"/> — the ordering prefix stays visible, which is the Mac's choice
/// (the reading order is the name, so hiding it would hide the thing being reordered).
/// </param>
/// <param name="Path">The article file or the chapter folder; null on a "New Article…" row.</param>
/// <param name="Folder">
/// The folder this row belongs to: the book root for a root article, a chapter header and the root's
/// own "New Article…" row; the chapter folder for a chapter's articles and its "New Article…" row.
/// </param>
public sealed record BookRow(BookRowKind Kind, string Title, string? Path, string Folder)
{
    public bool IsArticle => Kind is BookRowKind.RootArticle or BookRowKind.ChapterArticle;

    /// <summary>Only articles carry the ListView's selection; a header or an action row that could be selected would fight it.</summary>
    public bool IsSelectable => IsArticle;
}

/// <summary>
/// What the management menu on one row needs: the sibling group it renumbers inside and where this
/// item sits in it (Swift's <c>articleContext</c>, plus the chapter group the section header's menu
/// passes by hand).
/// </summary>
public sealed record BookRowCommands(
    string Name,
    string Folder,
    IReadOnlyList<string> Siblings,
    int Index,
    bool IsChapter)
{
    /// <summary>Move Up is dead at the top of the group (Swift <c>.disabled(index == 0)</c>).</summary>
    public bool CanMoveUp => Index > 0;

    /// <summary>Move Down is dead at the bottom (<c>.disabled(index == siblings.count - 1)</c>).</summary>
    public bool CanMoveDown => Index >= 0 && Index < Siblings.Count - 1;

    /// <summary>The Delete… alert's message — a chapter takes its articles with it, and says so.</summary>
    public string DeleteMessage => IsChapter ? Strings.Books.DeleteChapterMessage : Strings.Books.DeleteArticleMessage;
}

/// <summary>
/// The sidebar's contents, derived from a <see cref="Book"/> snapshot. Section 1 is the root
/// articles and its "New Article…" row (no header — the front matter has no title on the Mac
/// either); then one section per chapter: its header, its articles, its "New Article…" row. A
/// chapter with no articles still gets its section, so there is somewhere to put the first one.
/// </summary>
public static class BookSidebarModel
{
    /// <summary>Every row in display order. An empty list when there is no book.</summary>
    public static IReadOnlyList<BookRow> Rows(Book? book)
    {
        if (book is null) return [];
        var rows = new List<BookRow>();
        foreach (var article in book.Articles)
        {
            rows.Add(new BookRow(BookRowKind.RootArticle, article.Name, article.Path, book.Root));
        }
        rows.Add(new BookRow(BookRowKind.NewArticle, Strings.Books.NewArticleRow, null, book.Root));
        foreach (var chapter in book.Chapters)
        {
            rows.Add(new BookRow(BookRowKind.ChapterHeader, chapter.Name, chapter.Path, book.Root));
            foreach (var article in chapter.Articles)
            {
                rows.Add(new BookRow(BookRowKind.ChapterArticle, article.Name, article.Path, chapter.Path));
            }
            rows.Add(new BookRow(BookRowKind.NewArticle, Strings.Books.NewArticleRow, null, chapter.Path));
        }
        return rows;
    }

    /// <summary>
    /// The management menu's state for the article or chapter at <paramref name="path"/>, or null
    /// when the path is not in this book. A chapter's siblings are the chapter list (renumbered
    /// inside the root); an article's are its own folder's articles.
    /// </summary>
    public static BookRowCommands? CommandsFor(Book? book, string? path)
    {
        if (book is null || path is null) return null;
        if (BookFolder.SiblingsOf(book, path) is not { } siblings) return null;
        var isChapter = book.Chapters.Any(chapter => Same(chapter.Path, path));
        return new BookRowCommands(siblings.Name, siblings.Folder, siblings.Names, siblings.Index, isChapter);
    }

    /// <summary>The row index of an article path, or -1 — what the ListView's SelectedIndex is set from.</summary>
    public static int IndexOfArticle(IReadOnlyList<BookRow> rows, string? path)
    {
        if (path is null) return -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].IsArticle && rows[i].Path is { } candidate && Same(candidate, path)) return i;
        }
        return -1;
    }

    static bool Same(string a, string b) => string.Equals(a, b, BookPaths.Comparison);
}
