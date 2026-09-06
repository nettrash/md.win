using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.App.Logic.View;

/// <summary>
/// Everything the window derives from the document text off the typing path (shell-design.md §5.5):
/// the footer's two numbers and the Go menu's Contents / Notes / Diagram rows. Recomputed by
/// <see cref="DerivedTextScheduler"/> on a 250 ms trailing debounce, never inside a keystroke.
/// </summary>
public sealed record DerivedText(
    int Words,
    int Characters,
    IReadOnlyList<OutlineEntry> Outline,
    IReadOnlyList<NoteEntry> Notes,
    IReadOnlyList<DiagramSvg.Diagram> Diagrams)
{
    /// <summary>What a window shows before its first computation lands: "0 words · 0 characters", empty menus.</summary>
    public static readonly DerivedText Empty = new(0, 0, [], [], []);

    /// <summary>
    /// Structural, element by element. The synthesised record equality would compare the three
    /// lists with <see cref="EqualityComparer{T}.Default"/> — which for <c>IReadOnlyList&lt;T&gt;</c>
    /// is <b>reference</b> equality — so two runs over the same text would never be equal and
    /// <c>DocumentWindowState.Derived</c>'s "only on a real change" guard would fire on every
    /// 250 ms tick, relaying the window out and re-evaluating the Go menu for numbers that had not
    /// moved. The lists are an outline, a notes list and a diagram list: short, and compared only
    /// once per debounce window.
    /// </summary>
    public bool Equals(DerivedText? other) =>
        other is not null
        && (ReferenceEquals(this, other)
            || (Words == other.Words
                && Characters == other.Characters
                && Outline.SequenceEqual(other.Outline)
                && Notes.SequenceEqual(other.Notes)
                && Diagrams.SequenceEqual(other.Diagrams)));

    /// <summary>Counts, not contents: cheap, and enough to keep equal values in one bucket.</summary>
    public override int GetHashCode() =>
        HashCode.Combine(Words, Characters, Outline.Count, Notes.Count, Diagrams.Count);
}
