namespace Md.Core.Tests;

/// <summary>
/// The one deliberate patch inside a vendored engine, pinned (md.vscode's
/// <c>test/plantuml-limit.test.ts</c>, ported).
///
/// The browser build of PlantUML (1.2026.4beta4) compares a laid-out diagram's width and
/// height against <c>4096.0</c> and throws the drawing away past either — "Diagram too large
/// for browser rendering: 7655x1445 (max 4096)" — so a 48-participant sequence diagram never
/// rendered. md.win's consumers take the SVG itself (preview, HTML, SVG, PDF) or the source
/// (LaTeX); the EPUB snapshot (Scripts.CanvasRasterise) draws the element's laid-out rect, which
/// MarkdownCss caps with <c>.plantuml svg { max-width: 100% }</c>, so the gate
/// defended a raster budget nothing here can overspend. Both literals are raised to 1.0E9,
/// exactly as md.vscode did — the patched <c>rich/plantuml.js</c> is byte-identical to the
/// extension's.
///
/// A re-vendored engine would bring the gate back silently. This test fails the day that
/// happens, by design: the pinned fragment is minifier output a fresh build will not
/// reproduce. Re-apply with
/// <c>perl -pi -e 's/o&lt;=4096\.0\)\{p=m\.bDm;if\(p&lt;=4096\.0\)/o&lt;=1.0E9){p=m.bDm;if(p&lt;=1.0E9)/' src/Md.App/rich/plantuml.js</c>
/// (adjusting the identifiers to the new build's), re-verify in a browser, update the pin.
/// </summary>
public class PlantUmlGateTests
{
    private const string PatchedGate = "o<=1.0E9){p=m.bDm;if(p<=1.0E9)";

    private static string Engine()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "md.slnx"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "src", "Md.App", "rich", "plantuml.js"), System.Text.Encoding.Latin1);
    }

    [Fact]
    public void HoldsTheRaisedGateOnce()
    {
        var source = Engine();
        Assert.Equal(1, source.Split(PatchedGate).Length - 1);
    }

    [Fact]
    public void HoldsNo4096PixelComparison()
    {
        // `4096` alone appears legitimately (palette names, Unicode tables); the gate compared
        // doubles, and no other `4096.0` exists in the build.
        Assert.DoesNotContain("4096.0", Engine(), StringComparison.Ordinal);
    }
}
