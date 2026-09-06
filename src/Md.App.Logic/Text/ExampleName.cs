namespace Md.App.Logic.Text;

/// <summary>
/// The title of an Examples row (shell-final.md §2.3): the file's stem with a leading ordering
/// prefix <c>^[0-9]+-</c> removed — only when something remains, and only for ASCII digits (never
/// <c>\d</c>, which would also strip Arabic-Indic or Devanagari numerals). "01-Welcome.md" → "Welcome",
/// "09-Writer Tools.md" → "Writer Tools", "01-" → "01-". Pure; the enumeration and ordering live in
/// <c>ExampleLibrary</c>.
/// </summary>
public static class ExampleName
{
    /// <param name="fileName">A file name or path; the extension and any directory part are dropped.</param>
    public static string DisplayName(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var stem = Path.GetFileNameWithoutExtension(LastSegment(fileName));
        var digits = 0;
        while (digits < stem.Length && stem[digits] is >= '0' and <= '9') digits++;
        if (digits == 0 || digits >= stem.Length || stem[digits] != '-') return stem;
        var rest = stem[(digits + 1)..];
        return rest.Length == 0 ? stem : rest;
    }

    // Path.GetFileName only splits on the host's separators; the examples' names must read the same on every OS.
    static string LastSegment(string path)
    {
        var cut = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return cut < 0 ? path : path[(cut + 1)..];
    }
}
