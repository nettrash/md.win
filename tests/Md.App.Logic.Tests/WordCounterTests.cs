using Md.App.Logic.Text;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-design.md §5.5 — the footer's word count. The five vectors are pinned for every counter
/// the app can end up running, because the number is user-visible and gets compared between a
/// writer's machines. Non-ASCII vectors are escapes so nothing can normalise them away.
/// </summary>
public sealed class WordCounterTests
{
    /// <summary>"" is 0, whitespace is 0, punctuation is not a word, "it's" is one, newlines separate.</summary>
    public static TheoryData<string, int> Vectors => new()
    {
        { "", 0 },
        { "   \n\n", 0 },
        { "Hello, world!", 2 },
        { "it's \u2014 done", 2 },
        { "One\ntwo\n\nthree", 3 },
    };

    [Theory]
    [MemberData(nameof(Vectors))]
    public void TheSimpleCounterPassesTheFiveVectors(string text, int expected) =>
        Assert.Equal(expected, SimpleWordCounter.Instance.Count(text));

    [Theory]
    [MemberData(nameof(Vectors))]
    public void WhicheverCounterThisMachineGetsPassesTheFiveVectors(string text, int expected) =>
        Assert.Equal(expected, WordCounters.Create().Count(text));

    [Fact]
    public void TheSimpleCounterIsCoresUaxTwentyNineCounterAndNotAWordApproximation()
    {
        // A "runs of letters and digits" counter answers 1 for the first and 0 for the last two;
        // UAX #29 with ICU's root tailorings answers what macOS and Android answer. This is the
        // assertion that fails the day the portable counter is swapped for a regex.
        Assert.Equal(2, SimpleWordCounter.Instance.Count("a:b"));
        Assert.Equal(1, SimpleWordCounter.Instance.Count("\uFF11\uFF12\uFF13"));   // full-width digits: one number
        Assert.Equal(1, SimpleWordCounter.Instance.Count("\u216B"));           // ROMAN NUMERAL TWELVE
    }

    [Fact]
    public void OffWindowsThereIsNoIcuAndTheFallbackIsWhatRuns()
    {
        if (OperatingSystem.IsWindows()) return;

        Assert.Null(IcuWordCounter.TryCreate());
        Assert.Same(SimpleWordCounter.Instance, WordCounters.Create());
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    [Trait("Platform", "Windows")]
    public void IcuPassesTheFiveVectorsToo(string text, int expected)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (IcuWordCounter.TryCreate() is not { } icu) return;   // an image without icu.dll: the fallback is pinned above

        Assert.Equal(expected, icu.Count(text));
    }

    [Fact]
    [Trait("Platform", "Windows")]
    public void IcuSegmentsCjkWithItsDictionaryAndTheFallbackCannot()
    {
        // Core's counter has no dictionary and makes one word per ideograph — the documented gap,
        // and the whole reason the P/Invoke is kept. Pinned as a relation, not a number, because the
        // exact figure belongs to whichever ICU the running Windows ships.
        const string University = "\u5317\u4EAC\u5927\u5B66";       // "Peking University", two words to ICU
        Assert.Equal(4, SimpleWordCounter.Instance.Count(University));

        if (!OperatingSystem.IsWindows()) return;
        if (IcuWordCounter.TryCreate() is not { } icu) return;

        Assert.InRange(icu.Count(University), 1, 3);
    }

    [Fact]
    public void CreateNeverReturnsNull() => Assert.NotNull(WordCounters.Create());
}
