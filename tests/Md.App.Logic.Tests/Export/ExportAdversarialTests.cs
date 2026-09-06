using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.Core.Book;
using Md.Core.Document;
using Md.Core.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The ways an export can go wrong that a happy-path test would never notice: a page that never
/// loads, a window that closes mid-render, two exports at once, a menu row that no longer matches
/// the text, a book whose photographs could drift between articles, and the two DOM selectors that
/// must never be confused for one another.
/// </summary>
public class ExportAdversarialTests
{
    const string Plain = "# Title\n\nA paragraph.\n";
    const string Rich = "# Title\n\nInline $x^2$ maths.\n\n```mermaid\ngraph TD;A-->B;\n```\n";

    [Fact]
    public void APageThatNeverLoadedIsTheNamedErrorAndNoFile()
    {
        using var h = new ExportHarness { LoadFailure = new ExportException(Strings.Exports.PageNotCaptured) };
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportPdf, warning.Title);
        Assert.Equal(Strings.Exports.PageNotCaptured, warning.Message);
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public void TheRendererIsClosedEvenWhenTheExportFails()
    {
        using var h = new ExportHarness { LoadFailure = new InvalidOperationException("boom") };
        h.WillSave(".html");

        h.Drain(h.Pipeline.ExportHtmlAsync(Plain, "Doc"));

        Assert.True(Assert.Single(h.Created).IsDisposed);
    }

    [Fact]
    public void TheRendererIsClosedOnTheHappyPathToo()
    {
        using var h = new ExportHarness();
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        Assert.True(Assert.Single(h.Created).IsDisposed);
    }

    [Fact]
    public void AWindowClosingUnderAnExportSaysNothingToNobody()
    {
        using var h = new ExportHarness();
        h.Pipeline.Cancel();
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        Assert.Empty(h.Alerts.Warnings);
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public void ASecondExportWaitsForTheFirstToFinish()
    {
        using var h = new ExportHarness();
        var dismissed = new TaskCompletionSource();
        h.WillSave(".tex");

        var print = h.Pipeline.PrintAsync(Plain, "Doc", _ => dismissed.Task);
        var latex = h.Pipeline.ExportLaTeXAsync(Plain, "Doc");

        Assert.False(latex.IsCompleted);
        Assert.Empty(h.Pickers.SaveCalls);          // the LaTeX export has not even opened its picker

        dismissed.SetResult();
        h.Drain(print);
        h.Drain(latex);
        Assert.Single(h.Pickers.SaveCalls);
    }

    [Fact]
    public void BusyIsRaisedAroundEachExportSoTheFooterCanShowItsRing()
    {
        using var h = new ExportHarness();
        var states = new List<bool>();
        h.Pipeline.BusyChanged += states.Add;
        h.WillSave(".tex");

        h.Drain(h.Pipeline.ExportLaTeXAsync(Plain, "Doc"));

        Assert.Equal([true, false], states);
    }

    [Fact]
    public void ADiagramRowThatNoLongerMatchesTheTextDoesNothingAtAll()
    {
        using var h = new ExportHarness();
        h.WillSave(".svg");

        // The menu was built from a 250 ms-old snapshot; the writer has deleted the fence since.
        h.Drain(h.Pipeline.ExportDiagramSvgAsync(Plain, "Doc", ordinal: 0));

        Assert.Empty(h.Created);
        Assert.Empty(h.Alerts.Warnings);
        Assert.Empty(h.Pickers.SaveCalls);
    }

    [Fact]
    public void TheOrdinalOverloadResolvesTheSameDiagramTheMenuNamed()
    {
        var diagrams = Md.Core.Export.DiagramSvg.Diagrams(Rich);
        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.DiagramSvg(diagrams[0].Ordinal) ? ExportHarness.Json("<svg/>") : "null";
        var path = h.WillSave(".svg");

        h.Drain(h.Pipeline.ExportDiagramSvgAsync(Rich, "Doc", diagrams[0].Ordinal));

        Assert.NotNull(h.WrittenText(path));
    }

    [Fact]
    public void EachArticleOfABookIsPhotographedAgainstItsOwnMarkup()
    {
        var book = new StructuredBook(
            "Two Articles",
            [new BookUnit("One", Rich), new BookUnit("Two", Rich)],
            []);
        var plan = EpubExport.PlanBook(book);
        var rich = plan.RichUnits;
        Assert.Equal(2, rich.Count);

        using var h = new ExportHarness();
        h.Eval = script => script == Scripts.ScrollHeight ? "1500" : ExportHarness.Rects(rich[0].RichElements.Count);
        var path = h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportBookEpubAsync(book));

        var surface = Assert.Single(h.Created);          // one renderer for the whole export, reloaded per unit
        Assert.Equal(2, surface.LoadedHtml.Count);
        Assert.Equal(rich[0].RichElements.Count + rich[1].RichElements.Count, surface.Captures.Count);
        Assert.NotNull(h.WrittenBytes(path));
    }

    [Fact]
    public void ASnapshotThatCameBackEmptyIsRefusedRatherThanPackedAsAZeroBytePng()
    {
        var expected = EpubExport.PlanDocument(Rich, "Doc").Units[0].RichElements.Count;
        using var h = new ExportHarness { Png = [] };
        h.Eval = script => script == Scripts.ScrollHeight ? "2000" : ExportHarness.Rects(expected);
        h.WillSave(".epub");

        h.Drain(h.Pipeline.ExportEpubAsync(Rich, "Doc.md"));

        Assert.Equal(Strings.Exports.RichImagesNotCaptured, Assert.Single(h.Alerts.Warnings).Message);
        Assert.Empty(h.Files.Writes);
    }

    [Fact]
    public async Task TheImageWidthIsTheMeasuredWidthNotTheClampedOne()
    {
        // A zero-width formula is captured as a 1-px clip (CDP refuses an empty one) but must still
        // be written as width="0", exactly as the Mac writes Int(rect.width.rounded()).
        using var deterministic = new NoSyncContext();
        var scheduler = new FakeScheduler();
        var renderers = new FakeRenderSurfaceFactory
        {
            Create = kind => new FakeRenderSurface
            {
                Kind = kind,
                EvalHandler = script => script == Scripts.ScrollHeight ? "900" : "[[5,6,0,0,1]]",
            },
        };

        var plan = EpubExport.PlanDocument("$x$\n", "Doc");
        Assert.Single(plan.Units[0].RichElements);

        var task = new RichSnapshotter(renderers, scheduler).CaptureAsync(plan);
        scheduler.Advance(RichSnapshotter.RepaintDelay);

        Assert.True(task.IsCompletedSuccessfully);
        var snapshots = await task;
        Assert.Equal(0, Assert.Single(snapshots[0]).DisplayWidth);

        // The capture itself was clamped to a pixel, because CDP refuses an empty clip.
        Assert.Equal(new RectD(5, 6, 1, 1), Assert.Single(Assert.Single(renderers.Created).Captures).Rect);
    }

    [Fact]
    public void TheTwoDomSelectorsAreDifferentAndNeitherScriptUsesTheOthers()
    {
        // The EPUB set has maths and no plot; the diagram set has a plot and no maths. Merging them
        // would shift every EPUB image or export the wrong diagram, and both are silent failures.
        Assert.Contains(".md-mathi", EpubExport.RichSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("plot", EpubExport.RichSelector, StringComparison.Ordinal);
        Assert.Contains("div.plot", Md.Core.Export.DiagramSvg.DomSelector, StringComparison.Ordinal);
        Assert.DoesNotContain("md-math", Md.Core.Export.DiagramSvg.DomSelector, StringComparison.Ordinal);

        Assert.Contains(EpubExport.RichSelector, Scripts.RichElements, StringComparison.Ordinal);
        Assert.DoesNotContain(Md.Core.Export.DiagramSvg.DomSelector, Scripts.RichElements, StringComparison.Ordinal);
        Assert.Contains(Md.Core.Export.DiagramSvg.DomSelector, Scripts.DiagramSvg(0), StringComparison.Ordinal);
        Assert.DoesNotContain(EpubExport.RichSelector, Scripts.DiagramSvg(0), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRichElementScriptIsTheMacsFiveColumnOneLiner()
    {
        Assert.StartsWith("Array.from(document.querySelectorAll(", Scripts.RichElements, StringComparison.Ordinal);
        Assert.Contains("r.left + window.scrollX", Scripts.RichElements, StringComparison.Ordinal);
        Assert.Contains("r.top + window.scrollY", Scripts.RichElements, StringComparison.Ordinal);
        Assert.Contains("(e.classList.contains('md-mathi') || e.classList.contains('md-mathd')) ? 1 : 0", Scripts.RichElements, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", Scripts.RichElements, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDiagramScriptInterpolatesTheOrdinalInvariantlyAndHandlesAMissingNode()
    {
        var script = Scripts.DiagramSvg(12);

        Assert.Contains("nodes[12]", script, StringComparison.Ordinal);
        Assert.Contains("if (!el) return null;", script, StringComparison.Ordinal);
        Assert.Contains("return svg ? svg.outerHTML : null;", script, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", script, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCanvasContingencyIsWrittenAndSpellsItsNumbersInvariantly()
    {
        var script = Scripts.CanvasRasterise(3, 2.0);

        Assert.Contains("[3]", script, StringComparison.Ordinal);
        Assert.Contains("rect.width * 2", script, StringComparison.Ordinal);      // never "2,0"
        Assert.Contains("window.__mdRaster", script, StringComparison.Ordinal);
        Assert.Contains("String.fromCharCode(bytes[b])", script, StringComparison.Ordinal);
        Assert.Contains("window.__mdRaster", Scripts.CanvasRasteriseResult, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaperHtmlIsIdempotentBecausePrintAndPdfBothPassThroughIt()
    {
        var once = ExportPipeline.PaperHtml(Plain, "Doc");

        Assert.Equal(once, ScreenHtml.WithWindowsFonts(once));
        Assert.Contains(ScreenHtml.IdMarker, once, StringComparison.Ordinal);
    }

    [Fact]
    public void ATextBundleWhoseWriteFailsSaysSoAndLeavesNothingBehind()
    {
        using var h = new ExportHarness();
        h.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);

        var pipeline = new ExportPipeline(
            h.Renderers, h.Pickers, h.Share, h.Alerts, h.Files, _ => null,
            h.Scheduler, h.Identity, ExportHarness.Temp,
            (_, _) => throw new IOException("the disk is full"));

        h.Drain(pipeline.ExportTextBundleAsync(Plain, null, "Doc"));

        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportTextBundle, warning.Title);
        Assert.Equal("the disk is full", warning.Message);
    }
}
