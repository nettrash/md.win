// The testable half of BookLibraryHost (shell-design.md §8.2): which folder is the book, and the
// one naming rule the example-book unpack needs. The grant itself — the FutureAccessList token —
// is WinRT and stays in Md.App.Book.BookLibraryHost.
using Md.App.Logic.Settings;

namespace Md.App.Logic.Books;

/// <summary>
/// The open book as a setting. <c>md.bookBookmark</c> holds the folder <em>path</em> on Windows,
/// not a bookmark blob: the access grant is the FutureAccessList token <c>md.book</c>, and a
/// readable path is what makes a support question answerable and PRIVACY.md honest. A non-empty
/// value means "a book is open"; every window watches the key, so opening or closing a book in one
/// window re-lists the other.
/// </summary>
public sealed class BookLibraryModel(ISettingsStore settings)
{
    /// <summary>The open book's folder path, or null when no book is open.</summary>
    public string? BookPath
    {
        get
        {
            var stored = settings.GetString(SettingsKeys.BookBookmark);
            return string.IsNullOrEmpty(stored) ? null : stored;
        }
    }

    public bool HasBook => BookPath is not null;

    /// <summary>Remember <paramref name="path"/> as the open book. The caller has already taken the grant.</summary>
    public void Store(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        settings.SetString(SettingsKeys.BookBookmark, path);
    }

    /// <summary>Close Book: forget the path (and, in the host, drop the token). The folder is untouched.</summary>
    public void CloseBook() => settings.Remove(SettingsKeys.BookBookmark);

    /// <summary>
    /// The folder name to unpack the example book under: <paramref name="baseName"/>, then
    /// "<c>{baseName} 2</c>", "<c>{baseName} 3</c>", … until one is free. A folder picker cannot ask
    /// "Replace?", so an existing Example Book is never written over — Kotlin does exactly this, and
    /// it is the only honest answer when the panel has no Replace step of its own.
    /// </summary>
    /// <param name="exists">Whether a folder or file of that name is already there.</param>
    /// <param name="maxAttempts">The point at which the caller is asked to pick another folder instead.</param>
    /// <returns>The free name, or null when <paramref name="maxAttempts"/> names were all taken.</returns>
    public static string? DedupedName(string baseName, Func<string, bool> exists, int maxAttempts = 100)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(exists);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var candidate = attempt == 1 ? baseName : $"{baseName} {attempt}";
            if (!exists(candidate)) return candidate;
        }
        return null;
    }
}
