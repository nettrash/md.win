// Real keystrokes for the in-app self-test (shell-design.md §11.4): SendInput into the foreground
// window, so what reaches the editor is exactly what a keyboard delivers — PreviewKeyDown, the
// accelerators, KeyDown, BeforeTextChanging, TextChanging, the control's own undo grouping — and not
// a programmatic SelectedText that skips half of them. #if SELFTEST only; a Store build never carries
// input injection.
#if SELFTEST
using System.Runtime.InteropServices;

namespace Md.App.Interop;

internal static unsafe partial class SelfTestInput
{
    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    public const ushort VK_BACK = 0x08;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_RETURN = 0x0D;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_F3 = 0x72;

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // MOUSEINPUT is the union's largest member; it is declared only so INPUT has the size Windows
    // checks cbSize against (40 bytes on x64 and ARM64).
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint SendInput(uint cInputs, INPUT* pInputs, int cbSize);

    [LibraryImport("user32.dll")]
    internal static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial short VkKeyScanW(ushort ch);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static partial int GetWindowText(nint hWnd, char* text, int maxCount);

    /// <summary>
    /// The window to the front, for the keystrokes. SetForegroundWindow is refused unless our
    /// process received the last input or was started by the foreground process — and a GUI
    /// process started from a PowerShell prompt was started by powershell.exe, which owns no window;
    /// the terminal that does is another process. So the first real run (2026-09-26) never got in
    /// front and typed nothing. The fallback is the long-standing workaround: attach this thread's
    /// input queue to the foreground window's for the one call, so the request counts as that
    /// window's own. Self-test only; nothing in a Store build does this.
    /// </summary>
    public static bool ForceForeground(nint hwnd)
    {
        if (NativeMethods.SetForegroundWindow(hwnd) && GetForegroundWindow() == hwnd) return true;

        var front = GetForegroundWindow();
        if (front == 0 || front == hwnd) return front == hwnd;
        var theirs = GetWindowThreadProcessId(front, out _);
        var ours = GetCurrentThreadId();
        if (theirs == 0 || theirs == ours || !AttachThreadInput(ours, theirs, true)) return false;
        try
        {
            BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
        }
        finally
        {
            AttachThreadInput(ours, theirs, false);
        }
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>A window for a failure detail — handle, title, owning process: what was in front instead of md.</summary>
    public static string Describe(nint hwnd)
    {
        if (hwnd == 0) return "no foreground window";
        var buffer = stackalloc char[256];
        var length = GetWindowText(hwnd, buffer, 256);
        var title = new string(buffer, 0, Math.Max(0, length));
        GetWindowThreadProcessId(hwnd, out var pid);
        string process;
        try
        {
            process = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName;
        }
        catch (Exception)
        {
            process = "?";
        }
        return $"hwnd {hwnd} \"{title}\" ({process}, pid {pid})";
    }

    /// <summary>
    /// <paramref name="text"/> as a keyboard sends it: for every scalar the active layout can type,
    /// its virtual key and scan code, with Shift held for a shifted one — WM_KEYDOWN, WM_CHAR,
    /// WM_KEYUP, the road a letter takes. The first real run (2026-09-26) sent every letter as a
    /// Unicode packet (VK_PACKET) instead, and the box takes those through the text-services path,
    /// where they arrive as a composition — which the typing hooks, rightly, never judge: the typed
    /// text came out right and not one keystroke was capitalized. A scalar the layout cannot type
    /// (an emoji, a letter of another script) is still sent as a packet, one per UTF-16 unit as
    /// Windows expects.
    /// </summary>
    public static bool Text(string text)
    {
        var inputs = new List<INPUT>(text.Length * 4);
        foreach (var rune in text.EnumerateRunes())
        {
            var scan = rune.IsBmp ? VkKeyScanW((ushort)rune.Value) : (short)-1;
            if (scan != -1 && (scan & 0x0600) == 0)          // typable, and without Ctrl or Alt
            {
                var virtualKey = (ushort)(scan & 0xFF);
                var scanCode = (ushort)MapVirtualKeyW(virtualKey, 0);   // MAPVK_VK_TO_VSC
                var shifted = (scan & 0x0100) != 0;
                if (shifted) inputs.Add(Key(VK_SHIFT, 0, 0));
                inputs.Add(Key(virtualKey, scanCode, 0));
                inputs.Add(Key(virtualKey, scanCode, KEYEVENTF_KEYUP));
                if (shifted) inputs.Add(Key(VK_SHIFT, 0, KEYEVENTF_KEYUP));
                continue;
            }
            foreach (var unit in rune.ToString())
            {
                inputs.Add(Key(0, unit, KEYEVENTF_UNICODE));
                inputs.Add(Key(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
            }
        }
        return Send(inputs);
    }

    /// <summary>One virtual key, pressed and released, with Ctrl held around it when asked.</summary>
    public static bool Key(ushort virtualKey, bool control = false)
    {
        var inputs = new List<INPUT>(4);
        if (control) inputs.Add(Key(VK_CONTROL, 0, 0));
        inputs.Add(Key(virtualKey, 0, 0));
        inputs.Add(Key(virtualKey, 0, KEYEVENTF_KEYUP));
        if (control) inputs.Add(Key(VK_CONTROL, 0, KEYEVENTF_KEYUP));
        return Send(inputs);
    }

    static INPUT Key(ushort virtualKey, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = virtualKey, wScan = scan, dwFlags = flags } },
    };

    static bool Send(List<INPUT> inputs)
    {
        var array = inputs.ToArray();
        fixed (INPUT* first = array)
        {
            // All or nothing: a partial send (UIPI blocked it, the desktop is locked) is reported, not retried.
            return SendInput((uint)array.Length, first, sizeof(INPUT)) == (uint)array.Length;
        }
    }
}
#endif
