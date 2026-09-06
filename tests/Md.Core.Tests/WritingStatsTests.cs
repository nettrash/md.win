using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// The footer counters (mdTests: testWordCountIsLocaleAwareNotAWhitespaceSplit, the
/// same five vectors Android's WritingStatsTest pins) plus the UAX #29 rules the port
/// implements by hand and the limits its header documents. Invisible code points are
/// spelled as escapes so the expectations stay ordinal and reviewable.
/// </summary>
public class WritingStatsTests
{
    private const string Zwj = "\u200D";
    private const string Family = "\U0001F468" + Zwj + "\U0001F469" + Zwj + "\U0001F467";

    [Fact]
    public void WordCountIsLocaleAwareNotAWhitespaceSplit()
    {
        Assert.Equal(0, WritingStats.Words(""));
        Assert.Equal(0, WritingStats.Words("   \n\n"));
        Assert.Equal(2, WritingStats.Words("Hello, world!"));
        // An apostrophe joins a word; a dash alone is none.
        Assert.Equal(2, WritingStats.Words("it's — done"));
        Assert.Equal(3, WritingStats.Words("One\ntwo\n\nthree"));
    }

    [Fact]
    public void SegmentsCoverTheTextExactly()
    {
        var text = "Hello, world!";
        var pieces = WritingStats.Segments(text).Select(s => text[s.Start..s.End]).ToArray();
        Assert.Equal(new[] { "Hello", ",", " ", "world", "!" }, pieces);
        Assert.Equal(text, string.Concat(pieces));
    }

    [Fact]
    public void InWordPunctuationFollowsUax29()
    {
        Assert.Equal(1, WritingStats.Words("3.14"));               // WB11 / WB12
        Assert.Equal(1, WritingStats.Words("1,000,000"));
        Assert.Equal(1, WritingStats.Words("a.b"));                // WB6 / WB7
        Assert.Equal(1, WritingStats.Words("U.S.A."));
        Assert.Equal(1, WritingStats.Words("don’t"));         // curly apostrophe is MidNumLet
        Assert.Equal(1, WritingStats.Words("x_y"));                // WB13a / WB13b
        Assert.Equal(1, WritingStats.Words("snake_case_name"));
        Assert.Equal(2, WritingStats.Words("e-mail"));             // a hyphen breaks
        Assert.Equal(1, WritingStats.Words("$100"));
        Assert.Equal(2, WritingStats.Words("re: subject"));
    }

    [Fact]
    public void CombiningMarksStayInsideTheWord()
    {
        Assert.Equal(1, WritingStats.Words("e\u0301tat"));
        Assert.Equal(1, WritingStats.Words("état"));
        Assert.Equal(1, WritingStats.Words("Zoë"));
        Assert.Equal(2, WritingStats.Words("Ко\u0301фе чай"));
        Assert.Equal(6, WritingStats.Words("Привет, мир! Как дела у тебя?"));
    }

    [Fact]
    public void SymbolsAndEmojiAreNotWords()
    {
        Assert.Equal(0, WritingStats.Words("\U0001F600 \U0001F44D"));
        Assert.Equal(0, WritingStats.Words("— – … # * ~"));
        Assert.Equal(1, WritingStats.Words("hello \U0001F600"));
        // ZWJ emoji sequences and flags hold together as one non-word segment.
        Assert.Single(WritingStats.Segments(Family));
        Assert.Single(WritingStats.Segments("\U0001F1FA\U0001F1F8"));
        Assert.Equal(2, WritingStats.Segments("\U0001F1FA\U0001F1F8\U0001F1E9\U0001F1EA").Count);
    }

    [Fact]
    public void NewlinesOfEveryKindSeparateWords()
    {
        Assert.Equal(2, WritingStats.Words("One\r\ntwo"));
        Assert.Equal(2, WritingStats.Words("a\u2028b"));
        Assert.Equal(2, WritingStats.Words("a\u2029b"));
        Assert.Equal(2, WritingStats.Words("a\u000Bb"));
        // CR LF is one segment, not two (WB3).
        Assert.Equal(3, WritingStats.Segments("a\r\nb").Count);
    }

    [Fact]
    public void ScriptsWithoutSpacesFollowTheDocumentedLimits()
    {
        // No dictionary: an ideograph or a Hiragana syllable is a segment each;
        // Katakana runs join (WB13).
        Assert.Equal(2, WritingStats.Words("北京"));
        Assert.Equal(1, WritingStats.Words("テスト"));
        Assert.Equal(3, WritingStats.Words("これは"));
        // Thai and friends stay in ALetter: an unspaced run is one word, not one per letter.
        Assert.Equal(2, WritingStats.Words("สวัสดี ครับ"));
        // Hebrew letters around a double quote: WB7b / WB7c.
        Assert.Equal(1, WritingStats.Words("צה\"ל"));
    }

    [Fact]
    public void CharactersAreGraphemeClustersLikeSwift()
    {
        Assert.Equal(0, WritingStats.Characters(""));
        Assert.Equal(3, WritingStats.Characters("abc"));
        Assert.Equal(6, WritingStats.Characters("Привет"));
        Assert.Equal(1, WritingStats.Characters("e\u0301"));
        Assert.Equal(1, WritingStats.Characters(Family));
        Assert.Equal(1, WritingStats.Characters("\r\n"));
        Assert.Equal(2, WritingStats.Characters("\U0001F600\U0001F600"));
    }
}
