// Compile to PDF, Print Book, Export Book as EPUB / LaTeX (shell-design.md §8.7, book.md §8).
// Every one of them reads the book off disk, so every one of them first asks the in-place editor to
// save — through Md.Core.Book.BookFlushGate, which is synchronous for exactly that reason.
using Md.App.Logic.Settings;
using Md.Core.Book;
using Md.Core.Document;

namespace Md.App.Logic.Books;

/// <summary>
/// The app's export half, as this package needs it. WP6's <c>Export.ExportPipeline</c> is the
/// implementation (see <c>Md.App.Book.BookExportOutputs</c>); the interface exists so the book
/// window can be built and tested without it, and so nothing here has to know about pickers,
/// renderers or the print overlay. Every book render is light (<c>dark: false</c>) — a compiled book
/// is paper, whatever the window it was started from looks like.
/// </summary>
public interface IBookOutputs
{
    /// <summary>Share ▸ Share as PDF — render <paramref name="source"/> at <paramref name="pageSize"/> and hand the file to the Share sheet.</summary>
    Task SharePdfAsync(string source, string title, PageSize pageSize);

    /// <summary>Share ▸ Export as PDF… — the same render, saved through the picker.</summary>
    Task ExportPdfAsync(string source, string title, PageSize pageSize);

    /// <summary>Share ▸ Print… — always A4, the page size setting is not consulted (core-api.md A4 §4 step 5).</summary>
    Task PrintAsync(string source, string title);

    /// <summary>Share ▸ Export as EPUB… — the structured book, one file per article.</summary>
    Task ExportEpubAsync(StructuredBook book);

    /// <summary>Share ▸ Export as LaTeX… — the structured book as one .tex.</summary>
    Task ExportLaTeXAsync(StructuredBook book);
}

/// <summary>
/// Holds a subscription to <see cref="BookFlushGate"/> for as long as a book editor exists: every
/// output posts a request before it reads the folder, and this answers it by flushing. A failed
/// flush sets <c>Vetoed</c> and the output abandons itself silently — the footer already says why,
/// and a second alert on top of it would be noise. No book window this launch means no subscriber,
/// which leaves the gate open, which is exactly right: there is nothing unsaved.
/// </summary>
/// <remarks>
/// <see cref="BookFlushGate.Requested"/> is a process-wide static event, so a test that touches it
/// must not run beside another that does (Core keeps its own suite in one collection for this).
/// </remarks>
public sealed class BookFlushGateBinding : IDisposable
{
    readonly Func<bool> flush;
    bool disposed;

    public BookFlushGateBinding(Func<bool> flush)
    {
        ArgumentNullException.ThrowIfNull(flush);
        this.flush = flush;
        BookFlushGate.Requested += OnRequested;
    }

    /// <summary>How many requests this binding has answered — for the window's own diagnostics and the tests.</summary>
    public int Requests { get; private set; }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BookFlushGate.Requested -= OnRequested;
    }

    void OnRequested(BookFlushGate gate)
    {
        Requests++;
        if (!flush()) gate.Vetoed = true;
    }
}

/// <summary>
/// The four whole-book outputs. Each one: flush the editor (abandon on a veto), read the book, then
/// hand the result to <see cref="IBookOutputs"/>. Reading is where a book fails — no book open, the
/// folder gone, one article unreadable — and each failure is a modal alert with the Mac's wording,
/// never a half-finished file.
/// </summary>
public sealed class BookOutput
{
    readonly IBookOutputs outputs;
    readonly ISettingsStore settings;
    readonly Func<Book?> book;
    readonly Func<string, string?> readArticle;

    /// <param name="book">The current listing — re-read at the moment of the output, never cached from window construction.</param>
    /// <param name="readArticle">
    /// How an article's Markdown is read. The default is <see cref="BookFolder.ReadArticle"/>, which
    /// decodes through <c>PlainTextCodec</c>: the recorded divergence from the Mac, whose compile
    /// path decodes UTF-8-then-Latin-1 and turns a CP1251 article that edits correctly into mojibake.
    /// </param>
    public BookOutput(
        IBookOutputs outputs,
        ISettingsStore settings,
        Func<Book?> book,
        Func<string, string?>? readArticle = null)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(book);
        this.outputs = outputs;
        this.settings = settings;
        this.book = book;
        this.readArticle = readArticle ?? (path => BookFolder.ReadArticle(path));
    }

    /// <summary>(title, message) for the modal alert.</summary>
    public event Action<string, string>? AlertRequested;

    /// <summary>The trim size the two PDF outputs use — <c>md.pdfPageSize</c>, shared with the document window's share menu. Unknown ids read as A4.</summary>
    public PageSize PageSize => Md.Core.Document.PageSize.Named(settings.GetString(SettingsKeys.PdfPageSize));

    public Task SharePdfAsync() =>
        Compile() is { } compiled ? outputs.SharePdfAsync(compiled.Source, compiled.Title, PageSize) : Task.CompletedTask;

    public Task ExportPdfAsync() =>
        Compile() is { } compiled ? outputs.ExportPdfAsync(compiled.Source, compiled.Title, PageSize) : Task.CompletedTask;

    public Task PrintAsync() =>
        Compile() is { } compiled ? outputs.PrintAsync(compiled.Source, compiled.Title) : Task.CompletedTask;

    public Task ExportEpubAsync() =>
        Structured(Strings.Exports.CouldNotExportEpub) is { } structured ? outputs.ExportEpubAsync(structured) : Task.CompletedTask;

    public Task ExportLaTeXAsync() =>
        Structured(Strings.Exports.CouldNotExportLaTeX) is { } structured ? outputs.ExportLaTeXAsync(structured) : Task.CompletedTask;

    /// <summary>
    /// The whole book as one Markdown source — a title page, then every chapter heading and article
    /// on a page of its own. Null when the gate vetoed (silently) or the read failed (with an alert).
    /// </summary>
    (string Title, string Source)? Compile()
    {
        if (Ready() is not { } current) return null;
        var source = BookCompiler.CompileBookSource(current, readArticle, out var unreadable);
        if (source is null)
        {
            Alert(Strings.Books.CouldNotCompileBook, Strings.Books.ArticleUnreadable(unreadable ?? ""));
            return null;
        }
        return (BookCompiler.Title(current), source);
    }

    /// <summary>The book with its chapter and article boundaries kept — what EPUB and LaTeX need. <paramref name="failureTitle"/> is the caller's alert title, as on the Mac.</summary>
    StructuredBook? Structured(string failureTitle)
    {
        if (Ready(failureTitle) is not { } current) return null;
        var structured = BookCompiler.ReadStructuredBook(current, readArticle, out var unreadable);
        if (structured is null) Alert(failureTitle, Strings.Books.ArticleUnreadable(unreadable ?? ""));
        return structured;
    }

    // The gate first, then the listing: a veto is silent, an inaccessible book is an alert.
    Book? Ready(string? failureTitle = null)
    {
        if (!BookFlushGate.FlushEditor()) return null;
        var current = book();
        if (current is null) Alert(failureTitle ?? Strings.Books.CouldNotCompileBook, Strings.Books.NoBookAccessible);
        return current;
    }

    void Alert(string title, string message) => AlertRequested?.Invoke(title, message);
}
