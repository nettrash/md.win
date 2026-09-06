namespace Md.App.Logic.Commands;

/// <summary>
/// The modifier set of a <see cref="Chord"/>. The library's own enum, deliberately not
/// <c>Windows.System.VirtualKeyModifiers</c> (§2.9: no WinRT in Md.App.Logic); the installer maps it
/// one flag at a time — Ctrl → Control, Alt → <b>Menu</b>, Shift → Shift. No chord in md uses Win.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Ctrl = 1,
    Shift = 2,
    Alt = 4,
}

/// <summary>
/// The Win32 virtual-key codes the command table uses, by name. The values are the ones
/// <c>Windows.System.VirtualKey</c> carries (that enum *is* the VK table), so the installer's cast is
/// a cast and not a lookup: letters are their upper-case ASCII, digits their ASCII.
/// </summary>
public static class VirtualKeys
{
    public const int Enter = 0x0D;
    public const int Escape = 0x1B;
    public const int Delete = 0x2E;
    public const int Up = 0x26;
    public const int Down = 0x28;
    public const int Number1 = 0x31;
    public const int Number2 = 0x32;
    public const int Number3 = 0x33;
    public const int A = 0x41;
    public const int B = 0x42;
    public const int C = 0x43;
    public const int E = 0x45;
    public const int F = 0x46;
    public const int N = 0x4E;
    public const int O = 0x4F;
    public const int P = 0x50;
    public const int S = 0x53;
    public const int V = 0x56;
    public const int W = 0x57;
    public const int X = 0x58;
    public const int Y = 0x59;
    public const int Z = 0x5A;
    public const int F1 = 0x70;
    public const int F3 = 0x72;
    public const int F11 = 0x7A;
}

/// <summary>
/// One keyboard chord: a virtual key and its modifiers (§13.2). <see cref="DisplayText"/> is the
/// Windows spelling the menu shows through <c>KeyboardAcceleratorTextOverride</c> — modifiers in the
/// order Ctrl, Alt, Shift ("Ctrl+Shift+Enter", "Ctrl+Alt+Up", "Shift+F3"), then the key name.
/// </summary>
public readonly record struct Chord(int VirtualKey, KeyModifiers Modifiers)
{
    public Chord(int virtualKey) : this(virtualKey, KeyModifiers.None) { }

    /// <summary>"Ctrl+Shift+S". Pure; the only place a chord is ever spelled for a human.</summary>
    public string DisplayText
    {
        get
        {
            var text = new System.Text.StringBuilder();
            if (Modifiers.HasFlag(KeyModifiers.Ctrl)) text.Append("Ctrl+");
            if (Modifiers.HasFlag(KeyModifiers.Alt)) text.Append("Alt+");
            if (Modifiers.HasFlag(KeyModifiers.Shift)) text.Append("Shift+");
            return text.Append(KeyName(VirtualKey)).ToString();
        }
    }

    /// <summary>
    /// The key half of <see cref="DisplayText"/>: the Windows menu spellings — "Del" not "Delete",
    /// "Esc" not "Escape", "Enter", the arrows by name, function keys as written, letters and digits
    /// as themselves. An unlisted code falls back to its hex, which is loud enough to be noticed.
    /// </summary>
    public static string KeyName(int virtualKey) => virtualKey switch
    {
        VirtualKeys.Enter => "Enter",
        VirtualKeys.Escape => "Esc",
        VirtualKeys.Delete => "Del",
        VirtualKeys.Up => "Up",
        VirtualKeys.Down => "Down",
        >= 0x30 and <= 0x39 => ((char)virtualKey).ToString(),
        >= 0x41 and <= 0x5A => ((char)virtualKey).ToString(),
        >= 0x70 and <= 0x87 => "F" + (virtualKey - 0x6F).ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => "0x" + virtualKey.ToString("X2", System.Globalization.CultureInfo.InvariantCulture),
    };

    public override string ToString() => DisplayText;
}
