namespace Md.App.Logic.Preview;

/// <summary>
/// The Windows typography graft (§4.3). The shared stylesheet's prose family is
/// <c>"American Typewriter", "Courier New", serif</c> and stays byte-identical across the ports, so
/// on Windows prose would fall all the way to Courier New. The app — never Md.Core — appends one
/// style element that puts <b>Lucida Sans Typewriter</b> in front: a typewriter face that ships with
/// Windows itself in Regular, Bold and Italic, so md reads as a typewriter on Windows the way
/// American Typewriter does on the Mac. It is deliberately <em>not</em> the family's Georgia
/// stand-in (md.vscode, md.Android, the shared EPUB CSS) — nettrash chose the typewriter over
/// matching the other ports' screen fallback, and nothing about it is load-bearing for parity
/// because it never reaches a file. Code is untouched: the stylesheet's
/// <c>code, pre { font-family: "Courier New", monospace; }</c> rule wins by specificity, and KaTeX,
/// Mermaid, Graphviz and PlantUML carry their own faces.
///
/// Exactly three callers, all of them screen or paper: the live preview
/// (<see cref="PreviewCoordinator"/>, <see cref="RenderKind.Screen"/>), Print and PDF
/// (<see cref="RenderKind.Paper"/>). HTML, EPUB and SVG exports go through
/// <see cref="RenderKind.Export"/> and load the pure Core HTML, so nothing Windows-specific reaches
/// a file another port pins byte for byte.
/// </summary>
public static class ScreenHtml
{
    /// <summary>The style element, exactly as it goes into the page.</summary>
    public const string FontStyle = "<style id=\"md-win-fonts\">body{font-family:\"Lucida Sans Typewriter\",\"Courier New\",serif;}</style>";

    /// <summary>What the inserted text is looked up by, so a second call is a no-op.</summary>
    public const string IdMarker = "id=\"md-win-fonts\"";

    const string HeadEnd = "</head>";

    /// <summary>
    /// <paramref name="coreHtml"/> with <see cref="FontStyle"/> on its own line before the first
    /// <c>&lt;/head&gt;</c>. Idempotent, and a fragment with no head comes back untouched (nothing
    /// Md.Core's document writer ever produces, but the Print pipeline hands this method whatever it
    /// is given).
    /// </summary>
    public static string WithWindowsFonts(string coreHtml)
    {
        ArgumentNullException.ThrowIfNull(coreHtml);
        if (coreHtml.Contains(IdMarker, StringComparison.Ordinal)) return coreHtml;

        var head = coreHtml.IndexOf(HeadEnd, StringComparison.Ordinal);
        if (head < 0) return coreHtml;

        return string.Concat(coreHtml.AsSpan(0, head), "\n", FontStyle, coreHtml.AsSpan(head));
    }
}
