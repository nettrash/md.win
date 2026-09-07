// The WebView2 self-test (shell-design.md §11.4). A tests/Md.App.Tests project is impossible — app
// types need Windows and the XAML compiler — so the assertions that only a real browser can settle
// live in the app as a hidden mode: `md.exe --selftest <outDir>` drives the real ExportRenderer and
// the real ExportPipeline over fixture documents, writes report.json and exits 0 or 1.
//
// This is the only place the WebView2 half of WP6 is ever proved. It is compiled in only when CI
// asks for it (-p:SelfTest=true defines SELFTEST); a Store build carries the stub below and nothing
// else, so the mode cannot be reached from a shipped package.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Md.App.Logic.Export;
using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.Core.Document;
using Md.Core.Export;
using Md.Core.Markdown;
using Microsoft.UI.Dispatching;

namespace Md.App.Services;

/// <summary>
/// <c>md.exe --selftest &lt;outDir&gt;</c>. <see cref="TryStart"/> is called first thing on activation;
/// it answers false in every build that is not a self-test build and in every run without the flag,
/// and the app carries on as normal.
/// </summary>
internal static partial class SelfTest
{
    public const string Flag = "--selftest";

    public const string ReportFileName = "report.json";

    /// <summary>
    /// True when this build carries the self-test <b>and</b> this process was asked for it. Read
    /// before the single-instance redirection of §1.1: a self-test run drives its own WebView2 and
    /// exits with its own code, so it must never hand its arguments to a running md and exit 0
    /// having proved nothing. False in every shipped build, whatever the command line says.
    /// </summary>
    public static bool Requested => IsAvailable() && Array.IndexOf(Environment.GetCommandLineArgs(), Flag) >= 0;

    /// <summary>
    /// True when this process is a self-test run and has taken over: the caller must open no window
    /// and route no activation. The run ends by exiting the process.
    /// </summary>
    public static bool TryStart()
    {
        var arguments = Environment.GetCommandLineArgs();
        var flag = Array.IndexOf(arguments, Flag);
        if (flag < 0) return false;

        var directory = flag + 1 < arguments.Length && !arguments[flag + 1].StartsWith('-')
            ? arguments[flag + 1]
            : Path.Combine(Path.GetTempPath(), "md-selftest");

        return Start(directory);
    }

    /// <summary>Takes the process over and answers true, or — in a shipped build — answers false.</summary>
    private static partial bool Start(string directory);

    /// <summary>Whether this build was compiled with the self-test at all (<c>-p:SelfTest=true</c>).</summary>
    private static partial bool IsAvailable();
}

#if SELFTEST

internal static partial class SelfTest
{
    /// <summary>One assertion and its answer, as report.json carries it.</summary>
    sealed record Check(string Name, bool Passed, string Detail);

    // ── fixtures: small on purpose, so a failure names one engine rather than a whole example ──
    //
    // Spelled as joined arrays, not raw string literals: this whole region is inside `#if SELFTEST`,
    // and in a region the preprocessor has switched off a line whose first non-blank character is `#`
    // is read as a directive — so a Markdown heading would not compile in the shipped build.

    static readonly string MathSource = string.Join("\n",
    [
        "# Maths",
        "",
        "Inline $x^2 + y^2 = z^2$ and a display block:",
        "",
        "$$\\int_0^1 x\\,dx = \\tfrac12$$",
        "",
        "Chemistry through mhchem: $\\ce{H2O}$ and $\\ce{SO4^2-}$.",
    ]);

    static readonly string DiagramSource = string.Join("\n",
    [
        "# Diagrams",
        "",
        "```mermaid",
        "graph TD; A-->B; B-->C;",
        "```",
        "",
        "```graphviz",
        "digraph { a -> b; }",
        "```",
        "",
        "```plantuml",
        "@startuml",
        "Alice -> Bob: hello",
        "@enduml",
        "```",
    ]);

    /// <summary>
    /// Two curves, so <c>plot.twoSeries</c> means something. They are <em>formulas</em>: the plot
    /// grammar has `x:`, `y:`, `title:`, `xlabel:`, `ylabel:`, `legend`, `grid`, `axes`, `width`,
    /// `height`, `samples` and a line per curve — and no `data:` directive at all. The fixture used
    /// to spell one, so the engine drew nothing and both plot checks failed while
    /// <c>Examples\08-Plots.md</c>, which is written in the real grammar, rendered perfectly.
    /// </summary>
    static readonly string PlotSource = string.Join("\n",
    [
        "# Plot",
        "",
        "```plot",
        "x: -10..10",
        "y: -1.2..1.2",
        "title: Two series",
        "xlabel: x",
        "ylabel: y",
        "sin(x)",
        "cos(x)",
        "```",
    ]);

    static readonly string CodeSource = string.Join("\n",
    [
        "# Code",
        "",
        "```csharp",
        "// a comment",
        "public static string Greet(string name) => $\"hello {name}\";",
        "```",
    ]);

    /// <summary>Three sections separated by page breaks, and a code line long enough to have to wrap.</summary>
    static readonly string PagesSource = string.Join("\n",
    [
        "# First",
        "",
        "Alpha paragraph.",
        "",
        "\\newpage",
        "",
        "# Second",
        "",
        "Beta paragraph.",
        "",
        "\\newpage",
        "",
        "# Third",
        "",
        "Gamma paragraph, and a long code line follows.",
        "",
        "```text",
        "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen "
        + "seventeen eighteen nineteen twenty twentyone twentytwo twentythree twentyfour twentyfive twentysix "
        + "twentyseven twentyeight twentynine thirty thirtyone thirtytwo thirtythree thirtyfour thirtyfive "
        + "thirtysix thirtyseven thirtyeight thirtynine ENDMARKER",
        "```",
    ]);

    private static partial bool Start(string directory)
    {
        _ = RunAsync(directory);
        return true;
    }

    private static partial bool IsAvailable() => true;

    static async Task RunAsync(string directory)
    {
        var checks = new List<Check>();
        var started = DateTimeOffset.UtcNow;

        try
        {
            Directory.CreateDirectory(directory);
            var scheduler = new DispatcherScheduler(DispatcherQueue.GetForCurrentThread());

            using var host = new Web.ExportHostWindow();
            await RunChecksAsync(host, scheduler, directory, checks);
        }
        catch (Exception e)
        {
            checks.Add(new Check("selftest.completed", false, e.ToString()));
        }

        var failed = checks.Count(c => !c.Passed);
        Write(directory, started, checks);
        Environment.Exit(failed == 0 && checks.Count > 0 ? 0 : 1);
    }

    static async Task RunChecksAsync(Web.ExportHostWindow host, IScheduler scheduler, string directory, List<Check> checks)
    {
        await EnginesAsync(host, scheduler, checks);
        await SelfContainedHtmlAsync(host, scheduler, directory, checks);
        await EpubAsync(host, scheduler, directory, checks);
        await PdfAsync(host, scheduler, directory, checks);
        await ExamplesAsync(host, scheduler, checks);
    }

    // ── the engines, offline ──

    static async Task EnginesAsync(Web.ExportHostWindow host, IScheduler scheduler, List<Check> checks)
    {
        await using var renderer = await Web.ExportRenderer.OffCanvasAsync(host.Host, scheduler);

        await renderer.LoadAsync(Export(MathSource, "Maths"), CancellationToken.None);
        checks.Add(await AtLeast(renderer, "katex.typesets", ".katex", 1));
        checks.Add(await Exactly(renderer, "katex.mhchem.noErrors", ".katex-error", 0));

        await renderer.LoadAsync(Export(DiagramSource, "Diagrams"), CancellationToken.None);
        checks.Add(await AtLeast(renderer, "mermaid.renders", ".mermaid svg", 1));
        checks.Add(await AtLeast(renderer, "graphviz.renders", ".graphviz svg", 1));
        checks.Add(await AtLeast(renderer, "plantuml.renders", ".plantuml svg", 1));

        await renderer.LoadAsync(Export(CodeSource, "Code"), CancellationToken.None);
        checks.Add(await AtLeast(renderer, "highlight.paints", "code.hljs", 1));
        checks.Add(await AtLeast(renderer, "highlight.tokens", "code.hljs .hljs-keyword, code.hljs .hljs-string, code.hljs .hljs-comment", 1));

        await renderer.LoadAsync(Export(PlotSource, "Plot"), CancellationToken.None);
        checks.Add(await Exactly(renderer, "plot.oneSvg", ".plot svg", 1));
        checks.Add(await AtLeast(renderer, "plot.twoSeries", ".plot svg polyline", 2));
        // The plot is drawn by Md.Core, not by an engine: exactly one script tag, md-init.js's own.
        checks.Add(await Exactly(renderer, "plot.noEngineScript", "script[src*=\"plot\"]", 0));
    }

    // ── the self-contained HTML export, opened from disk ──

    static async Task SelfContainedHtmlAsync(Web.ExportHostWindow host, IScheduler scheduler, string directory, List<Check> checks)
    {
        var destination = Path.Combine(directory, "self-contained.html");
        var pipeline = Pipeline(host, scheduler, _ => destination, out var alerts);

        await pipeline.ExportHtmlAsync(MathSource + "\n\n" + DiagramSource, "Self Contained");
        checks.Add(new Check("html.exported", File.Exists(destination) && alerts.Warnings.Count == 0, Detail(alerts, destination)));
        if (!File.Exists(destination)) return;

        var bytes = await File.ReadAllBytesAsync(destination);
        checks.Add(new Check(
            "html.utf8NoBom",
            !(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF),
            bytes.Length.ToString(CultureInfo.InvariantCulture) + " bytes"));

        await using var renderer = await Web.ExportRenderer.OffCanvasAsync(host.Host, scheduler);
        await renderer.NavigateAsync(new Uri(destination).AbsoluteUri, CancellationToken.None);

        checks.Add(await Says(renderer, "html.standsAlone.noEngines", "document.querySelectorAll('script').length", 0));
        checks.Add(await Says(renderer, "html.standsAlone.noAssetUrls", "document.documentElement.outerHTML.split('rich/').length - 1", 0));
        checks.Add(await Text(renderer, "html.standsAlone.standardsMode", "document.compatMode", "CSS1Compat"));
        checks.Add(await AtLeast(renderer, "html.standsAlone.keepsFormulas", ".katex", 1));
        checks.Add(await AtLeast(renderer, "html.standsAlone.keepsDiagrams", "svg", 1));
    }

    // ── the EPUB's photographs ──

    static async Task EpubAsync(Web.ExportHostWindow host, IScheduler scheduler, string directory, List<Check> checks)
    {
        var destination = Path.Combine(directory, "document.epub");
        var pipeline = Pipeline(host, scheduler, _ => destination, out var alerts);
        var source = MathSource + "\n\n" + DiagramSource;

        await pipeline.ExportEpubAsync(source, "document.md");

        var expected = EpubExport.PlanDocument(source, EpubExport.DocumentTitle(source, "document.md")).Units[0].RichElements.Count;
        checks.Add(new Check("epub.exported", File.Exists(destination) && alerts.Warnings.Count == 0, Detail(alerts, destination)));
        if (!File.Exists(destination)) return;

        var entries = ZipReader.Entries(await File.ReadAllBytesAsync(destination), _ => true);
        var images = entries?.Count(e => e.Name.StartsWith("OEBPS/images/", StringComparison.Ordinal) && e.Name.EndsWith(".png", StringComparison.Ordinal)) ?? -1;

        // The count check the whole EPUB rests on: one photograph per rich container, no drift.
        checks.Add(new Check(
            "epub.snapshotCountMatchesMarkup",
            images == expected,
            $"{images} images for {expected} rich elements"));
        checks.Add(new Check(
            "epub.imagesAreRealPngs",
            entries is not null && entries.Where(e => e.Name.EndsWith(".png", StringComparison.Ordinal)).All(e => IsPng(e.Data)),
            "PNG signature"));
    }

    static bool IsPng(byte[] data) =>
        data.Length > 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;

    // ── the PDF's geometry, measured out of the file itself ──

    static async Task PdfAsync(Web.ExportHostWindow host, IScheduler scheduler, string directory, List<Check> checks)
    {
        foreach (var size in new[] { PageSize.A4, PageSize.SixByNine })
        {
            var destination = Path.Combine(directory, "pages-" + size.Id + ".pdf");
            var pipeline = Pipeline(host, scheduler, _ => destination, out var alerts);

            await pipeline.ExportPdfAsync(PagesSource, "Pages", size);
            checks.Add(new Check("pdf." + size.Id + ".exported", File.Exists(destination) && alerts.Warnings.Count == 0, Detail(alerts, destination)));
            if (!File.Exists(destination)) continue;

            var pdf = Latin1(await File.ReadAllBytesAsync(destination));

            // Chromium writes page dictionaries uncompressed, so a regex is enough and no PDF
            // library has to be taken on for one measurement.
            var boxes = MediaBoxPattern().Matches(pdf)
                .Select(m => (Width: Parse(m.Groups[3].Value), Height: Parse(m.Groups[4].Value)))
                .ToList();
            var pages = PagePattern().Matches(pdf).Count;

            checks.Add(new Check(
                "pdf." + size.Id + ".mediaBox",
                boxes.Count > 0 && boxes.All(b => Math.Abs(b.Width - size.Width) <= 1 && Math.Abs(b.Height - size.Height) <= 1),
                string.Join(", ", boxes.Select(b => Invariant(b.Width) + "x" + Invariant(b.Height)))));

            // Three \newpage-separated sections must paginate to exactly three pages.
            checks.Add(new Check("pdf." + size.Id + ".threePages", pages == 3, pages.ToString(CultureInfo.InvariantCulture) + " pages"));
        }
    }

    // ── every bundled example renders without an engine failing ──

    static async Task ExamplesAsync(Web.ExportHostWindow host, IScheduler scheduler, List<Check> checks)
    {
        var library = new ExampleLibrary();
        if (library.Examples.Count == 0)
        {
            checks.Add(new Check("examples.present", false, ExampleLibrary.FolderPath));
            return;
        }

        await using var renderer = await Web.ExportRenderer.OffCanvasAsync(host.Host, scheduler);
        foreach (var example in library.Examples)
        {
            if (library.ReadText(example) is not { } text) continue;

            await renderer.LoadAsync(Export(text, example.Name), CancellationToken.None);
            checks.Add(await Exactly(renderer, "example." + example.FileName + ".noMathErrors", ".katex-error", 0));
            checks.Add(await Says(renderer, "example." + example.FileName + ".complete",
                "document.documentElement.getAttribute('data-md-render-complete') === '1' ? 1 : 0", 1));
        }
    }

    // ── plumbing ──

    static string Export(string source, string title) => MarkdownHtml.Document(source, title, dark: false, export: true);

    static ExportPipeline Pipeline(Web.ExportHostWindow host, IScheduler scheduler, Func<string, string> destination, out RecordingAlerts alerts)
    {
        alerts = new RecordingAlerts();
        return new ExportPipeline(
            new Web.ExportRendererFactory(host.Host, scheduler),
            new FixedPickers(destination),
            new NoShare(),
            alerts,
            Md.App.Logic.Documents.SystemIoFileSystem.Instance,
            Md.App.Export.RichAssets.Read,
            scheduler,
            Md.App.Logic.Documents.FileIdentity.Instance,
            Md.App.Export.TemporaryFiles.Folder);
    }

    static string Detail(RecordingAlerts alerts, string destination) =>
        alerts.Warnings.Count == 0 ? destination : string.Join("; ", alerts.Warnings.Select(w => w.Title + ": " + w.Message));

    static async Task<Check> Says(Web.ExportRenderer renderer, string name, string expression, double expected)
    {
        var value = JsonScript.Number(await renderer.EvalAsync(expression));
        return new Check(name, value == expected, $"{Invariant(value ?? double.NaN)} (wanted {Invariant(expected)})");
    }

    static async Task<Check> Text(Web.ExportRenderer renderer, string name, string expression, string expected)
    {
        var value = JsonScript.String(await renderer.EvalAsync(expression));
        return new Check(name, string.Equals(value, expected, StringComparison.Ordinal), value ?? "null");
    }

    static Task<Check> AtLeast(Web.ExportRenderer renderer, string name, string selector, int minimum) =>
        Count(renderer, name, selector, count => count >= minimum, "at least " + minimum.ToString(CultureInfo.InvariantCulture));

    static Task<Check> Exactly(Web.ExportRenderer renderer, string name, string selector, int expected) =>
        Count(renderer, name, selector, count => count == expected, "exactly " + expected.ToString(CultureInfo.InvariantCulture));

    static async Task<Check> Count(Web.ExportRenderer renderer, string name, string selector, Func<double, bool> ok, string wanted)
    {
        var script = "document.querySelectorAll(" + JsonSerializer.Serialize(selector) + ").length";
        var value = JsonScript.Number(await renderer.EvalAsync(script));
        return new Check(name, value is { } count && ok(count), $"{Invariant(value ?? double.NaN)} (wanted {wanted})");
    }

    static void Write(string directory, DateTimeOffset started, IReadOnlyList<Check> checks)
    {
        var report = new
        {
            app = Md.App.Logic.Strings.AppName,
            startedUtc = started.ToString("O", CultureInfo.InvariantCulture),
            finishedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            passed = checks.Count(c => c.Passed),
            failed = checks.Count(c => !c.Passed),
            checks = checks.Select(c => new { name = c.Name, passed = c.Passed, detail = c.Detail }),
        };

        try
        {
            File.WriteAllText(
                Path.Combine(directory, ReportFileName),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch (Exception)
        {
            // Nothing left to report to; the exit code still carries the verdict.
        }
    }

    static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

    static double Parse(string value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

    static string Invariant(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    [System.Text.RegularExpressions.GeneratedRegex(@"/MediaBox\s*\[\s*([\d.-]+)\s+([\d.-]+)\s+([\d.]+)\s+([\d.]+)\s*\]")]
    private static partial System.Text.RegularExpressions.Regex MediaBoxPattern();

    [System.Text.RegularExpressions.GeneratedRegex(@"/Type\s*/Page[^s]")]
    private static partial System.Text.RegularExpressions.Regex PagePattern();

    /// <summary>Every save picker answers with the file the run has already decided on.</summary>
    sealed class FixedPickers(Func<string, string> destination) : IPickers
    {
        public Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions) => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension) =>
            Task.FromResult<string?>(destination(suggestedName));

        public Task<string?> PickFolderAsync(string? title) => Task.FromResult<string?>(null);
    }

    /// <summary>Alerts become failures in the report rather than dialogs nobody is there to dismiss.</summary>
    sealed class RecordingAlerts : IAlerts
    {
        public List<(string Title, string Message)> Warnings { get; } = [];

        public Task WarnAsync(string title, string message)
        {
            Warnings.Add((title, message));
            return Task.CompletedTask;
        }

        public Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel) => Task.FromResult<string?>(null);
        public Task<bool> ConfirmDeleteAsync(string title, string message) => Task.FromResult(false);
        public Task<CloseChoice> AskSaveChangesAsync(string title) => Task.FromResult(CloseChoice.Cancel);
        public Task<bool> ConfirmReplaceAsync(string message) => Task.FromResult(true);
    }

    sealed class NoShare : IShare
    {
        public Task ShareFileAsync(string path, string title) => Task.CompletedTask;
    }
}

#else

internal static partial class SelfTest
{
    /// <summary>
    /// The shipped build. The mode is not merely disabled — it is not compiled, so a packaged md.exe
    /// has no self-test code in it at all and the flag does nothing.
    /// </summary>
    private static partial bool Start(string directory) => false;

    private static partial bool IsAvailable() => false;
}

#endif
