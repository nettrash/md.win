namespace Md.App.Logic.Documents;

/// <summary>
/// The find bar's engine (§3.5): ordinal, case-insensitive, wrapping — for the search and for the
/// replace, which is the same one rule and no second one. Ordinal because a Markdown document is not
/// prose in one language — a culture-sensitive search would fold "ß" into "ss" and select a range
/// whose length does not match the query, which is exactly what the caller then hands to
/// <c>TextBox.Select</c>. Offsets are in the <c>TextBox</c>'s own UTF-16 units. No regular
/// expressions: a query is the characters typed into the box.
/// </summary>
public static class TextSearch
{
    /// <summary>Where a hit sits in the searched string; <see cref="Length"/> is the query's, ordinal matching being 1:1 in UTF-16 units.</summary>
    public readonly record struct Match(int Index, int Length);

    /// <summary>One edit to the searched string: <c>[Start, Start + Length)</c> becomes <see cref="Text"/>. Plain data — nothing here touches a control.</summary>
    public readonly record struct Edit(int Start, int Length, string Text);

    /// <summary>
    /// One press of Replace. <see cref="Apply"/> is the edit to make, or null when the caret is not
    /// standing on a hit — Replace is a plain Find Next then, which is what every find bar does and
    /// what makes "Replace, Replace, Replace…" walk the document. <see cref="SearchFrom"/> is where
    /// the search that follows starts, measured in the text the edit leaves behind: past the
    /// replacement, so one that contains the query is not found again by the step that made it.
    /// </summary>
    public readonly record struct ReplaceStep(Edit? Apply, int SearchFrom);

    /// <summary>
    /// What Replace All does, decided in one pass. <see cref="Text"/> is the whole new string and
    /// <see cref="Count"/> the number of hits replaced; <see cref="Apply"/> is the same answer as the
    /// <b>single</b> edit that produces it — from the first hit's start to the last hit's end, with
    /// everything between them carried across — so the shell can put it in with one
    /// <c>SelectedText</c> assignment and the control's undo treats the lot as one step. The two
    /// agree by construction: <c>text[..Apply.Start] + Apply.Text + text[(Apply.Start + Apply.Length)..] == Text</c>.
    /// With no hit, <see cref="Count"/> is 0, <see cref="Text"/> is the text as given and
    /// <see cref="Apply"/> is an empty edit at 0.
    /// </summary>
    public readonly record struct ReplaceAllPlan(string Text, int Count, Edit Apply);

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

    /// <summary>
    /// Whether the selection is itself a hit for <paramref name="query"/> — "the match we are
    /// standing on", which is the whole of Replace's decision. Ordinal and case-insensitive like the
    /// search, so the <c>Alpha</c> that Next selected is replaceable with <c>alpha</c> still in the
    /// query box; a selection of any other length never is, ordinal matching being 1:1 in UTF-16
    /// units. A selection outside the text, or a negative one, is not a match rather than a throw:
    /// the caller reads it off a live control.
    /// </summary>
    public static bool SelectionIsMatch(string text, string query, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || selectionLength != query.Length) return false;
        if (selectionStart < 0 || selectionStart > text.Length - query.Length) return false;
        return text.AsSpan(selectionStart, query.Length).Equals(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// One press of Replace over the selection the editor reports. When the selection is the hit
    /// (<see cref="SelectionIsMatch"/>) the step replaces it and the next search starts after what
    /// went in; otherwise nothing is edited and the next search starts at the selection's end, which
    /// is where <see cref="Next"/> would start for Find Next. The caller applies the edit, re-reads
    /// the text and calls <see cref="Next"/> from <see cref="ReplaceStep.SearchFrom"/>.
    /// </summary>
    public static ReplaceStep Replace(string text, string query, string replacement, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        return SelectionIsMatch(text, query, selectionStart, selectionLength)
            ? new ReplaceStep(new Edit(start, length, replacement), start + replacement.Length)
            : new ReplaceStep(null, start + length);
    }

    /// <summary>
    /// Every hit replaced, left to right, in one pass. The scan resumes after each hit <b>in the old
    /// text</b> and never re-enters what was just put in, so a replacement that contains the query
    /// ("a" → "aa") replaces each hit exactly once instead of growing forever, and the pass is
    /// linear. Hits do not overlap: "aa" over "aaaa" is two, not three.
    /// </summary>
    public static ReplaceAllPlan ReplaceAll(string text, string query, string replacement)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(replacement);
        var nothing = new ReplaceAllPlan(text, 0, new Edit(0, 0, string.Empty));
        if (query.Length == 0 || query.Length > text.Length) return nothing;

        var built = new System.Text.StringBuilder();
        var first = -1;
        var after = 0;
        var from = 0;
        var count = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            if (first < 0) first = at;
            else built.Append(text, after, at - after);   // what stood between this hit and the last
            built.Append(replacement);
            after = at + query.Length;
            from = after;
            count++;
        }
        if (count == 0) return nothing;

        var edit = new Edit(first, after - first, built.ToString());
        var whole = string.Concat(text.AsSpan(0, first), edit.Text, text.AsSpan(after));
        return new ReplaceAllPlan(whole, count, edit);
    }

    /// <summary>The find panel's count: <see cref="Current"/> is the selection's place among the hits (1-based), 0 when the selection is not one.</summary>
    public readonly record struct Tally(int Current, int Total);

    /// <summary>
    /// "3 of 12" (2026-09-27, the find panel). <see cref="Tally.Total"/> counts hits the way
    /// <see cref="ReplaceAll"/> replaces them — left to right, never overlapping — so the number the
    /// panel shows is the number Replace All would change. <see cref="Tally.Current"/> is the
    /// selection's place in that run when the selection is a hit (<see cref="SelectionIsMatch"/>):
    /// one plus the hits that start before it, which also places a hit that overlaps a counted one.
    /// </summary>
    public static Tally Count(string text, string query, int selectionStart, int selectionLength)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length == 0 || query.Length > text.Length) return new Tally(0, 0);
        var onHit = SelectionIsMatch(text, query, selectionStart, selectionLength);
        var total = 0;
        var before = 0;
        var from = 0;
        while (from <= text.Length - query.Length)
        {
            var at = text.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) break;
            total++;
            if (onHit && at < selectionStart) before++;
            from = at + query.Length;
        }
        // A hit that overlaps the last counted one ("aa" at 1 in "aaa") is placed on that one, never
        // past the total: the panel must not read "2 of 1".
        return new Tally(onHit ? Math.Min(before + 1, total) : 0, total);
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
