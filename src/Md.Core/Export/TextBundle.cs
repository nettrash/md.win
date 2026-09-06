using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Document;

namespace Md.Core.Export;

/// <summary>
/// TextBundle (<c>.textbundle</c>, a folder) and TextPack (<c>.textpack</c>, that folder
/// zipped) — the Markdown-with-assets container Ulysses, iA Writer and Bear write (macOS
/// <c>TextBundle</c>). The document model is a single string, so the round-trip here is
/// text, not pictures: import loads <c>text.md</c>; export copies the findable local images
/// into <c>assets/</c> and rewrites their refs, leaving every unfindable ref exactly as the
/// author wrote it. Bundles are read-only for the document (saving one back would drop its
/// assets); producing one is the explicit Export action.
/// </summary>
public static class TextBundle
{
    /// <summary>
    /// The <c>info.json</c> every bundle we write carries — a fixed canonical document
    /// (stable key order, no trailing newline, 81 bytes) so it is trivially testable.
    /// </summary>
    public const string InfoJson = "{\n  \"version\": 2,\n  \"type\": \"net.daringfireball.markdown\",\n  \"transient\": false\n}";

    public const string TextFileName = "text.md";
    public const string InfoFileName = "info.json";
    public const string AssetsDirectoryName = "assets";
    public const string BundleExtension = ".textbundle";
    public const string PackExtension = ".textpack";

    /// <summary>One image copied into a written bundle's <c>assets/</c>. Equality is by name and bytes.</summary>
    public sealed record Asset(string Name, byte[] Data)
    {
        public bool Equals(Asset? other) =>
            other is not null
            && string.Equals(Name, other.Name, StringComparison.Ordinal)
            && Data.AsSpan().SequenceEqual(other.Data);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Name, StringComparer.Ordinal);
            hash.AddBytes(Data);
            return hash.ToHashCode();
        }

        public override string ToString() => $"Asset({Name}, {Data.Length} bytes)";
    }

    /// <summary>The rewritten document and the assets to write beside it.</summary>
    public sealed record Rewrite(string Text, IReadOnlyList<Asset> Assets);

    // MARK: Import

    /// <summary>
    /// The text of a <c>.textbundle</c> given as its top-level files (name → bytes), decoded
    /// with the same encoding detection a bare file gets so a legacy-encoded bundle
    /// round-trips. Prefers <c>text.md</c>, then <c>text.markdown</c>, then any
    /// <c>text.*</c> (ordinal-smallest name: Swift's dictionary order is unspecified, so
    /// the tie is made deterministic here). Null when there is no text file.
    /// </summary>
    public static DecodedText? TextFromBundle(IReadOnlyDictionary<string, byte[]> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        var name = PickTextFile(children.Keys);
        if (name is null) return null;
        return PlainTextCodec.Decode(children[name]);
    }

    /// <summary>The same import over a <c>.textbundle</c> folder on disk (Windows has no package type).</summary>
    public static DecodedText? TextFromBundle(string bundleDirectory)
    {
        ArgumentNullException.ThrowIfNull(bundleDirectory);
        string[] files;
        try
        {
            if (!Directory.Exists(bundleDirectory)) return null;
            files = Directory.GetFiles(bundleDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in files) byName.TryAdd(Path.GetFileName(path), path);
        var name = PickTextFile(byName.Keys);
        if (name is null) return null;
        try
        {
            return PlainTextCodec.Decode(File.ReadAllBytes(byName[name]));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The text of a <c>.textpack</c>. The pack wraps a <c>.textbundle</c> folder, so the
    /// entry is nested under it and matched by its last path component. Only <c>text.*</c>
    /// entries are inflated — every asset is listed but never decompressed, so a pack that
    /// declares gigabytes of assets costs nothing to open. Null when the bytes are not a
    /// readable zip, carry no text file, or the text entry is empty.
    /// </summary>
    public static DecodedText? TextFromPack(byte[] archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        var entries = ZipReader.Entries(archive, name => LastComponent(name).StartsWith("text.", StringComparison.Ordinal));
        if (entries is null) return null;
        var entry = entries.FirstOrDefault(e => string.Equals(LastComponent(e.Name), "text.md", StringComparison.Ordinal))
            ?? entries.FirstOrDefault(e => string.Equals(LastComponent(e.Name), "text.markdown", StringComparison.Ordinal))
            ?? entries.FirstOrDefault(e => LastComponent(e.Name).StartsWith("text.", StringComparison.Ordinal));
        if (entry is null || entry.Data.Length == 0) return null;
        return PlainTextCodec.Decode(entry.Data);
    }

    /// <summary>
    /// Whether an opened file should be read as a pack — named <c>.textpack</c>, or carrying
    /// the full four-byte local-header magic <c>PK\x03\x04</c> (Android's sniff). The full
    /// four bytes, never just "PK": prose that begins with the letters PK is ordinary text.
    /// </summary>
    public static bool LooksLikePack(string name, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.EndsWith(PackExtension, StringComparison.OrdinalIgnoreCase)) return true;
        return bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04;
    }

    // MARK: Export

    /// <summary>
    /// Whether a Markdown image destination names a local file by relative path — the only
    /// kind that can be copied into <c>assets/</c>. Remote URLs (anything with a scheme),
    /// inline <c>data:</c> images, absolute paths and bare fragments are left untouched.
    /// Ordinal throughout: a combining mark after ':' or '/' is its own UTF-16 unit, which is
    /// the scalar-exact behaviour Swift needs <c>ScalarText</c> for. The scheme fold is
    /// ASCII-only, which is exact: Swift's and Kotlin's full case mapping never turn a
    /// non-ASCII letter into one of these needles' letters (İ becomes "i" + U+0307, not i),
    /// whereas what ToLowerInvariant does with İ depends on the runtime's casing tables
    /// (NLS vs ICU) — and a Turkish culture's ToLower would miss "DATA:" outright.
    /// </summary>
    public static bool IsLocalRelativeReference(string url)
    {
        ArgumentNullException.ThrowIfNull(url);
        if (url.Length == 0) return false;
        if (url.Contains("://", StringComparison.Ordinal)) return false;   // http:, https:, file:, custom
        if (StartsWithAsciiIgnoringCase(url, "data:")) return false;       // inline image bytes
        if (StartsWithAsciiIgnoringCase(url, "mailto:")) return false;
        if (url.StartsWith("/", StringComparison.Ordinal)) return false;    // absolute path — not "next to" the source
        if (url.StartsWith("#", StringComparison.Ordinal)) return false;    // in-document fragment
        return true;
    }

    // The same image syntax the HTML writer renders as <img>, so what gets an asset is
    // exactly what shows as an image. ICU's \s (NSRegularExpression), measured on macOS
    // against every candidate: TAB, LF, VT, FF, CR, NEL (U+0085) and every Z (Zs, Zl, Zp);
    // not U+200B, not U+FEFF. Spelled out rather than trusting an engine's own \s (the
    // house rule — it happens to equal .NET's, but Java's is ASCII-only, so the Kotlin
    // port keeps a destination whole at an NBSP where Swift cuts it; Swift wins). The
    // HTML writer's inline() must use the identical class.
    private static readonly Regex ImageReference = new(
        @"!\[[^\]]*\]\(([^)\t\n\v\f\r\u0085\p{Z}]+)(?:[\t\n\v\f\r\u0085\p{Z}]+(?:""[^""]*""|'[^']*'))?\)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Rewrite the document for export into a bundle: every local image ref
    /// <paramref name="resolveAsset"/> can satisfy is copied into <c>assets/</c> and its ref
    /// rewritten to <c>assets/&lt;name&gt;</c>; refs it cannot satisfy (a file that is not
    /// there, a remote URL, an absolute path) stay exactly as written. Only the URL capture
    /// is replaced, so a following "title" survives. The same path used twice becomes one
    /// asset; two distinct paths sharing a file name are disambiguated (<c>logo-2.png</c>),
    /// case-insensitively. An image-looking string inside a code span is also matched —
    /// accepted: it is only rewritten when a real file exists, and then still resolves.
    /// </summary>
    public static Rewrite ExportRewriting(string source, Func<string, byte[]?> resolveAsset)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resolveAsset);

        var assets = new List<Asset>();
        var assetForPath = new Dictionary<string, string>(StringComparer.Ordinal);   // source ref -> asset file name
        var usedNames = new HashSet<string>(StringComparer.Ordinal);                 // lower-cased, for collision-free names
        var edits = new List<(int Start, int Length, string Replacement)>();

        foreach (Match match in ImageReference.Matches(source))
        {
            var group = match.Groups[1];
            if (!group.Success) continue;
            var url = group.Value;
            if (!IsLocalRelativeReference(url)) continue;

            if (assetForPath.TryGetValue(url, out var existing))
            {
                edits.Add((group.Index, group.Length, "assets/" + existing));
                continue;
            }
            var data = resolveAsset(url);
            if (data is null) continue;
            var baseName = LastComponent(url);
            if (baseName.Length == 0) continue;

            var name = baseName;
            var counter = 2;
            while (usedNames.Contains(name.ToLowerInvariant()))
            {
                name = Disambiguated(baseName, counter);
                counter++;
            }
            usedNames.Add(name.ToLowerInvariant());
            assetForPath[url] = name;
            assets.Add(new Asset(name, data));
            edits.Add((group.Index, group.Length, "assets/" + name));
        }

        if (edits.Count == 0) return new Rewrite(source, assets);
        // Matches are disjoint and in order, so a forward splice equals Swift's
        // back-to-front replacement on the NSMutableString.
        var builder = new StringBuilder(source.Length + edits.Count * 8);
        var cursor = 0;
        foreach (var (start, length, replacement) in edits)
        {
            builder.Append(source, cursor, start - cursor);
            builder.Append(replacement);
            cursor = start + length;
        }
        builder.Append(source, cursor, source.Length - cursor);
        return new Rewrite(builder.ToString(), assets);
    }

    /// <summary>
    /// Assemble the bundle: <c>text.md</c> (UTF-8, no BOM), <c>info.json</c> and an
    /// <c>assets/</c> folder — always present, empty when there are no images.
    /// </summary>
    public static BundleWrapper BundleWrapper(string text, IReadOnlyList<Asset> assets)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(assets);
        return new BundleWrapper(Utf8NoBom.GetBytes(text), Utf8NoBom.GetBytes(InfoJson), assets.ToArray());
    }

    /// <summary>
    /// Read an image the document references by relative path, if it sits beside the saved
    /// document. The resolved path is constrained to the document's own folder: a
    /// <c>../…</c> that climbs out, or a symlink beside the document pointing anywhere else,
    /// is treated as not found (and so left untouched). Links are resolved on both sides
    /// before the prefix test, the candidate must lie strictly inside the folder, and what
    /// is read is the resolved path the test approved. An unsaved document (null path) finds
    /// nothing, so it exports with an empty <c>assets/</c>.
    /// </summary>
    public static byte[]? ReadAsset(string relativePath, string? documentPath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (documentPath is null) return null;
        string folder, candidate;
        try
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(documentPath));
            if (parent is null) return null;
            folder = ResolveLinks(parent, 0);
            candidate = ResolveLinks(Path.GetFullPath(Path.Combine(folder, relativePath)), 0);
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
        // NTFS compares names case-insensitively; a `..` that comes back in another case
        // still names the same folder there, and nowhere else.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = folder.EndsWith(Path.DirectorySeparatorChar) ? folder : folder + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, comparison)) return null;
        try
        {
            return File.Exists(candidate) ? File.ReadAllBytes(candidate) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // MARK: Helpers

    internal static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Exact-name preference: text.md, text.markdown, then the ordinal-smallest text.*.</summary>
    private static string? PickTextFile(IEnumerable<string> names)
    {
        string? any = null;
        var hasMd = false;
        var hasMarkdown = false;
        foreach (var name in names)
        {
            if (string.Equals(name, "text.md", StringComparison.Ordinal)) hasMd = true;
            else if (string.Equals(name, "text.markdown", StringComparison.Ordinal)) hasMarkdown = true;
            else if (name.StartsWith("text.", StringComparison.Ordinal)
                     && (any is null || string.CompareOrdinal(name, any) < 0)) any = name;
        }
        if (hasMd) return "text.md";
        if (hasMarkdown) return "text.markdown";
        return any;
    }

    /// <summary>The last '/'-separated component (empty for a trailing slash, like the Swift split and Kotlin's substringAfterLast).</summary>
    internal static string LastComponent(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>Prefix test folding only A–Z; <paramref name="needle"/> is lower-case ASCII.</summary>
    private static bool StartsWithAsciiIgnoringCase(string text, string needle)
    {
        if (text.Length < needle.Length) return false;
        for (var i = 0; i < needle.Length; i++)
        {
            var c = text[i];
            if (c >= 'A' && c <= 'Z') c = (char)(c + 32);
            if (c != needle[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// photo.png → photo-2.png; README → README-2. The dot must be past the first unit so a
    /// dotfile (.gitignore) keeps its whole name as the stem. (A dot can never sit at UTF-16
    /// index 1 after an astral first scalar, so unit and scalar indices agree here.)
    /// </summary>
    private static string Disambiguated(string name, int counter)
    {
        var dot = name.LastIndexOf('.');
        if (dot > 0) return string.Concat(name.AsSpan(0, dot), "-", counter.ToString(System.Globalization.CultureInfo.InvariantCulture), name.AsSpan(dot));
        return name + "-" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Resolve every symlink along a full path, the way <c>resolvingSymlinksInPath</c> does;
    /// <c>ResolveLinkTarget</c> alone looks only at the last component, and a linked folder
    /// beside the document would otherwise pass the containment test while reading elsewhere.
    /// </summary>
    private static string ResolveLinks(string fullPath, int depth)
    {
        if (depth > 40) throw new IOException("Too many levels of symbolic links.");
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var parts = fullPath[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is null) continue;
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            if (target is null) continue;
            var remainder = string.Join(Path.DirectorySeparatorChar, parts, i + 1, parts.Length - i - 1);
            var next = remainder.Length == 0 ? target.FullName : Path.Combine(target.FullName, remainder);
            return ResolveLinks(Path.GetFullPath(next), depth + 1);
        }
        return current;
    }
}

/// <summary>
/// The assembled <c>.textbundle</c> as an in-memory description — what the folder holds —
/// plus the two ways of materialising it: a folder on disk, or the entry set of a
/// <c>.textpack</c> (Windows pickers cannot create a folder-typed document, so the zipped
/// form is the natural save-picker export, as on Android).
/// </summary>
public sealed class BundleWrapper
{
    internal BundleWrapper(byte[] textBytes, byte[] infoJsonBytes, IReadOnlyList<TextBundle.Asset> assets)
    {
        TextBytes = textBytes;
        InfoJsonBytes = infoJsonBytes;
        Assets = assets;
    }

    /// <summary><c>text.md</c>: the (rewritten) document as UTF-8 without a BOM.</summary>
    public byte[] TextBytes { get; }

    /// <summary><c>info.json</c>: <see cref="TextBundle.InfoJson"/> as UTF-8.</summary>
    public byte[] InfoJsonBytes { get; }

    /// <summary>The files of <c>assets/</c>; the folder exists even when this is empty.</summary>
    public IReadOnlyList<TextBundle.Asset> Assets { get; }

    /// <summary>
    /// Write the bundle as a folder at <paramref name="bundleDirectory"/>. Like FileWrapper's
    /// atomic write, an existing bundle there is replaced whole (stale assets do not linger):
    /// the tree is built beside it and swapped in.
    /// </summary>
    public void Write(string bundleDirectory)
    {
        ArgumentNullException.ThrowIfNull(bundleDirectory);
        var target = Path.GetFullPath(bundleDirectory);
        var parent = Path.GetDirectoryName(target) ?? throw new IOException("A bundle cannot be written at a file-system root.");
        var staging = Path.Combine(parent, "." + Path.GetFileName(target) + "-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(staging);
            File.WriteAllBytes(Path.Combine(staging, TextBundle.TextFileName), TextBytes);
            File.WriteAllBytes(Path.Combine(staging, TextBundle.InfoFileName), InfoJsonBytes);
            var assetsDir = Path.Combine(staging, TextBundle.AssetsDirectoryName);
            Directory.CreateDirectory(assetsDir);
            foreach (var asset in Assets) File.WriteAllBytes(Path.Combine(assetsDir, asset.Name), asset.Data);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            else if (File.Exists(target)) File.Delete(target);
            Directory.Move(staging, target);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    /// <summary>
    /// The archive entries of a <c>.textpack</c>, nested under <c>&lt;folderName&gt;.textbundle/</c>
    /// (Android's <c>bundleEntries</c>): <c>info.json</c>, <c>text.md</c>, then the assets —
    /// or an explicit empty <c>assets/</c> directory entry, so the folder is always present.
    /// </summary>
    public IReadOnlyList<ZipEntry> PackEntries(string folderName)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        var root = folderName + TextBundle.BundleExtension + "/";
        var entries = new List<ZipEntry>(3 + Assets.Count)
        {
            new(root + TextBundle.InfoFileName, InfoJsonBytes),
            new(root + TextBundle.TextFileName, TextBytes),
        };
        if (Assets.Count == 0) entries.Add(new ZipEntry(root + TextBundle.AssetsDirectoryName + "/", Array.Empty<byte>()));
        else foreach (var asset in Assets) entries.Add(new ZipEntry(root + TextBundle.AssetsDirectoryName + "/" + asset.Name, asset.Data));
        return entries;
    }

    /// <summary>The bundle zipped as a <c>.textpack</c> through the app's own STORED writer.</summary>
    public byte[] ToTextPack(string folderName) => ZipWriter.Archive(PackEntries(folderName));
}
