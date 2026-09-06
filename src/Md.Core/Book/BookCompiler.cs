namespace Md.Core.Book;

/// <summary>One already-read piece of the book, in reading order (Swift <c>BookLibrary.Part</c>).</summary>
public abstract record BookPart
{
    private BookPart() { }

    /// <summary>A chapter boundary — becomes a heading on its own page.</summary>
    public sealed record Chapter(string Name) : BookPart;

    /// <summary>An article's Markdown source, verbatim.</summary>
    public sealed record Article(string Text) : BookPart;
}

/// <summary>
/// The whole book as one Markdown source. Port of <c>BookLibrary.compile</c> (md.macOS
/// BookNavigator.swift; Kotlin <c>compileBook</c> builds the same units): a title page,
/// then every part on a page of its own — <c>\newpage</c> between them, the marker the
/// export pipeline's parser turns into a page break. Chapter headings get their own
/// page before their articles; article text is untouched (a <c>---</c> inside stays an
/// ordinary rule, an author's own <c>\newpage</c> passes through). Pure — strings in,
/// string out; reading the files is the app's job (<c>compileBookSource</c>).
/// </summary>
public static class BookCompiler
{
    /// <summary>Exactly <c>"\n\n\\newpage\n\n"</c> in Swift source: LF LF backslash newpage LF LF.</summary>
    public const string PageSeparator = "\n\n\\newpage\n\n";

    public static string Compile(string bookName, IReadOnlyList<BookPart> parts)
    {
        var sections = new List<string>(parts.Count + 1) { "# " + bookName };
        foreach (var part in parts)
        {
            sections.Add(part switch
            {
                BookPart.Chapter chapter => "# " + chapter.Name,
                BookPart.Article article => article.Text,
                _ => throw new ArgumentException("Unknown book part", nameof(parts)),
            });
        }
        return string.Join(PageSeparator, sections);
    }

    // MARK: Reading the whole book (Swift's compileBookSource / readStructuredBook)

    /// <summary>
    /// The book's compile title: the display name of the root folder, prefix stripped —
    /// not <see cref="Book.Name"/>, which is the raw folder name the window shows. It also
    /// names the exported PDF and EPUB.
    /// </summary>
    public static string Title(Book book) => BookNaming.DisplayName(book.Name);

    /// <summary>
    /// Read every article in reading order and compile the book (see <see cref="Compile"/>).
    /// <paramref name="readArticle"/> takes an article path and returns its Markdown, or
    /// null when it cannot be read — one unreadable article aborts the whole compile, as
    /// on the Mac, and <paramref name="unreadable"/> then names it
    /// (<see cref="BookArticle.Name"/>, ordering prefix kept) for the alert.
    /// <see cref="BookFolder.ReadArticle"/> is the reader the app passes.
    /// </summary>
    public static string? CompileBookSource(Book book, Func<string, string?> readArticle, out string? unreadable)
    {
        unreadable = null;
        var parts = new List<BookPart>();
        foreach (var article in book.Articles)
        {
            var text = readArticle(article.Path);
            if (text is null) { unreadable = article.Name; return null; }
            parts.Add(new BookPart.Article(text));
        }
        foreach (var chapter in book.Chapters)
        {
            parts.Add(new BookPart.Chapter(BookNaming.DisplayName(chapter.Name)));
            foreach (var article in chapter.Articles)
            {
                var text = readArticle(article.Path);
                if (text is null) { unreadable = article.Name; return null; }
                parts.Add(new BookPart.Article(text));
            }
        }
        return Compile(Title(book), parts);
    }

    /// <summary>Same, for callers that only need to know it failed.</summary>
    public static string? CompileBookSource(Book book, Func<string, string?> readArticle) =>
        CompileBookSource(book, readArticle, out _);

    /// <summary>
    /// Read the book in the same reading order as the compile, but with the chapter and
    /// article boundaries kept rather than flattened: the EPUB gives each one its own file
    /// and the LaTeX turns them into <c>\chapter</c> and <c>\section</c>. Same abort rule
    /// as <see cref="CompileBookSource"/> — one unreadable article and the export stops,
    /// named in <paramref name="unreadable"/>.
    /// </summary>
    public static StructuredBook? ReadStructuredBook(Book book, Func<string, string?> readArticle, out string? unreadable)
    {
        unreadable = null;
        var front = new List<BookUnit>();
        foreach (var article in book.Articles)
        {
            var text = readArticle(article.Path);
            if (text is null) { unreadable = article.Name; return null; }
            front.Add(new BookUnit(BookNaming.DisplayName(BookPaths.Name(article.Path)), text));
        }
        var sections = new List<BookSection>();
        foreach (var chapter in book.Chapters)
        {
            var units = new List<BookUnit>();
            foreach (var article in chapter.Articles)
            {
                var text = readArticle(article.Path);
                if (text is null) { unreadable = article.Name; return null; }
                units.Add(new BookUnit(BookNaming.DisplayName(BookPaths.Name(article.Path)), text));
            }
            sections.Add(new BookSection(BookNaming.DisplayName(chapter.Name), units));
        }
        return new StructuredBook(Title(book), front, sections);
    }

    /// <summary>Same, for callers that only need to know it failed.</summary>
    public static StructuredBook? ReadStructuredBook(Book book, Func<string, string?> readArticle) =>
        ReadStructuredBook(book, readArticle, out _);
}
