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
