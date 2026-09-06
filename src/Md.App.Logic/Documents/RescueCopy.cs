using Md.App.Logic.Seams;

namespace Md.App.Logic.Documents;

/// <summary>
/// The last-resort save (§6.3): when the real write is refused right as a window closes — read-only
/// file, conflict, vanished folder, full disk — the keystrokes go to a fresh sibling instead of
/// nowhere. A rescue is a copy, not a save: the buffer stays dirty and the original is never touched.
/// </summary>
public static class RescueCopy
{
    /// <summary>Names tried before giving up; the Mac's hundred.</summary>
    public const int MaxAttempts = 100;

    /// <summary>
    /// <c>"01-Scene (rescued).md"</c>, then <c>"(rescued 2)"</c>, <c>"(rescued 3)"</c>, … — never
    /// <c>"(rescued 1)"</c>. A file with no extension is rescued as <c>.md</c>, as the Mac does
    /// (<c>url.pathExtension.isEmpty ? "md" : …</c>), and the two Foundation splits the Mac builds
    /// this from disagree on odd names — <see cref="FileNames"/> mirrors each.
    /// </summary>
    public static string NameFor(string fileName, int attempt)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        var name = FileNames.NameOf(fileName);
        var extension = FileNames.ExtensionOf(name);
        if (extension.Length == 0) extension = ".md";
        return Strings.Documents.RescueCopyName(FileNames.StemOf(name), extension, attempt);
    }

    /// <summary>
    /// Write <paramref name="data"/> beside <paramref name="path"/> under the first free rescue name.
    /// Returns the path written, or null when the write failed (immediately — a failing folder will
    /// fail a hundred times too) or a hundred names were taken. Never overwrites: a directory
    /// squatting on the name counts as taken, exactly as Foundation's <c>fileExists(atPath:)</c> does.
    /// </summary>
    public static string? Write(IFileSystem fileSystem, string path, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(data);
        var folder = FileNames.DirectoryOf(path);
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var candidate = FileNames.Combine(folder, NameFor(path, attempt));
            if (Exists(fileSystem, candidate)) continue;
            try
            {
                fileSystem.WriteAllBytesInPlace(candidate, data);
                return candidate;
            }
            catch (Exception e) when (FileFailure.IsOne(e))
            {
                return null;
            }
        }
        return null;
    }

    static bool Exists(IFileSystem fileSystem, string candidate)
    {
        try
        {
            return fileSystem.FileExists(candidate) || fileSystem.DirectoryExists(candidate);
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            return true;      // cannot even look: treat the name as taken and try the next
        }
    }
}

/// <summary>
/// The exceptions a file operation is allowed to fail with — the set the Mac's <c>catch</c> covers,
/// so an <c>OutOfMemoryException</c> or a bug in our own code still escapes instead of being logged
/// as "could not save". Shared by the session, the rescue copy and the loader.
/// </summary>
internal static class FileFailure
{
    public static bool IsOne(Exception e) =>
        e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or System.Security.SecurityException;

    /// <summary>The message to show, never empty (an empty one would render as a blank InfoBar).</summary>
    public static string Message(Exception e, string fallback) => e.Message.Length == 0 ? fallback : e.Message;
}
