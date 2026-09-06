namespace Md.Core.Book;

/// <summary>
/// Path identity for the book model. The Swift side compares
/// <c>standardizedFileURL.path</c> strings — case-sensitive, symlinks left alone —
/// because every path it sees comes from its own listing. Windows file systems are
/// case-insensitive, so the same article can legitimately arrive spelled two ways (a
/// restored selection, a watcher event); the comparison follows the platform for that
/// reason and for no other. Everything else here is <c>Path.GetFullPath</c>, which does
/// exactly what <c>standardizedFileURL</c> does: drops "." and "..", normalises
/// separators, and does <em>not</em> resolve symlinks.
/// </summary>
public static class BookPaths
{
    /// <summary>How two paths are judged the same file on this OS.</summary>
    public static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>
    /// <c>standardizedFileURL.path</c>: absolute, dot-segments removed, no trailing
    /// separator (so "/x/" and "/x" name one folder). A bare root ("/", "C:\") keeps
    /// its separator — there is no shorter spelling.
    /// </summary>
    public static string Standardize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>Same file, after standardizing both sides.</summary>
    public static bool Same(string a, string b) =>
        string.Equals(Standardize(a), Standardize(b), Comparison);

    /// <summary>
    /// <c>lastPathComponent</c> for a file or a folder — a trailing separator on a
    /// folder path is not part of its name.
    /// </summary>
    public static string Name(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    /// <summary>
    /// Foundation's <c>deletingPathExtension().lastPathComponent</c> for a file name:
    /// the text before the last dot, unless that dot is first or last or everything
    /// before it is dots — "README", "notes.", ".hidden", "..md" and "...md" keep their
    /// whole name (probed against Foundation on macOS; the two dot-only shapes are what
    /// a plain "last dot" rule gets wrong).
    /// </summary>
    public static string DeletingPathExtension(string file)
    {
        var dot = file.LastIndexOf('.');
        if (dot <= 0 || dot == file.Length - 1) return file;
        for (var i = 0; i < dot; i++)
        {
            if (file[i] != '.') return file[..dot];
        }
        return file;
    }

    /// <summary>
    /// Foundation's <c>URL.pathExtension</c> for a file name: the text after the last
    /// dot when <see cref="DeletingPathExtension"/> would remove it, except that CFURL
    /// refuses an extension containing a space — U+0020 exactly: a TAB, a no-break space
    /// or a newline inside the extension is accepted ("a.b c", "a.md " → ""; "a.b\tc" →
    /// "b\tc"; probed on macOS) — while <c>deletingPathExtension</c> still strips it. The
    /// two Foundation calls the rescue-copy name is built from do not agree with each
    /// other, and the port mirrors each. Article extensions never contain a space, so
    /// this only shapes names the listing would not have offered.
    /// </summary>
    public static string PathExtension(string file)
    {
        var stem = DeletingPathExtension(file);
        if (stem.Length == file.Length) return "";
        var ext = file[(stem.Length + 1)..];
        return ext.Contains(' ') ? "" : ext;
    }

    /// <summary>
    /// Swift's <c>urlPath.hasPrefix(folderPath + "/")</c>: <paramref name="standardizedPath"/>
    /// lies strictly inside <paramref name="standardizedFolder"/>. Both must already be
    /// standardized. A root folder already ends with its separator; appending another
    /// would make every path fail the test.
    /// </summary>
    public static bool IsInside(string standardizedPath, string standardizedFolder)
    {
        var prefix = standardizedFolder.EndsWith(Path.DirectorySeparatorChar)
            ? standardizedFolder
            : standardizedFolder + Path.DirectorySeparatorChar;
        return standardizedPath.StartsWith(prefix, Comparison);
    }
}
