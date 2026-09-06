// Previous / Next Article (shell-design.md §8.4-§8.5, book.md §13.5): walking the whole book in
// reading order, root articles first, then each chapter's. Pure — a snapshot of the order and where
// the selection sits in it; the caller applies the answer as a selection change.
using Md.Core.Book;

namespace Md.App.Logic.Books;

/// <summary>
/// The two toolbar chevrons and Ctrl+Alt+Up / Ctrl+Alt+Down, as a value. Nothing is selected yet
/// (the empty pane, or a book just opened) is a real state and not a dead end: Next enters the book
/// from the front and Previous from the back, which is what makes the chevrons live on an empty
/// selection. While "Open Articles in Separate Windows" is on there is no persistent selection to
/// step from, so both flags go false — Swift returns an empty stepper there rather than stepping a
/// selection the sidebar deliberately clears.
/// </summary>
/// <param name="Order">Every article path in reading order.</param>
/// <param name="Index">Where the selection sits in <paramref name="Order"/>, or null when nothing in the order is selected.</param>
public sealed record BookStepper(IReadOnlyList<string> Order, int? Index)
{
    /// <summary>No book, or articles that open in their own windows: both directions dead.</summary>
    public static readonly BookStepper Disabled = new([], null);

    /// <summary>The stepper for a book snapshot and the current selection.</summary>
    public static BookStepper For(Book? book, string? selection, bool opensInSeparateWindows)
    {
        if (book is null || opensInSeparateWindows) return Disabled;
        var articles = BookModel.ReadingOrder(book);
        var order = articles.Select(article => article.Path).ToList();
        return new BookStepper(order, BookFolder.OrderIndex(articles, selection));
    }

    /// <summary>Swift's <c>index.map { $0 &gt; 0 } ?? !order.isEmpty</c>: at the front there is nowhere back, with no selection there is the back of the book.</summary>
    public bool CanPrevious => Index is { } index ? index > 0 : Order.Count > 0;

    public bool CanNext => Index is { } index ? index < Order.Count - 1 : Order.Count > 0;

    /// <summary>The article to select, or null when the step is not possible (the caller then does nothing at all).</summary>
    public string? Previous() => Step(-1);

    public string? Next() => Step(1);

    string? Step(int delta)
    {
        if (Order.Count == 0) return null;
        // Entering from the end the step comes from: Next starts at the first article, Previous at
        // the last — so one keypress with nothing selected puts the writer somewhere sensible.
        if (Index is not { } index) return delta > 0 ? Order[0] : Order[^1];
        var target = index + delta;
        return target >= 0 && target < Order.Count ? Order[target] : null;
    }
}
