using System.Xml.Linq;
using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>
/// §6.1: <c>Package.appxmanifest</c>'s file-type associations and <see cref="DocumentLoader"/>'s
/// picker lists are one list — the family's canonical extension sets — pinned here as literals
/// against the checked-in manifest, so neither side can drift without this file noticing. (They
/// did once: the loader offered <c>.text</c> in the Open dialog while the manifest registered only
/// <c>.txt</c>, so a double-click on a <c>.text</c> went to another app.)
///
/// Every extension is a literal on purpose: a test that compares the manifest to
/// <c>DocumentLoader.MarkdownExtensions</c> alone passes for any list the two happen to share.
/// </summary>
public class FileAssociationTests
{
    // The canonical sets, per kind, in the order every md port declares them.
    static readonly string[] Markdown = [".md", ".markdown", ".mdown", ".markdn", ".mdtext", ".mdtxt", ".mkd", ".mkdn", ".mdwn", ".mkdown"];
    static readonly string[] PlantUml = [".puml", ".plantuml", ".iuml", ".pu"];
    static readonly string[] Graphviz = [".gv"];
    static readonly string[] TextPack = [".textpack"];
    static readonly string[] PlainText = [".txt", ".text"];

    static readonly XDocument Manifest = XDocument.Load(RepoFiles.At("src", "Md.App", "Package.appxmanifest"));

    /// <summary>Each <c>uap3:FileTypeAssociation</c> by its <c>Name</c>, with its FileType values and their ContentType, in document order.</summary>
    static List<(string Name, List<(string Extension, string? ContentType)> Types)> Associations() =>
        Manifest.Descendants()
            .Where(e => e.Name.LocalName == "FileTypeAssociation")
            .Select(a => (
                a.Attribute("Name")!.Value,
                a.Descendants().Where(e => e.Name.LocalName == "FileType")
                    .Select(e => (e.Value.Trim(), e.Attribute("ContentType")?.Value))
                    .ToList()))
            .ToList();

    static List<string> ManifestExtensions() => Associations().SelectMany(a => a.Types).Select(t => t.Extension).ToList();

    [Fact]
    public void TheManifestDeclaresExactlyTheCanonicalSetsOneAssociationPerKind()
    {
        var associations = Associations();

        Assert.Equal(["markdown", "plantuml", "graphviz", "textpack", "plaintext"], associations.Select(a => a.Name));
        Assert.Equal(Markdown, associations[0].Types.Select(t => t.Extension));
        Assert.Equal(PlantUml, associations[1].Types.Select(t => t.Extension));
        Assert.Equal(Graphviz, associations[2].Types.Select(t => t.Extension));
        Assert.Equal(TextPack, associations[3].Types.Select(t => t.Extension));
        Assert.Equal(PlainText, associations[4].Types.Select(t => t.Extension));
    }

    [Fact]
    public void EveryFileTypeCarriesItsKindsContentType()
    {
        // The new aliases must not be pasted in without a ContentType, or with the wrong one: a
        // .mkd is text/markdown like a .md, a .pu is text/plain like a .puml.
        foreach (var (name, types) in Associations())
        {
            var expected = name switch
            {
                "markdown" => "text/markdown",
                "graphviz" => "text/vnd.graphviz",
                "textpack" => "application/zip",
                _ => "text/plain", // plantuml, plaintext
            };
            Assert.All(types, t => Assert.Equal(expected, t.ContentType));
        }
    }

    [Fact]
    public void AnExtensionIsDeclaredOnceLowercaseAndWithItsDot()
    {
        // MSIX rejects a FileType declared under two associations, and matches case-insensitively
        // but expects the lowercase spelling with its leading dot.
        var all = ManifestExtensions();

        Assert.Equal(all.Count, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, e =>
        {
            Assert.StartsWith(".", e, StringComparison.Ordinal);
            Assert.Equal(e.ToLowerInvariant(), e);
            Assert.Equal(1, e.Count(c => c == '.'));
            Assert.DoesNotContain(' ', e);
        });
    }

    [Fact]
    public void TheOpenPickerOffersEveryAssociatedTypeAndNothingElse()
    {
        // Both directions. A type Windows hands us through a double-click that the Open dialog
        // cannot show, and a type the dialog shows that File Explorer never sends us, are the same
        // drift. .textbundle is in neither: it is a folder, opened through File ▸ Open TextBundle
        // Folder… (§6.5), which a file picker cannot list and MSIX cannot associate.
        var manifest = ManifestExtensions();

        Assert.Equal(manifest.Order(StringComparer.Ordinal), DocumentLoader.OpenExtensions.Order(StringComparer.Ordinal));
        Assert.Equal(manifest.Count, DocumentLoader.OpenExtensions.Count);
        Assert.DoesNotContain(".textbundle", manifest);
        Assert.DoesNotContain(".textbundle", DocumentLoader.OpenExtensions);
        // .dot stays unclaimed on Windows as on macOS (Word's template); Graphviz is .gv only.
        Assert.DoesNotContain(".dot", manifest);
        Assert.DoesNotContain(".dot", DocumentLoader.OpenExtensions);
    }

    [Fact]
    public void TheLoaderListsAreTheCanonicalSetsInTheManifestsOrder()
    {
        Assert.Equal(Markdown, DocumentLoader.MarkdownExtensions);
        Assert.Equal(PlantUml, DocumentLoader.PlantUmlExtensions);
        Assert.Equal(Graphviz, DocumentLoader.GraphvizExtensions);
        Assert.Equal(PlainText, DocumentLoader.PlainTextExtensions);
    }

    [Fact]
    public void TheSavePickerWritesEveryAssociatedTypeExceptThePack()
    {
        // Save As offers each kind under its own label with the manifest's extensions, so a .mkd or
        // an .iuml saves back in place under its own name; .textpack is import-only (§6.5).
        var byLabel = DocumentLoader.SaveChoices.ToDictionary(c => c.Label, c => c.Extensions, StringComparer.Ordinal);

        Assert.Equal(Markdown, byLabel[Strings.Exports.MarkdownDocument]);
        Assert.Equal(PlainText, byLabel[Strings.Exports.PlainText]);
        Assert.Equal(PlantUml, byLabel[Strings.Exports.PlantUmlDiagram]);
        Assert.Equal(Graphviz, byLabel[Strings.Exports.GraphvizDotGraph]);
        Assert.Equal(4, byLabel.Count);

        var writable = DocumentLoader.SaveChoices.SelectMany(c => c.Extensions).ToList();
        Assert.Equal(
            ManifestExtensions().Except(TextPack, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            writable.Order(StringComparer.Ordinal));
    }
}
