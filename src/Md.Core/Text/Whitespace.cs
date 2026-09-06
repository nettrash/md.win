namespace Md.Core.Text;

/// <summary>
/// The whitespace and newline sets the parser and the writers trim with, spelled out.
/// </summary>
/// <remarks>
/// Five sets, never conflated, because every port that conflated two of them shipped a
/// divergence: the same document was two paragraphs on one platform and one on another.
/// <list type="bullet">
/// <item><b>WS</b> — Foundation's <c>CharacterSet.whitespaces</c>: Unicode <c>Zs</c> ∪ U+0009 as
/// Apple's frozen tables have it, which still puts <b>U+200B ZERO WIDTH SPACE</b> in <c>Zs</c>
/// (current Unicode says <c>Cf</c>). Kotlin adds it by hand; TypeScript lists it; so does this.
/// U+180E is <i>not</i> in the set (it has been <c>Cf</c> since 6.3 and Foundation agrees), and
/// no line terminator is. Decides blank lines, fence info strings, closing fences, front-matter
/// fences, footnote-definition and heading trims, table cells.</item>
/// <item><b>WSNL</b> — <c>.whitespacesAndNewlines</c>: WS plus the seven line terminators. Used
/// only by the note-body trim and the raw-diagram probes. U+0085 NEL is whitespace here and
/// content everywhere else.</item>
/// <item><b>SP</b> — U+0020 alone: every indentation drop in the block grammar.</item>
/// <item><b>SPTAB</b> — U+0020 and U+0009: thematic-break filtering, the closing-hash check and
/// the CSV alignment cell — the <i>opposite</i> decision from the blank-line rule, on purpose,
/// so that <c>U+200B 1 U+200B</c> is not a number on any platform.</item>
/// <item><b>NL</b> — Foundation's <c>.newlines</c>: the wider splitter the raw <c>.puml</c> /
/// <c>.gv</c> probes use. The block scanner never uses it.</item>
/// </list>
/// None of this may be replaced by <c>char.IsWhiteSpace</c>, <c>string.Trim()</c> or a regex
/// <c>\s</c>: each is a different set (they include the terminators and U+0085 and exclude
/// U+200B). Every member is a BMP code unit, so a <c>char</c> walk and a scalar walk agree, and no
/// half of a surrogate pair equals any of them.
/// </remarks>
public static class Whitespace
{
    /// <summary>WS — Foundation's <c>.whitespaces</c>, nineteen code units, U+200B included.</summary>
    public static bool IsWhitespace(char c) => c switch
    {
        '\t' or ' ' or '\u00A0' or '\u1680' => true,
        >= '\u2000' and <= '\u200B' => true,          // U+2000–U+200A are Zs; U+200B is the frozen-table entry
        '\u202F' or '\u205F' or '\u3000' => true,
        _ => false,
    };

    /// <summary>NL — Foundation's <c>.newlines</c>. CRLF is handled by the splitter, not here.</summary>
    public static bool IsNewline(char c) =>
        c is '\n' or '\u000B' or '\u000C' or '\r' or '\u0085' or '\u2028' or '\u2029';

    /// <summary>WSNL — Foundation's <c>.whitespacesAndNewlines</c>, twenty-six code units.</summary>
    public static bool IsWhitespaceOrNewline(char c) => IsWhitespace(c) || IsNewline(c);

    /// <summary>
    /// <c>trimmingCharacters(in: .whitespaces)</c>. Trims code units, as Foundation trims scalars:
    /// <c>" \u0301abc"</c> loses the space even though space-plus-mark is one grapheme.
    /// </summary>
    public static string TrimWS(string s) => Trim(s, IsWhitespace);

    /// <summary><c>trimmingCharacters(in: .whitespacesAndNewlines)</c> — the note body and nothing else in the parser.</summary>
    public static string TrimWSNL(string s) => Trim(s, IsWhitespaceOrNewline);

    public static string TrimLeadingWS(string s)
    {
        var start = 0;
        while (start < s.Length && IsWhitespace(s[start])) start++;
        return start == 0 ? s : s.Substring(start);
    }

    public static string TrimTrailingWS(string s)
    {
        var end = s.Length;
        while (end > 0 && IsWhitespace(s[end - 1])) end--;
        return end == s.Length ? s : s.Substring(0, end);
    }

    /// <summary>SPTAB trim — <c>CharacterSet(charactersIn: " \t")</c>. The CSV alignment cell only.</summary>
    public static string TrimSpaceTab(string s) => Trim(s, static c => c is ' ' or '\t');

    /// <summary>SP — drop leading U+0020 only; a tab is content.</summary>
    public static string DropLeadingSpaces(string s)
    {
        var start = 0;
        while (start < s.Length && s[start] == ' ') start++;
        return start == 0 ? s : s.Substring(start);
    }

    /// <summary>SPTAB — drop leading U+0020 / U+0009 only, not the whitespace set (the DOT header scan).</summary>
    public static string DropSpaceTab(string s)
    {
        var start = 0;
        while (start < s.Length && (s[start] == ' ' || s[start] == '\t')) start++;
        return start == 0 ? s : s.Substring(start);
    }

    /// <summary>
    /// The block scanner's line splitter: <c>\r</c>, <c>\n</c> and <c>\r\n</c> each end exactly one
    /// line; VT, FF, NEL, LS and PS are content. The final segment is always appended, so
    /// <c>"a\n"</c> is <c>["a", ""]</c> and <c>""</c> is <c>[""]</c> — observable through an unclosed
    /// fence at EOF, which gains a trailing newline (a wart every port replicates).
    /// </summary>
    /// <remarks>
    /// Not <c>Split('\n')</c> plus <c>TrimEnd('\r')</c>: a lone CR would not split and a line ending in
    /// CR would be altered. <c>parse</c>, <c>outline</c> and <c>notes</c> all use this so their line
    /// numbers agree.
    /// </remarks>
    public static IReadOnlyList<string> NormalizedLines(string source)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (c == '\r')
            {
                lines.Add(source.Substring(start, i - start));
                if (i + 1 < source.Length && source[i + 1] == '\n') i++;
                start = i + 1;
            }
            else if (c == '\n')
            {
                lines.Add(source.Substring(start, i - start));
                start = i + 1;
            }
        }
        lines.Add(source.Substring(start));
        return lines;
    }

    /// <summary>
    /// Foundation's <c>components(separatedBy: .newlines)</c>, CRLF as one terminator — the wider
    /// splitter the raw-diagram probes use (U+0085 splits a line here and nowhere else). Kept
    /// separate from <see cref="NormalizedLines"/> on purpose; merging them changes which documents
    /// are diagrams.
    /// </summary>
    public static IReadOnlyList<string> NewlineSetLines(string source)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];
            if (!IsNewline(c)) continue;
            lines.Add(source.Substring(start, i - start));
            if (c == '\r' && i + 1 < source.Length && source[i + 1] == '\n') i++;
            start = i + 1;
        }
        lines.Add(source.Substring(start));
        return lines;
    }

    private static string Trim(string s, Func<char, bool> member)
    {
        var start = 0;
        var end = s.Length;
        while (start < end && member(s[start])) start++;
        while (end > start && member(s[end - 1])) end--;
        return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
    }
}
