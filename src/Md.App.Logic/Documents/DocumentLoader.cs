using Md.App.Logic.Seams;
using Md.Core.Export;

namespace Md.App.Logic.Documents;

/// <summary>What was opened. The two bundle kinds import read-only (§6.5); a plain file is edited in place.</summary>
public enum DocumentKind
{
    PlainText,
    TextPack,
    TextBundleFolder,
}

/// <summary>Why an open produced nothing — each maps to one alert (§6.3).</summary>
public enum LoadFailure
{
    None,

    /// <summary>Unreadable or undecodable: "The document could not be opened." plus the system's message.</summary>
    Unreadable,

    /// <summary>Not a readable pack/bundle: "The TextPack could not be read." Zip bytes are never fed to the text decoder.</summary>
    BundleUnreadable,
}

/// <summary>A file read and decoded, ready for the session.</summary>
/// <param name="Kind">Plain text edits in place; a bundle opens untitled, titled after the bundle.</param>
/// <param name="Path">The file or bundle folder that was read.</param>
/// <param name="Text">LF, as the model wants it.</param>
/// <param name="Dressing">Encoding, BOM and newline to write back with (a bundle is never written back; its dressing seeds the untitled document's).</param>
/// <param name="Title">The window title: a file's stem, a bundle's stem.</param>
public sealed record LoadedDocument(DocumentKind Kind, string Path, string Text, TextFileDressing Dressing, string Title);

/// <summary>The outcome of one open: a document, or a failure with the text to show beside its title.</summary>
public sealed record DocumentLoad(LoadedDocument? Document, LoadFailure Failure, string ErrorText)
{
    /// <summary>The alert's title, or null when nothing failed.</summary>
    public string? AlertTitle => Failure switch
    {
        LoadFailure.None => null,
        LoadFailure.BundleUnreadable => Strings.Documents.TextPackUnreadable,
        _ => Strings.Documents.CouldNotOpen,
    };
}

/// <summary>
/// Opening one path (§6.1, §6.3, §6.5): decide what it is, read it, decode it. Pure over
/// <see cref="IFileSystem"/> so every branch — a legacy-encoded file, a pack whose zip is corrupt, a
/// <c>.textbundle</c> folder with only <c>text.markdown</c> in it — is testable without Windows.
/// </summary>
public static class DocumentLoader
{
    /// <summary>The extensions md owns, in the Mac's order (Package.appxmanifest declares the same set).</summary>
    public static readonly IReadOnlyList<string> MarkdownExtensions = [".md", ".markdown", ".mdown", ".markdn", ".mdtext"];

    /// <summary>Plain-text alternates.</summary>
    public static readonly IReadOnlyList<string> PlainTextExtensions = [".txt", ".text"];

    /// <summary>What the Open picker filters on (§6.1) — a superset of the writable types plus <c>.textpack</c>.</summary>
    public static readonly IReadOnlyList<string> OpenExtensions =
        [".md", ".markdown", ".mdown", ".markdn", ".mdtext", ".txt", ".text", ".puml", ".plantuml", ".gv", ".textpack"];

    /// <summary>
    /// The Save picker's labelled type groups, in the Mac's order (§6.1). Never a bundle type:
    /// saving a bundle back would drop its <c>assets/</c>.
    /// </summary>
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> SaveChoices { get; } =
    [
        (Strings.Exports.MarkdownDocument, MarkdownExtensions),
        (Strings.Exports.PlainText, PlainTextExtensions),
        (Strings.Exports.PlantUmlDiagram, [".puml", ".plantuml"]),
        (Strings.Exports.GraphvizDotGraph, [".gv"]),
    ];

    /// <summary>The extension a Save As should default to for <paramref name="path"/> — its own, or <c>.md</c> for an untitled or extension-less document.</summary>
    public static string DefaultExtensionFor(string? path)
    {
        var ext = path is null ? "" : FileNames.ExtensionOf(path);
        return ext.Length == 0 ? ".md" : ext;
    }

    /// <summary>
    /// Read and decode. A directory named <c>*.textbundle</c> imports as a bundle; bytes that
    /// <c>TextBundle.LooksLikePack</c> claims import as a pack — never through the text decoder,
    /// which ISO-8859-1 would let "succeed" on zip bytes and bake mojibake into the next save.
    /// </summary>
    public static DocumentLoad Load(string path, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(fileSystem);
        var name = FileNames.NameOf(path);

        try
        {
            if (name.EndsWith(TextBundle.BundleExtension, StringComparison.OrdinalIgnoreCase) && fileSystem.DirectoryExists(path))
                return LoadBundleFolder(path, fileSystem);

            var bytes = fileSystem.ReadAllBytes(path);
            if (TextBundle.LooksLikePack(name, bytes)) return LoadPack(path, bytes);
            if (TextFileDressing.Undress(bytes) is not { } undressed)
                return new DocumentLoad(null, LoadFailure.Unreadable, Strings.Documents.CouldNotOpen);
            return new DocumentLoad(
                new LoadedDocument(DocumentKind.PlainText, path, undressed.Text, undressed.Dressing, FileNames.StemOf(path)),
                LoadFailure.None, "");
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            return new DocumentLoad(null, LoadFailure.Unreadable, FileFailure.Message(e, Strings.Documents.CouldNotOpen));
        }
    }

    static DocumentLoad LoadPack(string path, byte[] bytes)
    {
        if (TextBundle.TextFromPack(bytes) is not { } decoded)
            return new DocumentLoad(null, LoadFailure.BundleUnreadable, "");
        return Imported(DocumentKind.TextPack, path, decoded);
    }

    // The dictionary overload, not TextFromBundle(string): the folder is listed through the seam so
    // the branch is testable off Windows. Only text.* is read — the assets are neither shown (§6.5)
    // nor small, and a bundle may declare a lot of them.
    static DocumentLoad LoadBundleFolder(string path, IFileSystem fileSystem)
    {
        var children = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var entry in fileSystem.EnumerateEntries(path))
        {
            var child = FileNames.NameOf(entry);
            if (!child.StartsWith("text.", StringComparison.Ordinal)) continue;
            if (!fileSystem.FileExists(entry)) continue;
            children.TryAdd(child, fileSystem.ReadAllBytes(entry));
        }
        if (TextBundle.TextFromBundle(children) is not { } decoded)
            return new DocumentLoad(null, LoadFailure.BundleUnreadable, "");
        return Imported(DocumentKind.TextBundleFolder, path, decoded);
    }

    static DocumentLoad Imported(DocumentKind kind, string path, Md.Core.Document.DecodedText decoded)
    {
        // The bundle's own newline convention seeds the untitled document, so Save As writes the
        // file the writer's other tools produced; the BOM does not travel (the bytes we would write
        // are a new .md, and Core's own bundle writer emits none).
        var dressing = new TextFileDressing(decoded.Encoding, false, LineEndings.Detect(decoded.Text));
        return new DocumentLoad(
            new LoadedDocument(kind, path, LineEndings.Normalize(decoded.Text), dressing, FileNames.StemOf(path)),
            LoadFailure.None, "");
    }
}
