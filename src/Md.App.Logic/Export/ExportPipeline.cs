using System.Text;
using Md.App.Logic.Documents;
using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.Core.Book;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.App.Logic.Export;

/// <summary>
/// Every export, print and share flow of §7, end to end and in one place, driving the browser only
/// through <see cref="IRenderSurface"/> and the machine only through the seams — so the order of
/// operations, the HTML each flow loads, the suggested names and every alert are testable on a Mac
/// with no WebView2 anywhere in sight. <c>Md.App</c> supplies the four adapters and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>The order matters and differs per flow, deliberately.</b> PDF renders first and opens the
/// picker afterwards (the Mac's order: the pages exist before the user is asked where to put them).
/// The document EPUB works out its title first, opens the picker <em>before</em> building, and only
/// then photographs — because photographing a book of diagrams takes a while and the user may
/// cancel. LaTeX and TextBundle never open a renderer at all. These are pinned by tests, not by
/// comments.
/// </para>
/// <para>
/// <b>Which HTML each flow loads is the typography decision (§4.3).</b> Print and PDF load
/// <see cref="RenderKind.Paper"/> — Core's export HTML plus the app's typewriter style, because paper a
/// Windows reader holds should not be Courier New. HTML, EPUB and SVG load
/// <see cref="RenderKind.Export"/>, the pure Core bytes, because those files are pinned byte for
/// byte against three other ports and nothing Windows-specific may reach them.
/// </para>
/// <para>
/// Exports are serialised per pipeline (one per window): two at once would share the window's export
/// canvas and its alerts. A cancelled picker is silence, never an alert; every real failure is one
/// <c>ContentDialog</c> whose title is fixed by §7.9 and whose body is the exception's own message.
/// </para>
/// </remarks>
public sealed class ExportPipeline
{
    static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    readonly IRenderSurfaceFactory _renderers;
    readonly IPickers _pickers;
    readonly IShare _share;
    readonly IAlerts _alerts;
    readonly IFileSystem _fileSystem;
    readonly Func<string, byte[]?> _readRichAsset;
    readonly IScheduler? _scheduler;
    readonly IFileIdentity _identity;
    readonly string _temporaryFolder;
    readonly Action<BundleWrapper, string> _writeBundle;
    readonly RichSnapshotter _snapshotter;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly CancellationTokenSource _cancellation = new();

    /// <param name="renderers">One offscreen surface per export (<c>ExportRenderer</c> in the App).</param>
    /// <param name="pickers">The save and folder pickers; a null answer is a cancel.</param>
    /// <param name="share">The Windows Share sheet, for one already-written file.</param>
    /// <param name="alerts">§7.9's dialogs.</param>
    /// <param name="fileSystem">Where the finished bytes go — always over the file the picker created.</param>
    /// <param name="readRichAsset">
    /// The bundled engine assets, by slash-separated key (<c>rich/katex.min.css</c>,
    /// <c>rich/fonts/&lt;face&gt;.woff2</c>). Core's contract: return the bytes, or null when the file
    /// is not in the package, and <b>never throw</b>.
    /// </param>
    /// <param name="scheduler">The UI-thread timer the render-complete poll and the repaint wait use.</param>
    /// <param name="identity">Link resolution for the TextBundle's image containment; the platform one by default.</param>
    /// <param name="temporaryFolder">Where Share writes its copy; the process temp folder by default.</param>
    /// <param name="writeBundle">
    /// How a TextBundle folder is written. Core does the real I/O (staging directory, atomic swap);
    /// the parameter exists so a test can watch it happen without touching a disk.
    /// </param>
    public ExportPipeline(
        IRenderSurfaceFactory renderers,
        IPickers pickers,
        IShare share,
        IAlerts alerts,
        IFileSystem fileSystem,
        Func<string, byte[]?> readRichAsset,
        IScheduler? scheduler = null,
        IFileIdentity? identity = null,
        string? temporaryFolder = null,
        Action<BundleWrapper, string>? writeBundle = null)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        ArgumentNullException.ThrowIfNull(pickers);
        ArgumentNullException.ThrowIfNull(share);
        ArgumentNullException.ThrowIfNull(alerts);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(readRichAsset);

        _renderers = renderers;
        _pickers = pickers;
        _share = share;
        _alerts = alerts;
        _fileSystem = fileSystem;
        _readRichAsset = readRichAsset;
        _scheduler = scheduler;
        _identity = identity ?? FileIdentity.Instance;
        _temporaryFolder = temporaryFolder ?? Path.GetTempPath();
        _writeBundle = writeBundle ?? ((wrapper, folder) => wrapper.Write(folder));
        _snapshotter = new RichSnapshotter(renderers, scheduler);
    }

    /// <summary>
    /// True while an export holds the pipeline. The footer shows its progress ring
    /// <see cref="BusyRingDelay"/> after this goes true (§7.1) — a fast export never flickers one.
    /// </summary>
    public event Action<bool>? BusyChanged;

    /// <summary>
    /// How long an export must still be running before the footer shows its progress ring (§7.1).
    /// </summary>
    /// <remarks>
    /// The number lives here, beside the event that starts the clock, rather than as a literal in
    /// the window that draws the ring: §7 is this package's, and two packages holding two copies of
    /// one delay is how a design's number quietly becomes two. Most exports — LaTeX, TextBundle,
    /// Share ▸ Source… — finish inside it and show nothing at all, which is the point.
    /// </remarks>
    public static readonly TimeSpan BusyRingDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>The window is closing: whatever is rendering stops rather than talking to a dead
    /// XamlRoot. The pipeline is finished after this — a window gets one.</summary>
    public void Cancel() => _cancellation.Cancel();

    // ─────────────────────────────── Print (§7.2) ───────────────────────────────

    /// <summary>
    /// Print… — the paper HTML, handed to the App's in-window <c>PrintOverlay</c>, which loads it in a
    /// <em>visible</em> WebView2 and calls <c>ShowPrintUI(Browser)</c>. Chromium's preview is drawn
    /// inside the control's own rectangle and is not displayed at all for a hidden one, so the
    /// off-canvas export renderer can never host it.
    /// </summary>
    /// <param name="showPrintOverlay">
    /// Shows the overlay and completes when the user dismisses it — <c>ShowPrintUI</c> has no
    /// completion event of its own, which is why the overlay carries a Done button.
    /// </param>
    /// <remarks>
    /// No page-size rewrite: print is always A4 and paper is the printer's business, as on macOS and
    /// Android. A failure here shows nothing — there is nothing actionable to say, and the Mac
    /// swallows it too.
    /// </remarks>
    public Task PrintAsync(string source, string title, Func<string, Task> showPrintOverlay)
    {
        ArgumentNullException.ThrowIfNull(showPrintOverlay);
        // announceBusy: false — the overlay is up for as long as the dialog is, and it carries a
        // progress ring of its own; a footer ring spinning behind it would say nothing.
        return Serialised(async () =>
        {
            try
            {
                await showPrintOverlay(PaperHtml(source, title)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Swallowed, as on the Mac: the dialog is gone and there is no failed file to explain.
            }
        }, announceBusy: false);
    }

    // ──────────────────────────────── PDF (§7.3) ────────────────────────────────

    /// <summary>Export ▸ PDF… — render, then ask where to put it (the Mac's order).</summary>
    public Task ExportPdfAsync(string source, string title, PageSize size) =>
        Guarded(Strings.Exports.CouldNotExportPdf, async ct =>
        {
            var bytes = await PdfBytesAsync(source, title, size, ct).ConfigureAwait(false);

            var picked = await _pickers.SaveFileAsync(ExportNames.Pdf(title), ExportNames.PdfChoices, ExportNames.PdfExtension).ConfigureAwait(false);
            if (picked is null) return;

            _fileSystem.WriteAllBytesInPlace(picked, bytes);
        });

    /// <summary>
    /// Share ▸ Rendered PDF… — render <em>first</em>, then hand the finished file to the Share sheet.
    /// A <c>DataRequested</c> deferral that rendered a slow PlantUML page inside the sheet would time
    /// out, so the bytes exist before the sheet opens.
    /// </summary>
    public Task SharePdfAsync(string source, string title, PageSize size) =>
        Guarded(Strings.Exports.CouldNotGeneratePdf, async ct =>
        {
            var bytes = await PdfBytesAsync(source, title, size, ct).ConfigureAwait(false);

            var path = FileNames.Combine(_temporaryFolder, ExportNames.Pdf(title));
            _fileSystem.WriteAllBytesInPlace(path, bytes);
            await _share.ShareFileAsync(path, title).ConfigureAwait(false);
        });

    async Task<byte[]> PdfBytesAsync(string source, string title, PageSize size, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(size);

        // StyledForExport rewrites one declaration — the body padding — and nothing else; the paper
        // geometry itself lives in the print settings, never in the document.
        var paper = ScreenHtml.WithWindowsFonts(PdfExport.StyledForExport(ExportDocumentHtml(source, title), size));

        await using var surface = await _renderers.CreateAsync(RenderKind.Paper).ConfigureAwait(false);
        await surface.LoadAsync(paper, ct).ConfigureAwait(false);

        var bytes = await surface.PdfAsync(PrintGeometry.For(size)).ConfigureAwait(false);
        if (bytes.Length == 0) throw new PdfPaginationException();
        return bytes;
    }

    // ─────────────────────────────── HTML (§7.4) ────────────────────────────────

    /// <summary>
    /// Export ▸ HTML… — one file that opens anywhere with no engines and no network: the rendered DOM
    /// with its scripts and stylesheet links stripped, KaTeX's CSS and faces inlined when the
    /// document has math, and the licence notices that inlining obliges.
    /// </summary>
    public Task ExportHtmlAsync(string source, string title) =>
        Guarded(Strings.Exports.CouldNotExportHtml, async ct =>
        {
            // HtmlExport.ExportDocument, not MarkdownHtml.Document: it swaps the page-break rule for
            // the visible one, and loading the wrong string loses the author's page breaks silently.
            var document = HtmlExport.ExportDocument(source, title);

            string captured;
            await using (var surface = await _renderers.CreateAsync(RenderKind.Export).ConfigureAwait(false))
            {
                await surface.LoadAsync(document, ct).ConfigureAwait(false);
                captured = JsonScript.String(await surface.EvalAsync(HtmlExport.CaptureScript).ConfigureAwait(false)) ?? string.Empty;
            }
            if (captured.Length == 0) throw new ExportException(Strings.Exports.PageNotCaptured);

            // The same document as the math gate: the <link> to katex.min.css is already gone from
            // the capture, so asking the captured page would always answer "no maths".
            var page = HtmlExport.PreparePage(captured, document, _readRichAsset);

            var picked = await _pickers.SaveFileAsync(ExportNames.Html(title), ExportNames.HtmlChoices, ExportNames.HtmlExtension).ConfigureAwait(false);
            if (picked is null) return;

            WriteText(picked, page);
        });

    // ─────────────────────────────── EPUB (§7.5) ────────────────────────────────

    /// <summary>
    /// Export ▸ EPUB… for one document. The title is worked out first so the suggested file name
    /// matches it, the picker opens before anything is photographed, and the plan is fixed before the
    /// browser is involved at all.
    /// </summary>
    public Task ExportEpubAsync(string source, string fileName) =>
        Guarded(Strings.Exports.CouldNotExportEpub, async ct =>
        {
            var title = EpubExport.DocumentTitle(source, fileName);

            var picked = await _pickers.SaveFileAsync(ExportNames.Epub(title), ExportNames.EpubChoices, ExportNames.EpubExtension).ConfigureAwait(false);
            if (picked is null) return;

            await BuildEpubAsync(EpubExport.PlanDocument(source, title), picked, ct).ConfigureAwait(false);
        });

    /// <summary>
    /// Book ▸ Export Book ▸ EPUB… — panel first, then build: photographing a book of diagrams takes a
    /// while and the user may cancel. The caller has already flushed the article being edited.
    /// </summary>
    public Task ExportBookEpubAsync(StructuredBook book) =>
        Guarded(Strings.Exports.CouldNotExportEpub, async ct =>
        {
            ArgumentNullException.ThrowIfNull(book);

            var picked = await _pickers.SaveFileAsync(ExportNames.Epub(book.Title), ExportNames.EpubChoices, ExportNames.EpubExtension).ConfigureAwait(false);
            if (picked is null) return;

            await BuildEpubAsync(EpubExport.PlanBook(book), picked, ct).ConfigureAwait(false);
        });

    async Task BuildEpubAsync(EpubPlan plan, string path, CancellationToken ct)
    {
        var snapshots = await _snapshotter.CaptureAsync(plan, ct).ConfigureAwait(false);
        _fileSystem.WriteAllBytesInPlace(path, EpubExport.Assemble(plan, snapshots));
    }

    // ────────────────────────── LaTeX and SVG (§7.6) ────────────────────────────

    /// <summary>Export ▸ LaTeX… — pure Md.Core, no browser: the source is transformed, not rendered.</summary>
    public Task ExportLaTeXAsync(string source, string title) =>
        Guarded(Strings.Exports.CouldNotExportLaTeX, async _ =>
        {
            var document = LaTeXExport.Document(source, title);
            await SaveTextAsync(document, ExportNames.LaTeX(title), ExportNames.LaTeXChoices, ExportNames.LaTeXExtension).ConfigureAwait(false);
        });

    /// <summary>Book ▸ Export Book ▸ LaTeX… — the structured book, likewise with no browser.</summary>
    public Task ExportBookLaTeXAsync(StructuredBook book) =>
        Guarded(Strings.Exports.CouldNotExportLaTeX, async _ =>
        {
            ArgumentNullException.ThrowIfNull(book);

            var document = LaTeXExport.Book(book);
            await SaveTextAsync(document, ExportNames.LaTeX(book.Title), ExportNames.LaTeXChoices, ExportNames.LaTeXExtension).ConfigureAwait(false);
        });

    /// <summary>
    /// Export ▸ Diagram as SVG ▸ … — one rendered diagram, lifted out of the DOM as it was drawn and
    /// made a standalone file. Light, always: an <c>.svg</c> has no theme, Mermaid bakes its colours
    /// in and a plot draws with <c>currentColor</c>.
    /// </summary>
    /// <summary>
    /// The same, from the menu's own argument. WP1 dispatches <c>ExportDiagramSvg</c> with the
    /// ordinal, not the diagram, so the row is re-resolved against the text as it is <em>now</em>:
    /// the menu was built from a 250 ms-old snapshot and the writer may have deleted that fence in
    /// the meantime, in which case the command quietly does nothing rather than exporting the
    /// neighbour.
    /// </summary>
    public Task ExportDiagramSvgAsync(string source, string title, int ordinal)
    {
        foreach (var candidate in Md.Core.Export.DiagramSvg.Diagrams(source))
            if (candidate.Ordinal == ordinal)
                return ExportDiagramSvgAsync(source, title, candidate);

        return Task.CompletedTask;
    }

    public Task ExportDiagramSvgAsync(string source, string title, Md.Core.Export.DiagramSvg.Diagram diagram) =>
        Guarded(Strings.Exports.CouldNotExportSvg, async ct =>
        {
            ArgumentNullException.ThrowIfNull(diagram);

            string? svg;
            await using (var surface = await _renderers.CreateAsync(RenderKind.Export).ConfigureAwait(false))
            {
                await surface.LoadAsync(ExportDocumentHtml(source, title), ct).ConfigureAwait(false);
                svg = JsonScript.String(await surface.EvalAsync(Scripts.DiagramSvg(diagram.Ordinal)).ConfigureAwait(false));
            }

            // null means the engine threw or timed out and its source text is all that is there.
            // Writing an empty .svg would be worse than saying so.
            if (string.IsNullOrEmpty(svg)) throw new ExportException(Strings.Exports.DiagramNotCaptured);

            var page = Md.Core.Export.DiagramSvg.StandaloneDocument(svg);
            await SaveTextAsync(page, ExportNames.DiagramSvg(title, diagram.Ordinal), ExportNames.SvgChoices, ExportNames.SvgExtension).ConfigureAwait(false);
        });

    // ───────────────────────────── TextBundle (§7.7) ────────────────────────────

    /// <summary>
    /// Export ▸ TextBundle… — a <c>.textbundle</c> <em>folder</em>, which is what the product is on
    /// every other port. <c>FileSavePicker</c> cannot create a folder, so this asks for the parent
    /// and does the Replace question itself.
    /// </summary>
    /// <param name="documentPath">
    /// The saved document's path, or null. Images are copied only from the document's own folder, so
    /// an unsaved document exports with an empty <c>assets/</c> and every reference left as written.
    /// </param>
    public Task ExportTextBundleAsync(string source, string? documentPath, string title) =>
        Guarded(Strings.Exports.CouldNotExportTextBundle, async _ =>
        {
            var parent = await _pickers.PickFolderAsync(Strings.Exports.ChooseTextBundleFolder).ConfigureAwait(false);
            if (parent is null) return;

            var name = ExportNames.TextBundleFolder(title);
            var target = FileNames.Combine(parent, name);

            if ((_fileSystem.DirectoryExists(target) || _fileSystem.FileExists(target))
                && !await _alerts.ConfirmReplaceAsync(Strings.Exports.FolderExists(name)).ConfigureAwait(false))
                return;

            var rewrite = TextBundle.ExportRewriting(source, AssetReader.Beside(documentPath, _fileSystem, _identity));
            _writeBundle(TextBundle.BundleWrapper(rewrite.Text, rewrite.Assets), target);
        });

    // ─────────────────────────────── Share (§7.8) ───────────────────────────────

    /// <summary>
    /// Share ▸ Source… — the real file when the document has one, otherwise a UTF-8 copy in the temp
    /// folder named after the title. The Book window's detail pane publishes no path, so from there
    /// this always shares a copy of the article text, exactly as on the Mac.
    /// </summary>
    /// <remarks>
    /// Silent on failure: macOS's <c>shareSource</c> has no alert of its own, and §7.9 defines no
    /// title for one. The Share sheet simply does not appear.
    /// </remarks>
    public Task ShareSourceAsync(string? path, string text, string title) =>
        Serialised(async () =>
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && _fileSystem.FileExists(path))
                {
                    await _share.ShareFileAsync(path, title).ConfigureAwait(false);
                    return;
                }

                var copy = FileNames.Combine(_temporaryFolder, ExportNames.Source(title));
                WriteText(copy, text);
                await _share.ShareFileAsync(copy, title).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // No alert exists for this one on any port.
            }
        });

    // ──────────────────────────────── plumbing ──────────────────────────────────

    /// <summary>Core's export page: light, <c>export: true</c>, and pure — the base of every rendered flow.</summary>
    static string ExportDocumentHtml(string source, string title) =>
        MarkdownHtml.Document(source, title, dark: false, export: true);

    /// <summary>The same page with the app's typewriter style: screen and paper only, never a file (§4.3).</summary>
    internal static string PaperHtml(string source, string title) =>
        ScreenHtml.WithWindowsFonts(ExportDocumentHtml(source, title));

    async Task SaveTextAsync(string text, string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string extension)
    {
        var picked = await _pickers.SaveFileAsync(suggestedName, choices, extension).ConfigureAwait(false);
        if (picked is null) return;
        WriteText(picked, text);
    }

    /// <summary>UTF-8 without a BOM, everywhere: what every other port writes, and what a browser, a
    /// TeX distribution and an SVG parser all expect.</summary>
    void WriteText(string path, string text) => _fileSystem.WriteAllBytesInPlace(path, Utf8NoBom.GetBytes(text));

    Task Guarded(string alertTitle, Func<CancellationToken, Task> body) =>
        Serialised(async () =>
        {
            try
            {
                await body(_cancellation.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The window closed under the export; there is no one left to tell.
            }
            catch (Exception e)
            {
                await _alerts.WarnAsync(alertTitle, e.Message).ConfigureAwait(false);
            }
        });

    async Task Serialised(Func<Task> body, bool announceBusy = true)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        if (announceBusy) BusyChanged?.Invoke(true);
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            if (announceBusy) BusyChanged?.Invoke(false);
            _gate.Release();
        }
    }
}
