namespace Md.App.Logic.Settings;

/// <summary>
/// <c>md.wrapSelection</c> (docs/formatting.md §4): typing <c>* _ ` $</c> over a selection wraps it.
/// On by default; Edit ▸ Typing ▸ Wrap Selection When Typing * _ ` $ flips it, live in every window
/// through <see cref="ISettingsStore.Changed"/>, like the two SmartTyping switches beside it.
/// Kept apart from <see cref="TypingSettings"/>, which is SmartTyping's own pair.
/// </summary>
public static class WrapSelectionSetting
{
    /// <summary>The current value; an absent key (or one of the wrong type) reads as on.</summary>
    public static bool Read(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.GetBool(SettingsKeys.WrapSelection, SettingsKeys.WrapSelectionDefault);
    }

    /// <summary>Whether a <see cref="ISettingsStore.Changed"/> key is this one.</summary>
    public static bool IsKey(string key) => key == SettingsKeys.WrapSelection;

    /// <summary>Flip the stored value; absent counts as on, so the first click turns it off.</summary>
    public static void Toggle(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.SetBool(SettingsKeys.WrapSelection, !Read(settings));
    }
}
