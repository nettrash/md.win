// The traps the porting reports name, and the three mutants the first suite let live. Each test
// here exists because a plausible edit passed the whole suite: the review that added them ran
// seventeen one-line mutations through `dotnet test` and kept the ones nothing caught.
using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Settings;
using Md.Core.Book;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// Book rules that a mutation survived, plus the Windows-specific traps book.md §16 and §18 list:
/// path spellings, the rooted-second-argument hole in <c>Path.Combine</c>, the two-phase reorder a
/// swap needs, and the case-only folder rename <c>Directory.Move</c> can refuse.
/// </summary>
/// <remarks>
/// In the "BookFlushGate" collection with <see cref="BookOutputTests"/>: the gate is a process-wide
/// static event and two classes subscribing to it at once would answer each other's requests.
/// </remarks>
[Collection("BookFlushGate")]
public class BookAdversarialTests
{
    static (BookNavigatorModel Model, FakeArticleSession Session, FakeSettingsStore Settings) Open(BookFixture fixture)
    {
        var session = new FakeArticleSession();
        var settings = new FakeSettingsStore();
        var model = new BookNavigatorModel(session, settings);
        model.OpenBook(fixture.Root);
        return (model, session, settings);
    }

    // MARK: What a failed save must not remember

    /// <summary>
    /// book.md §13.7: <c>if session.select(url) { lastArticlePath = … }</c> — the remembered article
    /// is the one the writer actually reached. Writing it before the flush is answered survives every
    /// other test in the suite and quietly reopens the wrong article next launch.
    /// </summary>
    [Fact]
    public void AnArticleTheSessionRefusedToOpenIsNeverTheRememberedOne()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        fixture.Article("02-One/01-a.md");
        var (model, session, settings) = Open(fixture);
        Assert.Equal("01-Preface.md", settings.GetString(SettingsKeys.BookLastArticle));

        session.FlushFails = true;
        model.Select(fixture.At("02-One", "01-a.md"));

        Assert.Equal("01-Preface.md", settings.GetString(SettingsKeys.BookLastArticle));
        Assert.DoesNotContain(settings.Writes, write => Equals(write.Value, "02-One/01-a.md"));
    }

    // MARK: A veto is silent, whatever else is wrong

    /// <summary>
    /// §8.7: a vetoed output "aborts silently — the footer already says why". The gate is asked
    /// <em>before</em> the listing is read, so a veto with no book open must still raise nothing;
    /// reading the listing first turns the silent abort into a "No book is open" alert.
    /// </summary>
    [Fact]
    public async Task AVetoIsSilentEvenWhenThereIsNoBookToCompile()
    {
        var outputs = new FakeBookOutputs();
        var alerts = new List<(string Title, string Message)>();
        var output = new BookOutput(outputs, new FakeSettingsStore(), () => null);
        output.AlertRequested += (title, message) => alerts.Add((title, message));

        using var binding = new BookFlushGateBinding(() => false);
        await output.ExportPdfAsync();
        await output.ExportEpubAsync();
        await output.PrintAsync();

        Assert.Empty(alerts);
        Assert.Empty(outputs.Pdfs);
        Assert.Empty(outputs.Epubs);
        Assert.Empty(outputs.Prints);
    }

    // MARK: The example book's second copy

    /// <summary>
    /// §8.2 spells the deduped name "Example Book 2". Only asserting the third name lets a codec
    /// that skips straight from the base name to "… 3" through.
    /// </summary>
    [Fact]
    public void TheSecondExampleBookIsNumberTwo()
    {
        var taken = new HashSet<string>(StringComparer.Ordinal) { "Example Book" };

        Assert.Equal("Example Book 2", BookLibraryModel.DedupedName("Example Book", taken.Contains));
    }

    // MARK: Names Windows will not hold (§8.3, §6.4, book.md §16.3)

    [Theory]
    [InlineData("CON")]
    [InlineData("a?b")]
    [InlineData("trailing.")]
    public void AChapterNameWindowsRefusesIsRefusedOutLoud(string typed)
    {
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));

        model.CreateChapter(typed);

        Assert.Equal([(Strings.Documents.CouldNotRename, Strings.Documents.InvalidNameMessage)], alerts);
        Assert.Equal(["01-Preface.md"], fixture.Names());
    }

    [Fact]
    public void AnArticleNameIsVettedWithItsExtensionOnAndATrailingSpaceIsStillLegal()
    {
        using var fixture = new BookFixture();
        var (model, _, _) = Open(fixture);
        var alerts = new List<(string Title, string Message)>();
        model.AlertRequested += (title, message) => alerts.Add((title, message));

        // "CON" is a device name with or without ".md" — refused.
        model.CreateArticle("CON", fixture.Root);
        Assert.Single(alerts);
        Assert.Empty(fixture.Names());

        // "Draft " trims to "Draft"; nothing about it is refusable, and the file lands.
        model.CreateArticle("Draft ", fixture.Root);
        Assert.Single(alerts);
        Assert.Equal(["Draft.md"], fixture.Names());
    }

    // MARK: Path traps (book.md §16.1, §18.5)

    /// <summary>
    /// Core's own warning: <c>Path.Combine(root, rooted)</c> returns the second argument whole, so a
    /// remembered value that is an absolute path would walk straight out of the folder the app holds
    /// rights to. The existing suite only pins the <c>..</c> spelling of the same hole.
    /// </summary>
    [Fact]
    public void AnAbsoluteRememberedPathIsNeverFollowedOutOfTheBook()
    {
        using var outside = new BookFixture();
        outside.Article("secret.md");
        using var fixture = new BookFixture();
        fixture.Article("01-Preface.md");

        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.BookLastArticle, outside.At("secret.md").Replace(Path.DirectorySeparatorChar, '/'));
        var model = new BookNavigatorModel(new FakeArticleSession(), settings);
        model.OpenBook(fixture.Root);

        Assert.Equal(BookPaths.Standardize(fixture.At("01-Preface.md")), model.Selection);
    }

    // MARK: The two-phase reorder (§8.2, book.md §17 testRenumberPlanSwapsIdenticalStems)

    /// <summary>
    /// The swap every one-phase executor corrupts: both targets collide with a source, so a plain
    /// rename loop either overwrites one article or fails half way. The bytes are checked, not just
    /// the names — a staging bug that swaps the names and not the contents would otherwise pass.
    /// </summary>
    [Fact]
    public void ASwapOfIdenticalStemsKeepsBothArticlesAndTheirBytes()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-a.md", "# first\n");
        fixture.Article("02-a.md", "# second\n");
        var (model, _, _) = Open(fixture);
        Assert.Equal("01-a.md", Path.GetFileName(model.Selection));

        model.Move(fixture.At("01-a.md"), +1);

        Assert.Equal(["01-a.md", "02-a.md"], fixture.Names());
        Assert.Equal("# second\n", File.ReadAllText(fixture.At("01-a.md")));
        Assert.Equal("# first\n", File.ReadAllText(fixture.At("02-a.md")));
        // The selection followed its article through the plan, not its old name.
        Assert.Equal(BookPaths.Standardize(fixture.At("02-a.md")), model.Selection);
    }

    /// <summary>
    /// §8.2: the staging names are hidden <c>.md-reorder-*</c> temporaries. None may survive the
    /// operation, and none may ever reach the listing — a leaked one becomes a chapter called
    /// ".md-reorder-{Guid}" that the writer cannot explain.
    /// </summary>
    [Fact]
    public void AReorderLeavesNoStagingNamesBehindAndNoneInTheListing()
    {
        using var fixture = new BookFixture();
        fixture.Article("01-a.md");
        fixture.Article("02-a.md");
        fixture.Article("03-c.md");
        var (model, _, _) = Open(fixture);

        model.Move(fixture.At("03-c.md"), -1);

        var everything = Directory.EnumerateFileSystemEntries(fixture.Root).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain(everything, name => name!.StartsWith(".md-reorder-", StringComparison.Ordinal));
        Assert.Equal(3, everything.Count);
        Assert.DoesNotContain(BookSidebarModel.Rows(model.Book), row => row.Title.StartsWith('.'));
    }

    // MARK: The case-only rename (book.md §16.4)

    /// <summary>
    /// <c>Directory.Move</c> refuses a case-only change on Windows and accepts it on APFS, so Core
    /// routes it through the reorder's staging name. From the navigator that has to look like any
    /// other rename: the folder is renamed and the article inside it stays selected.
    /// </summary>
    [Fact]
    public void ACaseOnlyChapterRenameLandsAndCarriesTheSelectedArticle()
    {
        using var fixture = new BookFixture();
        fixture.Article("02-one/01-a.md", "# a\n");
        var (model, _, _) = Open(fixture);
        Assert.Equal("01-a.md", Path.GetFileName(model.Selection));

        model.PerformRename(fixture.At("02-one"), "One");

        Assert.Equal(["02-One"], fixture.Names());
        Assert.Equal("02-One", new DirectoryInfo(fixture.At("02-One")).Name);
        Assert.Equal(BookPaths.Standardize(fixture.At("02-One", "01-a.md")), model.Selection);
        Assert.Equal("# a\n", File.ReadAllText(fixture.At("02-One", "01-a.md")));
    }
}
