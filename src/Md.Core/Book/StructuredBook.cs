namespace Md.Core.Book;

/// <summary>
/// One article read for a whole-book export: its display name and its Markdown. Swift's
/// <c>EPUBBook.Article { name, markdown }</c>, Kotlin's <c>ArticleContent(title, source)</c>.
/// </summary>
public sealed record BookUnit(string Title, string Source);

/// <summary>One chapter with its articles in reading order. Swift's <c>EPUBBook.Chapter { name, articles }</c>.</summary>
public sealed record BookSection(string Title, IReadOnlyList<BookUnit> Units);

/// <summary>
/// A book read with its structure kept — the input the EPUB and LaTeX exports share, as opposed
/// to the one flat Markdown source the PDF compile stitches. Swift's <c>EPUBBook</c>.
/// </summary>
/// <remarks>
/// Reading order is <see cref="FrontUnits"/> (the root articles, the front matter before any
/// chapter) first, then each <see cref="BookSection"/> in order with its units in order — the same
/// order the PDF compile and the EPUB spine use. Titles are display names: the book library has
/// already stripped the <c>01-</c> ordering prefixes and the <c>.md</c> extension.
/// </remarks>
public sealed record StructuredBook(
    string Title,
    IReadOnlyList<BookUnit> FrontUnits,
    IReadOnlyList<BookSection> Sections);
