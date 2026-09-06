namespace Md.App.Logic.Seams;

/// <summary>The answer to "Do you want to save the changes…?" (§6.3 close policy).</summary>
public enum CloseChoice
{
    Save,
    DontSave,
    Cancel,
}

/// <summary>
/// The dialogs the logic needs answered. App: <c>WinUiAlerts</c> — <c>ContentDialog</c>s with
/// <c>XamlRoot</c> set, one at a time per window (§7.9); tests: <c>FakeAlerts</c> with scripted
/// answers. Titles and messages come from <c>Strings</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IAlerts
{
    /// <summary>Title + message + OK.</summary>
    Task WarnAsync(string title, string message);

    /// <summary>A name prompt (Rename, New Chapter, New Article, New Book); null when cancelled.</summary>
    Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel);

    /// <summary>Delete / Cancel with Cancel as the default button.</summary>
    Task<bool> ConfirmDeleteAsync(string title, string message);

    /// <summary>Save / Don't Save / Cancel.</summary>
    Task<CloseChoice> AskSaveChangesAsync(string title);

    /// <summary>Replace / Cancel (the save panel's Replace, for the TextBundle folder).</summary>
    Task<bool> ConfirmReplaceAsync(string message);
}
