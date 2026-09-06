namespace Md.App.Logic.Settings;

/// <summary>
/// <see cref="ISettingsStore"/> over a dictionary — the Logic-side store for tests and for any
/// code path that runs before the WinRT settings exist. Same semantics as <c>LocalSettingsStore</c>:
/// a string read of a bool (or the reverse) is "absent"; <see cref="Changed"/> fires only when the
/// stored value differs from what was there.
/// </summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    readonly Dictionary<string, object> _values = new(StringComparer.Ordinal);

    public event Action<string> Changed = delegate { };

    /// <summary>The keys currently stored, for tests and for PRIVACY's "exactly these" check.</summary>
    public IReadOnlyCollection<string> Keys => _values.Keys;

    public string? GetString(string key) => _values.TryGetValue(key, out var v) && v is string s ? s : null;

    public void SetString(string key, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        Set(key, value);
    }

    public bool GetBool(string key, bool fallback) => _values.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    public void SetBool(string key, bool value) => Set(key, value);

    public void Remove(string key)
    {
        if (_values.Remove(key)) Changed(key);
    }

    void Set(string key, object value)
    {
        if (_values.TryGetValue(key, out var old) && Equals(old, value)) return;
        _values[key] = value;
        Changed(key);
    }
}
