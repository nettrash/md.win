namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IDocumentRegistry"/> over dictionaries: canonical path → window (OrdinalIgnoreCase,
/// one path per window), plus the title list the Window menu shows. Untitled windows are added
/// with <see cref="AddWindow"/>; <see cref="Register"/> gives a window its path (and a default
/// title, the file's stem) — <see cref="SetTitle"/> changes it. <see cref="Changed"/> fires on
/// every mutation; <see cref="ChangedCount"/> counts them.
/// </summary>
public sealed class FakeDocumentRegistry : IDocumentRegistry
{
    readonly Dictionary<string, Guid> _byPath = new(StringComparer.OrdinalIgnoreCase);
    readonly List<Guid> _order = [];
    readonly Dictionary<Guid, string> _titles = [];

    public event Action Changed = delegate { };
    public int ChangedCount { get; private set; }

    public IReadOnlyList<(Guid Id, string Title)> Windows => _order.Select(id => (id, _titles[id])).ToList();

    public Guid? Owning(string canonicalPath) => _byPath.TryGetValue(canonicalPath, out var id) ? id : null;

    /// <summary>The path a window currently owns, or null.</summary>
    public string? PathOf(Guid windowId) => _byPath.FirstOrDefault(kv => kv.Value == windowId).Key;

    public void AddWindow(Guid windowId, string title)
    {
        if (!_order.Contains(windowId)) _order.Add(windowId);
        _titles[windowId] = title;
        Raise();
    }

    public void Register(Guid windowId, string canonicalPath)
    {
        foreach (var old in _byPath.Where(kv => kv.Value == windowId).Select(kv => kv.Key).ToList()) _byPath.Remove(old);
        _byPath[canonicalPath] = windowId;
        if (!_order.Contains(windowId)) _order.Add(windowId);
        _titles.TryAdd(windowId, Path.GetFileNameWithoutExtension(canonicalPath));
        Raise();
    }

    public void Unregister(Guid windowId)
    {
        foreach (var old in _byPath.Where(kv => kv.Value == windowId).Select(kv => kv.Key).ToList()) _byPath.Remove(old);
        _order.Remove(windowId);
        _titles.Remove(windowId);
        Raise();
    }

    public void SetTitle(Guid windowId, string title)
    {
        if (!_titles.ContainsKey(windowId)) throw new KeyNotFoundException($"window {windowId} is not registered");
        _titles[windowId] = title;
        Raise();
    }

    void Raise()
    {
        ChangedCount++;
        Changed();
    }
}
