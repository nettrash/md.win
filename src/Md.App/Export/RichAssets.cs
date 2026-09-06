// The bundled engine files the self-contained HTML export inlines (core-api.md §A4, "the
// readRichAsset contract"): rich/katex.min.css and the twenty rich/fonts/*.woff2 faces.
namespace Md.App.Export;

/// <summary>
/// <c>Func&lt;string, byte[]?&gt; readRichAsset</c> as Md.Core specifies it: the key is always
/// slash-separated and is exactly <c>rich/katex.min.css</c> or <c>rich/fonts/&lt;face&gt;.woff2</c>;
/// the answer is the file's bytes, or <see langword="null"/> when it is not in the package; and it
/// <b>never throws</b>. A zero-length array means "an empty file that exists", not "missing".
/// </summary>
/// <remarks>
/// At most 21 calls per export, and none at all for a document without maths — Core gates on the
/// input document, so an unstyled formula in a file is the worst this can produce. Containment is
/// kept even though every key is Core's own: this reads from a path built out of a string, and the
/// day someone passes a document-supplied one it must already refuse to climb out.
/// </remarks>
internal static class RichAssets
{
    /// <summary>Where the engines live: <c>&lt;install&gt;\web\</c>, the same folder the virtual host maps.</summary>
    public static string Root => Web.AssetHost.WebRoot;

    public static byte[]? Read(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        try
        {
            var relative = key.Replace('/', Path.DirectorySeparatorChar);
            var file = Path.GetFullPath(Path.Combine(Root, relative));

            var contained = file.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            return contained && File.Exists(file) ? File.ReadAllBytes(file) : null;
        }
        catch (Exception)
        {
            // An over-long path, an unreadable file, a key with a NUL in it: all "not in the package".
            return null;
        }
    }
}
