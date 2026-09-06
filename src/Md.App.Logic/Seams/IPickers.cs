namespace Md.App.Logic.Seams;

/// <summary>
/// The three system pickers, reduced to paths (§6.1). App: <c>Pickers</c> over
/// <c>Windows.Storage.Pickers</c> initialised with the window handle; tests: <c>FakePickers</c>.
/// Extensions carry the dot (".md").
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IPickers
{
    /// <summary>Multi-select open; empty when cancelled.</summary>
    Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions);

    /// <summary>
    /// Save picker; the choices are the labelled type groups in order ("Markdown Document" → the five
    /// Markdown extensions, …). Null when cancelled. The WinRT picker returns a created, empty file —
    /// the caller writes over it and deletes it on a failed write.
    /// </summary>
    Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension);

    /// <summary>Folder picker; null when cancelled. The title is shown as a message first (the picker has none of its own).</summary>
    Task<string?> PickFolderAsync(string? title);
}
