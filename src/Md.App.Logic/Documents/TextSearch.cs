namespace Md.App.Logic.Documents;

/// <summary>
/// The find bar's engine (§3.5): ordinal, case-insensitive, wrapping. Ordinal because a Markdown
/// document is not prose in one language — a culture-sensitive search would fold "ß" into "ss" and
/// select a range whose length does not match the query, which is exactly what the caller then hands
/// to <c>TextBox.Select</c>. Offsets are in the <c>TextBox</c>'s own UTF-16 units.
/// </summary>
public static class TextSearch
{
    /// <summary>Where a hit sits in the searched string; <see cref="Length"/> is the query's, ordinal matching being 1:1 in UTF-16 units.</summary>
    public readonly record struct Match(int Index, int Length);

    /// <summary>The first hit at or after <paramref name="from"/>, wrapping to the top; null when the query is empty or absent.</summary>
    public static Match? Next(string text, string query, int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || query.Length > text.Length) return null;
        var start = Math.Clamp(from, 0, text.Length);
        var at = start > text.Length - query.Length ? -1 : text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (at < 0) at = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        return at < 0 ? null : new Match(at, query.Length);
    }

    /// <summary>The last hit starting before <paramref name="from"/>, wrapping to the bottom; null when the query is empty or absent.</summary>
    public static Match? Previous(string text, string query, int from)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || query.Length > text.Length) return null;
        var limit = Math.Clamp(from, 0, text.Length);
        var at = LastIndexBefore(text, query, limit);
        if (at < 0) at = LastIndexBefore(text, query, text.Length);
        return at < 0 ? null : new Match(at, query.Length);
    }

    // Forward scan keeping the last hit that starts before the limit. IndexOf in a loop, not
    // LastIndexOf: LastIndexOf's (startIndex, count) window is measured backwards from startIndex
    // and is the classic source of an off-by-one at either end of the string.
    static int LastIndexBefore(string text, string query, int limit)
    {
        var best = -1;
        var i = 0;
        while (i <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, i, StringComparison.OrdinalIgnoreCase);
            if (at < 0 || at >= limit) break;
            best = at;
            i = at + 1;
        }
        return best;
    }
}
