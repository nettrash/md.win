using Md.App.Logic.Settings;

namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="ISettingsStore"/> for tests: <see cref="InMemorySettingsStore"/>'s semantics plus a
/// log of every write and removal, so a test can assert "Zen never touched md.viewModeMemory".
/// </summary>
public sealed class FakeSettingsStore : ISettingsStore
{
    readonly InMemorySettingsStore _inner = new();

    public FakeSettingsStore() => _inner.Changed += key => Changed(key);

    public List<(string Key, object Value)> Writes { get; } = [];
    public List<string> Removals { get; } = [];
    public IReadOnlyCollection<string> Keys => _inner.Keys;

    public event Action<string> Changed = delegate { };

    public string? GetString(string key) => _inner.GetString(key);

    public void SetString(string key, string value)
    {
        Writes.Add((key, value));
        _inner.SetString(key, value);
    }

    public bool GetBool(string key, bool fallback) => _inner.GetBool(key, fallback);

    public void SetBool(string key, bool value)
    {
        Writes.Add((key, value));
        _inner.SetBool(key, value);
    }

    public void Remove(string key)
    {
        Removals.Add(key);
        _inner.Remove(key);
    }
}
