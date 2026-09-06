using Md.App.Logic.Settings;
using Windows.Storage;

namespace Md.App.Services;

/// <summary>
/// <see cref="ISettingsStore"/> over <c>ApplicationData.Current.LocalSettings.Values</c> (shell-final.md §9).
/// Strings and bools only; a value of the other type reads as absent, as in the in-memory store.
/// Each value is capped at 8 KB by the platform — the view-mode memory stays under it by design
/// (≤ 200 entries); per-window state never comes here. <see cref="Changed"/> fires only when the
/// stored value actually changes.
/// </summary>
internal sealed class LocalSettingsStore : ISettingsStore
{
    readonly IDictionary<string, object> _values = ApplicationData.Current.LocalSettings.Values;

    public event Action<string> Changed = delegate { };

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
