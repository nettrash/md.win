// Which folder is the book, and how the app is allowed to touch it (shell-design.md §8.2). The Mac
// mints a security-scoped bookmark and balances every access; Windows has no scope to start and
// stop — a FutureAccessList grant is permanent once given — so beginAccess/endAccess collapse to
// "resolve the token to a folder". The rules that survive the collapse are in Md.App.Logic.Books;
// this file is the WinRT half: the picker, the token, and the copy that unpacks the example book.
using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Services;
using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Md.App.Book;

/// <summary>
/// The book grant. One fixed FutureAccessList token (<c>md.book</c>, <see cref="SettingsKeys.BookAccessToken"/>)
/// keeps the list at one entry of its thousand, and <c>md.bookBookmark</c> keeps the folder path in
/// readable form beside it — the token is the permission, the path is what a support answer and
/// PRIVACY.md can name.
///
/// <see cref="ResolveAsync"/> prefers what the token resolves to, because a book folder that the
/// writer moved or renamed still resolves through the grant while the stored path no longer points
/// anywhere. A token that will not resolve falls back to the stored path if it is still there, and
/// only then gives up — and even then the setting is kept, which is the Mac's behaviour when
/// <c>beginAccess</c> returns nil: the book is not forgotten just because the drive is unplugged.
/// </summary>
internal sealed class BookLibraryHost(ISettingsStore settings, IPickers pickers, IAlerts alerts)
{
    readonly BookLibraryModel model = new(settings);

    /// <summary>The stored folder path, or null. Non-empty means "a book is open" for the Book menu's enablement.</summary>
    public string? BookPath => model.BookPath;

    public bool HasBook => model.HasBook;

    /// <summary>
    /// The folder the window should list, or null when there is no book or it cannot be reached.
    /// </summary>
    public async Task<string?> ResolveAsync()
    {
        var stored = model.BookPath;
        if (stored is null) return null;
        try
        {
            if (StorageApplicationPermissions.FutureAccessList.ContainsItem(SettingsKeys.BookAccessToken))
            {
                var folder = await StorageApplicationPermissions.FutureAccessList.GetFolderAsync(SettingsKeys.BookAccessToken);
                if (!string.IsNullOrEmpty(folder.Path))
                {
                    // The grant is authoritative: re-file the readable copy when the folder moved.
                    if (!string.Equals(folder.Path, stored, StringComparison.OrdinalIgnoreCase)) model.Store(folder.Path);
                    return folder.Path;
                }
            }
        }
        catch (Exception e) when (IsStorageFailure(e))
        {
            // Fall through to the stored path — a token can go stale where the folder has not.
        }
        return Directory.Exists(stored) ? stored : null;
    }

    /// <summary>Book ▸ Open Book… — pick a folder, take the grant, remember it. True when a book was opened.</summary>
    public async Task<bool> ChooseBookAsync()
    {
        var picked = await pickers.PickFolderAsync(Strings.Books.ChooseBookFolder);
        return picked is not null && await StoreAsync(picked);
    }

    /// <summary>
    /// Book ▸ New Book… — the Mac's save panel becomes a folder picker for the parent plus a name
    /// prompt, because <c>FileSavePicker</c> cannot create a folder and <c>FolderPicker</c> cannot
    /// ask for a name. An existing folder is not adopted silently: Foundation's
    /// <c>withIntermediateDirectories: false</c> fails on one, and so does this.
    /// </summary>
    public async Task<bool> NewBookAsync()
    {
        var parent = await pickers.PickFolderAsync(Strings.Books.NewBookMessage);
        if (parent is null) return false;
        var typed = await alerts.PromptNameAsync(
            Strings.Books.NewBookTitle,
            Strings.Books.NewBookMessage,
            Strings.Books.NewBookDefaultName,
            Strings.Buttons.Create);
        if (typed is null) return false;
        var name = Md.Core.Book.BookFolder.TrimmedPromptName(typed);
        if (name.Length == 0) return false;
        // CreateChapter is "make exactly one directory, refusing an existing one and vetting the
        // name" — which is what a new book is. It answers bool, so the two causes are told apart
        // here rather than inventing a message the file system never gave.
        if (!Md.Core.Book.BookFolder.CreateChapter(parent, name))
        {
            var message = Md.Core.Book.BookFolder.IsValidName(name) && Directory.Exists(Path.Combine(parent, name))
                ? Strings.Exports.FolderExists(name)
                : Strings.Documents.InvalidNameMessage;
            await alerts.WarnAsync(Strings.Books.CouldNotCreateBook, message);
            return false;
        }
        return await StoreAsync(Path.Combine(parent, name));
    }

    /// <summary>
    /// File ▸ Examples ▸ Example Book… — copy the bundled book somewhere the writer can edit it. A
    /// folder picker has no Replace step, so the name is deduped ("Example Book 2", …) rather than
    /// writing over a book that is already there; a copy that fails half way is removed.
    /// </summary>
    public async Task<bool> UnpackExampleBookAsync()
    {
        var parent = await pickers.PickFolderAsync(Strings.Books.ChooseExampleBookFolder);
        if (parent is null) return false;

        var source = ExampleLibrary.BookFolderPath;
        var baseName = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
        var name = BookLibraryModel.DedupedName(baseName, candidate => Directory.Exists(Path.Combine(parent, candidate)) || File.Exists(Path.Combine(parent, candidate)));
        if (name is null)
        {
            await alerts.WarnAsync(Strings.Books.CouldNotUnpackExampleBook, Strings.Exports.FolderExists(baseName));
            return false;
        }

        var destination = Path.Combine(parent, name);
        try
        {
            CopyFolder(source, destination);
        }
        catch (Exception e) when (IsStorageFailure(e))
        {
            try { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
            catch (Exception) { /* the alert names the copy failure, not the cleanup */ }
            await alerts.WarnAsync(Strings.Books.CouldNotUnpackExampleBook, e.Message);
            return false;
        }
        return await StoreAsync(destination);
    }

    /// <summary>Book ▸ Close Book — drop the grant and the path. The folder itself is untouched.</summary>
    public void CloseBook()
    {
        try
        {
            StorageApplicationPermissions.FutureAccessList.Remove(SettingsKeys.BookAccessToken);
        }
        catch (Exception e) when (IsStorageFailure(e))
        {
            // A token that is already gone is the state we wanted.
        }
        model.CloseBook();
    }

    /// <summary>Take the permanent grant for <paramref name="path"/> and remember it as the open book.</summary>
    public async Task<bool> StoreAsync(string path)
    {
        try
        {
            var folder = await StorageFolder.GetFolderFromPathAsync(path);
            StorageApplicationPermissions.FutureAccessList.AddOrReplace(SettingsKeys.BookAccessToken, folder);
        }
        catch (Exception e) when (IsStorageFailure(e))
        {
            // No grant: an unpackaged run has no FutureAccessList at all, and the path alone still
            // works there. Recording the path is what makes the book open either way.
        }
        model.Store(path);
        return true;
    }

    static void CopyFolder(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            CopyFolder(folder, Path.Combine(destination, Path.GetFileName(Path.TrimEndingDirectorySeparator(folder))));
        }
    }

    static bool IsStorageFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException or InvalidOperationException;
}
