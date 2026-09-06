using Md.App.Logic.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// What each save picker opens with. The family rule and the Windows layer both live in
/// <c>Md.Core.Export.ExportFileNames</c> and are pinned there; these are the shapes this port builds
/// on top of them.
/// </summary>
public class ExportNamesTests
{
    [Fact]
    public void EachExportSuggestsTheTitleWithItsOwnExtension()
    {
        Assert.Equal("Notes.pdf", ExportNames.Pdf("Notes"));
        Assert.Equal("Notes.html", ExportNames.Html("Notes"));
        Assert.Equal("Notes.epub", ExportNames.Epub("Notes"));
        Assert.Equal("Notes.tex", ExportNames.LaTeX("Notes"));
        Assert.Equal("Notes.md", ExportNames.Source("Notes"));
        Assert.Equal("Notes.textbundle", ExportNames.TextBundleFolder("Notes"));
    }

    [Fact]
    public void TheFamilysSanitiserRunsFirstOnEveryOneOfThem()
    {
        // Split-join on the forbidden set: two offending characters make two dashes, as every port does.
        Assert.Equal("a-b.pdf", ExportNames.Pdf("a/b"));
        Assert.Equal("a--b.epub", ExportNames.Epub("a:?b"));
        Assert.Equal("Document.html", ExportNames.Html("   "));
    }

    [Fact]
    public void ADiagramIsNumberedFromOneForTheReader()
    {
        Assert.Equal("Notes-1.svg", ExportNames.DiagramSvg("Notes", 0));
        Assert.Equal("Notes-4.svg", ExportNames.DiagramSvg("Notes", 3));
        Assert.Equal("Document-1.svg", ExportNames.DiagramSvg("", 0));
    }

    [Fact]
    public void ADeviceNameKeepsItsOrdinalRatherThanCollectingTwoDashes()
    {
        // The suffix is appended to the family stem and the Windows layer runs after it, so "CON"
        // stops being a device name on its own — sanitising first would have given "CON--1.svg".
        Assert.Equal("CON-1.svg", ExportNames.DiagramSvg("CON", 0));

        // On its own it is still escaped, because there "CON" really is the whole stem.
        Assert.Equal("CON-.pdf", ExportNames.Pdf("CON"));
    }

    [Fact]
    public void EveryPickerOffersExactlyOneTypeWithTheMacsLabel()
    {
        Assert.Equal((Strings.Exports.Pdf, ".pdf"), One(ExportNames.PdfChoices));
        Assert.Equal((Strings.Exports.Html, ".html"), One(ExportNames.HtmlChoices));
        Assert.Equal((Strings.Exports.Epub, ".epub"), One(ExportNames.EpubChoices));
        Assert.Equal((Strings.Exports.LaTeX, ".tex"), One(ExportNames.LaTeXChoices));
        Assert.Equal((Strings.Exports.Svg, ".svg"), One(ExportNames.SvgChoices));

        static (string Label, string Extension) One(IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices)
        {
            var row = Assert.Single(choices);
            return (row.Label, Assert.Single(row.Extensions));
        }
    }
}
