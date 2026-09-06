using Md.App.Logic.Seams;

namespace Md.App.Logic.Windows;

/// <summary>
/// Who owns which file, and what the Window menu lists (shell-design.md §8.6). Two maps, one
/// object: canonical path → window id (<c>OrdinalIgnoreCase</c>, at most one path per window), and
/// the ordered rows of every live window with its title.
///
/// The ownership half is <see cref="IDocumentRegistry"/>, which <c>TextFileSession</c> drives: a
/// document window's session registers the file it edits and unregisters when it lets go, and the
/// book's detail pane asks <see cref="Owning"/> before it writes a byte. The rows half is the
/// window manager's: a window joins with <see cref="Add"/> when it opens and leaves with
/// <see cref="Remove"/> when it closes.
///
/// Those two halves are deliberately separate, and that is the one place this differs from the
/// test fake: <see cref="Unregister"/> drops the <em>ownership</em> only. A session detaching from
/// its file is not a window closing — an untitled window has never owned a path and must still
/// appear in the Window menu — so a row disappears when, and only when, its window does.
/// </summary>
public sealed class WindowRegistry : IDocumentRegistry
{
    readonly Dictionary<string, Guid> _byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Guid> _order = [];
    readonly Dictionary<Guid, string> _titles = [];

    /// <summary>Raised when the rows or the ownership actually changed — never for a write of the value already there, so a menu rebuild cannot loop.</summary>
    public event Action Changed = delegate { };

    /// <summary>Every live window in creation order, for the Window menu (§2.8).</summary>
    public IReadOnlyList<(Guid Id, string Title)> Windows
    {
        get
        {
            var rows = new List<(Guid, string)>(_order.Count);
            foreach (var id in _order) rows.Add((id, _titles[id]));
            return rows;
        }
    }

    /// <summary>The window editing this canonical path, or null. Compared <c>OrdinalIgnoreCase</c>, as the seam specifies.</summary>
    public Guid? Owning(string canonicalPath) =>
        canonicalPath is not null && _byPath.TryGetValue(canonicalPath, out var id) ? id : null;

    /// <summary>
    /// The same question from a path that has not been canonicalised yet — the activation router's
    /// "is this file already open?" (§1.2). Canonicalising is what makes <c>C:\Docs\A.md</c> and
    /// <c>c:\docs\a.md</c>, or a junction and its target, one file.
    /// </summary>
    public Guid? FindByPath(string path, IFileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return string.IsNullOrEmpty(path) ? null : Owning(identity.Canonical(path));
    }

    /// <summary>The path a window owns, or null. For the window manager's own bookkeeping and for tests.</summary>
    public string? PathOf(Guid windowId)
    {
        foreach (var (path, id) in _byPath)
        {
            if (id == windowId) return path;
        }
        return null;
    }

    /// <summary>A window opened: it joins the Window menu at once, before it has any file.</summary>
    public void Add(Guid windowId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        var changed = false;
        if (!_titles.ContainsKey(windowId))
        {
            _order.Add(windowId);
            changed = true;
        }
        if (!_titles.TryGetValue(windowId, out var old) || !string.Equals(old, title, StringComparison.Ordinal))
        {
            _titles[windowId] = title;
            changed = true;
        }
        if (changed) Changed();
    }

    /// <summary>The window's title moved (a save, a rename, an edit that added "— Edited"). Ignored for a window that is not open.</summary>
    public void SetTitle(Guid windowId, string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        if (!_titles.TryGetValue(windowId, out var old) || string.Equals(old, title, StringComparison.Ordinal)) return;
        _titles[windowId] = title;
        Changed();
    }

    /// <summary>The window closed: its row and any ownership go together.</summary>
    public void Remove(Guid windowId)
    {
        var changed = DropOwnership(windowId);
        if (_titles.Remove(windowId))
        {
            _order.Remove(windowId);
            changed = true;
        }
        if (changed) Changed();
    }

    /// <summary>
    /// This window now edits this file (<see cref="IDocumentRegistry"/>). One path per window: a
    /// Save As moves the ownership rather than adding a second entry. A window that registers before
    /// the manager added its row gets one, so the ordering is still creation order.
    ///
    /// Known limitation, pinned by <c>WindowRegistryTests</c>: the dictionary is
    /// <c>OrdinalIgnoreCase</c> and .NET keeps the key it already holds, so after a CASE-ONLY rename
    /// (<c>a.md</c> → <c>A.md</c>) the stored key keeps the old spelling. <see cref="Owning"/> and
    /// <see cref="FindByPath"/> are unaffected — they compare ignoring case — but
    /// <see cref="PathOf"/> then reports the previous spelling. Nothing in Md.App reads
    /// <see cref="PathOf"/> today; anyone who starts to must canonicalise again rather than trust it.
    /// </summary>
    public void Register(Guid windowId, string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(canonicalPath);
        var changed = false;
        if (!_titles.ContainsKey(windowId))
        {
            _order.Add(windowId);
            _titles[windowId] = "";
            changed = true;
        }
        changed |= DropOwnership(windowId, keep: canonicalPath);
        if (!_byPath.TryGetValue(canonicalPath, out var current) || current != windowId)
        {
            _byPath[canonicalPath] = windowId;
            changed = true;
        }
        if (changed) Changed();
    }

    /// <summary>
    /// This window no longer edits a file — a detach, a hand-off, a close. The window's row stays:
    /// closing is <see cref="Remove"/>.
    /// </summary>
    public void Unregister(Guid windowId)
    {
        if (DropOwnership(windowId)) Changed();
    }

    /// <summary>Drop every path this window owns except <paramref name="keep"/>; true when anything went.</summary>
    bool DropOwnership(Guid windowId, string? keep = null)
    {
        List<string>? stale = null;
        foreach (var (path, id) in _byPath)
        {
            if (id != windowId) continue;
            if (keep is not null && string.Equals(path, keep, StringComparison.OrdinalIgnoreCase)) continue;
            (stale ??= []).Add(path);
        }
        if (stale is null) return false;
        foreach (var path in stale) _byPath.Remove(path);
        return true;
    }
}
