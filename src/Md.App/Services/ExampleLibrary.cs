using Md.Core.Document;

namespace Md.App.Services;

/// <summary>
/// The bundled File ▸ Examples rows (§2.3). The nine files ship as content next to the executable in
/// <c>Examples\</c> and are byte-identical with the other md apps; the listing is the folder's TOP
/// LEVEL only, because <c>Example Book\</c> is not a menu item — it unpacks whole through
/// Examples ▸ Example Book….
///
/// Order and labels are Core's (<c>ExampleLibrary.FromListing</c>): Finder's natural sort, and the
/// ordering prefix stripped only when something remains, so the menu reads the same here as on
/// macOS and Android.
/// </summary>
internal sealed class ExampleLibrary
{
    /// <summary>Where the content files land, packaged or not: beside the executable.</summary>
    public static string FolderPath { get; } = Path.Combine(AppContext.BaseDirectory, "Examples");

    /// <summary>The example book's source folder, copied out by Examples ▸ Example Book… (never listed as a row).</summary>
    public static string BookFolderPath { get; } = Path.Combine(FolderPath, "Example Book");

    readonly Lazy<IReadOnlyList<Example>> examples = new(Load);

    /// <summary>The rows, in menu order. Empty when the folder is missing — a broken install shows no Examples rather than throwing at start-up.</summary>
    public IReadOnlyList<Example> Examples => examples.Value;

    /// <summary>
    /// The example's text, read as UTF-8, or null when it cannot be read — the Mac silently does
    /// nothing in that case, and so do we. The window that opens it is untitled and dirty, so
    /// closing it offers to save.
    /// </summary>
    public string? ReadText(Example example)
    {
        ArgumentNullException.ThrowIfNull(example);
        try
        {
            return File.ReadAllText(Path.Combine(FolderPath, example.FileName), System.Text.Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    static IReadOnlyList<Example> Load()
    {
        try
        {
            return Md.Core.Document.ExampleLibrary.FromListing(
                Directory.EnumerateFiles(FolderPath, "*.md", SearchOption.TopDirectoryOnly).Select(Path.GetFileName)!);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }
}
