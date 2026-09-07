using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>
/// The nine files that ship in <c>src\Md.App\Examples\</c>, through the model the app actually uses.
///
/// <para><c>Md.App.Services.ExampleLibrary.Load()</c> is one call —
/// <c>ExampleLibrary.FromListing(Directory.EnumerateFiles(FolderPath, "*.md", TopDirectoryOnly))</c>
/// — so the menu's rows are Core's answer over that folder's listing, and this asserts exactly that
/// pair. Md.Core.Tests pins the same rule over its own fixture copy of the folder; this one pins the
/// ASSET, which is what a dropped, renamed or renumbered example would break.</para>
///
/// <para>There was a second, subtly different copy of the prefix rule in
/// <c>Md.App.Logic.Text.ExampleName</c> that nothing called — Core's is the cross-port one (its
/// <c>DisplayName</c> compares Swift <c>Character</c>s, so a dash wearing a combining mark is not a
/// prefix separator, where the shadow's <c>char</c> comparison said it was). The shadow and its
/// tests are gone; this is what remained worth keeping.</para>
/// </summary>
public class BundledExamplesTests
{
    [Fact]
    public void TheNineBundledExamplesProduceTheMenusRowsInOrder()
    {
        // TopDirectoryOnly, as the app enumerates: "Example Book\" is not a row — it unpacks whole
        // through Examples ▸ Example Book… (§2.3).
        var listing = Directory
            .EnumerateFiles(RepoFiles.At("src", "Md.App", "Examples"), "*.md", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Select(name => name!)
            // The file system's order is not the menu's; FromListing is what sorts.
            .OrderByDescending(name => name, StringComparer.Ordinal)
            .ToList();

        var examples = ExampleLibrary.FromListing(listing);

        Assert.Equal(
            new[]
            {
                "01-Welcome.md", "02-Formatting.md", "03-Tables.md", "04-Code.md", "05-Images.md",
                "06-Math.md", "07-Diagrams.md", "08-Plots.md", "09-Writer Tools.md",
            },
            examples.Select(e => e.FileName));
        Assert.Equal(
            new[] { "Welcome", "Formatting", "Tables", "Code", "Images", "Math", "Diagrams", "Plots", "Writer Tools" },
            examples.Select(e => e.Name));
    }
}
