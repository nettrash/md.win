using System.Globalization;
using System.Text;

namespace Md.Core.Book;

/// <summary>
/// The author-facing counters in the book footer ("N words · M characters"). Port of
/// <c>WritingStats.words(in:)</c> and <c>DerivedText.characters</c> (md.macOS
/// BookWorkspace.swift).
///
/// Swift counts with Foundation's <c>enumerateSubstrings(.byWords)</c>, Kotlin with
/// <c>BreakIterator.getWordInstance()</c> counting only segments that contain a letter
/// or digit — both UAX #29 word segmentation with ICU behind it. .NET has no public
/// break iterator, so the segmentation is implemented here from the UAX #29 rules
/// (WB1–WB16, WB999) over a property table built from .NET's Unicode categories plus
/// the code-point lists the table needs (MidLetter, MidNum, MidNumLet, Katakana,
/// Hebrew_Letter, Ideographic, Hiragana, Regional_Indicator).
///
/// Both siblings run ICU's rules, not the bare UAX #29 defaults, and ICU's root
/// tailorings (CLDR segments/root.xml) are reproduced where a 490-line differential
/// against Foundation's <c>enumerateSubstrings(.byWords)</c> showed them: the colon
/// family (U+003A, U+FE55, U+FF1A) is <em>not</em> MidLetter ("a:b" is two words;
/// only Swedish and Finnish keep it), wide decimal digits (U+FF10–FF19) <em>are</em>
/// Numeric ("１２３" is one word), and a segment counts as a word when it carries a
/// letter, a digit, a letter-number (Roman numerals) or an alphabetic symbol (circled
/// or squared Latin letters) — ICU's word tokens, wider than Kotlin's
/// <c>isLetterOrDigit</c> filter, which misses "Ⅻ".
///
/// Known limits against the siblings, in order of how likely a writer meets them:
/// no dictionary segmentation — a CJK ideograph or a Hiragana syllable is one segment
/// each (so counts one word each, roughly the CJK convention of counting characters,
/// but higher than ICU's dictionary words: "北京" is 1 on macOS, 2 here); scripts ICU
/// hands to a dictionary (Thai, Lao, Khmer, Myanmar, the Tai scripts and their
/// neighbours — Line_Break=SA) form their own class here, joining only with
/// themselves like ICU's dictionary characters — so "ก1" is two words as on macOS
/// but an unspaced Thai run counts as one word where ICU's dictionary would find
/// several; the Extended_Pictographic set behind WB3c is a coarse range (it only
/// shapes emoji sequences, which never count as words); Foundation's Japanese
/// dictionary path reports a bare connector or space between Katakana runs as a word
/// token ("カ_カ" and "カタ カナ" count 3 on macOS) — the segments match here, the
/// connector is not counted; and property lists follow Unicode 15 — characters added
/// since are classified by category alone.
/// </summary>
public static class WritingStats
{
    /// <summary>Locale-independent UAX #29 word count: segments carrying a letter, digit, letter-number or alphabetic symbol.</summary>
    public static int Words(string text)
    {
        if (text.Length == 0) return 0;
        var count = 0;
        foreach (var (start, end) in Segments(text))
        {
            if (IsWordSegment(text, start, end)) count++;
        }
        return count;
    }

    /// <summary>
    /// Swift's <c>text.count</c>: extended grapheme clusters, not UTF-16 units — "é"
    /// is 1, a family emoji is 1, "\r\n" is 1. (The Android footer shows <c>length</c>
    /// and already disagrees with macOS; this port follows the source of truth.)
    /// </summary>
    public static int Characters(string text) =>
        text.Length == 0 ? 0 : new StringInfo(text).LengthInTextElements;

    /// <summary>UAX #29 word segments as UTF-16 ranges, covering the whole text.</summary>
    public static IReadOnlyList<(int Start, int End)> Segments(string text)
    {
        var segments = new List<(int, int)>();
        if (text.Length == 0) return segments;
        var starts = new List<int>(text.Length);
        var props = new List<Wb>(text.Length);
        var pictographic = new List<bool>(text.Length);
        for (var i = 0; i < text.Length;)
        {
            int length;
            if (Rune.TryGetRuneAt(text, i, out var rune))
            {
                length = rune.Utf16SequenceLength;
                props.Add(Classify(rune));
                pictographic.Add(IsExtendedPictographic(rune.Value));
            }
            else
            {
                // A lone surrogate is not a scalar; it breaks like any other symbol.
                length = 1;
                props.Add(Wb.Other);
                pictographic.Add(false);
            }
            starts.Add(i);
            i += length;
        }
        var segmentStart = 0;
        for (var i = 1; i < props.Count; i++)
        {
            if (IsBoundary(props, pictographic, i))
            {
                segments.Add((starts[segmentStart], starts[i]));
                segmentStart = i;
            }
        }
        segments.Add((starts[segmentStart], text.Length));
        return segments;
    }

    /// <summary>
    /// Whether a segment is a word token in ICU's sense: it carries a letter or digit
    /// (as Kotlin's <c>isLetterOrDigit</c>), a letter-number such as a Roman numeral
    /// ("Ⅻ" is one word on macOS, none on Android), or one of the alphabetic symbols —
    /// circled, parenthesized and squared Latin letters ("🄰", "ⓐ") — that Foundation
    /// counts. Circled digits ("①"), fractions ("½") and superscripts are not words.
    /// </summary>
    private static bool IsWordSegment(string text, int start, int end)
    {
        for (var i = start; i < end;)
        {
            if (Rune.TryGetRuneAt(text, i, out var rune))
            {
                if (Rune.IsLetterOrDigit(rune) || Rune.GetUnicodeCategory(rune) == UnicodeCategory.LetterNumber
                    || IsAlphabeticSymbol(rune.Value))
                    return true;
                i += rune.Utf16SequenceLength;
            }
            else
            {
                i++;
            }
        }
        return false;
    }

    /// <summary>Other_Alphabetic symbols: circled Latin letters, parenthesized and squared Latin letters.</summary>
    private static bool IsAlphabeticSymbol(int v) =>
        v is >= 0x24B6 and <= 0x24E9 || v is >= 0x1F110 and <= 0x1F129 || v is >= 0x1F130 and <= 0x1F149 || v is >= 0x1F150 and <= 0x1F169;

    // MARK: Word_Break property values

    private enum Wb : byte
    {
        Other, CR, LF, Newline, Extend, ZWJ, RegionalIndicator, Format, Katakana, HebrewLetter,
        ALetter, SingleQuote, DoubleQuote, MidNumLet, MidLetter, MidNum, Numeric, ExtendNumLet, WSegSpace,
        /// <summary>Line_Break=SA letters (Thai, Lao, Khmer, Myanmar, the Tai scripts): ICU's dictionary characters, which join only each other.</summary>
        Complex,
    }

    private static Wb Classify(Rune rune)
    {
        var v = rune.Value;
        switch (v)
        {
            case 0x0D: return Wb.CR;
            case 0x0A: return Wb.LF;
            case 0x0B: case 0x0C: case 0x85: case 0x2028: case 0x2029: return Wb.Newline;
            case 0x200D: return Wb.ZWJ;
            case 0x200C: return Wb.Extend;
            case 0x200B: return Wb.Other;          // ZWSP: Cf, but excluded from Format by the table
            case 0x27: return Wb.SingleQuote;
            case 0x22: return Wb.DoubleQuote;
            case 0x2E: case 0x2018: case 0x2019: case 0x2024: case 0xFE52: case 0xFF07: case 0xFF0E:
                return Wb.MidNumLet;
            // ICU's root rules subtract the colon family (U+003A, U+FE55, U+FF1A) from
            // MidLetter — only the Swedish and Finnish tailorings keep it — so "a:b" is
            // two words on macOS and Android; the colons fall through to Other.
            case 0xB7: case 0x387: case 0x55F: case 0x5F4: case 0x2027: case 0xFE13:
                return Wb.MidLetter;
            case 0x2C: case 0x3B: case 0x37E: case 0x589: case 0x60C: case 0x60D: case 0x66C: case 0x7F8:
            case 0x2044: case 0xFE10: case 0xFE14: case 0xFE50: case 0xFE54: case 0xFF0C: case 0xFF1B:
                return Wb.MidNum;
            case 0x66B: return Wb.Numeric;         // Arabic decimal separator (Line_Break=NU)
            case 0x202F: return Wb.ExtendNumLet;   // narrow no-break space
            case 0x5F3: return Wb.ALetter;         // Hebrew geresh
        }
        if (v is >= 0x1F1E6 and <= 0x1F1FF) return Wb.RegionalIndicator;
        if (v is >= 0xFF9E and <= 0xFF9F || v is >= 0x1F3FB and <= 0x1F3FF) return Wb.Extend;
        if (v is >= 0x2C2 and <= 0x2C5 || v is >= 0x2D2 and <= 0x2D7 || v is 0x2DE or 0x2DF
            || v is >= 0x2E5 and <= 0x2EB || v is 0x2ED || v is >= 0x2EF and <= 0x2FF
            || v is >= 0x55A and <= 0x55C || v is 0x55E)
            return Wb.ALetter;                     // modifier symbols and Armenian marks the table lists as ALetter
        var category = Rune.GetUnicodeCategory(rune);
        switch (category)
        {
            case UnicodeCategory.NonSpacingMark:
            case UnicodeCategory.EnclosingMark:
            case UnicodeCategory.SpacingCombiningMark:
                return Wb.Extend;
            case UnicodeCategory.Format:
                return Wb.Format;
            case UnicodeCategory.DecimalDigitNumber:
                // UAX #29 leaves the fullwidth digits out of Numeric (Line_Break=ID); ICU's
                // root rules add every wide decimal digit back, so "１２３" is one word.
                return Wb.Numeric;
            case UnicodeCategory.ConnectorPunctuation:
                return Wb.ExtendNumLet;
            case UnicodeCategory.SpaceSeparator:
                return v is 0xA0 or 0x2007 ? Wb.Other : Wb.WSegSpace;           // no-break spaces are Glue, not WSegSpace
        }
        if (IsKatakana(v)) return Wb.Katakana;
        if (IsHebrewLetter(v)) return Wb.HebrewLetter;
        if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
            or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber)
        {
            if (IsIdeographic(v) || IsHiragana(v)) return Wb.Other;
            return IsComplexContext(v) ? Wb.Complex : Wb.ALetter;
        }
        return Wb.Other;
    }

    /// <summary>Line_Break=SA: the South-East Asian scripts ICU segments with a dictionary (letters only; their marks are Extend).</summary>
    private static bool IsComplexContext(int v) =>
        v is >= 0xE01 and <= 0xE3A || v is >= 0xE40 and <= 0xE4E            // Thai
        || v is >= 0xE81 and <= 0xEDF                                         // Lao
        || v is >= 0x1000 and <= 0x109F || v is >= 0xA9E0 and <= 0xA9FE || v is >= 0xAA60 and <= 0xAA7F   // Myanmar
        || v is >= 0x1780 and <= 0x17D3 || v is 0x17D7 || v is 0x17DC or 0x17DD   // Khmer
        || v is >= 0x1950 and <= 0x1974                                       // Tai Le
        || v is >= 0x1980 and <= 0x19DF                                       // New Tai Lue
        || v is >= 0x1A00 and <= 0x1A1E                                       // Buginese
        || v is >= 0x1A20 and <= 0x1AAD                                       // Tai Tham
        || v is >= 0x1B00 and <= 0x1B7C                                       // Balinese
        || v is >= 0xA980 and <= 0xA9DF                                       // Javanese
        || v is >= 0xAA00 and <= 0xAA5F                                       // Cham
        || v is >= 0xAA80 and <= 0xAADF;                                      // Tai Viet

    private static bool IsKatakana(int v) =>
        v is >= 0x3031 and <= 0x3035 || v is 0x309B or 0x309C || v is >= 0x30A0 and <= 0x30FA
        || v is >= 0x30FC and <= 0x30FF || v is >= 0x31F0 and <= 0x31FF || v is >= 0x32D0 and <= 0x32FE
        || v is >= 0x3300 and <= 0x3357 || v is >= 0xFF66 and <= 0xFF9D || v is >= 0x1AFF0 and <= 0x1AFF3
        || v is >= 0x1AFF5 and <= 0x1AFFB || v is 0x1AFFD or 0x1AFFE || v is 0x1B000
        || v is >= 0x1B120 and <= 0x1B122 || v is 0x1B155 || v is >= 0x1B164 and <= 0x1B167;

    private static bool IsHiragana(int v) =>
        v is >= 0x3041 and <= 0x3096 || v is >= 0x309D and <= 0x309F || v is 0x1B001 or 0x1B11F or 0x1B132 or 0x1B150 or 0x1B151 or 0x1B152
        || v is >= 0x1B002 and <= 0x1B11E || v is 0x1F200;

    private static bool IsHebrewLetter(int v) =>
        v is >= 0x5D0 and <= 0x5EA || v is >= 0x5EF and <= 0x5F2 || v is 0xFB1D || v is >= 0xFB1F and <= 0xFB28
        || v is >= 0xFB2A and <= 0xFB36 || v is >= 0xFB38 and <= 0xFB3C || v is 0xFB3E || v is 0xFB40 or 0xFB41
        || v is 0xFB43 or 0xFB44 || v is >= 0xFB46 and <= 0xFB4F;

    private static bool IsIdeographic(int v) =>
        v is 0x3006 or 0x3007 || v is >= 0x3021 and <= 0x3029 || v is >= 0x3038 and <= 0x303A
        || v is >= 0x3400 and <= 0x4DBF || v is >= 0x4E00 and <= 0x9FFF || v is >= 0xF900 and <= 0xFA6D
        || v is >= 0xFA70 and <= 0xFAD9 || v is 0x16FE4 || v is >= 0x17000 and <= 0x187F7
        || v is >= 0x18800 and <= 0x18CD5 || v is >= 0x18D00 and <= 0x18D08 || v is >= 0x1B170 and <= 0x1B2FB
        || v is >= 0x20000 and <= 0x2A6DF || v is >= 0x2A700 and <= 0x2EBE0 || v is >= 0x2F800 and <= 0x2FA1D
        || v is >= 0x30000 and <= 0x3134A || v is >= 0x31350 and <= 0x323AF;

    private static bool IsExtendedPictographic(int v) =>
        v is 0xA9 or 0xAE or 0x203C or 0x2049 or 0x2122 or 0x2139 || v is >= 0x2194 and <= 0x2199
        || v is 0x21A9 or 0x21AA || v is 0x231A or 0x231B || v is 0x2328 or 0x23CF || v is >= 0x23E9 and <= 0x23F3
        || v is >= 0x23F8 and <= 0x23FA || v is 0x24C2 || v is 0x25AA or 0x25AB || v is 0x25B6 or 0x25C0
        || v is >= 0x25FB and <= 0x25FE || v is >= 0x2600 and <= 0x27BF || v is 0x2934 or 0x2935
        || v is >= 0x2B05 and <= 0x2B07 || v is 0x2B1B or 0x2B1C || v is 0x2B50 or 0x2B55
        || v is 0x3030 or 0x303D or 0x3297 or 0x3299 || v is >= 0x1F000 and <= 0x1FAFF || v is >= 0x1FC00 and <= 0x1FFFD;

    // MARK: Boundary rules

    private static bool IsIgnorable(Wb p) => p is Wb.Extend or Wb.Format or Wb.ZWJ;
    private static bool IsAhLetter(Wb p) => p is Wb.ALetter or Wb.HebrewLetter;
    private static bool IsMidNumLetQ(Wb p) => p is Wb.MidNumLet or Wb.SingleQuote;
    private static bool IsNewline(Wb p) => p is Wb.Newline or Wb.CR or Wb.LF;

    /// <summary>Whether a word boundary falls before rune <paramref name="i"/>.</summary>
    private static bool IsBoundary(List<Wb> p, List<bool> pictographic, int i)
    {
        var prev = p[i - 1];
        var cur = p[i];
        if (prev == Wb.CR && cur == Wb.LF) return false;                     // WB3
        if (IsNewline(prev)) return true;                                    // WB3a
        if (IsNewline(cur)) return true;                                     // WB3b
        if (prev == Wb.ZWJ && pictographic[i]) return false;                 // WB3c
        if (prev == Wb.WSegSpace && cur == Wb.WSegSpace) return false;       // WB3d
        if (IsIgnorable(cur)) return false;                                  // WB4: X (Extend | Format | ZWJ)* → X

        // WB4 lookback: the last rune that is not glued onto its predecessor.
        var left = i - 1;
        while (left >= 0 && IsIgnorable(p[left])) left--;
        if (left < 0) return true;                                           // only glue before us → WB999
        var l = p[left];

        if (IsAhLetter(l) && IsAhLetter(cur)) return false;                                            // WB5
        if (IsAhLetter(l) && (cur == Wb.MidLetter || IsMidNumLetQ(cur)) && IsAhLetter(NextReal(p, i))) return false;   // WB6
        if ((l == Wb.MidLetter || IsMidNumLetQ(l)) && IsAhLetter(cur) && IsAhLetter(PrevReal(p, left))) return false;  // WB7
        if (l == Wb.HebrewLetter && cur == Wb.SingleQuote) return false;                               // WB7a
        if (l == Wb.HebrewLetter && cur == Wb.DoubleQuote && NextReal(p, i) == Wb.HebrewLetter) return false;          // WB7b
        if (l == Wb.DoubleQuote && cur == Wb.HebrewLetter && PrevReal(p, left) == Wb.HebrewLetter) return false;      // WB7c
        if (l == Wb.Numeric && cur == Wb.Numeric) return false;                                        // WB8
        if (IsAhLetter(l) && cur == Wb.Numeric) return false;                                          // WB9
        if (l == Wb.Numeric && IsAhLetter(cur)) return false;                                          // WB10
        if ((l == Wb.MidNum || IsMidNumLetQ(l)) && cur == Wb.Numeric && PrevReal(p, left) == Wb.Numeric) return false; // WB11
        if (l == Wb.Numeric && (cur == Wb.MidNum || IsMidNumLetQ(cur)) && NextReal(p, i) == Wb.Numeric) return false;  // WB12
        if (l == Wb.Katakana && cur == Wb.Katakana) return false;                                      // WB13
        if (l == Wb.Complex && cur == Wb.Complex) return false;                                        // ICU: dictionary run
        // WB13a / WB13b without Katakana: ICU segments Katakana with its Japanese
        // dictionary, outside the connector rules, so "カ_カ" is three tokens on macOS.
        if ((IsAhLetter(l) || l is Wb.Numeric or Wb.ExtendNumLet) && cur == Wb.ExtendNumLet) return false;   // WB13a
        if (l == Wb.ExtendNumLet && (IsAhLetter(cur) || cur == Wb.Numeric)) return false;                     // WB13b
        if (l == Wb.RegionalIndicator && cur == Wb.RegionalIndicator)                                  // WB15 / WB16
        {
            var run = 0;
            for (var k = left; k >= 0; k--)
            {
                if (IsIgnorable(p[k])) continue;
                if (p[k] != Wb.RegionalIndicator) break;
                run++;
            }
            if (run % 2 == 1) return false;
        }
        return true;                                                                                   // WB999
    }

    /// <summary>The property of the first non-glue rune after <paramref name="i"/>, or Other at the end.</summary>
    private static Wb NextReal(List<Wb> p, int i)
    {
        for (var k = i + 1; k < p.Count; k++)
        {
            if (!IsIgnorable(p[k])) return p[k];
        }
        return Wb.Other;
    }

    /// <summary>The property of the last non-glue rune before <paramref name="i"/>, or Other at the start.</summary>
    private static Wb PrevReal(List<Wb> p, int i)
    {
        for (var k = i - 1; k >= 0; k--)
        {
            if (!IsIgnorable(p[k])) return p[k];
        }
        return Wb.Other;
    }
}
