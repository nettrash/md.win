using Md.App.Logic.Settings;
using Md.Core.Document;

namespace Md.App.Logic.View;

/// <summary>
/// Core's <see cref="IViewModeStore"/> over the app-wide <see cref="ISettingsStore"/>: the one
/// <c>md.viewModeMemory</c> string (shell-design.md §5.2, §9). Nothing else in the app reads or
/// writes that key — <c>ViewModeMemory</c> owns the codec and this owns the location.
/// </summary>
public sealed class SettingsViewModeStore(ISettingsStore settings) : IViewModeStore
{
    /// <summary>Null when absent; the codec reads that as "nothing remembered".</summary>
    public string? Load() => settings.GetString(ViewModeMemory.SettingsKey);

    public void Save(string value) => settings.SetString(ViewModeMemory.SettingsKey, value);
}
