using System.Globalization;
using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.Core.Book;
using Md.Core.Document;
using Md.Core.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The rows a refuter's mutation run found unguarded, plus the traps export.md and the family's
/// own notes name. Each of these was a live mutant: the code was already right, and every one of
/// the 985 tests still passed with it made wrong.
/// </summary>
public sealed class ExportRefuterTests
{
    const string Plain = "# Title\n\nA paragraph.\n";
    const string Rich = "# Title\n\nInline $x^2$ maths.\n\n```mermaid\ngraph TD;A-->B;\n```\n";

    // Two diagrams, so "the row the menu named" and "the row next to it" are different answers.
    const string TwoDiagrams = "# Two\n\n```mermaid\ngraph TD;A-->B;\n```\n\n```graphviz\ndigraph { a -> b; }\n```\n";

    // ── the repaint wait (§7.5 step 2) ────────────────────────────────────────────────────────

    [Fact]
    public void NothingIsMeasuredUntilTheMacsThreeHundredMillisecondRepaintHasElapsed()
    {
        // The wait is KEPT deliberately: captureBeyondViewport would photograph past the viewport
        // without it, but Mermaid's `max-width: 100%` means its layout is not final until the
        // surface has been resized and laid out again — a rect read before that is a rect of the
        // old width, and the picture is cropped. Mutating the constant to zero left every test
        // green, so the number and the ordering are both asserted here.
        Assert.Equal(TimeSpan.FromMilliseconds(300), RichSnapshotter.RepaintDelay);

        using var deterministic = new NoSyncContext();
        var scheduler = new FakeScheduler();
        var renderers = new FakeRenderSurfaceFactory
        {
            Create = kind => new FakeRenderSurface
            {
                Kind = kind,
                EvalHandler = script => script == Scripts.ScrollHeight ? "2000" : "[[1,2,3,4,1]]",
            },
        };

        var plan = EpubExport.PlanDocument("$x$\n", "Doc");
        var task = new RichSnapshotter(renderers, scheduler).CaptureAsync(plan);

        // The height is set, and then the flow stops dead on a timer.
        var surface = Assert.Single(renderers.Created);
        Assert.Equal([2000d], surface.Heights);
        Assert.False(task.IsCompleted);
        Assert.Equal([TimeSpan.FromMilliseconds(300)], scheduler.PendingDelays);
        Assert.DoesNotContain(Scripts.RichElements, surface.Evals);
        Assert.Empty(surface.Captures);

        // 299 ms is not enough — the wait is a real wait, not a yield.
        scheduler.Advance(TimeSpan.FromMilliseconds(299));
        Assert.False(task.IsCompleted);
        Assert.Empty(surface.Captures);

        scheduler.Advance(TimeSpan.FromMilliseconds(1));
        Assert.True(task.IsCompletedSuccessfully);
        Assert.Contains(Scripts.RichElements, surface.Evals);
        Assert.Single(surface.Captures);
    }

    // ── the diagram the menu named, and only that one (§7.6) ──────────────────────────────────

    [Fact]
    public void TheOrdinalPicksItsOwnDiagramAndNeverTheNeighbourWhenTheRowHasGone()
    {
        var diagrams = Md.Core.Export.DiagramSvg.Diagrams(TwoDiagrams);
        Assert.Equal(2, diagrams.Count);

        // Asked for the second: the second is what the script is run for, not the first.
        using (var h = new ExportHarness())
        {
            h.Eval = script => script == Scripts.DiagramSvg(1) ? ExportHarness.Json("<svg id=\"second\"/>") : "null";
            var path = h.WillSave(".svg");

            h.Drain(h.Pipeline.ExportDiagramSvgAsync(TwoDiagrams, "Doc", ordinal: 1));

            Assert.Contains("second", h.WrittenText(path)!, StringComparison.Ordinal);
            Assert.Equal("Doc-2.svg", h.Pickers.SaveCalls[0].SuggestedName);   // 1-based for the reader
        }

        // The writer deleted the second fence between the menu build and the click. The command
        // does nothing at all — no renderer, no picker, no alert — rather than saving the survivor
        // under the name of the diagram that is gone.
        using (var h = new ExportHarness())
        {
            h.Eval = _ => ExportHarness.Json("<svg id=\"first\"/>");
            h.WillSave(".svg");

            h.Drain(h.Pipeline.ExportDiagramSvgAsync("# One\n\n```mermaid\ngraph TD;A-->B;\n```\n", "Doc", ordinal: 1));

            Assert.Empty(h.Created);
            Assert.Empty(h.Pickers.SaveCalls);
            Assert.Empty(h.Alerts.Warnings);
            Assert.Empty(h.Files.Writes);
        }
    }

    // ── the busy signal (§7.1) ────────────────────────────────────────────────────────────────

    [Fact]
    public void PrintNeverRaisesBusySoNoFooterRingSpinsBehindTheOverlay()
    {
        // The overlay carries a progress ring of its own and is up for as long as the print dialog
        // is; a second ring in the footer behind it would say nothing and would still be spinning
        // while the reader reads the preview. Every other flow does raise it, 500 ms before WP3
        // shows the ring.
        using var h = new ExportHarness();
        var states = new List<bool>();
        h.Pipeline.BusyChanged += states.Add;

        h.Drain(h.Pipeline.PrintAsync(Plain, "Doc", _ => Task.CompletedTask));
        Assert.Empty(states);                                   // print, and print alone

        h.WillSave(".tex");
        h.Drain(h.Pipeline.ExportLaTeXAsync(Plain, "Doc"));
        Assert.Equal([true, false], states);                    // every other flow does raise it

        h.Drain(h.Pipeline.ShareSourceAsync(null, Plain, "Doc"));
        Assert.Equal([true, false, true, false], states);

        // And the delay the footer waits before it draws anything is the design's, published here
        // rather than spelled again in the window that draws it.
        Assert.Equal(TimeSpan.FromMilliseconds(500), ExportPipeline.BusyRingDelay);
    }

    // ── the TextBundle's Replace question (§7.7) ──────────────────────────────────────────────

    [Fact]
    public void AnOrdinaryFileInTheWayIsTheSameReplaceQuestionAsAFolder()
    {
        // "Exists" is the save panel's question, and on Windows a plain file called
        // `Doc.textbundle` occupies the name exactly as a folder does. Answering Cancel must leave
        // it untouched; answering Replace must go ahead.
        var target = ExportHarness.Docs + "\\Doc.textbundle";

        using (var cancelled = new ExportHarness())
        {
            cancelled.Files.AddFile(target, "not a bundle at all");
            cancelled.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);
            cancelled.Alerts.ReplaceAnswers.Enqueue(false);

            cancelled.Drain(cancelled.Pipeline.ExportTextBundleAsync(Plain, null, "Doc"));

            Assert.Equal(Strings.Exports.FolderExists("Doc.textbundle"), Assert.Single(cancelled.Alerts.ReplaceConfirmations));
            Assert.Empty(cancelled.Bundles);
            Assert.Empty(cancelled.Alerts.Warnings);            // a cancel is silence, never an alert
        }

        using (var replaced = new ExportHarness())
        {
            replaced.Files.AddFile(target, "not a bundle at all");
            replaced.Pickers.FolderAnswers.Enqueue(ExportHarness.Docs);
            replaced.Alerts.ReplaceAnswers.Enqueue(true);

            replaced.Drain(replaced.Pipeline.ExportTextBundleAsync(Plain, null, "Doc"));

            Assert.Equal(target, Assert.Single(replaced.Bundles).Folder);
        }
    }

    // ── a book whose articles are not all rich (§7.5, core-api §A4 "epub") ────────────────────

    [Fact]
    public async Task OnlyTheRichArticlesOfAMixedBookAreOpenedAndTheirPhotographsKeepTheirOwnIndex()
    {
        // The drift this guards against is silent: snapshots keyed by a running counter instead of
        // the unit's own index put article three's diagram into article five, and the book still
        // saves. A book with a plain article between two rich ones is where a counter and an index
        // disagree.
        var book = new StructuredBook(
            "Mixed",
            [new BookUnit("One", Rich), new BookUnit("Two", Plain), new BookUnit("Three", Rich)],
            []);
        var plan = EpubExport.PlanBook(book);
        var rich = plan.RichUnits;
        Assert.Equal(2, rich.Count);
        Assert.True(rich[1].Index - rich[0].Index > 1, "the plain article must sit between the two rich ones");

        using var deterministic = new NoSyncContext();
        var scheduler = new FakeScheduler();
        var renderers = new FakeRenderSurfaceFactory
        {
            Create = kind => new FakeRenderSurface
            {
                Kind = kind,
                EvalHandler = script => script == Scripts.ScrollHeight ? "1500" : ExportHarness.Rects(rich[0].RichElements.Count),
            },
        };

        var task = new RichSnapshotter(renderers, scheduler).CaptureAsync(plan);
        for (var i = 0; i < 8 && !task.IsCompleted; i++) scheduler.Advance(RichSnapshotter.RepaintDelay);
        Assert.True(task.IsCompletedSuccessfully);

        var snapshots = await task;
        Assert.Equal([rich[0].Index, rich[1].Index], snapshots.Keys.Order());
        Assert.Equal(2, Assert.Single(renderers.Created).LoadedHtml.Count);   // the plain unit never opened a page

        // And Core accepts exactly this dictionary — a key it did not expect would throw.
        Assert.NotEmpty(EpubExport.Assemble(plan, snapshots));
    }

    // ── the comma-decimal trap (multi-port grammar divergence) ────────────────────────────────

    [Theory]
    [InlineData("de-DE")]
    [InlineData("ru-RU")]
    public void ACommaDecimalUiCultureChangesNoScript_NoName_AndNoGeometry(string culture)
    {
        // "1,5" in a CDP payload is a syntax error and "Doc-1,5.svg" is a different file. Every
        // number the export path spells goes through InvariantCulture; this proves it by running
        // the whole thing under a culture where the default would differ.
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);

            Assert.Equal("Doc-3.svg", ExportNames.DiagramSvg("Doc", ordinal: 2));
            Assert.Contains("[2]", Scripts.DiagramSvg(2), StringComparison.Ordinal);
            Assert.Contains("* 2)", Scripts.CanvasRasterise(0, 2.0), StringComparison.Ordinal);
            Assert.DoesNotContain(",5", Scripts.CanvasRasterise(0, 1.5), StringComparison.Ordinal);
            Assert.Contains("1.5", Scripts.CanvasRasterise(0, 1.5), StringComparison.Ordinal);

            // And the geometry the print settings take is arithmetic, not formatting: A4's
            // 595.2 × 841.8 pt is 8.2666… × 11.691… in whatever the thousands separator is.
            var a4 = PrintGeometry.For(PageSize.A4);
            Assert.Equal(595.2 / 72.0, a4.PageWidthIn, 10);
            Assert.Equal(841.8 / 72.0, a4.PageHeightIn, 10);
            Assert.Equal(0.5, a4.MarginIn);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }

    // ── the picker's empty file (Appendix A) ──────────────────────────────────────────────────

    [Fact]
    public void APdfThatCannotBeProducedNeverOpensAPickerAndSoLeavesNoEmptyFile()
    {
        // WinRT's FileSavePicker CREATES the file it returns and leaves it empty. Rendering first
        // and asking second (the Mac's order) is what keeps a failed PDF from leaving a 0-byte
        // .pdf on the reader's desktop as its only trace.
        using var h = new ExportHarness { Pdf = [] };
        h.WillSave(".pdf");

        h.Drain(h.Pipeline.ExportPdfAsync(Plain, "Doc", PageSize.A4));

        Assert.Empty(h.Pickers.SaveCalls);
        Assert.Empty(h.Files.Writes);
        var warning = Assert.Single(h.Alerts.Warnings);
        Assert.Equal(Strings.Exports.CouldNotExportPdf, warning.Title);
        Assert.Equal(Strings.Exports.PdfPagesNotProduced, warning.Message);
    }
}
