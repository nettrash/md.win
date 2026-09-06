using System.Text;
using Md.Core.Text;

namespace Md.Core.Tests;

/// <summary>
/// The text primitives the whole parity core stands on. Nothing here is interesting on its own;
/// every one is a set-membership question that two shipping platforms once answered differently.
/// Every invisible character is a named constant, never a literal buried in a string.
/// </summary>
public class ScalarTextTests
{
    private const string Acute = "\u0301";  // COMBINING ACUTE ACCENT
    private const string Vs16 = "\uFE0F";   // VARIATION SELECTOR-16
    private const string Zwj = "\u200D";    // ZERO WIDTH JOINER
    private const string Zwsp = "\u200B";   // ZERO WIDTH SPACE
    private const string Nbsp = "\u00A0";   // NO-BREAK SPACE
    private const string Nel = "\u0085";    // NEXT LINE
    private const string Bom = "\uFEFF";    // ZERO WIDTH NO-BREAK SPACE
    private const string Vt = "\u000B";     // LINE TABULATION
    private const string Ff = "\u000C";     // FORM FEED
    private const string Ls = "\u2028";     // LINE SEPARATOR
    private const string Ps = "\u2029";     // PARAGRAPH SEPARATOR

    private static readonly string[] Marks = [Acute, Vs16, Zwj];

    // MARK: - Whitespace sets

    [Fact]
    public void WhitespaceSetIsFoundationsZsPlusTab()
    {
        // Foundation's `CharacterSet.whitespaces`: the 18 Zs code points, tab, and U+200B.
        foreach (var code in new[]
        {
            0x0009, 0x0020, 0x00A0, 0x1680, 0x2000, 0x2001, 0x2002, 0x2003, 0x2004,
            0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A, 0x202F, 0x205F, 0x3000,
        })
        {
            Assert.True(Whitespace.IsWhitespace((char)code), $"U+{code:X4}");
        }
        // Exactly nineteen members over the whole BMP — nothing else slips in.
        var count = 0;
        for (var c = 0; c <= 0xFFFF; c++)
        {
            if (Whitespace.IsWhitespace((char)c)) count++;
        }
        Assert.Equal(19, count);
    }

    [Fact]
    public void WhitespaceSetExcludesTheLineTerminators()
    {
        // These belong to `whitespacesAndNewlines` only. U+0085 in particular is whitespace when
        // the note body is trimmed and is not a blank line anywhere else.
        foreach (var code in new[] { 0x000A, 0x000B, 0x000C, 0x000D, 0x0085, 0x2028, 0x2029 })
        {
            Assert.False(Whitespace.IsWhitespace((char)code), $"U+{code:X4}");
            Assert.True(Whitespace.IsNewline((char)code), $"U+{code:X4}");
            Assert.True(Whitespace.IsWhitespaceOrNewline((char)code), $"U+{code:X4}");
        }
    }

    [Fact]
    public void WhitespaceSetExcludesMongolianVowelSeparator()
    {
        // U+180E has been Cf and not Zs since Unicode 6.3, and Foundation agrees.
        Assert.False(Whitespace.IsWhitespace('\u180E'));
        Assert.False(Whitespace.IsWhitespaceOrNewline('\u180E'));
    }

    [Fact]
    public void ZeroWidthSpaceIsInFoundationsWhitespaceSet()
    {
        // Apple's tables are frozen at a Unicode version that still classified U+200B as a space
        // separator; the JVM and .NET say Cf. Kotlin adds it by hand, so does this. Without it a
        // line holding one ZWSP was a blank line on Apple and a paragraph elsewhere.
        Assert.True(Whitespace.IsWhitespace('\u200B'));
        Assert.Equal("", Whitespace.TrimWS(Zwsp));
        // Which is precisely why the set cannot be derived from the runtime's tables.
        Assert.False(char.IsWhiteSpace('\u200B'));
        Assert.NotEqual(System.Globalization.UnicodeCategory.SpaceSeparator, char.GetUnicodeCategory('\u200B'));
    }

    // MARK: - Trimming

    [Fact]
    public void TrimsTheFoundationWhitespaceSetNotTheRuntimes()
    {
        Assert.Equal("js", Whitespace.TrimWS(" js "));
        Assert.Equal("js", Whitespace.TrimWS(Nbsp + "js" + Nbsp));
        Assert.Equal("padded", Whitespace.TrimWS("  padded \t"));
        Assert.Equal("", Whitespace.TrimWS(""));
        Assert.Equal("", Whitespace.TrimWS("   "));
        Assert.Equal("a", Whitespace.TrimWS("\u3000\u1680a\u2000\u205F"));
    }

    [Fact]
    public void TrimWsLeavesNewlinesAndTheBomAlone()
    {
        // `string.Trim()` would eat the newlines; Foundation's `.whitespaces` keeps all of them.
        // Blank-line detection turns on it.
        Assert.Equal("\n a \n", Whitespace.TrimWS("\n a \n"));
        Assert.Equal(Bom, Whitespace.TrimWS(Bom));
        Assert.Equal(Bom, Whitespace.TrimWSNL(Bom));
    }

    [Fact]
    public void TrimWsnlIsTheNoteBodyTrim()
    {
        Assert.Equal("a", Whitespace.TrimWSNL("\n a \n"));
        Assert.Equal("a", Whitespace.TrimWSNL(Vt + Ff + "a" + Ls + Ps + "\r"));
        // U+0085 NEL: whitespace here and nowhere else.
        Assert.Equal("note", Whitespace.TrimWSNL(Nel + "note" + Nel));
        Assert.Equal(Nel + "note" + Nel, Whitespace.TrimWS(Nel + "note" + Nel));
    }

    [Fact]
    public void TrimsCodeUnitsNotGraphemes()
    {
        // Foundation trims scalars: `" \u0301abc"` loses the space even though space-plus-mark is
        // one grapheme. A code-unit trim does the same.
        foreach (var mark in Marks)
        {
            Assert.Equal(mark + "abc", Whitespace.TrimWS(" " + mark + "abc"));
            Assert.Equal("abc" + mark, Whitespace.TrimWS("abc" + mark + " "));
        }
    }

    [Fact]
    public void TrimsOneEndAtATime()
    {
        Assert.Equal("a  ", Whitespace.TrimLeadingWS("  a  "));
        Assert.Equal("  a", Whitespace.TrimTrailingWS("  a  "));
        Assert.Equal("a", Whitespace.TrimLeadingWS("a"));
        Assert.Equal("a", Whitespace.TrimTrailingWS("a"));
        Assert.Equal("", Whitespace.TrimLeadingWS(Nbsp));
        Assert.Equal("", Whitespace.TrimTrailingWS(Nbsp));
    }

    [Fact]
    public void SpaceTabTrimIsAsciiOnly()
    {
        // The CSV alignment cell: U+200B and U+00A0 are content here, on purpose.
        Assert.Equal("1", Whitespace.TrimSpaceTab(" \t1\t "));
        Assert.Equal(Zwsp + "1" + Zwsp, Whitespace.TrimSpaceTab(Zwsp + "1" + Zwsp));
        Assert.Equal(Nbsp + "1", Whitespace.TrimSpaceTab(Nbsp + "1 "));
    }

    [Fact]
    public void IndentationDropsAreSpaceOnlyOrSpaceTab()
    {
        Assert.Equal("\tx", Whitespace.DropLeadingSpaces("  \tx"));
        Assert.Equal("x", Whitespace.DropSpaceTab(" \t x"));
        Assert.Equal(Nbsp + "x", Whitespace.DropSpaceTab(" " + Nbsp + "x"));
        Assert.Equal("", Whitespace.DropLeadingSpaces("   "));
    }

    // MARK: - Scalar helpers

    [Fact]
    public void FindsADelimiterThatCarriesACombiningMark()
    {
        // Swift's ScalarText exists because `"a%\u0301b".contains("%")` is false there. Ordinal
        // UTF-16 search is exact already; these assert the behaviour, not the mechanism.
        foreach (var mark in Marks)
        {
            Assert.True(ScalarText.Contains("a%" + mark + "b", "%"));
            Assert.True(ScalarText.HasPrefix("[" + mark + "x", "["));
            Assert.True(ScalarText.HasSuffix("x]" + mark, "]" + mark));
            Assert.Equal(1, ScalarText.FirstIndex("a-->" + mark + "b", "-->"));
        }
    }

    [Fact]
    public void IgnorableCharactersAreNotIgnored()
    {
        // The trap this port has instead of Swift's: a culture-sensitive search treats ZWJ, ZWSP
        // and the soft hyphen as ignorable, so `"a\u200Db".IndexOf("ab")` is 0 under ICU.
        Assert.False(ScalarText.Contains("a" + Zwj + "b", "ab"));
        Assert.Null(ScalarText.FirstIndex("a" + Zwj + "b", "ab"));
        Assert.False(ScalarText.HasPrefix("\u00ADabc", "abc"));
        Assert.False(ScalarText.HasSuffix("abc" + Zwsp, "abc"));
        Assert.False(ScalarText.Contains("-->" + Zwj, "-->" + Zwj + Zwj));
    }

    [Fact]
    public void NeverFindsAnEmptyNeedleUnlikeTheBcl()
    {
        Assert.False(ScalarText.Contains("abc", ""));
        Assert.Null(ScalarText.FirstIndex("abc", ""));
        // The prefix / suffix pair keep the stdlib answer, as the Swift does.
        Assert.True(ScalarText.HasPrefix("abc", ""));
        Assert.True(ScalarText.HasSuffix("abc", ""));
        Assert.Contains("", "abc");
    }

    [Fact]
    public void RefusesANeedleLongerThanTheHaystackAndHonoursTheStartIndex()
    {
        Assert.Null(ScalarText.FirstIndex("ab", "abc"));
        Assert.Equal(3, ScalarText.FirstIndex("abcabc", "abc", 1));
        Assert.Null(ScalarText.FirstIndex("abc", "z"));
        Assert.Null(ScalarText.FirstIndex("abc", "c", 3));
        Assert.Equal(0, ScalarText.FirstIndex("abc", "a", -5));
        Assert.Null(ScalarText.FirstIndex("", "a"));
    }

    [Fact]
    public void SplitKeepsEmptyEdgePieces()
    {
        // Kotlin's `split(String)`: empty pieces at either end included; an empty separator yields
        // the whole text.
        Assert.Equal([""], ScalarText.Split("", "\n"));
        Assert.Equal(["", "a", ""], ScalarText.Split("XaX", "X"));
        Assert.Equal(["a\r", "b"], ScalarText.Split("a\r\nb", "\n"));
        Assert.Equal(["abc"], ScalarText.Split("abc", ""));
        Assert.Equal(["a", "", "b"], ScalarText.Split("a--b", "-"));
    }

    [Fact]
    public void ReplacingNeverRescansWhatItWrote()
    {
        Assert.Equal("aa", ScalarText.Replacing("aaa", "aa", "a"));
        Assert.Equal("xbxb", ScalarText.Replacing("abab", "a", "x"));
        Assert.Equal("abc", ScalarText.Replacing("abc", "", "x"));
        Assert.Equal("abc", ScalarText.Replacing("abc", "z", "x"));
        Assert.Equal("a" + Zwj + "b", ScalarText.Replacing("a" + Zwj + "b", "ab", "x"));
    }

    [Fact]
    public void DropFirstCountsCodePoints()
    {
        // What the Swift drops in scalars. The parser only ever drops a known ASCII prefix, where
        // units and code points agree; an astral character shows the helper counting honestly.
        Assert.Equal("ab", ScalarText.DropFirst("\U0001F600ab", 1));
        Assert.Equal("b", ScalarText.DropFirst("\U0001F600ab", 2));
        Assert.Equal(Acute + "x", ScalarText.DropFirst("[ ]" + Acute + "x", 3));
        Assert.Equal("", ScalarText.DropFirst("ab", 5));
        Assert.Equal("ab", ScalarText.DropFirst("ab", 0));
    }

    // MARK: - Line splitting

    [Fact]
    public void NormalizedLinesTreatsCrLfAndCrLfAsExactlyOneTerminatorEach()
    {
        Assert.Equal(["one", "two", "three", "four"], Whitespace.NormalizedLines("one\r\ntwo\rthree\nfour"));
        Assert.Equal(["a", "", "b"], Whitespace.NormalizedLines("a\r\n\r\nb"));
        Assert.Equal(["a", "", "b"], Whitespace.NormalizedLines("a\n\rb"));
    }

    [Fact]
    public void NormalizedLinesNeverSplitsOnTheWiderNewlineSet()
    {
        // VT, FF, NEL, LS and PS are ordinary content to the block scanner.
        var line = "a" + Vt + "b" + Ff + "c" + Nel + "d" + Ls + "e" + Ps + "f";
        Assert.Equal([line], Whitespace.NormalizedLines(line));
    }

    [Fact]
    public void NormalizedLinesAlwaysAppendsTheFinalLine()
    {
        // Observable: an unclosed fence at EOF gains a trailing "\n". Wart, replicated.
        Assert.Equal(["a", ""], Whitespace.NormalizedLines("a\n"));
        Assert.Equal(["a", ""], Whitespace.NormalizedLines("a\r\n"));
        Assert.Equal(["a", ""], Whitespace.NormalizedLines("a\r"));
        Assert.Equal([""], Whitespace.NormalizedLines(""));
        Assert.Equal(["", ""], Whitespace.NormalizedLines("\n"));
    }

    [Fact]
    public void NewlineSetLinesIsTheWiderSplitterWithCrLfAsOne()
    {
        Assert.Equal(["a", "b", "c", "d", "e", "f", "g", "h", ""], Whitespace.NewlineSetLines("a\nb\r\nc\rd" + Nel + "e" + Ls + "f" + Ps + "g" + Vt + "h" + Ff));
        Assert.Equal([""], Whitespace.NewlineSetLines(""));
        // A lone CR followed by a lone LF are two terminators, so an empty line sits between.
        Assert.Equal(["a", "", "b"], Whitespace.NewlineSetLines("a\n\rb"));
    }

    // MARK: - Code points

    [Fact]
    public void FullLowercaseIsSwiftsScalarByScalarMapping()
    {
        Assert.Equal("abc", ScalarText.FullLowercase("ABC"));
        Assert.Equal("ünïcödé", ScalarText.FullLowercase("ÜNÏCÖDÉ"));
        // U+0130 is the one context-free full mapping that is not 1:1.
        Assert.Equal("i\u0307", ScalarText.FullLowercase("\u0130"));
        Assert.Equal("i\u0307i", ScalarText.FullLowercase("\u0130I"));
        // No Final_Sigma: Swift lowercases each scalar on its own.
        Assert.Equal("οδοσ σασ σ", ScalarText.FullLowercase("ΟΔΟΣ ΣΑΣ Σ"));
        // Compatibility letters, letter-numbers and circled letters map like Swift's tables.
        Assert.Equal("kå", ScalarText.FullLowercase("\u212A\u212B"));
        Assert.Equal("ⅹ", ScalarText.FullLowercase("Ⅹ"));
        Assert.Equal("\u24D0", ScalarText.FullLowercase("\u24B6"));
        // Astral letters are mapped whole, never split into surrogates.
        Assert.Equal("\U00010428", ScalarText.FullLowercase("\U00010400"));
        Assert.Equal("\U0001D400", ScalarText.FullLowercase("\U0001D400"));
        // Marks and format characters pass through.
        Assert.Equal("a" + Acute + Zwj, ScalarText.FullLowercase("A" + Acute + Zwj));
    }

    [Fact]
    public void MarkAndEnclosedAlphabeticPredicates()
    {
        Assert.True(ScalarText.IsMark(new Rune(0x0301)));   // Mn
        Assert.True(ScalarText.IsMark(new Rune(0x0903)));   // Mc DEVANAGARI SIGN VISARGA
        Assert.True(ScalarText.IsMark(new Rune(0x20DD)));   // Me COMBINING ENCLOSING CIRCLE
        Assert.False(ScalarText.IsMark(new Rune(0x200D)));  // Cf
        Assert.False(ScalarText.IsMark(new Rune('a')));

        Assert.True(ScalarText.IsEnclosedAlphabetic(new Rune(0x24B6)));
        Assert.True(ScalarText.IsEnclosedAlphabetic(new Rune(0x24E9)));
        Assert.True(ScalarText.IsEnclosedAlphabetic(new Rune(0x1F130)));
        Assert.True(ScalarText.IsEnclosedAlphabetic(new Rune(0x1F189)));
        Assert.False(ScalarText.IsEnclosedAlphabetic(new Rune(0x24EA)));  // CIRCLED DIGIT ZERO is No
        Assert.False(ScalarText.IsEnclosedAlphabetic(new Rune(0x1F14A))); // SQUARED HV is not Alphabetic
        Assert.False(ScalarText.IsEnclosedAlphabetic(new Rune('a')));
    }
}
