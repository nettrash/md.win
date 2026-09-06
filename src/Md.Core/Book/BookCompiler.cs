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
}
