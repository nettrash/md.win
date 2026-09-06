using Md.App.Logic.Export;
using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// Recording <see cref="IRenderSurface"/>: the HTML loaded, scripts, captures, heights and PDF
/// geometries are kept; results are scripted (<see cref="EvalHandler"/> / <see cref="EvalResults"/>,
/// <see cref="PngBytes"/>, <see cref="PdfBytes"/>); <see cref="LoadFailure"/> and
/// <see cref="PdfFailure"/> make the corresponding call throw. Disposal is observable.
/// </summary>
public sealed class FakeRenderSurface : IRenderSurface
{
    public RenderKind Kind { get; init; }

    public List<string> LoadedHtml { get; } = [];
    public List<string> Evals { get; } = [];
    public List<(RectD Rect, double Scale)> Captures { get; } = [];
    public List<double> Heights { get; } = [];
    public List<PrintGeometry> PdfRequests { get; } = [];
    public bool IsDisposed { get; private set; }

    public Queue<string> EvalResults { get; } = new();
    public Func<string, string>? EvalHandler { get; set; }
    public byte[] PngBytes { get; set; } = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    public byte[] PdfBytes { get; set; } = "%PDF-1.7\n%fake\n"u8.ToArray();
    public Exception? LoadFailure { get; set; }
    public Exception? PdfFailure { get; set; }

    public Task LoadAsync(string html, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LoadedHtml.Add(html);
        return LoadFailure is { } f ? Task.FromException(f) : Task.CompletedTask;
    }

    public Task<string> EvalAsync(string script)
    {
        Evals.Add(script);
        var result = EvalHandler?.Invoke(script) ?? (EvalResults.Count > 0 ? EvalResults.Dequeue() : "null");
        return Task.FromResult(result);
    }

    public Task<byte[]> CaptureRegionPngAsync(RectD cssRect, double scale)
    {
        Captures.Add((cssRect, scale));
        return Task.FromResult(PngBytes.ToArray());
    }

    public Task SetHeightAsync(double cssPx)
    {
        Heights.Add(cssPx);
        return Task.CompletedTask;
    }

    public Task<byte[]> PdfAsync(PrintGeometry geometry)
    {
        PdfRequests.Add(geometry);
        return PdfFailure is { } f ? Task.FromException<byte[]>(f) : Task.FromResult(PdfBytes.ToArray());
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// <see cref="IRenderSurfaceFactory"/> handing out <see cref="FakeRenderSurface"/>s (a fresh one per
/// call, or whatever <see cref="Create"/> returns) and recording the <see cref="RenderKind"/> asked for —
/// how a test pins "HTML export renders as Export, PDF as Paper".
/// </summary>
public sealed class FakeRenderSurfaceFactory : IRenderSurfaceFactory
{
    public List<RenderKind> Kinds { get; } = [];
    public List<FakeRenderSurface> Created { get; } = [];
    public Func<RenderKind, FakeRenderSurface>? Create { get; set; }

    public Task<IRenderSurface> CreateAsync(RenderKind kind)
    {
        var surface = Create?.Invoke(kind) ?? new FakeRenderSurface { Kind = kind };
        Kinds.Add(kind);
        Created.Add(surface);
        return Task.FromResult<IRenderSurface>(surface);
    }
}
