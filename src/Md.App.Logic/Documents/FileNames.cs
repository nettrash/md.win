using Md.Core.Book;

namespace Md.App.Logic.Documents;

/// <summary>
/// Windows file-name rules (§6.4) and the path arithmetic that goes with them.
///
/// The path helpers exist because <c>System.IO.Path</c> splits on the <em>host's</em> separators:
/// on the Linux leg of the CI matrix <c>Path.GetFileName(@"C:\Docs\a.md")</c> is the whole string,
/// so every Windows path a test feeds the session would name one file. These split on both
/// separators and rebuild with the one the input already uses, so the same string comes out on
/// every OS — the session's titles, rescue names and asset paths are then testable off Windows.
/// </summary>
public static class FileNames
{
    // A char[], never Split('/', '\\', options): C# would bind that to Split(char, int count,
    // options) — '\\' converts to int 92 — and quietly split on '/' alone, up to 92 times. It
    // compiles, it runs, and every backslash path comes back as one segment.
    static readonly char[] Separators = ['/', '\\'];

    /// <summary>The characters Windows refuses in a name (control characters are refused too).</summary>
    public const string InvalidCharacters = "\\/:*?\"<>|";

    /// <summary>
    /// The device names that are reserved in every directory, with or without an extension
    /// (<c>CON.md</c> is refused as well). Ordinal-ignore-case, as the file system compares them.
    /// </summary>
    public static readonly IReadOnlyList<string> ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    /// <summary>
    /// Whether the Rename dialog may accept <paramref name="name"/>: not empty or blank, none of
    /// <c>\ / : * ? " &lt; &gt; |</c> or a control character, no trailing dot or space (Explorer
    /// silently strips those, so the file would not be the one the writer named), and not a
    /// reserved device name. The message for a rejection is <c>Strings.Documents.InvalidNameMessage</c>.
    /// </summary>
    public static bool Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var c in name)
        {
            if (c < ' ' || c == '\u007F') return false;
            if (InvalidCharacters.Contains(c, StringComparison.Ordinal)) return false;
        }
        if (name[^1] is '.' or ' ') return false;
        return !IsReservedDeviceName(name);
    }

    /// <summary>Reserved with or without an extension: the part before the first dot is what the system looks at.</summary>
    public static bool IsReservedDeviceName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        foreach (var reserved in ReservedDeviceNames)
        {
            if (stem.Equals(reserved, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>
    /// A name safe to hand a picker (§7.9): every invalid character and control becomes <c>-</c>,
    /// trailing dots and spaces go, a reserved device name gains a <c>_</c>, and nothing left means
    /// <c>"Document"</c>. Core's <c>ExportFileNames.Sanitized</c> handles the shared punctuation
    /// rule; this adds only what is Windows'.
    /// </summary>
    public static string ForWindows(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Document";
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] < ' ' || chars[i] == '\u007F' || InvalidCharacters.Contains(chars[i], StringComparison.Ordinal))
                chars[i] = '-';
        }
        var trimmed = new string(chars).TrimEnd('.', ' ');
        if (trimmed.Length == 0) return "Document";
        return IsReservedDeviceName(trimmed) ? "_" + trimmed : trimmed;
    }

    /// <summary>
    /// Rename keeps the extension (§6.4): the new stem with the old file's extension re-attached.
    /// Foundation's split, so <c>".hidden"</c> and <c>"notes."</c> keep their whole name as the stem
    /// and gain no extension — the same rule the rescue-copy name is built from.
    /// </summary>
    public static string WithExtensionOf(string originalFileName, string newStem)
    {
        ArgumentNullException.ThrowIfNull(originalFileName);
        ArgumentNullException.ThrowIfNull(newStem);
        var ext = BookPaths.PathExtension(NameOf(originalFileName));
        return ext.Length == 0 ? newStem : newStem + "." + ext;
    }

    // ---- OS-independent path arithmetic (see the type comment) ----

    /// <summary>The last <c>/</c>- or <c>\</c>-separated component; a trailing separator is not part of a name.</summary>
    public static string NameOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var end = path.Length;
        while (end > 0 && (path[end - 1] == '/' || path[end - 1] == '\\')) end--;
        var cut = end - 1;
        while (cut >= 0 && path[cut] != '/' && path[cut] != '\\') cut--;
        return path[(cut + 1)..end];
    }

    /// <summary>Everything before <see cref="NameOf"/>, without its trailing separator (a bare root keeps one). Empty when the path has no directory part.</summary>
    public static string DirectoryOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var end = path.Length;
        while (end > 0 && (path[end - 1] == '/' || path[end - 1] == '\\')) end--;
        var cut = end - 1;
        while (cut >= 0 && path[cut] != '/' && path[cut] != '\\') cut--;
        if (cut < 0) return "";
        var dir = path[..cut];
        // "C:\a.md" -> "C:\", "\a.md" -> "\", "\\server\share\a.md" -> "\\server\share".
        if (dir.Length == 0 || (dir.Length == 2 && dir[1] == ':')) return path[..(cut + 1)];
        return dir;
    }

    /// <summary>The file's stem, Foundation's way (<c>README</c>, <c>notes.</c>, <c>.hidden</c> keep their whole name).</summary>
    public static string StemOf(string path) => BookPaths.DeletingPathExtension(NameOf(path));

    /// <summary>The extension with its dot, or <c>""</c> — what <c>Path.GetExtension</c> shape the rescue name wants.</summary>
    public static string ExtensionOf(string path)
    {
        var ext = BookPaths.PathExtension(NameOf(path));
        return ext.Length == 0 ? "" : "." + ext;
    }

    /// <summary>Join with the separator the directory already uses (<c>\</c> when it has none).</summary>
    public static string Combine(string directory, string name)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(name);
        if (directory.Length == 0) return name;
        var last = directory[^1];
        if (last == '/' || last == '\\') return directory + name;
        return directory + (directory.Contains('/', StringComparison.Ordinal) && !directory.Contains('\\', StringComparison.Ordinal) ? "/" : "\\") + name;
    }

    /// <summary>Whether a reference is rooted rather than relative: a drive (<c>C:\…</c>, <c>C:/…</c>), a leading separator, or a UNC path.</summary>
    public static bool IsRooted(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.Length == 0) return false;
        if (reference[0] == '/' || reference[0] == '\\') return true;
        return reference.Length >= 3
            && char.IsAsciiLetter(reference[0]) && reference[1] == ':'
            && (reference[2] == '/' || reference[2] == '\\');
    }

    /// <summary>
    /// <paramref name="relative"/> resolved against <paramref name="directory"/> with <c>.</c> and
    /// <c>..</c> collapsed — <c>Path.GetFullPath</c>'s job, done here because it is a no-op on a
    /// Windows path when the host is not Windows. Null when the reference is rooted or climbs out
    /// of the directory's root: neither can be an asset beside the document.
    /// </summary>
    public static string? ResolveRelative(string directory, string relative)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(relative);
        if (relative.Length == 0 || IsRooted(relative)) return null;

        var root = RootOf(directory);
        var segments = new List<string>();
        foreach (var part in Split(directory[root.Length..])) segments.Add(part);
        foreach (var part in Split(relative))
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (segments.Count == 0) return null;      // out of the root: not ours to read
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(part);
        }
        if (segments.Count == 0) return null;
        var separator = root.Length > 0 && root[^1] == '/' ? "/"
            : directory.Contains('\\', StringComparison.Ordinal) || root.Length > 0 ? "\\"
            : "/";
        return root + string.Join(separator, segments);

        static IEnumerable<string> Split(string s) => s.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// <c>standardizedFileURL.path</c> for a rooted path: dot segments collapsed, no trailing
    /// separator, separators left as written. A relative path is returned unchanged — the app only
    /// ever holds absolute paths (a picker, an activation, a book listing), and inventing a working
    /// directory for one would be a guess.
    /// </summary>
    public static string Standardize(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var root = RootOf(path);
        if (root.Length == 0) return path;
        var segments = path[root.Length..].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var kept = new List<string>(segments.Length);
        foreach (var part in segments)
        {
            if (part == ".") continue;
            if (part == "..")
            {
                if (kept.Count > 0) kept.RemoveAt(kept.Count - 1);
                continue;                                  // ".." at the root is the root, as GetFullPath has it
            }
            kept.Add(part);
        }
        var separator = root[^1] == '/' ? "/" : "\\";
        return kept.Count == 0 ? root : root + string.Join(separator, kept);
    }

    /// <summary>
    /// Whether two paths name one file. Standardized, then compared segment by segment with
    /// <c>/</c> and <c>\</c> treated as one separator and <c>OrdinalIgnoreCase</c> throughout —
    /// Windows' rule, applied on every OS so the suite pins Windows' behaviour wherever it runs.
    /// </summary>
    public static bool SamePath(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        var left = Standardize(a).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var right = Standardize(b).Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (left.Length != right.Length) return false;
        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    /// <summary>The un-removable head of a path: <c>"C:\"</c>, <c>"\\server\share\"</c>, <c>"/"</c>, or <c>""</c> for a relative one.</summary>
    internal static string RootOf(string path)
    {
        if (path.Length >= 2 && (path[0] == '/' || path[0] == '\\') && (path[1] == '/' || path[1] == '\\'))
        {
            // UNC: keep \\server\share as one indivisible head.
            var i = 2;
            var parts = 0;
            while (i < path.Length && parts < 2)
            {
                if (path[i] == '/' || path[i] == '\\')
                {
                    parts++;
                    if (parts == 2) break;
                }
                i++;
            }
            return path[..Math.Min(i + (parts == 2 ? 1 : 0), path.Length)];
        }
        if (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\')) return path[..3];
        if (path.Length >= 1 && (path[0] == '/' || path[0] == '\\')) return path[..1];
        return "";
    }
}
