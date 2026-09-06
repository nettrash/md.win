using Md.App.Logic.Seams;
using Md.Core.Book;

namespace Md.App.Logic.Text;

/// <summary>
/// The word counter that needs nothing from the operating system (shell-design.md §5.5): the
/// fallback when <see cref="IcuWordCounter"/> cannot load <c>icu.dll</c>, and the counter every
/// non-Windows leg of CI runs.
/// </summary>
/// <remarks>
/// "Simple" is about the dependency, not the rule: the count is <c>Md.Core.Book.WritingStats.Words</c>,
/// the UAX #29 segmentation with ICU's root tailorings that Core already ships and pins — itself the
/// port of the very <c>enumerateSubstrings(.byWords)</c> and <c>BreakIterator.getWordInstance()</c>
/// calls the Mac and Android footers make. The design's sketch for this class (runs of letters and
/// digits joined across apostrophes) would agree with those two ports on ASCII prose and disagree on
/// everything else, and the footer is a number the user compares between their machines. The one
/// thing Core's counter cannot do is dictionary segmentation for CJK and Thai, which is exactly what
/// <see cref="IcuWordCounter"/> is kept for.
/// </remarks>
public sealed class SimpleWordCounter : IWordCounter
{
    /// <summary>Stateless; one instance is enough.</summary>
    public static readonly SimpleWordCounter Instance = new();

    public int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return WritingStats.Words(text);
    }
}
