using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// Selection remapping after a rename plan (mdTests: testDestinationFollowsARenamedArticle,
/// testDestinationFollowsAnArticleInsideARenamedChapter, testDestinationLeavesUntouchedURLsAlone).
/// Paths are built with Path.Combine so the same tests hold on Windows.
/// </summary>
public class DestinationTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "Book"));

    [Fact]
    public void DestinationFollowsARenamedArticle()
    {
        var moved = Destination.Of(Path.Combine(Root, "02-Draft.md"), Root,
            new[] { new RenamePair("02-Draft.md", "01-Draft.md"), new RenamePair("01-Intro.md", "02-Intro.md") });
        Assert.Equal("01-Draft.md", Path.GetFileName(moved));
        Assert.Equal(Path.Combine(Root, "01-Draft.md"), moved);
    }

    [Fact]
    public void DestinationFollowsAnArticleInsideARenamedChapter()
    {
        var article = Path.Combine(Root, "03-Middle", "01-Scene.md");
        var moved = Destination.Of(article, Root, new[] { new RenamePair("03-Middle", "02-Middle") });
        Assert.Equal(Path.Combine(Root, "02-Middle", "01-Scene.md"), moved);
    }

    [Fact]
    public void DestinationLeavesUntouchedPathsAlone()
    {
        var plan = new[] { new RenamePair("01-a.md", "02-a.md") };
        // A sibling the plan doesn't mention…
        var bystander = Path.Combine(Root, "03-c.md");
        Assert.Equal(BookPaths.Standardize(bystander), Destination.Of(bystander, Root, plan));
        // …an article whose chapter is not in the plan…
        var nested = Path.Combine(Root, "02-One", "01-a.md");
        Assert.Same(nested, Destination.Of(nested, Root, plan));
        // …and anything outside the folder entirely, returned exactly as given.
        var outside = Path.Combine(Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "tmp", "Elsewhere")), "01-a.md");
        Assert.Same(outside, Destination.Of(outside, Root, plan));
        // A folder with a trailing separator names the same folder.
        Assert.Equal(Path.Combine(Root, "02-a.md"),
            Destination.Of(Path.Combine(Root, "01-a.md"), Root + Path.DirectorySeparatorChar, plan));
    }
}
