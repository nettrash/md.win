using Md.App.Logic.Seams;

namespace Md.App.Logic.Documents;

/// <summary>
/// The one place a document's own folder is read (§4.9, §7.7). The live preview never resolves a
/// relative image — a <c>photo.png</c> is fetched from the virtual host and 404s, as on every port —
/// so this exists only for Export ▸ TextBundle…, which copies the findable images into
/// <c>assets/</c>.
///
/// Containment is the whole point: a document is a text file from anywhere, and its
/// <c>![](..\..\Windows\System32\config\SAM)</c> must resolve to nothing rather than to a file the
/// export would then copy out. The reference must be relative (a drive, a leading separator or a
/// UNC head is refused — the documented Windows deviation), and the resolved path must lie
/// <em>strictly inside</em> the document's folder after links are resolved on both sides, which is
/// what <see cref="IFileIdentity"/> is for: a junction beside the document pointing at C:\ resolves
/// out of the folder and fails the test.
/// </summary>
public static class AssetReader
{
    /// <summary>
    /// The <c>resolveAsset</c> Core's <c>TextBundle.ExportRewriting</c> takes. An unsaved document
    /// (null path) resolves nothing, so it exports with an empty <c>assets/</c>.
    /// </summary>
    public static Func<string, byte[]?> Beside(string? documentPath, IFileSystem fileSystem, IFileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrEmpty(documentPath)) return _ => null;

        var folder = FileNames.DirectoryOf(documentPath);
        if (folder.Length == 0) return _ => null;
        var canonicalFolder = Canonical(identity, folder);

        return relative =>
        {
            if (Resolve(folder, canonicalFolder, relative, identity) is not { } path) return null;
            try
            {
                return fileSystem.FileExists(path) ? fileSystem.ReadAllBytes(path) : null;
            }
            catch (Exception e) when (FileFailure.IsOne(e))
            {
                return null;                       // unreadable is "not found": the ref stays as written
            }
        };
    }

    /// <summary>
    /// The path a relative reference names inside <paramref name="folder"/>, or null when it is
    /// rooted, climbs out lexically, or lands outside once links are resolved. Public so the
    /// containment rule can be pinned on its own, without a file system.
    /// </summary>
    public static string? Resolve(string folder, string canonicalFolder, string relative, IFileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrEmpty(relative) || FileNames.IsRooted(relative)) return null;
        if (FileNames.ResolveRelative(folder, relative) is not { } candidate) return null;
        return IsStrictlyInside(Canonical(identity, candidate), canonicalFolder) ? candidate : null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> sits under <paramref name="folder"/> and is not the folder
    /// itself. Compared segment by segment, <c>OrdinalIgnoreCase</c>, with <c>/</c> and <c>\</c>
    /// treated as one separator — a plain <c>StartsWith(folder + "\\")</c> would let
    /// <c>C:\Docs2\x.png</c> through for <c>C:\Docs</c> on a canonicaliser that dropped the
    /// separator, and would miss a mixed-separator spelling entirely.
    /// </summary>
    public static bool IsStrictlyInside(string path, string folder)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(folder);
        var inner = Segments(path);
        var outer = Segments(folder);
        if (inner.Length <= outer.Length) return false;
        for (var i = 0; i < outer.Length; i++)
        {
            if (!inner[i].Equals(outer[i], StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    // See FileNames.Separators: the two-char array is not a style choice.
    static readonly char[] Separators = ['/', '\\'];

    static string[] Segments(string path) => path.Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    // Canonical() is only ever asked about a path the app is about to read; a canonicaliser that
    // cannot see the file falls back to the lexical form, which is still the right thing to compare.
    static string Canonical(IFileIdentity identity, string path)
    {
        try
        {
            return identity.Canonical(path);
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            return path;
        }
    }
}
