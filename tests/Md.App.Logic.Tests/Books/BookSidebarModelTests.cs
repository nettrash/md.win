using Md.App.Logic;
using Md.App.Logic.Books;
using Md.Core.Book;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// The sidebar over a real folder tree (§8.3): which rows, in which order, called what — and which
/// of Move Up / Move Down each row offers.
/// </summary>
public class BookSidebarModelTests
{
    static Book Load(BookFixture fixture) => BookModel.Load(fixture.Root)!;

    [Fact]
    public void RowsAreRootArticlesThenEachChapterEachClosedByANewArticleRow()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        fixture.Article("02-One/01-a.md");
        fixture.Article("02-One/02-b.md");
        fixture.Article("03-Two/01-c.md");

        var rows = BookSidebarModel.Rows(Load(fixture));

        Assert.Equal(
        [
            (BookRowKind.RootArticle, "01-Preface"),
            (BookRowKind.NewArticle, Strings.Books.NewArticleRow),
            (BookRowKind.ChapterHeader, "02-One"),
            (BookRowKind.ChapterArticle, "01-a"),
            (BookRowKind.ChapterArticle, "02-b"),
            (BookRowKind.NewArticle, Strings.Books.NewArticleRow),
            (BookRowKind.ChapterHeader, "03-Two"),
            (BookRowKind.ChapterArticle, "01-c"),
            (BookRowKind.NewArticle, Strings.Books.NewArticleRow),
        ], rows.Select(row => (row.Kind, row.Title)).ToList());
    }

    [Fact]
    public void ANewArticleRowCarriesTheFolderItCreatesIn()
    {
        using var fixture = new BookFixture();
        var chapter = fixture.Chapter("02-One");

        var rows = BookSidebarModel.Rows(Load(fixture));

        var newRows = rows.Where(row => row.Kind == BookRowKind.NewArticle).ToList();
        Assert.Equal(2, newRows.Count);                                   // the root's, and the empty chapter's
        Assert.Equal(BookPaths.Standardize(fixture.Root), newRows[0].Folder);
        Assert.Equal(BookPaths.Standardize(chapter), newRows[1].Folder);
        Assert.All(newRows, row => Assert.Null(row.Path));
    }

    [Fact]
    public void OnlyArticlesAreSelectable()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        fixture.Article("02-One/01-a.md");

        var rows = BookSidebarModel.Rows(Load(fixture));

        Assert.Equal(
            [BookRowKind.RootArticle, BookRowKind.ChapterArticle],
            rows.Where(row => row.IsSelectable).Select(row => row.Kind).ToList());
    }

    [Fact]
    public void AChapterArticleBelongsToItsChapterFolderNotTheRoot()
    {
        using var fixture = new BookFixture();
        var chapter = fixture.Chapter("02-One");
        fixture.Article("02-One/01-a.md");

        var row = BookSidebarModel.Rows(Load(fixture)).Single(r => r.Kind == BookRowKind.ChapterArticle);

        Assert.Equal(BookPaths.Standardize(chapter), row.Folder);
    }

    [Fact]
    public void NoBookIsNoRows() => Assert.Empty(BookSidebarModel.Rows(null));

    [Fact]
    public void MoveUpIsDeadAtTheTopAndMoveDownAtTheBottom()
    {
        using var fixture = new BookFixture();
        var first = fixture.Article("01-a.md");
        var middle = fixture.Article("02-b.md");
        var last = fixture.Article("03-c.md");
        var book = Load(fixture);

        var top = BookSidebarModel.CommandsFor(book, first)!;
        Assert.False(top.CanMoveUp);
        Assert.True(top.CanMoveDown);

        var centre = BookSidebarModel.CommandsFor(book, middle)!;
        Assert.True(centre.CanMoveUp);
        Assert.True(centre.CanMoveDown);

        var bottom = BookSidebarModel.CommandsFor(book, last)!;
        Assert.True(bottom.CanMoveUp);
        Assert.False(bottom.CanMoveDown);
    }

    [Fact]
    public void AnArticleRenumbersAmongItsOwnFolderAndAChapterAmongTheChapters()
    {
        using var fixture = new BookFixture();
        fixture.Article("02-One/01-a.md");
        fixture.Article("02-One/02-b.md");
        var chapter = fixture.Chapter("02-One");
        fixture.Chapter("03-Two");
        var book = Load(fixture);

        var article = BookSidebarModel.CommandsFor(book, Path.Combine(chapter, "02-b.md"))!;
        Assert.Equal(BookPaths.Standardize(chapter), article.Folder);
        Assert.Equal(["01-a.md", "02-b.md"], article.Siblings);
        Assert.Equal(1, article.Index);
        Assert.False(article.IsChapter);
        Assert.Equal(Strings.Books.DeleteArticleMessage, article.DeleteMessage);

        var section = BookSidebarModel.CommandsFor(book, chapter)!;
        Assert.Equal(BookPaths.Standardize(fixture.Root), section.Folder);
        Assert.Equal(["02-One", "03-Two"], section.Siblings);
        Assert.Equal(0, section.Index);
        Assert.True(section.IsChapter);
        Assert.Equal(Strings.Books.DeleteChapterMessage, section.DeleteMessage);
    }

    [Fact]
    public void APathFromAnotherBookHasNoCommands()
    {
        using var fixture = new BookFixture();
        using var other = new BookFixture();
        fixture.Article("01-a.md");
        var stranger = other.Article("01-a.md");

        Assert.Null(BookSidebarModel.CommandsFor(Load(fixture), stranger));
        Assert.Null(BookSidebarModel.CommandsFor(Load(fixture), null));
        Assert.Null(BookSidebarModel.CommandsFor(null, stranger));
    }

    [Fact]
    public void TheSelectedRowIsFoundByPathAndNothingElseIs()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        var inner = fixture.Article("02-One/01-a.md");
        var rows = BookSidebarModel.Rows(Load(fixture));

        Assert.Equal(3, BookSidebarModel.IndexOfArticle(rows, inner));
        Assert.Equal(-1, BookSidebarModel.IndexOfArticle(rows, null));
        // A chapter folder is a row, but never a selected one.
        Assert.Equal(-1, BookSidebarModel.IndexOfArticle(rows, fixture.At("02-One")));
    }
}
