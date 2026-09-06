using System.Text;
using Md.App.Logic.Documents;
using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// Every flow of §7 driven end to end over the fakes: which HTML each one loads, which surface kind
/// it asks for, whether the picker comes before or after the rendering, what the file ends up
/// holding, and which alert a failure raises. The order-of-operations tests all work the same way —
/// cancel the picker and look at what happened anyway, because a flow that renders first has
/// rendered even when nothing is saved, and a flow that asks first has not.
/// </summary>
public class ExportPipelineTests
{
    const string Plain = "# Title\n\nA paragraph.\n";
    const string Rich = "# Title\n\nInline $x^2$ maths.\n\n```mermaid\ngraph TD;A-->B;\n```\n";

    // ─────────────────────────────── PDF ───────────────────────────────

    [Fact]
    public void ExportPdfRendersThePaperPageBeforeItAsksWhereToSaveIt()
    {
        using var h = new ExportHarness();
        // The picker cancels; the pages exist regardless, which is only possible if it rendered first.
        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        Assert.Equal([RenderKind.Paper], h.Renderers.Kinds);
        Assert.Single(h.Created[0].PdfRequests);
        Assert.Single(h.Pickers.SaveCalls);
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public void ExportPdfWritesThePagesOverTheFileThePickerMade()
    {
        using var h = new ExportHarness { Pdf = "%PDF-1.7\nreal\n"u8.ToArray() };
        var path = h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        Assert.Equal(h.Pdf, h.WrittenBytes(path));
        Assert.Equal("Doc.pdf", h.Pickers.SaveCalls[0].SuggestedName);
        Assert.Equal(".pdf", h.Pickers.SaveCalls[0].DefaultExtension);
        Assert.Equal(Strings.Exports.Pdf, h.Pickers.SaveCalls[0].Choices[0].Label);
    }

    [Fact]
    public void ThePdfPageIsCoresPaperHtmlWithTheWindowsFontStyle()
    {
        using var h = new ExportHarness();
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.SixByNine));

        var expected = ScreenHtml.WithWindowsFonts(
            PdfExport.StyledForExport(MarkdownHtml.Document(Plain, "Doc", dark: false, export: true), PageSize.SixByNine));
        Assert.Equal(expected, Assert.Single(h.Loaded));
    }

    [Fact]
    public void ThePaperGeometryIsInchesWithHalfInchMargins()
    {
        using var h = new ExportHarness();
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.SixByNine));

        Assert.Equal(new PrintGeometry(6, 9, 0.5), Assert.Single(h.Created[0].PdfRequests));
    }

    [Fact]
    public void APdfThatProducedNoPagesIsTheNamedFailure()
    {
        using var h = new ExportHarness { Pdf = [] };
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportPdf, warning.Title);
        Assert.Equal(Strings.Exports.PdfPagesNotProduced, warning.Message);
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public void SharePdfWritesATempCopyNamedAfterTheTitleAndSharesThatFile()
    {
        using var h = new ExportHarness();

        h.Drain(h.Pipeline.SharePdfAsync(Plain, "My Doc", PageSize.A4));

        var expected = FileNames.Combine(ExportHarness.Temp, "My Doc.pdf");
        Assert.Equal(expected, Assert.Single(h.Files.Writes));
        Assert.Equal((expected, "My Doc"), Assert.Single(h.Share.Shared));
        Assert.Empty(h.Pickers.SaveCalls);            // Share never opens a save panel
    }

    [Fact]
    public void ShareUsesItsOwnAlertTitle()
    {
        using var h = new ExportHarness { Pdf = [] };

        h.Drain(h.Pipeline.SharePdfAsync(Plain, "Doc", PageSize.A4));

        Assert.Equal(Strings.Exports.CouldNotGeneratePdf, Assert.Single(h.Alerts.Warnings).Title);
        Assert.Empty(h.Share.Shared);
    }

    // ─────────────────────────────── HTML ──────────────────────────────

    [Fact]
    public void ExportHtmlLoadsTheExportDocumentNotThePreviewDocument()
    {
        using var h = new ExportHarness();
        h.Eval = script => script == HtmlExport.CaptureScript ? ExportHarness.Json("<html><head></head><body>x</body></html>") : "null";
        h.WillSave(".html");

        h.Drain(h.Pipeline.ExportHtmlAsync("# T\n\n\\newpage\n\nAfter.\n", "Doc"));

        // The page-break rule swap is the whole difference, and loading the wrong string loses it.
        Assert.Equal(HtmlExport.ExportDocument("# T\n\n\\newpage\n\nAfter.\n", "Doc"), Assert.Single(h.Loaded));
        Assert.Equal([RenderKind.Export], h.Renderers.Kinds);
    }

    [Fact]
    public void TheExportedHtmlIsTheCapturedPageWithADoctypeInUtf8WithoutABom()
    {
        using var h = new ExportHarness();
        h.Eval = script => script == HtmlExport.CaptureScript ? ExportHarness.Json("<html><head></head><body>x</body></html>") : "null";
        var path = h.WillSave(".html");

        h.Drain(h.Pipeline.ExportHtmlAsync(Plain, "Doc"));

        var bytes = h.WrittenBytes(path)!;
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(HtmlExport.Doctype + "<html><head></head><body>x</body></html>", Encoding.UTF8.GetString(bytes));
        Assert.Equal("Doc.html", h.Pickers.SaveCalls[0].SuggestedName);
    }

    [Fact]
    public void AnEmptyCaptureWritesNothingAndSaysTheRenderedPageCouldNotBeCaptured()
    {
        using var h = new ExportHarness();                    // every script answers null
        h.WillSave(".html");

        h.Drain(h.Pipeline.ExportHtmlAsync(Plain, "Doc"));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportHtml, warning.Title);
        Assert.Equal(Strings.Exports.PageNotCaptured, warning.Message);
        Assert.Empty(h.Files.Writes);
        Assert.Empty(h.Pickers.SaveCalls);              // and it never got as far as asking
    }

    [Fact]
    public void TheMathGateReadsTheInputDocumentBecauseTheCaptureNoLongerHasTheLink()
    {
        using var withMath = new ExportHarness();
        withMath.Eval = _ => ExportHarness.Json("<html><head></head><body>x</body></html>");
        withMath.WillSave(".html");
        withMath.Drain(withMath.Pipeline.ExportHtmlAsync("$x^2$\n", "Doc"));

        using var plain = new ExportHarness();
        plain.Eval = _ => ExportHarness.Json("<html><head></head><body>x</body></html>");
        plain.WillSave(".html");
        plain.Drain(plain.Pipeline.ExportHtmlAsync(Plain, "Doc"));

        Assert.Contains(HtmlExport.KatexCssAsset, withMath.AssetKeys);
        Assert.Empty(plain.AssetKeys);                  // a document without maths reads nothing
    }

    // ─────────────────────────────── EPUB ──────────────────────────────

    [Fact]
    public void ExportEpubAsksWhereToSaveBeforeItPhotographsAnything()
    {
        using var h = new ExportHarness();                    // the picker cancels

        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "notes.md"));

        Assert.Single(h.Pickers.SaveCalls);
        Assert.Empty(h.Created);                        // no renderer was ever opened
    }

    [Fact]
    public void TheSuggestedEpubNameIsTheTitleTheBookWillCarry()
    {
        using var h = new ExportHarness();

        h.Drain(h.Pipeline.ExportEpubAsync("---\ntitle: From the Front Matter\n---\n\n# Ignored\n", "notes.md"));

        Assert.Equal("From the Front Matter.epub", h.Pickers.SaveCalls[0].SuggestedName);
        Assert.Equal(Strings.Exports.Epub, h.Pickers.SaveCalls[0].Choices[0].Label);
    }

    [Fact]
    public void EveryRichElementIsPhotographedAtTwiceTheSizeInDocumentOrder()
    {
        var plan = EpubExport.PlanDocument(Rich, "Doc");
        var expected = plan.Units[0].RichElements.Count;
        Assert.True(expected >= 2, "the fixture must carry both a formula and a diagram");

        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.ScrollHeight ? "2000" : ExportHarness.Rects(expected);
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "Doc.md"));

        var surface = Assert.Single(h.Created);
        Assert.Equal(RenderKind.Export, surface.Kind);
        Assert.Equal(expected, surface.Captures.Count);
        Assert.All(surface.Captures, c => Assert.Equal(2.0, c.Scale));
        Assert.Equal([2000], surface.Heights);          // grown to the content, which is taller than 842
        Assert.NotNull(h.WrittenBytes(path));
    }

    [Fact]
    public void TheOffscreenSurfaceIsNeverShorterThanTheStandardPage()
    {
        var expected = EpubExport.PlanDocument(Rich, "Doc").Units[0].RichElements.Count;
        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.ScrollHeight ? "120" : ExportHarness.Rects(expected);
        h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "Doc.md"));

        // The design's own number, spelled out: asserting against the constant the code reads would
        // pass for every value it could possibly hold, and a shorter viewport reflows Mermaid's
        // max-width: 100% figures before their rects are measured (§7.1, §7.5).
        Assert.Equal(842d, RichSnapshotter.MinimumHeightCssPx);
        Assert.Equal([842d], h.Created[0].Heights);
    }

    [Fact]
    public void ADomCountThatDisagreesWithTheMarkupAbortsTheWholeBook()
    {
        var expected = EpubExport.PlanDocument(Rich, "Doc").Units[0].RichElements.Count;
        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.ScrollHeight ? "2000" : ExportHarness.Rects(expected - 1);
        h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "Doc.md"));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportEpub, warning.Title);
        Assert.Equal(Strings.Exports.RichImagesNotCaptured, warning.Message);
        Assert.Empty(h.Files.Writes);                   // never a partial book
        Assert.Empty(h.Created[0].Captures);            // and not one pixel was taken first
    }

    [Fact]
    public void ADocumentWithNothingToPhotographNeverOpensARenderer()
    {
        using var h = new ExportHarness();
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Plain, "Doc.md"));

        Assert.Empty(h.Created);
        Assert.NotNull(h.WrittenBytes(path));
    }

    [Fact]
    public void TheBookEpubAsksFirstToo()
    {
        using var h = new ExportHarness();
        var book = new Md.Core.Book.StructuredBook("My Book", [new Md.Core.Book.BookUnit("One", Rich)], []);

        h.Drain(h.Pipeline.ExportBookEpubAsync(book));

        Assert.Equal("My Book.epub", h.Pickers.SaveCalls[0].SuggestedName);
        Assert.Empty(h.Created);
    }

    // ────────────────────────────── LaTeX ──────────────────────────────

    [Fact]
    public void LaTeXNeverOpensABrowser()
    {
        using var h = new ExportHarness();
        var path = h.WillSave(".tex");

        h.Drain(h.Pipeline.ExportLaTeXAsync(Plain, "Doc"));

        Assert.Empty(h.Created);
        Assert.Equal(LaTeXExport.Document(Plain, "Doc"), h.WrittenText(path));
        Assert.Equal("Doc.tex", h.Pickers.SaveCalls[0].SuggestedName);
        Assert.Equal(Strings.Exports.LaTeX, h.Pickers.SaveCalls[0].Choices[0].Label);
    }

    [Fact]
    public void TheBookLaTeXIsTheStructuredBook()
    {
        using var h = new ExportHarness();
        var book = new Md.Core.Book.StructuredBook("My Book", [new Md.Core.Book.BookUnit("One", Plain)], []);
        var path = h.WillSave(".tex");

        h.Drain(h.Pipeline.ExportBookLaTeXAsync(book));

        Assert.Equal(LaTeXExport.Book(book), h.WrittenText(path));
        Assert.Equal("My Book.tex", h.Pickers.SaveCalls[0].SuggestedName);
    }

    // ─────────────────────────────── SVG ───────────────────────────────

    [Fact]
    public void ADiagramIsLiftedOutOfThePureExportPageAndMadeStandalone()
    {
        var diagram = Md.Core.Export.DiagramSvg.Diagrams(Rich)[0];
        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.DiagramSvg(diagram.Ordinal) ? ExportHarness.Json("<svg viewBox=\"0 0 10 20\"></svg>") : "null";
        var path = h.WillSave(".svg");

        h.Drain(h.Pipeline.ExportDiagramSvgAsync(Rich, "Doc", diagram));

        Assert.Equal([RenderKind.Export], h.Renderers.Kinds);
        Assert.Equal(MarkdownHtml.Document(Rich, "Doc", dark: false, export: true), Assert.Single(h.Loaded));
        Assert.Equal(Md.Core.Export.DiagramSvg.StandaloneDocument("<svg viewBox=\"0 0 10 20\"></svg>"), h.WrittenText(path));
        Assert.Equal("Doc-1.svg", h.Pickers.SaveCalls[0].SuggestedName);
    }

    [Fact]
    public void ADiagramThatNeverRenderedIsSaidSoRatherThanSavedEmpty()
    {
        var diagram = Md.Core.Export.DiagramSvg.Diagrams(Rich)[0];
        using var h = new ExportHarness();                    // the ordinal script answers null
        h.WillSave(".svg");

        h.Drain(h.Pipeline.ExportDiagramSvgAsync(Rich, "Doc", diagram));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportSvg, warning.Title);
        Assert.Equal(Strings.Exports.DiagramNotCaptured, warning.Message);
        Assert.Empty(h.Files.Writes);
    }

    // ──────────────────────────── TextBundle ───────────────────────────

    [Fact]
    public void TheTextBundleAsksForAParentFolderAndWritesTheBundleInsideIt()
    {
        using var h = new ExportHarness();
        h.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);

        h.Drain(h.Pipeline.ExportTextBundleAsync(Plain, null, "My Doc"));

        Assert.Equal(Strings.Exports.ChooseTextBundleFolder, Assert.Single(h.Pickers.FolderCalls));
        var (wrapper, folder) = Assert.Single(h.Bundles);
        Assert.Equal(FileNames.Combine(ExportHarness.Docs, "My Doc.textbundle"), folder);
        Assert.Equal(Plain, Encoding.UTF8.GetString(wrapper.TextBytes));
        Assert.Empty(h.Created);
    }

    [Fact]
    public void AnExistingBundleFolderIsReplacedOnlyWhenTheUserSaysSo()
    {
        var target = FileNames.Combine(ExportHarness.Docs, "My Doc.textbundle");

        using var refused = new ExportHarness();
        refused.Files.AddDirectory(target);
        refused.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);
        refused.Drain(refused.Pipeline.ExportTextBundleAsync(Plain, null, "My Doc"));

        Assert.Equal(Strings.Exports.FolderExists("My Doc.textbundle"), Assert.Single(refused.Alerts.ReplaceConfirmations));
        Assert.Empty(refused.Bundles);

        using var accepted = new ExportHarness();
        accepted.Files.AddDirectory(target);
        accepted.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);
        accepted.Alerts.ReplaceAnswers.Enqueue(true);
        accepted.Drain(accepted.Pipeline.ExportTextBundleAsync(Plain, null, "My Doc"));

        Assert.Single(accepted.Bundles);
    }

    [Fact]
    public void ImagesAreCopiedOnlyFromTheDocumentsOwnFolder()
    {
        using var h = new ExportHarness();
        h.Files.AddFile(@"C:\Docs\logo.png", [1, 2, 3]);
        h.Files.AddFile(@"C:\secret.png", [9]);
        h.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);

        var source = "![a](logo.png) ![b](../secret.png)\n";
        h.Drain(h.Pipeline.ExportTextBundleAsync(source, @"C:\Docs\note.md", "Note"));

        var (wrapper, _) = Assert.Single(h.Bundles);
        Assert.Equal("logo.png", Assert.Single(wrapper.Assets).Name);
        Assert.Contains("assets/logo.png", Encoding.UTF8.GetString(wrapper.TextBytes), StringComparison.Ordinal);
        Assert.Contains("../secret.png", Encoding.UTF8.GetString(wrapper.TextBytes), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnsavedDocumentExportsWithAnEmptyAssetsFolder()
    {
        using var h = new ExportHarness();
        h.Files.AddFile(@"C:\Docs\logo.png", [1, 2, 3]);
        h.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);

        h.Drain(h.Pipeline.ExportTextBundleAsync("![a](logo.png)\n", null, "Note"));

        Assert.Empty(Assert.Single(h.Bundles).Wrapper.Assets);
    }

    // ───────────────────────── Share and Print ─────────────────────────

    [Fact]
    public void ShareSourceSharesTheRealFileWhenThereIsOne()
    {
        using var h = new ExportHarness();
        h.Files.AddFile(@"C:\Docs\note.md", "on disk");

        h.Drain(h.Pipeline.ShareSourceAsync(@"C:\Docs\note.md", "in the editor", "Note"));

        Assert.Equal((@"C:\Docs\note.md", "Note"), Assert.Single(h.Share.Shared));
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public void ShareSourceOfAnUntitledDocumentSharesATempCopyOfTheText()
    {
        using var h = new ExportHarness();

        h.Drain(h.Pipeline.ShareSourceAsync(null, "in the editor", "Untitled"));

        var expected = FileNames.Combine(ExportHarness.Temp, "Untitled.md");
        Assert.Equal("in the editor", h.WrittenText(expected));
        Assert.Equal((expected, "Untitled"), Assert.Single(h.Share.Shared));
    }

    [Fact]
    public void PrintHandsTheOverlayThePaperHtmlAndWaitsForIt()
    {
        using var h = new ExportHarness();
        string? shown = null;
        var dismissed = new TaskCompletionSource();

        var task = h.Pipeline.PrintAsync(Plain, "Doc", html => { shown = html; return dismissed.Task; });

        Assert.False(task.IsCompleted);                 // the overlay is still up
        dismissed.SetResult();
        h.Drain(task);

        // Paper, not the trim size: Print is always A4 and the page CSS is untouched.
        Assert.Equal(ScreenHtml.WithWindowsFonts(MarkdownHtml.Document(Plain, "Doc", dark: false, export: true)), shown);
        Assert.Empty(h.Created);                        // the overlay owns its own WebView2
    }

    [Fact]
    public void APrintThatFailsSaysNothing()
    {
        using var h = new ExportHarness();

        h.Drain(h.Pipeline.PrintAsync(Plain, "Doc", _ => throw new InvalidOperationException("no printer")));

        Assert.Empty(h.Alerts.Warnings);
    }

    // ───────────────────── the typography invariant ────────────────────

    [Fact]
    public void OnlyPaperCarriesTheWindowsFontStyleAndEveryExportedFileIsPure()
    {
        using var h = new ExportHarness();
        h.Eval = script =>
            script == HtmlExport.CaptureScript ? ExportHarness.Json("<html><head></head><body>x</body></html>")
            : script == Scripts.ScrollHeight ? "2000"
            : script.StartsWith("Array.from", StringComparison.Ordinal) ? ExportHarness.Rects(EpubExport.PlanDocument(Rich, "Doc").Units[0].RichElements.Count)
            : ExportHarness.Json("<svg></svg>");

        h.WillSave(".pdf");
        h.Drain(h.Pipeline.ExportPdfAsync(Rich, "Doc", PageSize.A4));
        h.WillSave(".html");
        h.Drain(h.Pipeline.ExportHtmlAsync(Rich, "Doc"));
        h.WillSave(".epub");
        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "Doc.md"));
        h.WillSave(".svg");
        h.Drain(h.Pipeline.ExportDiagramSvgAsync(Rich, "Doc", Md.Core.Export.DiagramSvg.Diagrams(Rich)[0]));

        Assert.Empty(h.Alerts.Warnings);

        // The other half of the rule: what the reader looks at, on screen and on paper, does carry it.
        Assert.Contains(ScreenHtml.IdMarker, ScreenHtml.WithWindowsFonts(MarkdownHtml.Document(Rich, "Doc", dark: false, export: false)), StringComparison.Ordinal);
        Assert.Contains(ScreenHtml.IdMarker, ExportPipeline.PaperHtml(Rich, "Doc"), StringComparison.Ordinal);

        foreach (var surface in h.Created)
        {
            var expectStyle = surface.Kind == RenderKind.Paper;
            foreach (var html in surface.LoadedHtml)
                Assert.Equal(expectStyle, html.Contains(ScreenHtml.IdMarker, StringComparison.Ordinal));
        }
        Assert.Contains(RenderKind.Paper, h.Renderers.Kinds);
        Assert.Contains(RenderKind.Export, h.Renderers.Kinds);
        Assert.DoesNotContain(RenderKind.Screen, h.Renderers.Kinds);
    }
}
