using Md.App.Logic.Preview;

namespace Md.App.Logic.Seams;

/// <summary>
/// Makes one <see cref="IRenderSurface"/> per export; the kind decides which HTML entry point the
/// surface serves (§4.3). App: <c>ExportRenderer</c> per export; tests: <c>FakeRenderSurfaceFactory</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IRenderSurfaceFactory
{
    Task<IRenderSurface> CreateAsync(RenderKind kind);
}
