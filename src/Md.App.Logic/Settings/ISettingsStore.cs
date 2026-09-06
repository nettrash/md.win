namespace Md.App.Logic.Settings;

/// <summary>
/// The app-wide key/value store (§9): <c>ApplicationData.Current.LocalSettings.Values</c> in the app
/// (<c>LocalSettingsStore</c>), a dictionary in Logic and tests (<see cref="InMemorySettingsStore"/>).
/// Keys are the constants in <see cref="SettingsKeys"/>; values are strings and bools only — the
/// per-value limit is 8 KB, and per-window state never goes here (it lives in session.json).
/// <see cref="Changed"/> is raised with the key when a write actually changes the stored value, so
/// a menu that re-writes the value it just observed cannot loop.
/// </summary>
public interface ISettingsStore
{
    /// <summary>Null when absent or not a string.</summary>
    string? GetString(string key);

    void SetString(string key, string value);

    /// <summary>The fallback when absent or not a bool.</summary>
    bool GetBool(string key, bool fallback);

    void SetBool(string key, bool value);

    void Remove(string key);

    event Action<string> Changed;
}
