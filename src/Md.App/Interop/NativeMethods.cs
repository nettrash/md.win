// The four Win32 / COM entry points the shell needs (shell-final.md §11.2). LibraryImport, not
// DllImport: the marshalling is generated at compile time (hence AllowUnsafeBlocks in the csproj)
// and there is nothing to resolve at run time.
using System.Runtime.InteropServices;

namespace Md.App.Interop;

internal static partial class NativeMethods
{
    /// <summary>
    /// After a redirected activation the running instance must call this on the target window: a
    /// process that is not the foreground process cannot bring its window forward by Activate()
    /// alone — the file opens but the window stays behind.
    /// </summary>
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetForegroundWindow(nint hWnd);

    /// <summary>The window's DPI (96 = 100 %), read before the window is shown to size its client area in physical pixels (§1.3).</summary>
    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint hWnd);

    internal const uint COWAIT_DEFAULT = 0;

    /// <summary>Contingency for <c>Redirection</c>: a COM-aware wait that keeps pumping while a worker thread redirects the activation.</summary>
    [LibraryImport("ole32.dll")]
    internal static partial int CoWaitForMultipleObjects(uint dwFlags, uint dwTimeout, uint cHandles, nint[] pHandles, out uint lpdwIndex);

    /// <summary>IID of <c>Windows.ApplicationModel.DataTransfer.DataTransferManager</c>, passed to <see cref="IDataTransferManagerInterop.GetForWindow"/>.</summary>
    internal static readonly Guid DataTransferManagerIid = new(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c);
}

/// <summary>
/// The desktop interop for the Share sheet (§7.8): <c>DataTransferManager.GetForCurrentView()</c>
/// throws in a desktop app, so the manager is obtained per window handle through this interface —
/// <c>DataTransferManager.As&lt;IDataTransferManagerInterop&gt;()</c>, <c>GetForWindow(hwnd, ref iid)</c>,
/// <c>WinRT.MarshalInterface&lt;DataTransferManager&gt;.FromAbi(ptr)</c>, then <c>ShowShareUIForWindow(hwnd)</c>.
/// ComImport on purpose: it is what the documented sample uses and what C#/WinRT's <c>As&lt;T&gt;</c>
/// handles; the source-generated alternative needs DisableRuntimeMarshalling on the whole assembly.
/// </summary>
#pragma warning disable SYSLIB1096
[ComImport]
[Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDataTransferManagerInterop
{
    IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid);
    void ShowShareUIForWindow(IntPtr appWindow);
}
#pragma warning restore SYSLIB1096
