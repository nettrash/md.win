using Md.App.Logic.Seams;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Md.App.Services;

/// <summary>
/// <see cref="IPickers"/> over the WinRT pickers (§6.1). Desktop apps must tell a picker which
/// window owns it — <c>InitializeWithWindow</c> with the window's HWND — or it throws; there is no
/// "current view" to fall back on.
///
/// Deliberately the WinRT <c>Windows.Storage.Pickers</c> classes, not the newer
/// <c>Microsoft.Windows.Storage.Pickers</c>: the WinRT save picker <em>creates</em> the file it
/// returns, which is what the session's write-in-place path expects, and the file it hands back
/// carries the consent the sandbox rules need. The caller must delete that empty file when its own
/// write then fails, so a cancelled save leaves nothing behind (§6.1).
/// </summary>
internal sealed class Pickers : IPickers
{
    readonly Window window;
    readonly IAlerts? alerts;

    /// <param name="alerts">
    /// Optional. A <c>FolderPicker</c> has no prompt of its own, so when a caller passes a title
    /// (§7.7's "Choose where to keep the TextBundle") it is shown as an alert first.
    /// </param>
    /// <summary>
    /// The window's thread. A picker is initialised with an HWND and shown by the shell on the
    /// thread that owns it, and <c>ExportPipeline</c> reaches Save As from a thread-pool
    /// continuation — see <see cref="UiDispatch"/>.
    /// </summary>
    readonly Microsoft.UI.Dispatching.DispatcherQueue ui;

    public Pickers(Window window, IAlerts? alerts = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        this.window = window;
        this.alerts = alerts;
        ui = window.DispatcherQueue;
    }

    public Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        return UiDispatch.OnAsync(ui, () => OpenFilesCoreAsync(extensions));
    }

    async Task<IReadOnlyList<string>> OpenFilesCoreAsync(IReadOnlyList<string> extensions)
    {
        var picker = new FileOpenPicker
        {
            ViewMode = PickerViewMode.List,
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        foreach (var extension in extensions) picker.FileTypeFilter.Add(extension);
        // An empty filter list makes PickMultipleFilesAsync throw; "*" is the documented "everything".
        if (picker.FileTypeFilter.Count == 0) picker.FileTypeFilter.Add("*");
        Attach(picker);

        var files = await picker.PickMultipleFilesAsync();
        if (files is null) return [];
        var paths = new List<string>(files.Count);
        foreach (var file in files)
        {
            if (!string.IsNullOrEmpty(file.Path)) paths.Add(file.Path);
        }
        return paths;
    }

    public Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension)
    {
        ArgumentNullException.ThrowIfNull(suggestedName);
        ArgumentNullException.ThrowIfNull(choices);
        return UiDispatch.OnAsync(ui, () => SaveFileCoreAsync(suggestedName, choices, defaultExtension));
    }

    async Task<string?> SaveFileCoreAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
            DefaultFileExtension = defaultExtension,
        };
        foreach (var (label, extensions) in choices) picker.FileTypeChoices.Add(label, extensions.ToList());
        Attach(picker);

        var file = await picker.PickSaveFileAsync();
        return string.IsNullOrEmpty(file?.Path) ? null : file.Path;
    }

    public Task<string?> PickFolderAsync(string? title) => UiDispatch.OnAsync(ui, () => PickFolderCoreAsync(title));

    async Task<string?> PickFolderCoreAsync(string? title)
    {
        if (title is { Length: > 0 } && alerts is not null) await alerts.WarnAsync(title, "");
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        // Documented: on desktop a FolderPicker with no filter throws when it is shown.
        picker.FileTypeFilter.Add("*");
        Attach(picker);

        var folder = await picker.PickSingleFolderAsync();
        return string.IsNullOrEmpty(folder?.Path) ? null : folder.Path;
    }

    void Attach(object picker) =>
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
}
