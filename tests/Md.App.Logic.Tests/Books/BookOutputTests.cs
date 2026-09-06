using System.Text;
using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Settings;
using Md.Core.Book;
using Md.Core.Document;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// The four whole-book outputs and the gate in front of them (§8.7). The gate is a process-wide
/// static event, so this class runs alone.
/// </summary>
[CollectionDefinition("BookFlushGate", DisableParallelization = true)]
public class BookFlushGateCollection;

/// <summary>Records what the export half was asked for; the pipeline itself is WP6's.</summary>
sealed class FakeBookOutputs : IBookOutputs
{
    public List<(string Source, string Title, string PageSize)> Pdfs { get; } = [];
    public List<(string Source, string Title, string PageSize)> Shares { get; } = [];
    public List<(string Source, string Title)> Prints { get; } = [];
    public List<StructuredBook> Epubs { get; } = [];
    public List<StructuredBook> LaTeX { get; } = [];

    public Task SharePdfAsync(string source, string title, PageSize pageSize)
    {
        Shares.Add((source, title, pageSize.Id));
        return Task.CompletedTask;
    }

    public Task ExportPdfAsync(string source, string title, PageSize pageSize)
    {
        Pdfs.Add((source, title, pageSize.Id));
        return Task.CompletedTask;
    }

    public Task PrintAsync(string source, string title)
    {
        Prints.Add((source, title));
        return Task.CompletedTask;
    }

    public Task ExportEpubAsync(StructuredBook book)
    {
        Epubs.Add(book);
        return Task.CompletedTask;
    }

    public Task ExportLaTeXAsync(StructuredBook book)
    {
        LaTeX.Add(book);
        return Task.CompletedTask;
    }
}

[Collection("BookFlushGate")]
public class BookOutputTests
{
    static BookFixture ThreeArticles()
    {
        var fixture = new BookFixture("out-" + Guid.NewGuid().ToString("N"));
        fixture.Article("01-Front.md", "Front matter.");
        fixture.Article("02-One/01-a.md", "First.");
        fixture.Article("02-One/02-b.md", "Second.");
        return fixture;
    }

    static BookOutput Output(BookFixture fixture, FakeBookOutputs outputs, ISettingsStore settings, List<(string, string)>? alerts = null)
    {
        var output = new BookOutput(outputs, settings, () => BookModel.Load(fixture.Root));
        if (alerts is not null) output.AlertRequested += (title, message) => alerts.Add((title, message));
        return output;
    }

    [Fact]
    public async Task ThePdfIsTheWholeBookOnePagePerPartAtTheStoredTrimSize()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.PdfPageSize, "5x8");

        await Output(fixture, outputs, settings).ExportPdfAsync();

        var (source, title, pageSize) = outputs.Pdfs.Single();
        Assert.Equal(BookNaming.DisplayName(Path.GetFileName(fixture.Root)), title);
        Assert.Equal("5x8", pageSize);
        Assert.StartsWith("# " + title + BookCompiler.PageSeparator, source, StringComparison.Ordinal);
        Assert.Contains(BookCompiler.PageSeparator + "# One" + BookCompiler.PageSeparator, source, StringComparison.Ordinal);
        Assert.EndsWith("Second.", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnknownTrimSizeIsA4()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        var settings = new FakeSettingsStore();
        settings.SetString(SettingsKeys.PdfPageSize, "tabloid");

        await Output(fixture, outputs, settings).SharePdfAsync();

        Assert.Equal("a4", outputs.Shares.Single().PageSize);
    }

    [Fact]
    public async Task PrintingTakesNoPageSizeAtAll()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();

        await Output(fixture, outputs, new FakeSettingsStore()).PrintAsync();

        Assert.Single(outputs.Prints);
    }

    [Fact]
    public async Task EpubAndLaTeXKeepTheChapterBoundariesTheCompileFlattens()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        var output = Output(fixture, outputs, new FakeSettingsStore());

        await output.ExportEpubAsync();
        await output.ExportLaTeXAsync();

        var book = outputs.Epubs.Single();
        Assert.Equal(["Front"], book.FrontUnits.Select(unit => unit.Title).ToList());
        Assert.Equal("One", book.Sections.Single().Title);
        Assert.Equal(["a", "b"], book.Sections.Single().Units.Select(unit => unit.Title).ToList());
        Assert.Equal(["First.", "Second."], book.Sections.Single().Units.Select(unit => unit.Source).ToList());
        // The same structure reaches LaTeX (a record's lists compare by reference, so compare the parts).
        var latex = outputs.LaTeX.Single();
        Assert.Equal(book.Title, latex.Title);
        Assert.Equal(book.FrontUnits, latex.FrontUnits);
        Assert.Equal(book.Sections.Single().Units, latex.Sections.Single().Units);
    }

    [Fact]
    public async Task WithNoBookOpenEveryOutputSaysSoAndProducesNothing()
    {
        var outputs = new FakeBookOutputs();
        var alerts = new List<(string, string)>();
        var output = new BookOutput(outputs, new FakeSettingsStore(), () => null);
        output.AlertRequested += (title, message) => alerts.Add((title, message));

        await output.ExportPdfAsync();
        await output.ExportEpubAsync();

        Assert.Empty(outputs.Pdfs);
        Assert.Empty(outputs.Epubs);
        Assert.Equal(
        [
            (Strings.Books.CouldNotCompileBook, Strings.Books.NoBookAccessible),
            (Strings.Exports.CouldNotExportEpub, Strings.Books.NoBookAccessible),
        ], alerts);
    }

    [Fact]
    public async Task OneUnreadableArticleAbortsTheWholeBookAndNamesIt()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        var alerts = new List<(string, string)>();
        var output = new BookOutput(
            outputs,
            new FakeSettingsStore(),
            () => BookModel.Load(fixture.Root),
            path => Path.GetFileName(path) == "02-b.md" ? null : "text");
        output.AlertRequested += (title, message) => alerts.Add((title, message));

        await output.ExportPdfAsync();
        await output.ExportLaTeXAsync();

        Assert.Empty(outputs.Pdfs);
        Assert.Empty(outputs.LaTeX);
        Assert.Equal(
        [
            (Strings.Books.CouldNotCompileBook, Strings.Books.ArticleUnreadable("02-b")),
            (Strings.Exports.CouldNotExportLaTeX, Strings.Books.ArticleUnreadable("02-b")),
        ], alerts);
    }

    [Fact]
    public async Task ALegacyEncodedArticleCompilesAsWhatItSaysNotAsMojibake()
    {
        using var fixture = ThreeArticles();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        File.WriteAllBytes(fixture.At("01-Front.md"), Encoding.GetEncoding(1251).GetBytes("Привет, мир!"));
        var outputs = new FakeBookOutputs();

        await Output(fixture, outputs, new FakeSettingsStore()).ExportPdfAsync();

        Assert.Contains("Привет, мир!", outputs.Pdfs.Single().Source, StringComparison.Ordinal);
    }

    // MARK: The flush gate

    [Fact]
    public async Task EveryOutputAsksTheEditorToSaveFirst()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        using var binding = new BookFlushGateBinding(() => true);

        var output = Output(fixture, outputs, new FakeSettingsStore());
        await output.ExportPdfAsync();
        await output.SharePdfAsync();
        await output.PrintAsync();
        await output.ExportEpubAsync();
        await output.ExportLaTeXAsync();

        Assert.Equal(5, binding.Requests);
    }

    [Fact]
    public async Task AFailedSaveVetoesTheOutputSilently()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();
        var alerts = new List<(string, string)>();
        using var binding = new BookFlushGateBinding(() => false);

        await Output(fixture, outputs, new FakeSettingsStore(), alerts).ExportPdfAsync();
        await Output(fixture, outputs, new FakeSettingsStore(), alerts).ExportEpubAsync();

        Assert.Empty(outputs.Pdfs);
        Assert.Empty(outputs.Epubs);
        Assert.Empty(alerts);                    // the book window's footer already says why
    }

    [Fact]
    public async Task NoBookWindowMeansNoSubscriberAndAnOpenGate()
    {
        using var fixture = ThreeArticles();
        var outputs = new FakeBookOutputs();

        // A binding that vetoes, disposed before the output runs: exactly a book window that closed.
        new BookFlushGateBinding(() => false).Dispose();
        await Output(fixture, outputs, new FakeSettingsStore()).ExportPdfAsync();

        Assert.Single(outputs.Pdfs);
    }

    [Fact]
    public void TheGateIsAnsweredSynchronouslySoTheCompileReadsTheSavedBytes()
    {
        var order = new List<string>();
        using var binding = new BookFlushGateBinding(() =>
        {
            order.Add("flush");
            return true;
        });

        order.Add("before");
        Assert.True(BookFlushGate.FlushEditor());
        order.Add("after");

        Assert.Equal(["before", "flush", "after"], order);
    }
}
