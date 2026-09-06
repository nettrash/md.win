using System.Text.Json;
using Md.App.Logic.Export;
using Md.Core.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// One <see cref="ExportPipeline"/> wired to fakes, plus the two things every export test needs: a
/// way to script what the page answers, and a way to run the whole flow to completion without a
/// clock. Nothing here sleeps — <see cref="Drain"/> advances the fake scheduler until the export is
/// done, which is why an export that forgot to await something fails loudly instead of hanging.
/// </summary>
sealed class ExportHarness : IDisposable
{
    readonly NoSyncContext _deterministic = new();

    public const string Temp = @"C:\Temp";
    public const string Docs = @"C:\Docs";
    public const string Destination = @"C:\Docs\out";

    public FakeRenderSurfaceFactory Renderers { get; } = new();
    public FakePickers Pickers { get; } = new();
    public FakeShare Share { get; } = new();
    public FakeAlerts Alerts { get; } = new();
    public FakeFileSystem Files { get; } = new();
    public FakeScheduler Scheduler { get; } = new();
    public FakeFileIdentity Identity { get; } = new();

    /// <summary>What each evaluated script answers, as raw JSON. Unscripted is the four characters <c>null</c>.</summary>
    public Func<string, string>? Eval { get; set; }

    public byte[] Png { get; set; } = [0x89, 0x50, 0x4E, 0x47];
    public byte[] Pdf { get; set; } = "%PDF-1.7\n"u8.ToArray();
    public Exception? LoadFailure { get; set; }

    /// <summary>The TextBundle folders written, instead of a directory tree on a real disk.</summary>
    public List<(BundleWrapper Wrapper, string Folder)> Bundles { get; } = [];

    /// <summary>The bundled engine assets a test wants <c>readRichAsset</c> to find.</summary>
    public Dictionary<string, byte[]> Assets { get; } = new(StringComparer.Ordinal);

    /// <summary>Every key <c>readRichAsset</c> was asked for, in order — the math gate leaves its fingerprints here.</summary>
    public List<string> AssetKeys { get; } = [];

    public ExportPipeline Pipeline { get; }

    /// <remarks>
    /// The harness holds a <see cref="NoSyncContext"/> for its lifetime, so
    /// <c>using var h = new ExportHarness();</c> is not optional — see that type for why.
    /// </remarks>
    public ExportHarness()
    {
        Files.AddDirectory(Temp);
        Files.AddDirectory(Docs);

        Renderers.Create = kind => new FakeRenderSurface
        {
            Kind = kind,
            EvalHandler = script => Eval?.Invoke(script) ?? "null",
            PngBytes = Png,
            PdfBytes = Pdf,
            LoadFailure = LoadFailure,
        };

        Pipeline = new ExportPipeline(
            Renderers, Pickers, Share, Alerts, Files, ReadAsset,
            Scheduler, Identity, Temp, (wrapper, folder) => Bundles.Add((wrapper, folder)));
    }

    byte[]? ReadAsset(string key)
    {
        AssetKeys.Add(key);
        return Assets.TryGetValue(key, out var bytes) ? bytes : null;
    }

    /// <summary>The surfaces the flow asked for, in order of creation.</summary>
    public IReadOnlyList<FakeRenderSurface> Created => Renderers.Created;

    /// <summary>Every string any surface was asked to load, in order.</summary>
    public IReadOnlyList<string> Loaded => Renderers.Created.SelectMany(s => s.LoadedHtml).ToList();

    /// <summary>The picker will hand back <see cref="Destination"/> with this extension.</summary>
    public string WillSave(string extension)
    {
        var path = Destination + extension;
        Pickers.SaveAnswers.Enqueue(path);
        return path;
    }

    /// <summary>Run an export to completion, surfacing anything it threw (the pipeline swallows into alerts, so this is for bugs).</summary>
    /// <remarks>
    /// Firing the timers is not quite enough. <see cref="SemaphoreSlim"/> completes its async
    /// waiters with <c>RunContinuationsAsynchronously</c> — it must, or <c>Release()</c> would run
    /// arbitrary code under its own lock — so an export that was queued behind another resumes on a
    /// <b>thread-pool</b> thread, not inline on this one. Advancing a fake clock takes microseconds,
    /// so without the settle below this loop reaches its assertion before that thread has started:
    /// <c>ASecondExportWaitsForTheFirstToFinish</c> failed 8 runs out of 8 on its own and passed
    /// every time inside the full suite, purely because a warm pool won the race and a cold one did
    /// not. The wait is only ever paid on a step that is genuinely waiting for that hand-off, and
    /// only until it happens.
    /// </remarks>
    public void Drain(Task task, int maxSteps = 200)
    {
        for (var i = 0; i < maxSteps && !task.IsCompleted; i++)
        {
            Scheduler.Advance(TimeSpan.FromSeconds(1));
            if (!task.IsCompleted) task.Wait(TimeSpan.FromMilliseconds(25));
        }
        Assert.True(task.IsCompleted, "the export never finished; something awaits a timer nobody fires");
        task.GetAwaiter().GetResult();
    }

    public void Dispose() => _deterministic.Dispose();

    public string? WrittenText(string path) => Files.Text(path);
    public byte[]? WrittenBytes(string path) => Files.Bytes(path);

    // ---- scripted answers ----

    /// <summary>A JSON string result, as <c>ExecuteScriptAsync</c> encodes one.</summary>
    public static string Json(string? value) => JsonSerializer.Serialize(value);

    /// <summary>The five-column answer <c>Scripts.RichElements</c> gives, one row per element.</summary>
    public static string Rects(int count, double width = 120, double height = 40)
    {
        var rows = new double[count][];
        for (var i = 0; i < count; i++) rows[i] = [10, 20 + (i * 60), width, height, i % 2];
        return JsonSerializer.Serialize(rows);
    }
}
