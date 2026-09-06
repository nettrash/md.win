using Md.App.Logic.Books;
using Md.Core.Book;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// Previous / Next Article (§8.4-§8.5): the reading order, entering the book from either end, and
/// the two ends where a step is not possible at all.
/// </summary>
public class BookStepperTests
{
    static BookFixture ThreeArticles()
    {
        var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        fixture.Article("02-One/01-a.md");
        fixture.Article("02-One/02-b.md");
        fixture.Article("03-Two/01-c.md");
        return fixture;
    }

    static Book Load(BookFixture fixture) => BookModel.Load(fixture.Root)!;

    [Fact]
    public void TheOrderIsRootArticlesThenEveryChaptersInOrder()
    {
        using var fixture = ThreeArticles();

        var stepper = BookStepper.For(Load(fixture), selection: null, opensInSeparateWindows: false);

        Assert.Equal(
            ["01-Preface.md", "01-a.md", "02-b.md", "01-c.md"],
            stepper.Order.Select(Path.GetFileName).ToList());
    }

    [Fact]
    public void WithNothingSelectedNextEntersFromTheFrontAndPreviousFromTheBack()
    {
        using var fixture = ThreeArticles();
        var stepper = BookStepper.For(Load(fixture), selection: null, opensInSeparateWindows: false);

        Assert.True(stepper.CanPrevious);
        Assert.True(stepper.CanNext);
        Assert.Equal("01-Preface.md", Path.GetFileName(stepper.Next()));
        Assert.Equal("01-c.md", Path.GetFileName(stepper.Previous()));
    }

    [Fact]
    public void StepsWalkTheOrderAcrossChapterBoundaries()
    {
        using var fixture = ThreeArticles();
        var book = Load(fixture);
        var last = fixture.At("02-One", "02-b.md");

        var stepper = BookStepper.For(book, last, opensInSeparateWindows: false);

        Assert.Equal("01-a.md", Path.GetFileName(stepper.Previous()));      // back inside the chapter
        Assert.Equal("01-c.md", Path.GetFileName(stepper.Next()));          // forward into the next one
    }

    [Fact]
    public void AtTheFirstArticlePreviousIsDeadAndAtTheLastNextIs()
    {
        using var fixture = ThreeArticles();
        var book = Load(fixture);

        var front = BookStepper.For(book, fixture.At("01-Preface.md"), opensInSeparateWindows: false);
        Assert.False(front.CanPrevious);
        Assert.Null(front.Previous());
        Assert.True(front.CanNext);

        var back = BookStepper.For(book, fixture.At("03-Two", "01-c.md"), opensInSeparateWindows: false);
        Assert.True(back.CanPrevious);
        Assert.False(back.CanNext);
        Assert.Null(back.Next());
    }

    [Fact]
    public void ASelectionThatIsNotInTheBookEntersFromTheEndsLikeNoSelection()
    {
        using var fixture = ThreeArticles();
        using var other = new BookFixture();
        var stranger = other.Article("01-a.md");

        var stepper = BookStepper.For(Load(fixture), stranger, opensInSeparateWindows: false);

        Assert.Null(stepper.Index);
        Assert.Equal("01-Preface.md", Path.GetFileName(stepper.Next()));
    }

    [Fact]
    public void AnEmptyBookStepsNowhere()
    {
        using var fixture = new BookFixture();
        fixture.Chapter("01-Empty");

        var stepper = BookStepper.For(Load(fixture), selection: null, opensInSeparateWindows: false);

        Assert.Empty(stepper.Order);
        Assert.False(stepper.CanPrevious);
        Assert.False(stepper.CanNext);
        Assert.Null(stepper.Previous());
        Assert.Null(stepper.Next());
    }

    [Fact]
    public void SeparateWindowsLeavesNothingToStepFrom()
    {
        using var fixture = ThreeArticles();

        var stepper = BookStepper.For(Load(fixture), fixture.At("01-Preface.md"), opensInSeparateWindows: true);

        Assert.False(stepper.CanPrevious);
        Assert.False(stepper.CanNext);
        Assert.Empty(stepper.Order);
    }

    [Fact]
    public void NoBookIsNoStepper()
    {
        var stepper = BookStepper.For(null, "anything.md", opensInSeparateWindows: false);

        Assert.False(stepper.CanPrevious);
        Assert.False(stepper.CanNext);
    }
}
