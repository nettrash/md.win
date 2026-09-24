namespace Md.App.Logic.Settings;

/// <summary>
/// The two SmartTyping preferences (docs/smart-typing.md §3.1) as the editor and the menus read
/// them: <c>md.continueLists</c> and <c>md.capitalizeSentences</c>, both bool, both default true,
/// both the same key on every md port. A function whose setting is off is never called
/// (<c>EditorPane</c> checks the bool before it asks <c>SmartTyping</c>), and a toggle takes effect
/// in every open editor at once through <see cref="ISettingsStore.Changed"/>.
/// </summary>
public readonly record struct TypingSettings(bool ContinueLists, bool CapitalizeSentences)
{
    /// <summary>Both on — what a fresh install and a store with neither key reads as.</summary>
    public static readonly TypingSettings Defaults = new(SettingsKeys.ContinueListsDefault, SettingsKeys.CapitalizeSentencesDefault);

    /// <summary>The current values; an absent key (or one of the wrong type) reads as its default.</summary>
    public static TypingSettings Read(ISettingsStore settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new TypingSettings(
            settings.GetBool(SettingsKeys.ContinueLists, SettingsKeys.ContinueListsDefault),
            settings.GetBool(SettingsKeys.CapitalizeSentences, SettingsKeys.CapitalizeSentencesDefault));
    }

    /// <summary>Whether a <see cref="ISettingsStore.Changed"/> key is one of the two — what the editor and the menus re-read on.</summary>
    public static bool IsKey(string key) => key is SettingsKeys.ContinueLists or SettingsKeys.CapitalizeSentences;

    /// <summary>
    /// The Edit menu's two toggles: flip the stored value (absent counts as the default, so the
    /// first click on a fresh install turns the feature <i>off</i>). Anything but the two keys throws
    /// — a menu row wired to the wrong key is a build-time bug, not a silent write.
    /// </summary>
    public static void Toggle(ISettingsStore settings, string key)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!IsKey(key)) throw new ArgumentOutOfRangeException(nameof(key), key, "Not a typing setting");
        var fallback = key == SettingsKeys.ContinueLists ? SettingsKeys.ContinueListsDefault : SettingsKeys.CapitalizeSentencesDefault;
        settings.SetBool(key, !settings.GetBool(key, fallback));
    }
}
