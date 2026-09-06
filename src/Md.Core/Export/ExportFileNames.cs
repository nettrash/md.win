using System.Text;
using Md.Core.Text;

namespace Md.Core.Export;

/// <summary>
/// The name every export suggests in its save picker, from the document's title.
/// </summary>
/// <remarks>
/// <para>
/// Two layers, deliberately separable. <see cref="PortableStem"/> is the family rule the other
/// three ports share (macOS <c>DocumentExport.sanitized</c>, Android <c>Exporter.sanitized</c>,
/// md.vscode <c>sanitized</c>): split on <c>/ \ : ? % * | " &lt; &gt;</c>, join with <c>-</c>, trim,
/// and fall back to <c>Document</c> when nothing is left. It is a <i>split-join</i>, so a run of two
/// offending characters becomes <b>two</b> dashes, not one — pinned by every port.
/// </para>
/// <para>
/// <see cref="Sanitized"/> adds what Windows refuses on top, which is this port's own layer and
/// exists nowhere else in the family: control characters U+0000–U+001F (illegal in a Win32 file
/// name — the family set covers every other character Windows forbids), a trailing dot or space (a
/// name may not end in either; the family trim already takes every trailing space off the stem, so
/// in practice this catches a stem of dots and a hand-written extension), and the
/// reserved device names <c>CON PRN AUX NUL COM1–COM9 LPT1–LPT9</c>, which are reserved with any
/// extension and are matched on the part before the first dot, ASCII-case-insensitively. A reserved
/// stem gains a <c>-</c> before its first dot — the sanitiser's own escape character, and enough to
/// make the name ordinary again. Not handled here: component length (255) and total path length,
/// which only the caller, holding the folder, can judge.
/// </para>
/// </remarks>
public static class ExportFileNames
{
    /// <summary>The name every port falls back to when a title sanitises away to nothing.</summary>
    public const string Fallback = "Document";

    /// <summary>The characters the family replaces, ASCII, in the order the Swift spells them.</summary>
    private static readonly char[] Forbidden = ['/', '\\', ':', '?', '%', '*', '|', '"', '<', '>'];

    /// <summary>
    /// The device names Windows reserves. Reserved with any extension (<c>CON.svg</c> is still the
    /// console), so the match is on the stem's first dot-separated part. <c>COM10</c> and up are
    /// ordinary names.
    /// </summary>
    private static readonly string[] ReservedDevices =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// The family's file-name stem, byte-for-byte with macOS, Android and md.vscode: no extension,
    /// no Windows rules. Use it to compare this port against the others; use
    /// <see cref="Sanitized"/> for a name a Windows picker will actually take.
    /// </summary>
    public static string PortableStem(string title)
    {
        var cleaned = Cleaned(title);
        return cleaned.Length == 0 ? Fallback : cleaned;
    }

    /// <summary>
    /// A file name a Windows save picker will take: <see cref="PortableStem"/> plus the Windows
    /// rules, then <c>.</c> and <paramref name="extension"/> (given with or without its leading
    /// dot; empty for none).
    /// </summary>
    public static string Sanitized(string title, string extension)
    {
        var stem = ForWindows(Cleaned(title));
        if (stem.Length == 0) stem = Fallback;
        var suffix = Extension(extension);
        return suffix.Length == 0 ? stem : stem + "." + suffix;
    }

    /// <summary>
    /// The family rule, without the fallback: split on the forbidden set, join with <c>-</c>, trim
    /// the whitespace-and-newlines set. <c>Whitespace.TrimWSNL</c>, not <c>string.Trim()</c> — the
    /// two differ on U+200B, which Foundation's frozen tables still call a space separator, so a
    /// title padded with zero-width spaces trims here exactly as it does on macOS.
    /// </summary>
    private static string Cleaned(string title) =>
        Whitespace.TrimWSNL(string.Join("-", title.Split(Forbidden)));

    /// <summary>The Windows-only layer: control characters, trailing dots and spaces, device names.</summary>
    private static string ForWindows(string stem)
    {
        var replaced = ReplacingControls(stem);
        var trimmed = DroppingTrailingDotsAndSpaces(replaced);
        var cut = trimmed.IndexOf('.');
        if (cut < 0) cut = trimmed.Length;
        return IsReservedDevice(trimmed[..cut]) ? trimmed[..cut] + "-" + trimmed[cut..] : trimmed;
    }

    /// <summary>
    /// The extension for <see cref="Sanitized"/>: the same character rule, any leading dots dropped
    /// (so <c>svg</c> and <c>.svg</c> both mean <c>.svg</c>), then trailing dots and spaces dropped
    /// so the finished name can never end in one.
    /// </summary>
    private static string Extension(string extension)
    {
        var cleaned = ReplacingControls(string.Join("-", extension.Split(Forbidden)));
        var start = 0;
        while (start < cleaned.Length && cleaned[start] == '.') start++;
        return DroppingTrailingDotsAndSpaces(cleaned[start..]);
    }

    /// <summary>
    /// Control characters are illegal in a Win32 file name; they become the same <c>-</c> the
    /// family's own forbidden set becomes. A line break in a title is the realistic case — the
    /// family rule leaves one alone, because no other platform minds.
    /// </summary>
    private static string ReplacingControls(string text)
    {
        var found = false;
        foreach (var c in text)
        {
            if (c >= ' ') continue;
            found = true;
            break;
        }
        if (!found) return text;
        var builder = new StringBuilder(text.Length);
        foreach (var c in text) builder.Append(c < ' ' ? '-' : c);
        return builder.ToString();
    }

    private static string DroppingTrailingDotsAndSpaces(string text)
    {
        var end = text.Length;
        while (end > 0 && (text[end - 1] == '.' || text[end - 1] == ' ')) end--;
        return end == text.Length ? text : text[..end];
    }

    /// <summary>
    /// ASCII-folded comparison against the device list. An ASCII fold, not
    /// <c>ToUpperInvariant</c>: the names are ASCII, and folding the whole string would drag in
    /// every locale-independent case mapping for no gain.
    /// </summary>
    private static bool IsReservedDevice(string stem)
    {
        if (stem.Length is < 3 or > 4) return false;
        Span<char> folded = stackalloc char[stem.Length];
        for (var i = 0; i < stem.Length; i++)
        {
            var c = stem[i];
            folded[i] = c is >= 'a' and <= 'z' ? (char)(c - 32) : c;
        }
        foreach (var device in ReservedDevices)
        {
            if (folded.SequenceEqual(device.AsSpan())) return true;
        }
        return false;
    }
}
