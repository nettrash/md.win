namespace Md.App.Logic.Windows;

/// <summary>
/// What a window is called (shell-design.md §6.7, §1.3). The Mac's title is the document's display
/// name with an "— Edited" proxy while the buffer differs from the last <em>explicit</em> save; an
/// autosave never clears it, only a Save does — which is why the flag the caller passes must be
/// <c>TextFileSession.IsDirty</c> and never <c>HasUnsavedChanges</c>.
/// </summary>
public static class WindowTitle
{
    /// <summary>" — Edited" (U+2014), the Mac's own suffix.</summary>
    public const string EditedSuffix = Strings.EditedSuffix;

    /// <summary>A document window: <c>"{name}"</c> or <c>"{name} — Edited"</c>.</summary>
    public static string For(string displayName, bool isDirty)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return isDirty ? displayName + EditedSuffix : displayName;
    }

    /// <summary>
    /// The Book window (§1.3): the book's folder name, or "Book" when none is open, plus
    /// <c>" — {article}"</c> while an article is being edited. The Book window carries no Edited
    /// proxy — a book article's save is its save (WP2's two-flag note).
    /// </summary>
    public static string ForBook(string? bookName, string? articleTitle)
    {
        var name = string.IsNullOrEmpty(bookName) ? Strings.Book : bookName;
        return string.IsNullOrEmpty(articleTitle) ? name : $"{name} {Strings.EmDash} {articleTitle}";
    }

    /// <summary>"Untitled", "Untitled 2", … — the display name, before the Edited suffix.</summary>
    public static string Untitled(int ordinal) => Strings.UntitledNumbered(ordinal);
}

/// <summary>
/// The per-process numbering of untitled windows (§6.7). The lowest free ordinal is handed out, so
/// closing "Untitled 2" and choosing File ▸ New gives "Untitled 2" again — NSDocumentController's
/// behaviour, and the reason this is a live set rather than a counter.
/// </summary>
public sealed class UntitledNames
{
    readonly HashSet<int> _taken = [];

    /// <summary>The lowest ordinal not in use. 1 is "Untitled".</summary>
    public int Take()
    {
        var ordinal = 1;
        while (!_taken.Add(ordinal)) ordinal++;
        return ordinal;
    }

    /// <summary>The window closed (or was saved and is no longer untitled): its number is free again.</summary>
    public void Release(int ordinal) => _taken.Remove(ordinal);

    /// <summary><see cref="Take"/> and the name in one step.</summary>
    public string TakeName(out int ordinal)
    {
        ordinal = Take();
        return WindowTitle.Untitled(ordinal);
    }
}
