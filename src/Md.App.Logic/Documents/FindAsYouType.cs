namespace Md.App.Logic.Documents;

/// <summary>
/// Find as you type (2026-09-27, the find panel): every change to the query searches again, from
/// the <b>anchor</b> — where the caret stood when the writer went to the query box, or the start of
/// the hit the last step landed on. Searching from the hit's start, not its end, is what lets a
/// longer query keep the hit it already has ("ca" on the third <c>cat</c>, then "cat", stays on
/// the third) — the way Notepad, Edge and VS Code refine. The matching is <see cref="TextSearch"/>'s
/// one rule; this class only remembers where to start.
/// </summary>
public sealed class FindAsYouType
{
    /// <summary>Where the next as-you-type search starts, in the editor's UTF-16 units.</summary>
    public int Anchor { get; private set; }

    /// <summary>The query box got focus: the search starts at the editor's selection as it stands.</summary>
    public void Focused(int selectionStart) => Anchor = Math.Max(0, selectionStart);

    /// <summary>Next, Previous or Replace landed on <paramref name="hit"/>: typing on refines from there.</summary>
    public void Stepped(TextSearch.Match hit) => Anchor = hit.Index;

    /// <summary>The query changed: the first hit at or after the anchor, wrapping; null for an empty query or none.</summary>
    public TextSearch.Match? Search(string text, string query) => TextSearch.Next(text, query, Anchor);
}
