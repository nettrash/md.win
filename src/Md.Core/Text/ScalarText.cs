using System.Globalization;
using System.Text;

namespace Md.Core.Text;

/// <summary>
/// The ordinal string operations every file that matches a delimiter inside author text goes
/// through, plus the code-point helpers the slug and the DOT header scan need.
/// </summary>
/// <remarks>
/// <para>
/// The Swift original exists to defeat grapheme matching: <c>"%́".contains("%")</c> is false
/// there. C# strings are UTF-16 code units, the same model as Kotlin and TypeScript, so every
/// block delimiter (all single ASCII scalars, none equal to a surrogate half) is found by a plain
/// ordinal search. The trap here is the opposite one: .NET's <c>IndexOf(string)</c>,
/// <c>StartsWith(string)</c>, <c>EndsWith(string)</c> and <c>ToLower()</c> are culture-sensitive
/// by default, and under ICU a culture-sensitive search treats ZWJ, ZWSP and the soft hyphen as
/// ignorable — <c>"a‍b".IndexOf("ab")</c> is 0. That is the Swift bug re-imported from the
/// other direction, and it passes every test that lacks a mark. Everything here is
/// <see cref="StringComparison.Ordinal"/>; nothing normalises; nothing walks graphemes.
/// </para>
/// <para>
/// The two contract differences from the BCL, both relied upon by call sites:
/// <see cref="Contains"/> never finds an empty needle (BCL: true), and <see cref="FirstIndex"/>
/// returns null for an empty or over-long needle instead of 0 / throwing.
/// </para>
/// </remarks>
public static class ScalarText
{
    /// <summary>True when <paramref name="needle"/> occurs in <paramref name="text"/>. An empty needle is never found.</summary>
    public static bool Contains(string text, string needle) =>
        needle.Length > 0 && text.Contains(needle, StringComparison.Ordinal);

    /// <summary>
    /// The first index at or after <paramref name="from"/> where <paramref name="needle"/> begins,
    /// or null — also null when the needle is empty or longer than the haystack.
    /// </summary>
    public static int? FirstIndex(string text, string needle, int from = 0)
    {
        if (needle.Length == 0 || needle.Length > text.Length) return null;
        var start = Math.Max(from, 0);
        if (start + needle.Length > text.Length) return null;
        var index = text.IndexOf(needle, start, StringComparison.Ordinal);
        return index < 0 ? null : index;
    }

    /// <summary>Exact code-unit prefix. An empty prefix is true, as in the stdlib.</summary>
    public static bool HasPrefix(string text, string prefix) => text.StartsWith(prefix, StringComparison.Ordinal);

    /// <summary>Exact code-unit suffix. An empty suffix is true, as in the stdlib.</summary>
    public static bool HasSuffix(string text, string suffix) => text.EndsWith(suffix, StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="text"/> cut at every occurrence of <paramref name="separator"/> — Kotlin's
    /// <c>split(String)</c>, empty edge pieces included; an empty separator yields the whole text.
    /// </summary>
    public static IReadOnlyList<string> Split(string text, string separator) =>
        separator.Length == 0 ? [text] : text.Split(separator, StringSplitOptions.None);

    /// <summary>Every occurrence replaced, left to right, never rescanning what was written; an empty target leaves the text alone.</summary>
    public static string Replacing(string text, string target, string replacement) =>
        target.Length == 0 ? text : text.Replace(target, replacement, StringComparison.Ordinal);

    /// <summary>
    /// <paramref name="text"/> without its first <paramref name="count"/> <b>code points</b> — what the
    /// Swift drops in scalars and Kotlin's <c>substring(n)</c> drops in units. The parser only ever
    /// drops a known ASCII prefix, where the two agree; counting code points keeps the helper honest
    /// for a caller that does not.
    /// </summary>
    public static string DropFirst(string text, int count)
    {
        if (count <= 0) return text;
        var offset = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (count == 0) break;
            offset += rune.Utf16SequenceLength;
            count--;
        }
        return offset >= text.Length ? "" : text.Substring(offset);
    }

    // MARK: - Code points

    /// <summary>Unicode general categories Mn, Mc, Me — the marks a slug keeps and a DOT name may carry.</summary>
    public static bool IsMark(Rune rune) => Rune.GetUnicodeCategory(rune) is
        UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    /// <summary>
    /// The part of Unicode's <c>Alphabetic</c> property that is neither a letter, a mark nor a
    /// letter-number: the circled and squared Latin letters, all general category <c>So</c>.
    /// </summary>
    /// <remarks>
    /// .NET exposes no <c>Alphabetic</c> property. Swift's slug asks <c>isAlphabetic</c> and the
    /// TypeScript port <c>\p{Alphabetic}</c>; Kotlin asks <c>Character.isLetter</c> and so already
    /// drops these. This table — <c>Other_Alphabetic</c> minus <c>L</c>, <c>M</c> and <c>Nl</c>, from
    /// <c>DerivedCoreProperties.txt</c> — is what closes the gap to Swift and TypeScript, the two
    /// ports the golden corpus was captured from.
    /// </remarks>
    public static bool IsEnclosedAlphabetic(Rune rune) => rune.Value is
        (>= 0x24B6 and <= 0x24E9)      // CIRCLED LATIN CAPITAL/SMALL LETTER A..Z
        or (>= 0x1F130 and <= 0x1F149) // SQUARED LATIN CAPITAL LETTER A..Z
        or (>= 0x1F150 and <= 0x1F169) // NEGATIVE CIRCLED LATIN CAPITAL LETTER A..Z
        or (>= 0x1F170 and <= 0x1F189); // NEGATIVE SQUARED LATIN CAPITAL LETTER A..Z

    /// <summary>
    /// Swift's <c>String.lowercased()</c>: every scalar replaced by its own
    /// <c>lowercaseMapping</c> — the full Unicode mapping, applied scalar by scalar with no
    /// context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ToLowerInvariant()</c> is the <i>simple</i> 1:1 mapping. The full mapping differs from it
    /// in one language-independent, context-free place in <c>SpecialCasing.txt</c>: U+0130 LATIN
    /// CAPITAL LETTER I WITH DOT ABOVE lowercases to <c>i</c> + U+0307 (two code points). The slug
    /// suite pins <c>İ</c> → <c>i̇</c>, so that one is written out; everything else is
    /// <see cref="Rune.ToLowerInvariant"/>.
    /// </para>
    /// <para>
    /// Deliberately <b>no</b> <c>Final_Sigma</c>: Swift maps each scalar on its own, so U+03A3 is
    /// always <c>σ</c> (checked against the real parser: <c>ΟΔΟΣ ΣΑΣ Σ</c> slugs to
    /// <c>οδοσ-σασ-σ</c>). Java's <c>toLowerCase(Locale.ROOT)</c> and JavaScript's
    /// <c>toLowerCase()</c> do apply the context rule and give <c>ς</c> at a word's end — a real
    /// three-way split between the ports, settled here in favour of md.macOS, the source of truth.
    /// </para>
    /// </remarks>
    public static string FullLowercase(string text)
    {
        var ascii = true;
        foreach (var c in text)
        {
            if (c > 0x7F) { ascii = false; break; }
        }
        if (ascii) return text.ToLowerInvariant();

        var builder = new StringBuilder(text.Length + 4);
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == 0x0130)
            {
                builder.Append('i').Append('̇');
                continue;
            }
            var lower = Rune.ToLowerInvariant(rune);
            var written = lower.EncodeToUtf16(buffer);
            builder.Append(buffer[..written]);
        }
        return builder.ToString();
    }
}
