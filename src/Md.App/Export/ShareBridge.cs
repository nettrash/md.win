// The Windows Share sheet (shell-design.md §7.8). DataTransferManager.GetForCurrentView() throws in
// a desktop app — there is no CoreWindow to get it for — so the manager is obtained per window
// handle through IDataTransferManagerInterop, which is the documented desktop path.
using Md.App.Interop;
using Md.App.Logic.Seams;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.Storage;

namespace Md.App.Export;

/// <summary>
/// Shares one file that already exists. Every caller renders or writes <em>first</em> and shares
/// afterwards: a <c>DataRequested</c> deferral that waited on a slow PlantUML page would time out and
/// the sheet would offer nothing.
/// </summary>
internal sealed class ShareBridge(Window window) : IShare
{
    // The manager and the handler have to outlive ShareFileAsync: the sheet opens asynchronously and
    // asks for the data afterwards, so a collected manager is an empty Share sheet.
    DataTransferManager? _manager;
    TypedEventHandler<DataTransferManager, DataRequestedEventArgs>? _handler;

    public async Task ShareFileAsync(string path, string title)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var file = await StorageFile.GetFileFromPathAsync(path);
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);

        var interop = DataTransferManager.As<IDataTransferManagerInterop>();
        var iid = NativeMethods.DataTransferManagerIid;
        var manager = WinRT.MarshalInterface<DataTransferManager>.FromAbi(interop.GetForWindow(handle, ref iid));

        Detach();
        _manager = manager;
        _handler = (sender, e) =>
        {
            e.Request.Data.Properties.Title = title;
            e.Request.Data.SetStorageItems([file]);
        };
        manager.DataRequested += _handler;

        interop.ShowShareUIForWindow(handle);
    }

    void Detach()
    {
        if (_manager is not null && _handler is not null) _manager.DataRequested -= _handler;
        _manager = null;
        _handler = null;
    }
}
