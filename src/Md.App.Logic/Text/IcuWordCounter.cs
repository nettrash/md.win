using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Md.App.Logic.Seams;

namespace Md.App.Logic.Text;

/// <summary>
/// The footer's word count through Windows' in-box ICU (shell-design.md §5.5): <c>ubrk_open</c>
/// with <c>UBRK_WORD</c>, then one segment per <c>ubrk_next</c>, counting the ones whose rule
/// status is ≥ <see cref="WordNoneLimit"/> — ICU's own definition of a word token, which is what
/// Foundation's <c>enumerateSubstrings(.byWords)</c> and Kotlin's <c>BreakIterator</c> count.
/// It is here for one reason: it is the only segmenter that runs ICU's <b>dictionaries</b>, so CJK
/// and Thai count the way they do on macOS and Android. Latin prose agrees with
/// <see cref="SimpleWordCounter"/> to the letter.
/// </summary>
/// <remarks>
/// <c>icu.dll</c> ships with Windows 10 1903 and later (so with every Windows 11), and its exports
/// are unversioned — no <c>_67</c> suffix to chase. The locale is null, the process default, exactly
/// as both siblings take theirs from the system: a word count that changed when the machine's
/// language changed would be a bug in only one direction, and this is the direction the sources of
/// truth are in. The iterator does not copy the text, so the whole open/iterate/close sequence stays
/// inside one <c>fixed</c> block.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed partial class IcuWordCounter : IWordCounter
{
    /// <summary><c>UBreakIteratorType.UBRK_WORD</c>.</summary>
    const int UbrkWord = 1;

    /// <summary><c>UBRK_DONE</c> — the iterator ran off the end.</summary>
    const int UbrkDone = -1;

    /// <summary>
    /// <c>UBRK_WORD_NONE_LIMIT</c>: below it the segment is punctuation or space; at 100 numbers,
    /// 200 letters, 300 kana, 400 ideographs. "≥ 100" is ICU's own "this segment is a word".
    /// </summary>
    const int WordNoneLimit = 100;

    IcuWordCounter()
    {
    }

    /// <summary>
    /// The counter when ICU is there and answers, else null so the caller falls back. The probe is
    /// a real count, not a load check: a present-but-broken <c>icu.dll</c> (a stub, a wrong
    /// architecture, an export that returns an error status) must fail here and not in the footer.
    /// </summary>
    public static IcuWordCounter? TryCreate()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            var counter = new IcuWordCounter();
            return counter.Count("md test") == 2 ? counter : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public unsafe int Count(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return 0;

        fixed (char* buffer = text)
        {
            var status = 0;
            var iterator = ubrk_open(UbrkWord, null, buffer, text.Length, &status);
            if (iterator == 0 || status > 0)
            {
                if (iterator != 0) ubrk_close(iterator);
                throw new InvalidOperationException($"ubrk_open failed with UErrorCode {status}.");
            }

            try
            {
                var words = 0;
                // ubrk_first is the boundary before the first segment and has no rule status of its
                // own; every ubrk_next returns the END of one segment, and getRuleStatus describes
                // that segment.
                ubrk_first(iterator);
                for (var boundary = ubrk_next(iterator); boundary != UbrkDone; boundary = ubrk_next(iterator))
                {
                    if (ubrk_getRuleStatus(iterator) >= WordNoneLimit) words++;
                }
                return words;
            }
            finally
            {
                ubrk_close(iterator);
            }
        }
    }

    // UBreakIterator* ubrk_open(UBreakIteratorType, const char* locale, const UChar* text, int32_t len, UErrorCode*)
    [LibraryImport("icu.dll")]
    private static unsafe partial nint ubrk_open(int type, byte* locale, char* text, int textLength, int* status);

    [LibraryImport("icu.dll")]
    private static partial int ubrk_first(nint iterator);

    [LibraryImport("icu.dll")]
    private static partial int ubrk_next(nint iterator);

    [LibraryImport("icu.dll")]
    private static partial int ubrk_getRuleStatus(nint iterator);

    [LibraryImport("icu.dll")]
    private static partial void ubrk_close(nint iterator);
}

/// <summary>
/// Which counter the app runs (shell-design.md §5.5): ICU when Windows lends us its, the portable
/// one otherwise. One place, so the footer, the book footer and the self-test cannot end up on
/// different rules.
/// </summary>
public static class WordCounters
{
    /// <summary>Never null, never throws: worst case it is <see cref="SimpleWordCounter.Instance"/>.</summary>
    public static IWordCounter Create()
    {
        if (OperatingSystem.IsWindows() && IcuWordCounter.TryCreate() is { } icu) return icu;
        return SimpleWordCounter.Instance;
    }
}
