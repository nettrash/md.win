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

    /// <summary>Every UTF-16 unit of <paramref name="text"/> as a Unicode key press and release — a surrogate pair is two units, as Windows expects.</summary>
    public static bool Text(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var unit in text)
        {
            inputs.Add(Key(0, unit, KEYEVENTF_UNICODE));
            inputs.Add(Key(0, unit, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
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
