using Md.App.Logic.Books;
using Md.App.Logic.Settings;

namespace Md.App.Logic.Tests.Books;

/// <summary>The open-book setting and the example book's name (§8.2).</summary>
public class BookLibraryModelTests
{
    [Fact]
    public void NoSettingAndAnEmptySettingBothMeanNoBook()
    {
        var settings = new FakeSettingsStore();
        var library = new BookLibraryModel(settings);

        Assert.Null(library.BookPath);
        Assert.False(library.HasBook);

        settings.SetString(SettingsKeys.BookBookmark, "");
        Assert.Null(library.BookPath);
        Assert.False(library.HasBook);
    }

    [Fact]
    public void OpeningAndClosingABookIsOneKey()
    {
        var settings = new FakeSettingsStore();
        var library = new BookLibraryModel(settings);

        library.Store(@"C:\Books\My Book");
        Assert.Equal(@"C:\Books\My Book", library.BookPath);
        Assert.True(library.HasBook);

        library.CloseBook();
        Assert.Null(library.BookPath);
        Assert.Equal([SettingsKeys.BookBookmark], settings.Removals);
    }

    [Fact]
    public void TheExampleBookKeepsItsNameWhenTheFolderIsFree()
    {
        Assert.Equal("Example Book", BookLibraryModel.DedupedName("Example Book", _ => false));
    }

    [Fact]
    public void AnOccupiedNameCountsUpAndNeverSaysTwo1()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "Example Book", "Example Book 2" };

        Assert.Equal("Example Book 3", BookLibraryModel.DedupedName("Example Book", taken.Contains));
        Assert.DoesNotContain("Example Book 1", taken);
    }

    [Fact]
    public void AFolderFullOfExampleBooksGivesUpRatherThanLoop()
    {
        Assert.Null(BookLibraryModel.DedupedName("Example Book", _ => true, maxAttempts: 5));
    }
}
