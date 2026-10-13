// The two layout facts HeadingChordGuard decides on (docs/formatting.md §5): does the current
// keyboard layout type a character for Ctrl+Alt+<key> — AltGr+2 is ² on German, @ on Polish — and
// which Alt is physically down. Asked on the UI thread, from the accelerator's Invoked, so
// GetKeyboardLayout(0) is the layout the writer is typing with.
namespace Md.App.Interop;

internal static class KeyboardLayout
{
    const int VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LCONTROL = 0xA2, VK_LMENU = 0xA4, VK_RMENU = 0xA5;

    /// <summary>True when Ctrl+Alt (that is, AltGr) with <paramref name="virtualKey"/> types a character or starts a dead key.</summary>
    public static bool TypesCharacterWithCtrlAlt(int virtualKey)
    {
        var layout = NativeMethods.GetKeyboardLayout(0);
        var scanCode = NativeMethods.MapVirtualKeyEx((uint)virtualKey, NativeMethods.MAPVK_VK_TO_VSC, layout);
        var state = new byte[256];
        state[VK_CONTROL] = state[VK_MENU] = state[VK_LCONTROL] = state[VK_RMENU] = 0x80;
        var buffer = new ushort[8];
        var count = NativeMethods.ToUnicodeEx((uint)virtualKey, scanCode, state, buffer, buffer.Length, NativeMethods.TOUNICODE_NO_STATE_CHANGE, layout);
        if (count < 0) return true;                      // a dead key: AltGr is composing a character
        return count > 0 && buffer[0] >= 0x20;           // a control character is not something typed
    }

    public static bool RightAltDown => (NativeMethods.GetKeyState(VK_RMENU) & 0x8000) != 0;

    public static bool LeftAltDown => (NativeMethods.GetKeyState(VK_LMENU) & 0x8000) != 0;
}
