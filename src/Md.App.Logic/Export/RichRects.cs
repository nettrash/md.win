using System.Text.Json;
using Md.App.Logic.Seams;

namespace Md.App.Logic.Export;

/// <summary>One measured rich element: its page-space rectangle in CSS pixels, and whether it is a formula.</summary>
/// <remarks>
/// The fifth column decides the EPUB's <c>alt</c> text on macOS; Md.Core derives it from its own
/// markup scan instead, so this port carries it for the self-test and for anyone reading the report,
/// not for the file that is written.
/// </remarks>
public readonly record struct RichRect(RectD Rect, bool IsMath);

/// <summary>
/// The JSON <c>Scripts.RichElements</c> hands back: an array of five-number rows, in DOM order.
/// </summary>
/// <remarks>
/// <para>
/// Two rules, both of which cost a corrupted book if they are got wrong. Rows that are not five
/// numbers are <b>dropped</b> (the Mac's <c>count != 5</c> guard — a page that answered with
/// something else is not a measurement). Degenerate rects — zero width, zero height — are
/// <b>kept</b>: the list must pair 1:1 with the containers in the markup, and dropping one shifts
/// every later image onto the wrong element. That pairing is then checked against the plan's own
/// count before a single pixel is captured.
/// </para>
/// <para>
/// Nothing throws. A script that failed, a page that went away and a malformed answer are all "no
/// measurements", and the count check turns that into the named error.
/// </para>
/// </remarks>
public static class RichRects
{
    /// <summary>The five columns, in the order the script emits them.</summary>
    public const int Columns = 5;

    public static IReadOnlyList<RichRect> Parse(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return [];
        }

        if (root.ValueKind != JsonValueKind.Array) return [];

        var rects = new List<RichRect>(root.GetArrayLength());
        var values = new double[Columns];                  // reused: one row at a time, never escapes
        foreach (var row in root.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != Columns) continue;

            var column = 0;
            var ok = true;
            foreach (var cell in row.EnumerateArray())
            {
                if (cell.ValueKind != JsonValueKind.Number || !cell.TryGetDouble(out var value) || !double.IsFinite(value))
                {
                    ok = false;
                    break;
                }
                values[column++] = value;
            }
            if (!ok) continue;

            rects.Add(new RichRect(new RectD(values[0], values[1], values[2], values[3]), values[4] != 0));
        }

        return rects;
    }
}
