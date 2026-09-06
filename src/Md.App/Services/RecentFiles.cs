using Windows.Storage;
using Windows.Storage.AccessCache;

namespace Md.App.Services;

/// <summary>
/// File ▸ Open Recent and, for free, Windows' own Recent items and the taskbar Jump List (§6.6).
/// <c>MostRecentlyUsedList</c> with <c>RecentStorageItemVisibility.AppAndSystem</c> is what feeds
/// both: the shell picks the entries up for our associated types, so there is no Jump List to build
/// by hand. The list caps itself at <c>MaximumItemsAllowed</c> (25), dropping the oldest.
///
/// A token is a promise, not a path: the file behind it may have been moved or deleted, so the menu
/// resolves one only when the row is clicked, and a token that no longer resolves is removed and
/// reported (§6.6). Untitled documents and bundle imports are never added — there is no file to
/// come back to.
/// </summary>
internal sealed class RecentFiles
{
    /// <summary>One row of Open Recent. <see cref="Path"/> is the metadata stored with the token, for the label; it is not proof the file is still there.</summary>
    public readonly record struct Entry(string Token, string Path);

    static StorageItemMostRecentlyUsedList List => StorageApplicationPermissions.MostRecentlyUsedList;

    /// <summary>Newest first, as the access cache orders them.</summary>
    public IReadOnlyList<Entry> Entries
    {
        get
        {
            var rows = new List<Entry>();
            try
            {
                foreach (var entry in List.Entries) rows.Add(new Entry(entry.Token, entry.Metadata ?? ""));
            }
            catch (Exception e) when (IsUnavailable(e))
            {
                return [];
            }
            return rows;
        }
    }

    /// <summary>Remember a file the user opened or saved. Best effort: an unpackaged debug run has no access cache, and a missing Open Recent must never break an open.</summary>
    public void Add(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        try
        {
            List.Add(file, file.Path, RecentStorageItemVisibility.AppAndSystem);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
            // no MRU here; the app is otherwise fine
        }
    }

    /// <summary>The same from a path (Save As, activation): the file is looked up first, because the cache stores items, not strings.</summary>
    public async Task AddAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Add(await StorageFile.GetFileFromPathAsync(path));
        }
        catch (Exception e) when (IsUnavailable(e) || e is FileNotFoundException)
        {
            // gone already: nothing to remember
        }
    }

    /// <summary>The file a row still points at, or null when the token no longer resolves — the caller then calls <see cref="Remove"/> and says so.</summary>
    public async Task<StorageFile?> ResolveAsync(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        try
        {
            return await List.GetFileAsync(token);
        }
        catch (Exception e) when (IsUnavailable(e) || e is FileNotFoundException or ArgumentException)
        {
            return null;
        }
    }

    public void Remove(string token)
    {
        try
        {
            List.Remove(token);
        }
        catch (Exception e) when (IsUnavailable(e))
        {
        }
    }

    /// <summary>Open Recent ▸ Clear Menu.</summary>
    public void Clear()
    {
        try
        {
            List.Clear();
        }
        catch (Exception e) when (IsUnavailable(e))
        {
        }
    }

    // The access cache needs package identity and a working profile; without either every call
    // throws, and every one of those is a "there is no Open Recent", never a bug in the caller.
    static bool IsUnavailable(Exception e) =>
        e is InvalidOperationException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or NotSupportedException;
}
