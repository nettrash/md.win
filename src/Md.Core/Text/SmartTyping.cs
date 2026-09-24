using System.Globalization;
using System.Text;

namespace Md.Core.Text;

/// <summary>
/// The two pure keystroke helpers behind list continuation and sentence capitalization in the
/// editor: <see cref="Enter"/> decides what Return does on a list item, a quote line or a table
/// row, and <see cref="Capitalize"/> decides whether the one lowercase letter being typed starts
/// a line or a sentence. Both are SmartTyping, the specification every md port implements
/// (<c>docs/smart-typing.md</c>), ported function for function from the TypeScript reference
/// that generated the shared vectors.
/// </summary>
/// <remarks>
/// <para>
/// <b>The vectors are the contract.</b> <c>tests/Md.Core.Tests/Fixtures/typing-vectors.json</c>
/// holds 1350 vectors (512 <c>enter</c>, 838 <c>capitalize</c>), byte-identical in md, md.macOS,
/// md.Android and here, exactly as <c>plot-vectors.json</c> is for the plotter. No port may
/// diverge from a vector; a disputed vector is changed in the JSON first, in one commit across
/// all repos. Where the prose of the specification and a vector disagree, the vector wins and the
/// disagreement is recorded — the one this port carries is in <see cref="Upper"/>.
/// </para>
/// <para>
/// <b>Character model.</b> Every offset in, out and in the vectors is a UTF-16 code unit
/// (<c>string.Length</c>), the same model as Kotlin and the reference; the Swift port works in
/// <c>NSString</c> offsets for the same reason. Where a rule reads a <i>scalar</i> (a letter, a
/// mark, a symbol, the typed character) the pair is decoded by hand and a lone surrogate is its
/// own scalar of category <c>Cs</c>, which belongs to no class. Character classes are Unicode
/// general categories via <see cref="CharUnicodeInfo.GetUnicodeCategory(int)"/> — never
/// <c>char.IsLetter</c> / <c>Rune.IsLower</c>, which answer for a BMP half or include
/// <c>Other_Lowercase</c>. Digits are ASCII <c>0–9</c>. "Whitespace" is always WS19, the nineteen
/// units of Foundation's <c>.whitespaces</c> spelled out in <see cref="IsWS19"/>, and never
/// <c>char.IsWhiteSpace</c>, <c>string.Trim()</c> or a regex <c>\s</c> — the family has already
/// shipped a divergence from "whitespace" having four answers. Every comparison is ordinal;
/// nothing normalises; there are no regular expressions (§0.8: ICU, <c>java.util.regex</c> and
/// .NET disagree on <c>\s</c>, <c>\w</c> and <c>\b</c>).
/// </para>
/// <para>
/// <b>Structure.</b> The reference materialises a line array (its own comment says §0.14 forbids
/// that in a port, not in the oracle). This port keeps the reference's shape so that the two can
/// be read side by side, but the "line array" is a table of <c>(start, end)</c> offsets built in
/// one forward pass, and every line is read as a <see cref="ReadOnlySpan{T}"/> over the original
/// text — no substring per line, no <c>Split</c>, no <c>Whitespace.NormalizedLines</c>. The state
/// scan (§0.10) is one pass to the caret's line; every other walk is bounded by the caret's run
/// (the lines between the nearest blank line above and the caret) or by the caret's line, and the
/// table test of a line is memoised per call so a run of a thousand items stays linear. The three
/// reads below the caret that §0.14 names — the front-matter closer, the delimiter row under a
/// header, and the "table continues below" line — are the only ones.
/// </para>
/// <para>
/// <b>Deliberate divergences from what md renders</b> are listed in the specification's §5 with a
/// vector id each (fences and comments inside quotes, a table directly under a paragraph, the
/// bare-marker line, code spans reset per line, …) and §6 lists where md's own parser differs from
/// CommonMark. None of them is a bug to fix here: fixing one side toward CommonMark or toward the
/// renderer breaks the vectors on four platforms.
/// </para>
/// </remarks>
public static class SmartTyping
{
    /// <summary>
    /// One undoable edit that <see cref="Enter"/> asks the platform to perform on the
    /// <i>original</i> text: replace <c>[Location, Location + Length)</c> with
    /// <see cref="Replacement"/> and place a collapsed caret at <see cref="Caret"/>, an offset into
    /// the text after the edit. The replacement always uses U+000A, even in a CRLF document
    /// (§0.1); the adapter normalises if the control needs it.
    /// </summary>
    public readonly record struct EnterEdit(int Location, int Length, string Replacement, int Caret);

    // MARK: - §0.1 units and scalars

    private const int SP = 0x20;
    private const int TAB = 0x09;
    private const int CR = 0x0D;
    private const int LF = 0x0A;

    /// <summary>The unit at <paramref name="i"/>, or -1 outside the span — the reference's <c>charCodeAt</c> NaN, which compares equal to nothing.</summary>
    private static int U(ReadOnlySpan<char> s, int i) => (uint)i < (uint)s.Length ? s[i] : -1;

    private static bool IsHigh(int u) => u >= 0xD800 && u <= 0xDBFF;

    private static bool IsLow(int u) => u >= 0xDC00 && u <= 0xDFFF;

    /// <summary>The scalar starting at unit <paramref name="i"/> and its unit length; a lone surrogate is its own scalar.</summary>
    private static int ScalarAt(ReadOnlySpan<char> s, int i, out int len)
    {
        int u = s[i];
        if (IsHigh(u) && i + 1 < s.Length && IsLow(s[i + 1]))
        {
            len = 2;
            return 0x10000 + ((u - 0xD800) << 10) + (s[i + 1] - 0xDC00);
        }
        len = 1;
        return u;
    }

    /// <summary>The scalar ending just before unit <paramref name="i"/>, or -1 at the start.</summary>
    private static int ScalarBefore(ReadOnlySpan<char> s, int i, out int len)
    {
        len = 0;
        if (i <= 0) return -1;
        int u = s[i - 1];
        if (IsLow(u) && i - 2 >= 0 && IsHigh(s[i - 2])) return ScalarAt(s, i - 2, out len);
        len = 1;
        return u;
    }

    private static List<int> ScalarsOf(ReadOnlySpan<char> s)
    {
        var out_ = new List<int>(s.Length);
        for (int i = 0; i < s.Length;)
        {
            out_.Add(ScalarAt(s, i, out int len));
            i += len;
        }
        return out_;
    }

    // MARK: - §0.3 WS19, blank, trim

    /// <summary>
    /// WS19 — the nineteen scalars of Foundation's <c>.whitespaces</c> (U+0009, U+0020, U+00A0,
    /// U+1680, U+2000–U+200A, U+200B, U+202F, U+205F, U+3000), all BMP. The one meaning of
    /// "whitespace", "blank" and "trim" in this file.
    /// </summary>
    private static bool IsWS19(int u) =>
        u == 0x09 || u == 0x20 || u == 0xA0 || u == 0x1680
        || (u >= 0x2000 && u <= 0x200A) || u == 0x200B || u == 0x202F || u == 0x205F || u == 0x3000;

    private static bool IsBlank(ReadOnlySpan<char> s)
    {
        foreach (char c in s)
        {
            if (!IsWS19(c)) return false;
        }
        return true;
    }

    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        int a = 0, b = s.Length;
        while (a < b && IsWS19(s[a])) a++;
        while (b > a && IsWS19(s[b - 1])) b--;
        return s.Slice(a, b - a);
    }

    // MARK: - §0.4 character classes (general category of the scalar)

    /// <summary>The general category of one scalar; a lone surrogate is <c>Cs</c>.</summary>
    private static UnicodeCategory Category(int cp) => CharUnicodeInfo.GetUnicodeCategory(cp);

    private static bool IsLetter(int cp) => Category(cp) is UnicodeCategory.UppercaseLetter
        or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
        or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter;

    private static bool IsLowercase(int cp) => Category(cp) == UnicodeCategory.LowercaseLetter;

    private static bool IsUppercase(int cp) => Category(cp) == UnicodeCategory.UppercaseLetter;

    private static bool IsMark(int cp) => Category(cp) is UnicodeCategory.NonSpacingMark
        or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;

    private static bool IsSymbolSoSk(int cp) => Category(cp) is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol;

    private static bool IsDigit(int u) => u >= 0x30 && u <= 0x39;

    // MARK: - §0.7 case mapping — simple, one-to-one, locale-independent

    /// <summary>
    /// <c>upper(s)</c> for one scalar, or -1 when undefined (§0.7): a Georgian letter, a Greek
    /// letter with ypogegrammeni, U+00B5 MICRO SIGN, a surrogate, or a scalar whose simple
    /// uppercase mapping is itself (<c>ß</c>, <c>ŉ</c>, <c>ǰ</c>: their full mappings are two
    /// scalars, and the reference's one-scalar guard rejects them the same way).
    /// </summary>
    /// <remarks>
    /// <see cref="Rune.ToUpperInvariant"/> is the simple mapping the specification names for C#,
    /// with one documented exception. .NET's invariant casing pins U+0131 LATIN SMALL LETTER
    /// DOTLESS I to itself on every platform (<c>pal_casing.c</c> special-cases it so ICU matches
    /// Windows NLS), while <c>UnicodeData.txt</c> maps it to U+0049 and the vector
    /// <c>cap-typed-dotless-i</c> pins <c>ı</c> → <c>I</c>, as Swift, JavaScript and Java all do.
    /// The vectors win, so that one scalar is written out here.
    /// </remarks>
    private static int Upper(int cp)
    {
        if ((cp >= 0x10D0 && cp <= 0x10FF) || (cp >= 0x2D00 && cp <= 0x2D2F)) return -1; // Georgian
        if ((cp >= 0x1F80 && cp <= 0x1FAF) || cp == 0x1FB3 || cp == 0x1FC3 || cp == 0x1FF3) return -1; // ypogegrammeni
        if (cp == 0xB5) return -1; // MICRO SIGN: never GREEK CAPITAL MU in a unit prefix
        if (cp >= 0xD800 && cp <= 0xDFFF) return -1;
        if (cp == 0x0131) return 0x0049; // dotless i: the Unicode simple mapping, not .NET's invariant pin
        int mapped = Rune.ToUpperInvariant(new Rune(cp)).Value;
        return mapped == cp ? -1 : mapped;
    }

    // MARK: - §0.2 lines

    /// <summary>One line as unit offsets; <see cref="End"/> excludes the terminator.</summary>
    private readonly record struct Line(int Start, int End);

    /// <summary>
    /// The text plus its line table, and the per-call memo of <see cref="TableContext"/>. Lines are
    /// never copied: <see cref="L"/> is a span over the text.
    /// </summary>
    private sealed class Doc
    {
        public readonly string T;
        public readonly Line[] Lines;
        public int[]? TableHeader; // per line: -2 unknown, -1 no table, else the header's index
        public int[]? TableN;

        public Doc(string t)
        {
            T = t;
            Lines = SplitLines(t);
        }

        public int Count => Lines.Length;

        public ReadOnlySpan<char> L(int k) => T.AsSpan(Lines[k].Start, Lines[k].End - Lines[k].Start);
    }

    /// <summary>One forward pass: U+000A, U+000D U+000A (one terminator) or a lone U+000D end a line; nothing else does.</summary>
    private static Line[] SplitLines(string t)
    {
        var lines = new List<Line>();
        int start = 0;
        int i = 0;
        while (i < t.Length)
        {
            char u = t[i];
            if (u == CR)
            {
                lines.Add(new Line(start, i));
                i += (i + 1 < t.Length && t[i + 1] == LF) ? 2 : 1;
                start = i;
            }
            else if (u == LF)
            {
                lines.Add(new Line(start, i));
                i += 1;
                start = i;
            }
            else
            {
                i += 1;
            }
        }
        lines.Add(new Line(start, t.Length));
        return lines.ToArray();
    }

    /// <summary>
    /// Index of the line containing <paramref name="caret"/>, or -1 when the caret sits strictly
    /// between the CR and the LF of one terminator (§0.2). Lines never overlap, so the last line
    /// whose start is at or before the caret is the only candidate.
    /// </summary>
    private static int LineOf(Line[] lines, int caret)
    {
        int lo = 0, hi = lines.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (lines[mid].Start <= caret) lo = mid; else hi = mid - 1;
        }
        return caret <= lines[lo].End ? lo : -1;
    }

    // MARK: - §0.6 columns

    private static int Columns(ReadOnlySpan<char> run)
    {
        int col = 0;
        foreach (char c in run)
        {
            if (c == TAB) col += 4 - (col % 4); else col += 1;
        }
        return col;
    }

    // MARK: - §0.12 block tests on one line

    private static bool IsThematicBreak(ReadOnlySpan<char> s)
    {
        int n = 0;
        int ch = -1;
        foreach (char c in s)
        {
            if (c == SP || c == TAB) continue;
            if (c != '-' && c != '*' && c != '_') return false;
            if (ch == -1) ch = c; else if (c != ch) return false;
            n++;
        }
        return n >= 3;
    }

    private static bool IsATXHeading(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && s[i] == SP) i++;
        int n = 0;
        while (i < s.Length && s[i] == '#') { n++; i++; }
        if (n < 1 || n > 6) return false;
        return i == s.Length || s[i] == SP;
    }

    private static bool IsPageBreak(ReadOnlySpan<char> s)
    {
        var t = Trim(s);
        return t.SequenceEqual("\\newpage") || t.SequenceEqual("\\pagebreak");
    }

    private static bool IsFootnoteIdUnit(int u) =>
        (u >= 0x41 && u <= 0x5A) || (u >= 0x61 && u <= 0x7A) || IsDigit(u) || u == '-' || u == '_';

    /// <summary>Length of the footnote prefix <c>[^id]:</c> + WS19* measured on <paramref name="t"/> from <paramref name="from"/> (the <c>[</c>), or -1.</summary>
    private static int FootnotePrefixLength(ReadOnlySpan<char> t, int from)
    {
        if (!(U(t, from) == '[' && U(t, from + 1) == '^')) return -1;
        int i = from + 2;
        int idStart = i;
        while (i < t.Length && IsFootnoteIdUnit(t[i])) i++;
        if (i == idStart) return -1;
        if (!(U(t, i) == ']' && U(t, i + 1) == ':')) return -1;
        i += 2;
        while (i < t.Length && IsWS19(t[i])) i++;
        return i - from;
    }

    private static bool IsFootnoteDefinition(ReadOnlySpan<char> s) => FootnotePrefixLength(Trim(s), 0) >= 0;

    private static bool IsQuoteLine(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && s[i] == SP) i++;
        return U(s, i) == '>';
    }

    private static bool IsCommentStart(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && s[i] == SP) i++;
        return s.Slice(i).StartsWith("<!--");
    }

    // MARK: - §0.11 prefix grammar

    /// <summary>
    /// The §0.11 prefix of one line as offsets into it: <c>indent0</c> is <c>[0, Indent0)</c>,
    /// <c>quotes</c> follows it, then <c>indent1</c>, so the row prefix
    /// (<c>indent0 + quotes + indent1</c>) is the line's first <see cref="RowPrefixLen"/> units.
    /// </summary>
    private struct Prefix
    {
        public int Indent0;
        public int QuotesLen;
        public int LastGroupStart;   // offset within quotes where the last group starts (§1.3c); -1 when none
        public int Depth;
        public int Indent1;
        public int InnerStart;       // after indent0 + quotes: the "inner text" (§0.13)
        public bool HasMarker;
        public char Bullet;          // '\0' for an ordered marker
        public int Number;
        public char Delimiter;       // '.' or ')' for an ordered marker
        public bool HasBox;          // "[ ]" | "[x]" | "[X]" followed by SP
        public bool BoxAtEnd;        // the content is exactly a box (`- [ ]`): content for the caret, a box for §1.3a / §1.4
        public int PrefixEnd;        // where content starts
        public int ListIndentCols;   // §0.11 list indent in columns
        public int ListLevel;        // §0.11 list level = floor(columns / 2), the parser's `indent / 2`

        public readonly int Indent0QuotesLen => Indent0 + QuotesLen;

        public readonly int RowPrefixLen => Indent0 + QuotesLen + Indent1;
    }

    /// <summary>
    /// Parse the §0.11 prefix of one line. A marker or box counts only when followed by SP: one at
    /// the line end is content for both functions (the parser renders a bare <c>-</c> as an empty
    /// item, but Enter must not delete a <c>2020.</c> line and a typed letter would land directly
    /// after the unit — <c>-</c> + <c>a</c> is <c>-a</c>; §6 records the difference).
    /// </summary>
    private static Prefix ParsePrefix(ReadOnlySpan<char> s)
    {
        var p = new Prefix { LastGroupStart = -1 };
        int i = 0;
        // indent0 := run of SP / TAB
        while (i < s.Length && (s[i] == SP || s[i] == TAB)) i++;
        p.Indent0 = i;
        // quotes := ( run of SP, ">", at most one SP )*  — only when indent0 has no
        // TAB (the parser's `isQuote` drops SP only: `\t> x` is a paragraph).
        int quotesStart = i;
        int depth = 0;
        if (s.Slice(0, i).IndexOf('\t') < 0)
        {
            for (;;)
            {
                int j = i;
                while (j < s.Length && s[j] == SP) j++;
                if (j < s.Length && s[j] == '>')
                {
                    p.LastGroupStart = i - quotesStart;
                    j++;
                    if (j < s.Length && s[j] == SP) j++;
                    i = j;
                    depth++;
                }
                else
                {
                    break;
                }
            }
        }
        p.QuotesLen = i - quotesStart;
        p.Depth = depth;
        // indent1 := run of SP / TAB, only after quotes
        int indent1Start = i;
        if (depth > 0)
        {
            while (i < s.Length && (s[i] == SP || s[i] == TAB)) i++;
        }
        p.Indent1 = i - indent1Start;
        p.InnerStart = quotesStart + p.QuotesLen; // inner text = line minus indent0 and quotes (§0.13)
        p.ListIndentCols = Columns(depth > 0 ? s.Slice(indent1Start, p.Indent1) : s.Slice(0, p.Indent0));
        p.ListLevel = p.ListIndentCols / 2;
        int afterIndent = i;
        p.PrefixEnd = afterIndent;

        // marker is absent when the inner text is a thematic break (tested first, as the parser does)
        if (!IsThematicBreak(s.Slice(p.InnerStart)))
        {
            int m = afterIndent;
            char bullet = '\0';
            int num = 0;
            char delim = '\0';
            bool ok = false;
            int u = U(s, m);
            if (u == '-' || u == '*' || u == '+')
            {
                bullet = (char)u;
                m++;
                ok = true;
            }
            else if (IsDigit(u))
            {
                int d = m;
                while (d < s.Length && IsDigit(s[d])) d++;
                int digits = d - m;
                int ud = U(s, d);
                if (digits <= 9 && (ud == '.' || ud == ')'))
                {
                    num = DecimalOf(s.Slice(m, digits));
                    delim = (char)ud;
                    m = d + 1;
                    ok = true;
                }
            }
            if (ok)
            {
                // followed by SP (a marker at the line end is content, §0.11)
                bool followedBySP = m < s.Length && s[m] == SP;
                if (followedBySP)
                {
                    p.HasMarker = true;
                    p.Bullet = bullet;
                    p.Number = num;
                    p.Delimiter = delim;
                    // the marker absorbs the run of SP after it (the parser drops every SP)
                    while (m < s.Length && s[m] == SP) m++;
                    p.PrefixEnd = m;
                    // box := "[ ]" | "[x]" | "[X]", followed by SP (same refinement)
                    if (m + 3 <= s.Length && s[m] == '[' && s[m + 2] == ']'
                        && (s[m + 1] == ' ' || s[m + 1] == 'x' || s[m + 1] == 'X'))
                    {
                        int e = m + 3;
                        bool bSP = e < s.Length && s[e] == SP;
                        if (bSP)
                        {
                            p.HasBox = true;
                            while (e < s.Length && s[e] == SP) e++;
                            p.PrefixEnd = e;
                        }
                        else if (e == s.Length)
                        {
                            // §0.11: a box that is the whole content (`- [ ]`, `1. [x]`) is
                            // content for the caret test (§1.2) and for §1.3 (the item is not
                            // empty), but the line HAS a box for §1.3a and §1.4: the parser
                            // renders an empty task, and Enter continues the checklist
                            // (`enter-empty-task-no-space`).
                            p.BoxAtEnd = true;
                        }
                    }
                }
            }
        }
        return p;
    }

    /// <summary>The digit run (at most nine ASCII digits) as a decimal integer; leading zeros allowed.</summary>
    private static int DecimalOf(ReadOnlySpan<char> digits)
    {
        int n = 0;
        foreach (char c in digits) n = n * 10 + (c - '0');
        return n;
    }

    // MARK: - §0.13 table grammar

    private static List<string> SplitCells(ReadOnlySpan<char> row)
    {
        var r = Trim(row);
        if (r.Length > 0 && r[0] == '|') r = r.Slice(1);
        if (r.Length > 0 && r[r.Length - 1] == '|') r = r.Slice(0, r.Length - 1);
        var cells = new List<string>();
        var cur = new StringBuilder();
        bool escaped = false;
        foreach (char ch in r)
        {
            if (escaped)
            {
                if (ch != '|') cur.Append('\\');
                cur.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else if (ch == '|')
            {
                cells.Add(Trim(cur.ToString()).ToString());
                cur.Clear();
            }
            else
            {
                cur.Append(ch);
            }
        }
        if (escaped) cur.Append('\\');
        cells.Add(Trim(cur.ToString()).ToString());
        return cells;
    }

    private static bool InnerMarkerExists(ReadOnlySpan<char> inner) => ParsePrefix(inner).HasMarker;

    private static bool IsTablePair(ReadOnlySpan<char> header, ReadOnlySpan<char> delimiter)
    {
        if (header.IndexOf('|') < 0) return false;
        if (InnerMarkerExists(header)) return false;
        if (IsATXHeading(header) || IsFootnoteDefinition(header) || IsCommentStart(header)) return false;
        if (Trim(delimiter).IndexOf('-') < 0) return false;
        {
            // §0.13: a delimiter line with a `marker` is excluded only when its
            // content contains no `|`: `- ` and `- x` are list items to a writer,
            // while `- | -` and `- |` are delimiter rows (the pipe says table, and
            // the parser tests tables before lists) — §5 table-delimiter-list-marker
            var dp = ParsePrefix(delimiter);
            if (dp.HasMarker && delimiter.Slice(dp.PrefixEnd).IndexOf('|') < 0) return false;
        }
        var dCells = SplitCells(delimiter);
        foreach (var c in dCells)
        {
            if (c.Length == 0) return false;
            bool dash = false;
            foreach (char u in c)
            {
                if (u == '-') dash = true; else if (u != ':') return false;
            }
            if (!dash) return false;
        }
        return SplitCells(header).Count == dCells.Count;
    }

    // MARK: - §0.10 state scan

    private sealed class Scan
    {
        public bool[] Code = [];      // per line ≤ c: front matter / fence line / comment line
        public bool[] Special = [];   // per line ≤ c: blank, code (as above) or ATX heading
        public bool CaretCode;        // the caret's line is code (incl. tentative front matter)
    }

    private static int LeadingSPCount(ReadOnlySpan<char> s)
    {
        int i = 0;
        while (i < s.Length && s[i] == SP) i++;
        return i;
    }

    /// <summary>Fence opener test (§0.10): ≤ 3 leading SP, a run of <c>`</c> or <c>~</c> of length ≥ 3, no backtick in the rest of a backtick fence.</summary>
    private static bool FenceOpener(ReadOnlySpan<char> s, out char ch, out int n)
    {
        ch = '\0';
        n = 0;
        int i = LeadingSPCount(s);
        if (i > 3) return false;
        int c = U(s, i);
        if (c != '`' && c != '~') return false;
        int j = i;
        while (j < s.Length && s[j] == c) j++;
        n = j - i;
        if (n < 3) return false;
        if (c == '`' && s.Slice(j).IndexOf('`') >= 0) return false;
        ch = (char)c;
        return true;
    }

    private static bool FenceCloses(ReadOnlySpan<char> s, char ch, int n)
    {
        int i = LeadingSPCount(s);
        int j = i;
        while (j < s.Length && s[j] == ch) j++;
        if (j - i < n) return false;
        return IsBlank(s.Slice(j));
    }

    private static bool IsFrontMatterCloser(ReadOnlySpan<char> t, bool yaml) =>
        yaml ? (t.SequenceEqual("---") || t.SequenceEqual("...")) : t.SequenceEqual("+++");

    private static Scan ScanState(Doc doc, int c, bool forEnter)
    {
        var code = new bool[c + 1];
        var special = new bool[c + 1];
        bool caretCode = false;

        // --- front matter (all the parser's guards)
        int fmEnd = -1;
        var opener = Trim(doc.L(0));
        bool yaml = opener.SequenceEqual("---");
        bool toml = opener.SequenceEqual("+++");
        char separator = yaml ? ':' : toml ? '=' : '\0';
        if (separator != '\0')
        {
            bool holds = doc.Count > 1 && !IsBlank(doc.L(1));
            int close = -1;
            if (holds)
            {
                for (int k = 1; k < doc.Count; k++)
                {
                    if (IsFrontMatterCloser(Trim(doc.L(k)), yaml)) { close = k; break; }
                }
                if (close < 0) holds = false;
            }
            if (holds)
            {
                bool field = false;
                for (int k = 1; k < close; k++)
                {
                    var t = Trim(doc.L(k));
                    if (t.Length == 0 || t[0] == '#' || t[0] == '-') continue;
                    int sep = t.IndexOf(separator);
                    if (sep < 0) continue;
                    if (IsBlank(t.Slice(0, sep))) continue;
                    field = true;
                    break;
                }
                if (!field) holds = false;
            }
            if (holds)
            {
                fmEnd = close;
            }
            else if (c >= 1)
            {
                // tentative front matter (§0.10): a block that is already closed above
                // the caret is complete and rejected — prose. Otherwise the caret's
                // line is code iff every line of the tested range is non-blank and
                // field-like. The range is 1 ..< c for capitalize (the caret's line
                // is being typed) and 1 … c for enter (the line is complete).
                bool closed = false;
                for (int k = 1; k < c; k++)
                {
                    if (IsFrontMatterCloser(Trim(doc.L(k)), yaml)) { closed = true; break; }
                }
                if (!closed)
                {
                    int last = forEnter ? c : c - 1;
                    bool tentative = true;
                    bool fieldSeen = false;
                    for (int k = 1; k <= last; k++)
                    {
                        var t = Trim(doc.L(k));
                        if (t.Length == 0) { tentative = false; break; }
                        int sep = t.IndexOf(separator);
                        bool hash = t[0] == '#';
                        bool dash = t[0] == '-';
                        // a field mirrors parseFrontMatter's guard: a `#` or `-` line is never
                        // a field (`---⏎- key: v⏎---` renders as rule + list + rule)
                        bool isField = !hash && !dash && sep > 0 && !IsBlank(t.Slice(0, sep));
                        // a `#` comment (YAML and TOML) or a `-` list item (YAML only: TOML has
                        // no `-` lists) counts only under a field line: a list or heading
                        // directly under a bare `---` is Markdown
                        bool fieldLike = isField || ((hash || (dash && separator == ':')) && fieldSeen);
                        if (!fieldLike) { tentative = false; break; }
                        if (isField) fieldSeen = true;
                    }
                    if (tentative) caretCode = true;
                }
            }
        }
        for (int k = 0; k <= Math.Min(fmEnd, c); k++) { code[k] = true; special[k] = true; }
        if (fmEnd >= c) caretCode = true;

        // --- fences and comments, at quote depth 0 only
        const int Normal = 0, Fence = 1, Comment = 2;
        int state = Normal;
        char fenceChar = '\0';
        int fenceRun = 0;
        for (int k = fmEnd + 1; k <= c; k++)
        {
            var s = doc.L(k);
            if (state == Normal)
            {
                if (FenceOpener(s, out char ch, out int n))
                {
                    state = Fence;
                    fenceChar = ch;
                    fenceRun = n;
                    code[k] = true;
                }
                else if (IsCommentStart(s))
                {
                    code[k] = true;
                    state = s.IndexOf("-->") >= 0 ? Normal : Comment;
                }
            }
            else if (state == Fence)
            {
                code[k] = true;
                if (FenceCloses(s, fenceChar, fenceRun)) state = Normal;
            }
            else
            {
                code[k] = true;
                if (s.IndexOf("-->") >= 0) state = Normal;
            }
            if (code[k] || IsBlank(s) || IsATXHeading(s)) special[k] = true;
        }
        if (code[c]) caretCode = true;
        return new Scan { Code = code, Special = special, CaretCode = caretCode };
    }

    // MARK: - §0.13 tableContext

    private static int DepthOf(Doc doc, int i) => ParsePrefix(doc.L(i)).Depth;

    private static ReadOnlySpan<char> Inner(Doc doc, int i)
    {
        var s = doc.L(i);
        return s.Slice(ParsePrefix(s).InnerStart);
    }

    /// <summary>
    /// Whether line <paramref name="k"/> is a table row (§0.13), and if so the header's index and
    /// the header's cell count <paramref name="n"/>. Memoised per call: the ancestor walk and the
    /// inline scan ask this for every line of the caret's run.
    /// </summary>
    private static bool TableContext(Doc doc, Scan scan, int k, out int header, out int n)
    {
        if (doc.TableHeader is null)
        {
            doc.TableHeader = new int[doc.Count];
            Array.Fill(doc.TableHeader, -2);
            doc.TableN = new int[doc.Count];
        }
        if (doc.TableHeader[k] != -2)
        {
            header = doc.TableHeader[k];
            n = doc.TableN![k];
            return header >= 0;
        }
        header = -1;
        n = 0;
        int d = DepthOf(doc, k);
        int top = k;
        while (top - 1 >= 0 && !IsBlank(doc.L(top - 1)) && DepthOf(doc, top - 1) == d && !scan.Code[top - 1]) top--;
        int h = top;
        while (h <= k)
        {
            if (h + 1 >= doc.Count || DepthOf(doc, h + 1) != d || !IsTablePair(Inner(doc, h), Inner(doc, h + 1)))
            {
                h++;
                continue;
            }
            // the pair at h opens a table; it runs while the lines below contain a
            // pipe. A line without one ends it (the parser's row loop stops there)
            // and is not a header itself, so the search resumes below that line —
            // greedy consumption, never a second pair inside the first table's rows.
            int ended = -1;
            for (int j = h + 2; j <= k; j++)
            {
                if (doc.L(j).IndexOf('|') < 0) { ended = j; break; }
            }
            if (ended < 0)
            {
                header = h;
                n = SplitCells(Inner(doc, h)).Count;
                break;
            }
            h = ended + 1;
        }
        doc.TableHeader[k] = header;
        doc.TableN![k] = n;
        return header >= 0;
    }

    private static bool IsPipeLine(ReadOnlySpan<char> s)
    {
        // §0.13: the inner text (line minus indent0 and quotes), after indent1, starts with `|`
        var p = ParsePrefix(s);
        return U(s, p.RowPrefixLen) == '|';
    }

    // MARK: - §0.9 inputs, outputs and offsets

    /// <summary>
    /// §0.9: order the selection, reject an offset outside the text, strictly between the CR and
    /// the LF of one terminator, or between the halves of a surrogate pair; produce <c>text′</c>
    /// (the text with the selection removed — the original string itself when the selection is
    /// empty, so nothing is copied on the common path) with the caret at <paramref name="start"/>.
    /// </summary>
    private static bool Normalize(string text, int selectionStart, int selectionEnd, out int start, out int end, out string t)
    {
        start = Math.Min(selectionStart, selectionEnd);
        end = Math.Max(selectionStart, selectionEnd);
        t = text;
        if (!(0 <= start && start <= end && end <= text.Length)) return false;
        if (BetweenCRLF(text, start) || BetweenCRLF(text, end)) return false;
        // §0.9: an offset strictly between the high and the low half of a surrogate
        // pair would split one scalar in two (a corrupt document, and one Swift's
        // NSString bridge repairs differently from Kotlin and C#)
        if (BetweenSurrogates(text, start) || BetweenSurrogates(text, end)) return false;
        if (start != end) t = string.Concat(text.AsSpan(0, start), text.AsSpan(end));
        return true;
    }

    private static bool BetweenCRLF(string text, int o) => o > 0 && o < text.Length && text[o - 1] == CR && text[o] == LF;

    private static bool BetweenSurrogates(string text, int o) => o > 0 && o < text.Length && IsHigh(text[o - 1]) && IsLow(text[o]);

    // MARK: - §1 enter

    /// <summary>§1.4 <c>next(n)</c>: <c>n + 1</c>, unless that would need more than nine digits.</summary>
    private static int NextNumber(int n) => n + 1 > 999999999 ? n : n + 1;

    private static string MarkerPrime(in Prefix m, int number) =>
        m.Bullet != '\0' ? new string(m.Bullet, 1) + " " : number.ToString(CultureInfo.InvariantCulture) + m.Delimiter + " ";

    private static string Repeat(string s, int count)
    {
        var out_ = new StringBuilder(s.Length * count);
        for (int i = 0; i < count; i++) out_.Append(s);
        return out_.ToString();
    }

    /// <summary>
    /// What Return does (§1): <c>null</c> for the platform's own newline, or one
    /// <see cref="EnterEdit"/> — the next list item or quote line, an exited empty item, an
    /// outdented one, or a new table row. <paramref name="selectionStart"/> and
    /// <paramref name="selectionEnd"/> are UTF-16 offsets in either order.
    /// </summary>
    public static EnterEdit? Enter(string text, int selectionStart, int selectionEnd)
    {
        if (!Normalize(text, selectionStart, selectionEnd, out int start, out int end, out string t)) return null;
        int caret = start;
        var doc = new Doc(t);
        int c = LineOf(doc.Lines, caret);
        if (c < 0) return null;                                   // §0.2
        var scan = ScanState(doc, c, forEnter: true);
        if (scan.CaretCode) return null;                          // §1.0

        int removed = end - start;
        var line = doc.L(c);
        int lineStart = doc.Lines[c].Start;
        int lineEnd = doc.Lines[c].End;

        // §1.1 table row: the selection must lie within one line of `text`
        bool withinLine = true;
        for (int i = start; i < end; i++)
        {
            char u = text[i];
            if (u == CR || u == LF) { withinLine = false; break; }
        }
        if (withinLine && TableContext(doc, scan, c, out int header, out int n))
        {
            var p = ParsePrefix(line);
            string rowPrefix = line.Slice(0, p.RowPrefixLen).ToString();
            string row = "\n" + rowPrefix + "|" + Repeat("  |", n);
            // insert the row after the line end `xPrime` of text′ (at or after caret′);
            // with a selection the same edit is one range from `start` that also
            // deletes the selection (§0.9)
            EnterEdit InsertAfter(int xPrime)
            {
                int x = xPrime + removed;
                if (removed == 0) return new EnterEdit(x, 0, row, x + 1 + rowPrefix.Length + 2);
                string tail = text.Substring(end, x - end);
                return new EnterEdit(start, x - start, tail + row, start + tail.Length + 1 + rowPrefix.Length + 2);
            }
            if (c == header)
            {
                if (caret == lineStart) return null; // column 0 of the header: a plain newline pushes the table down
                return InsertAfter(doc.Lines[header + 1].End);
            }
            // §1.1: the table continues below the caret's row iff line c+1 exists,
            // has the same quote depth and contains a `|` unit (the parser's row
            // loop). A blank row ends the table only when it is the LAST row: a
            // blank line in the middle would orphan the rows below it
            // (`enter-table-blank-row-mid-table`).
            bool continuesBelow = c + 1 < doc.Count && ParsePrefix(doc.L(c + 1)).Depth == p.Depth && doc.L(c + 1).IndexOf('|') >= 0;
            if (removed == 0 && !continuesBelow && AllEmpty(SplitCells(line.Slice(p.InnerStart))))
            {
                // end the table; inside a quote the writer stays in the quote (as §1.3b)
                string r = p.Depth > 0 ? line.Slice(0, p.Indent0QuotesLen).ToString() : "";
                return new EnterEdit(lineStart, lineEnd - lineStart, r, lineStart + r.Length);
            }
            if (removed == 0 && c >= header + 2 && caret == lineStart)
            {
                // column 0 of a body row: the new row goes ABOVE the caret's row, which
                // moves down intact exactly as a plain newline would push it — the
                // writer's only "insert a row above" gesture (A.1). The delimiter row
                // keeps the insert-after branch: a row between header and delimiter
                // would break the table.
                string above = rowPrefix + "|" + Repeat("  |", n) + "\n";
                return new EnterEdit(lineStart, 0, above, lineStart + rowPrefix.Length + 2);
            }
            return InsertAfter(lineEnd);
        }

        var pc = ParsePrefix(line);
        int rel = caret - lineStart;
        if (rel < pc.PrefixEnd) return null;                      // §1.2
        var content = line.Slice(pc.PrefixEnd);

        // replaces text′[lineStart, caret′) with r
        EnterEdit ReplaceHead(string r) => new(lineStart, (caret - lineStart) + removed, r, lineStart + r.Length);

        // §1.3 empty item
        if (IsBlank(content) && (pc.HasMarker || pc.Depth > 0))
        {
            if (pc.HasMarker)
            {
                // a. outdent
                int anc = Ancestor(doc, scan, c, pc);
                if (anc >= 0)
                {
                    var ancestorLine = doc.L(anc);
                    var a = ParsePrefix(ancestorLine);
                    string r = string.Concat(ancestorLine.Slice(0, a.RowPrefixLen), MarkerPrime(a, NextNumber(a.Number)),
                        a.HasBox || a.BoxAtEnd ? "[ ] " : "");
                    return ReplaceHead(r);
                }
                // b. terminate a list item
                return ReplaceHead(pc.Depth > 0 ? line.Slice(0, pc.Indent0QuotesLen).ToString() : "");
            }
            // c. terminate a quote level
            int last = pc.LastGroupStart;
            return ReplaceHead(last == 0 ? "" : line.Slice(0, pc.Indent0 + last).ToString());
        }

        // §1.4 continue
        if (pc.HasMarker || pc.Depth > 0)
        {
            string mp = pc.HasMarker ? MarkerPrime(pc, NextNumber(pc.Number)) : "";
            string r = string.Concat("\n", line.Slice(0, pc.RowPrefixLen), mp, pc.HasBox || pc.BoxAtEnd ? "[ ] " : "");
            return new EnterEdit(start, removed, r, start + r.Length);
        }

        return null;                                              // §1.5
    }

    private static bool AllEmpty(List<string> cells)
    {
        foreach (var cell in cells)
        {
            if (cell.Length != 0) return false;
        }
        return true;
    }

    /// <summary>§1.3a ancestor walk: the index of the ancestor line, or -1.</summary>
    private static int Ancestor(Doc doc, Scan scan, int c, in Prefix p)
    {
        for (int k = c - 1; k >= 0; k--)
        {
            var s = doc.L(k);
            if (IsBlank(s)) return -1;
            var pk = ParsePrefix(s);
            if (pk.Depth != p.Depth) return -1;
            if (scan.Code[k]) return -1;
            if (TableContext(doc, scan, k, out _, out _)) return -1;
            var inner = s.Slice(pk.InnerStart);
            if (IsBlank(inner)) return -1; // `> ` between quoted items: the renderer sees a blank line
            if (IsThematicBreak(inner) || IsATXHeading(inner) || IsPageBreak(inner)) return -1;
            // §1.3a: the ancestor's list LEVEL (floor(columns / 2), the parser's
            // `indent / 2`) is strictly smaller — a 1-SP or 3-SP item renders as a
            // sibling, so Enter on it must not manufacture another sibling
            if (pk.HasMarker && pk.ListLevel < p.ListLevel) return k;
        }
        return -1;
    }

    // MARK: - §2 capitalize

    /// <summary>§2.2 openers: <c>* _ ~ " ' ( [ { « » ‹ › “ ” ‘ ’ „ ‚ ¿ ¡</c> (and <c>!</c> only as the <c>![</c> pair, tested by the callers).</summary>
    private static bool IsOpener(int u) => u is '*' or '_' or '~' or '"' or '\'' or '(' or '[' or '{'
        or 0x00AB or 0x00BB or 0x2039 or 0x203A or 0x201C or 0x201D or 0x2018 or 0x2019 or 0x201E or 0x201A
        or 0x00BF or 0x00A1;

    /// <summary>§2.2 closers: <c>) ] } " ' » « ‹ › ” ’ “ ‘ * _ ~</c>.</summary>
    private static bool IsCloser(int u) => u is ')' or ']' or '}' or '"' or '\''
        or 0x00BB or 0x00AB or 0x2039 or 0x203A or 0x201D or 0x2019 or 0x201C or 0x2018 or '*' or '_' or '~';

    /// <summary>§2.2: the quotation marks among the openers (the colon rule, §2.5 1a).</summary>
    private static bool IsQuoteOpener(int u) => u is '"' or '\'' or 0x00AB or 0x00BB or 0x2039 or 0x203A
        or 0x201C or 0x201D or 0x2018 or 0x2019 or 0x201E or 0x201A;

    /// <summary>§2.2: guillemets, which carry French spacing after them when opening.</summary>
    private static bool IsGuillemet(int u) => u is 0x00AB or 0x2039 or 0x00BB or 0x203A;

    private static bool IsTerminator(int u) => u == '.' || u == '!' || u == '?';

    /// <summary>
    /// §2.3 dash units: em dash, en dash, minus, horizontal bar (Unicode's "quotation dash"),
    /// figure dash, the pasted bullets • ‣ · and the section / pilcrow signs § ¶ (Po since
    /// Unicode 6.1, leads to a writer).
    /// </summary>
    private static bool IsDashUnit(int u) =>
        u == 0x2014 || u == 0x2013 || u == 0x2212 || u == 0x2015 || u == 0x2012
        || u == 0x2022 || u == 0x2023 || u == 0xB7 || u == 0xA7 || u == 0xB6;

    /// <summary>§2.3: arrows (U+2190–U+21FF) are leads at the content start only; they are not in the mid-line group.</summary>
    private static bool IsArrowUnit(int u) => u >= 0x2190 && u <= 0x21FF;

    /// <summary>§2.3 <c>dash</c> lead run: dash units, arrows or U+002D, but never a single hyphen-minus (a list marker).</summary>
    private static bool IsLeadDashUnit(int u) => IsDashUnit(u) || IsArrowUnit(u) || u == '-';

    /// <summary>§2.5 step 1b: the mid-line dash group — dash units or the hyphen-minus, mixed freely; arrows are not in it.</summary>
    private static bool IsMidLineDashUnit(int u) => IsDashUnit(u) || u == '-';

    private static bool IsSectionUnit(int u) => u == 0xA7 || u == 0xB6;

    private static bool IsRomanUnit(int u) => u == 'i' || u == 'v' || u == 'x' || u == 'I' || u == 'V' || u == 'X';

    /// <summary>§2.3 <c>number</c> body: <c>Digit{1,9} ( "." Digit{1,9} )* ( "." | ")" )?</c> — the end index after it, or -1.</summary>
    private static int NumberBodyEnd(ReadOnlySpan<char> s, int i)
    {
        int j = Digits(s, i);
        if (j == i) return -1;
        for (;;)
        {
            if (U(s, j) == '.' && j + 1 < s.Length && IsDigit(s[j + 1])) { j = Digits(s, j + 1); continue; }
            break;
        }
        if (U(s, j) == '.' || U(s, j) == ')') j++;
        return j;

        static int Digits(ReadOnlySpan<char> s, int from)
        {
            int j = from;
            while (j < s.Length && IsDigit(s[j]) && j - from < 9) j++;
            return j;
        }
    }

    /// <summary>§2.3 <c>enum</c> body: <c>"(" ( Digit{1,3} | Letter | roman{1,4} ) ")"</c> or the same without the <c>"("</c> — the end index after the <c>)</c>, or -1.</summary>
    private static int EnumBodyEnd(ReadOnlySpan<char> s, int i)
    {
        int j = i;
        bool paren = U(s, j) == '(';
        if (paren) j++;
        int b = j;
        int e = -1;
        {
            int d = b;
            while (d < s.Length && IsDigit(s[d]) && d - b < 3) d++;
            if (d > b && !IsDigit(U(s, d))) e = d;
        }
        if (e < 0)
        {
            int r = b;
            while (r < s.Length && IsRomanUnit(s[r]) && r - b < 4) r++;
            if (r > b && U(s, r) == ')') e = r;
        }
        if (e < 0 && b < s.Length)
        {
            int cp = ScalarAt(s, b, out int len);
            if (IsLetter(cp)) e = b + len;
        }
        if (e < 0 || U(s, e) != ')') return -1;
        return e + 1;
    }

    /// <summary>§2.3 <c>citation</c> lead body: <c>"[" Digit{1,4} "]"</c> — the end index after the <c>]</c>, or -1.</summary>
    private static int CitationBodyEnd(ReadOnlySpan<char> s, int i)
    {
        if (U(s, i) != '[') return -1;
        int d = i + 1;
        while (d < s.Length && IsDigit(s[d]) && d - (i + 1) < 4) d++;
        if (d == i + 1 || U(s, d) != ']') return -1;
        return d + 1;
    }

    /// <summary>§2.5 step 1b: start of the <c>symbols</c> run (§2.3, keycaps included) that ends at unit <paramref name="end"/> of <paramref name="pre"/>, or <paramref name="end"/> when there is none.</summary>
    private static int SymbolsRunStart(ReadOnlySpan<char> pre, int end)
    {
        int j = end;
        for (;;)
        {
            int cp = ScalarBefore(pre, j, out int len);
            if (cp < 0) break;
            if (cp == 0x20E3)
            {
                // keycap: ( Digit | "#" | "*" ) U+FE0F? U+20E3
                int k = j - 1;
                if (k > 0 && pre[k - 1] == 0xFE0F) k--;
                int u = k > 0 ? pre[k - 1] : -1;
                if (IsDigit(u) || u == '#' || u == '*') { j = k - 1; continue; }
                j -= 1;
                continue;
            }
            if (IsSymbolSoSk(cp) || cp == 0xFE0E || cp == 0xFE0F || cp == 0x200D || (cp >= 0x1F3FB && cp <= 0x1F3FF)) { j -= len; continue; }
            break;
        }
        return j;
    }

    /// <summary>§2.5 step 2: a footnote reference <c>[^id]</c> or a bracketed digit run <c>[12]</c> whose <c>]</c> sits at <c>end - 1</c> of <paramref name="pre"/> — its start, or -1.</summary>
    private static int ReferenceStart(ReadOnlySpan<char> pre, int end)
    {
        if (end < 3 || pre[end - 1] != ']') return -1;
        int j = end - 1;
        while (j > 0 && IsFootnoteIdUnit(pre[j - 1])) j--;
        if (j == end - 1) return -1;
        if (j >= 2 && pre[j - 1] == '^' && pre[j - 2] == '[') return j - 2;
        bool allDigits = true;
        for (int k = j; k < end - 1; k++)
        {
            if (!IsDigit(pre[k])) { allDigits = false; break; }
        }
        if (allDigits && j >= 1 && pre[j - 1] == '[') return j - 1;
        return -1;
    }

    private static ReadOnlySpan<char> StripOpeners(ReadOnlySpan<char> tok)
    {
        int o = 0;
        while (o < tok.Length)
        {
            if (IsOpener(tok[o])) { o++; continue; }
            if (tok[o] == '!' && U(tok, o + 1) == '[') { o++; continue; }
            break;
        }
        return tok.Slice(o);
    }

    /// <summary>§2.5 B(ii): one Letter scalar followed by zero or more Marks.</summary>
    private static bool IsSingleLetter(List<int> cps)
    {
        if (cps.Count < 1 || !IsLetter(cps[0])) return false;
        for (int i = 1; i < cps.Count; i++)
        {
            if (!IsMark(cps[i])) return false;
        }
        return true;
    }

    /// <summary>§2.5 B(ii′): a compact initials chain — two or more groups of one Uppercase letter (+ Marks) separated by single <c>.</c> units (<c>И.И</c>, <c>J.R.R</c>, <c>U.S.A</c>; the final <c>.</c> is the terminator run).</summary>
    private static bool IsInitialsChain(List<int> cps)
    {
        int groups = 0;
        int i = 0;
        while (i < cps.Count)
        {
            if (!IsUppercase(cps[i])) return false;
            i++;
            while (i < cps.Count && IsMark(cps[i])) i++;
            groups++;
            if (i == cps.Count) break;
            if (cps[i] != '.') return false;
            i++;
            if (i == cps.Count) return false;
        }
        return groups >= 2;
    }

    /// <summary>§2.5 B(ii): the token before <paramref name="tokStart"/> (over ≥ 1 WS19) is itself a single letter plus a terminator run — an initials chain (<c>J. R.</c>) or a spaced abbreviation (<c>z. B.</c>).</summary>
    private static bool PrevTokenIsInitial(ReadOnlySpan<char> pre, int tokStart)
    {
        int i = tokStart;
        while (i > 0 && IsWS19(pre[i - 1])) i--;
        if (i == tokStart) return false;
        int e = i;
        while (i > 0 && !IsWS19(pre[i - 1])) i--;
        var t = StripOpeners(pre.Slice(i, e - i));
        int k = t.Length;
        while (k > 0 && IsTerminator(t[k - 1])) k--;
        if (k == t.Length) return false;
        return IsSingleLetter(ScalarsOf(t.Slice(0, k)));
    }

    /// <summary>§2.6 abbreviation list, stored exactly as listed (final dot removed, inner dots kept); compared ordinally after <see cref="Fold"/>.</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        // English
        "a.d", "a.m", "al", "approx", "apr", "assn", "aug", "ave", "b.c", "blvd", "ca", "cf", "ch", "co",
        "corp", "dec", "dept", "dr", "e.g", "e.u", "ed", "eds", "eq", "eqs", "esp", "etc", "excl", "ext",
        "feb", "ff", "fig", "figs", "fri", "govt", "i.e", "ibid", "inc", "incl", "jan", "jr", "jul", "jun",
        "ltd", "misc", "mr", "mrs", "ms", "mt", "nov", "oct", "p.m", "ph.d", "pp", "prof", "rd", "resp",
        "sep", "sept", "sr", "st", "tel", "thu", "tue", "u.k", "u.s", "univ", "viz", "vol", "vs",
        // German
        "abs", "bspw", "bzgl", "bzw", "d.h", "evtl", "exkl", "geb", "ggf", "hrsg", "inkl", "jh", "mio",
        "mrd", "nr", "o.ä", "o.g", "s.o", "s.u", "sog", "std", "str", "tsd", "u.a", "u.u", "usw",
        "vgl", "z.b", "z.t", "zzgl",
        // French
        "art", "av", "chap", "éd", "env", "ex", "mlle", "mme", "p.ex", "réf", "ste", "tél",
        "trad",
        // Spanish
        "aprox", "avda", "cap", "dña", "dpto", "ej", "núm", "p.ej", "pág", "págs",
        "sra", "srta", "ud", "uds",
        // Russian
        "акад", "англ", "г", "гг", "гл",
        "гос", "греч", "др", "зам",
        "изд", "им", "исп", "ит", "кв",
        "коп", "корп", "лат", "млн",
        "млрд", "напр", "нем", "н.э",
        "обл", "пер", "перев", "пл",
        "пп", "пр", "прим", "просп",
        "проф", "ред", "руб", "рус",
        "св", "см", "сокр", "сост",
        "ст", "стр", "табл", "тел",
        "т.д", "т.е", "т.к", "т.н", "т.о", "т.п",
        "т.ч", "тыс", "укр", "ул", "фр",
        "чел", "чл", "шт", "экз",
        // Ukrainian
        "буд", "вул", "грн", "див",
        "ін", "рр", "стор", "тис",
        "т.зв",
    };

    /// <summary>§2.6 fold: ASCII A–Z, Latin-1 À–Þ (not ×), Cyrillic А–Я, Ё Є І Ї Ґ only; every other unit is left as is.</summary>
    private static string Fold(ReadOnlySpan<char> s)
    {
        var out_ = new StringBuilder(s.Length);
        foreach (char u in s)
        {
            int f = u;
            if (u >= 0x41 && u <= 0x5A) f = u + 0x20;
            else if (u >= 0xC0 && u <= 0xDE && u != 0xD7) f = u + 0x20;
            else if (u >= 0x410 && u <= 0x42F) f = u + 0x20;
            else if (u == 0x401) f = 0x451;
            else if (u == 0x404) f = 0x454;
            else if (u == 0x406) f = 0x456;
            else if (u == 0x407) f = 0x457;
            else if (u == 0x490) f = 0x491;
            out_.Append((char)f);
        }
        return out_.ToString();
    }

    /// <summary>§2.3 <c>prefix2</c> length on line <paramref name="s"/> (measured on <c>before</c>, the line up to the caret).</summary>
    private static int Prefix2Length(ReadOnlySpan<char> s)
    {
        var p = ParsePrefix(s);
        int i = p.PrefixEnd;
        bool heading = false;
        bool footnote = false;
        if (!p.HasMarker)
        {
            int @base = p.RowPrefixLen;
            // headingPrefix := indent0 quotes indent1 "#"{1,6} SP  (+ the WS19 run after it)
            int h = @base;
            int n = 0;
            while (h < s.Length && s[h] == '#') { n++; h++; }
            // the parser's parseHeading drops SP only: `\t# h` is a paragraph (§0.12)
            bool tabFree = s.Slice(0, p.Indent0).IndexOf('\t') < 0
                && s.Slice(p.Indent0QuotesLen, p.Indent1).IndexOf('\t') < 0;
            if (tabFree && n >= 1 && n <= 6 && U(s, h) == SP)
            {
                h++;
                while (h < s.Length && IsWS19(s[h])) h++;
                i = h;
                heading = true;
            }
            else
            {
                // footnotePrefix := indent0 quotes indent1 "[^" id "]:" WS19*
                int f = FootnotePrefixLength(s, @base);
                if (f >= 0) { i = @base + f; footnote = true; }
            }
        }
        if (!heading && !footnote)
        {
            // prefix WS19*: every §0.11 prefix — a marker, a box, a quote group or
            // nothing at all — absorbs the WS19 run after it, as headingPrefix does:
            // the parser drops SP only, but `- \tt`, `> t` and `​t` all
            // render `t` as the first visible unit of the line
            while (i < s.Length && IsWS19(s[i])) i++;
        }
        // lead := ( section | enum | citation | number | dash | symbols ) WS19+
        //   enum and citation only as the first lead (directly after prefix,
        //   headingPrefix or footnotePrefix);
        //   number only as the first lead after headingPrefix (A.2: `2.5 cups`)
        bool first = true;
        for (;;)
        {
            int j = i;
            int u = U(s, j);
            int w = -1;
            if (IsSectionUnit(u))
            {
                // section := run of ( "§" | "¶" ), WS19*, number
                int k = j;
                while (k < s.Length && IsSectionUnit(s[k])) k++;
                int m = k;
                while (m < s.Length && IsWS19(s[m])) m++;
                int n = NumberBodyEnd(s, m);
                if (n >= 0) w = WsAfter(s, n);
                if (w < 0) w = WsAfter(s, k); // a bare `§ ` is a dash-group lead
            }
            else if (first && EnumBodyEnd(s, j) >= 0 && WsAfter(s, EnumBodyEnd(s, j)) >= 0)
            {
                w = WsAfter(s, EnumBodyEnd(s, j));
            }
            else if (first && CitationBodyEnd(s, j) >= 0 && WsAfter(s, CitationBodyEnd(s, j)) >= 0)
            {
                w = WsAfter(s, CitationBodyEnd(s, j));
            }
            else if (first && heading && NumberBodyEnd(s, j) >= 0 && WsAfter(s, NumberBodyEnd(s, j)) >= 0)
            {
                w = WsAfter(s, NumberBodyEnd(s, j));
            }
            if (w >= 0) { i = w; first = false; continue; }
            if (IsLeadDashUnit(u))
            {
                while (j < s.Length && IsLeadDashUnit(s[j])) j++;
                if (j == i + 1 && u == '-') break; // a single hyphen-minus is never a lead
            }
            else
            {
                while (j < s.Length)
                {
                    int cp = ScalarAt(s, j, out int len);
                    // a keycap sequence (1️⃣ #️⃣ *️⃣): Digit, `#` or `*`, optional U+FE0F, U+20E3
                    if (IsDigit(cp) || cp == '#' || cp == '*')
                    {
                        int k = j + 1;
                        if (U(s, k) == 0xFE0F) k++;
                        if (U(s, k) == 0x20E3) { j = k + 1; continue; }
                        break;
                    }
                    bool sym = IsSymbolSoSk(cp) || cp == 0xFE0E || cp == 0xFE0F || cp == 0x200D || cp == 0x20E3
                        || (cp >= 0x1F3FB && cp <= 0x1F3FF);
                    if (!sym) break;
                    j += len;
                }
                if (j == i) break;
            }
            w = WsAfter(s, j);
            if (w < 0) break;
            i = w;
            first = false;
        }
        return i;

        // the index after the WS19 run starting at j, or -1 when there is none
        static int WsAfter(ReadOnlySpan<char> s, int j)
        {
            int w = j;
            while (w < s.Length && IsWS19(s[w])) w++;
            return w > j ? w : -1;
        }
    }

    /// <summary>§2.1 inline scan over <c>P</c> = lines <c>s+1 … c</c>. True when the caret is inside a code span, display math or inline math.</summary>
    private static bool InsideInline(Doc doc, Scan scan, int c, ReadOnlySpan<char> before, string typed)
    {
        // s = the last special line above c (excluded from P); a thematic break
        // and a page break end a paragraph the same way (§2.1)
        // a table row (§0.13) ends a paragraph too: the parser's row loop consumed
        // it, so an unclosed `$$` in a cell does not leak into the prose below
        int s = -1;
        for (int k = c - 1; k >= 0; k--)
        {
            if (scan.Special[k] || IsThematicBreak(doc.L(k)) || IsPageBreak(doc.L(k)) || TableContext(doc, scan, k, out _, out _)) { s = k; break; }
        }
        // a quote line or a marker line starts a new block: P begins there (included)
        int first = s + 1;
        for (int k = c; k > s; k--)
        {
            if (IsQuoteLine(doc.L(k)) || ParsePrefix(doc.L(k)).HasMarker) { first = k; break; }
        }
        bool display = false;
        for (int k = first; k <= c; k++)
        {
            bool isCaretLine = k == c;
            var line = isCaretLine ? before : doc.L(k);
            int code = 0;
            bool inline = false;
            int i = 0;
            // the unit at idx, `typed`'s first unit just past the caret, or -1 past the line end
            int typedUnit = isCaretLine ? typed[0] : -1;
            while (i < line.Length)
            {
                char u = line[i];
                if (code > 0)                                                    // 1.
                {
                    if (u == '`') { int n = RunOf(line, i, '`'); if (n == code) code = 0; i += n; continue; }
                    i++;
                    continue;
                }
                if (display)                                                     // 2.
                {
                    if ((u == '$' && NextAt(line, i + 1, typedUnit) == '$') || (u == '\\' && NextAt(line, i + 1, typedUnit) == ']')) { display = false; i += 2; continue; }
                    i++;
                    continue;
                }
                if (inline)                                                      // 3.
                {
                    if (u == '$') { inline = false; i++; continue; }
                    if (u == '\\' && NextAt(line, i + 1, typedUnit) == ')') { inline = false; i += 2; continue; }
                    if (u == '\\') { i += 2; continue; }
                    i++;
                    continue;
                }
                // 4. normal
                if (u == '`') { code = RunOf(line, i, '`'); i += code; continue; }
                if (u == '\\')
                {
                    int n = NextAt(line, i + 1, typedUnit);
                    if (n == '[') display = true;
                    else if (n == '(') inline = true;
                    i += 2;
                    continue;
                }
                if (u == '$')
                {
                    if (NextAt(line, i + 1, typedUnit) == '$') { display = true; i += 2; continue; }
                    int prev = ScalarBefore(line, i, out _);
                    bool prevOk = prev < 0 || !(IsLetter(prev) || IsDigit(prev) || prev == '_' || prev == '$');
                    int n = NextAt(line, i + 1, typedUnit);
                    bool nextOk = n >= 0 && !IsWS19(n) && !IsDigit(n);
                    if (prevOk && nextOk) inline = true;
                    i++;
                    continue;
                }
                i++;
            }
            // the caret line: state at the caret decides
            if (isCaretLine) return code > 0 || display || inline;
        }
        return false;

        static int NextAt(ReadOnlySpan<char> line, int idx, int typedUnit)
        {
            if (idx < line.Length) return line[idx];
            if (idx == line.Length) return typedUnit;
            return -1;
        }

        static int RunOf(ReadOnlySpan<char> line, int idx, char ch)
        {
            int j = idx;
            while (j < line.Length && line[j] == ch) j++;
            return j - idx;
        }
    }

    /// <summary>
    /// What the one lowercase letter being typed becomes (§2): its uppercase when it is the first
    /// letter of a line or of a sentence within the line, Markdown-aware, or <c>null</c> to insert
    /// <paramref name="typed"/> unchanged. <paramref name="typed"/> holds exactly one scalar (a
    /// surrogate pair for a non-BMP letter); anything else is <c>null</c>.
    /// </summary>
    public static string? Capitalize(string text, int selectionStart, int selectionEnd, string typed)
    {
        if (typed is null) return null;
        if (!Normalize(text, selectionStart, selectionEnd, out int start, out _, out string t)) return null;
        int caret = start;
        // §2.1a typed is one Lowercase letter with a defined upper()
        var tcp = ScalarsOf(typed);
        if (tcp.Count != 1) return null;
        if (!IsLowercase(tcp[0])) return null;
        int up = Upper(tcp[0]);
        if (up < 0) return null;
        string upperTyped = char.ConvertFromUtf32(up);

        var doc = new Doc(t);
        int c = LineOf(doc.Lines, caret);
        if (c < 0) return null;                                   // §0.2
        var scan = ScanState(doc, c, forEnter: false);
        if (scan.CaretCode) return null;                          // §2.1b
        var line = doc.L(c);
        if (TableContext(doc, scan, c, out _, out _) || IsPipeLine(line)) return null; // §2.1c
        int rel = caret - doc.Lines[c].Start;
        var before = line.Slice(0, rel);
        if (InsideInline(doc, scan, c, before, typed)) return null; // §2.1d

        // §2.1e link destination
        int lp = before.LastIndexOf("](");
        if (lp >= 0 && before.Slice(lp + 2).IndexOf(')') < 0) return null;

        // §2.1f current token. prefix2 is measured on `before` (§2.3): a caret
        // inside the SP run a marker absorbs is still at the content start.
        int p2 = Prefix2Length(before);
        int lastWS = -1;
        for (int i = before.Length - 1; i >= 0; i--)
        {
            if (IsWS19(before[i])) { lastWS = i; break; }
        }
        int tokenStart = Math.Max(lastWS + 1, Math.Min(p2, before.Length));
        var token = before.Slice(tokenStart);
        if (token.IndexOf("://") >= 0 || token.IndexOf("www.") >= 0 || token.IndexOf('@') >= 0 || token.IndexOf('/') >= 0) return null;

        // §2.4: a task box being typed by hand — `[` directly after the marker's SP
        // run and the typed letter is `x` — is not link text: `- [x] done` must not
        // become `- [X] done`
        {
            var p = ParsePrefix(line);
            if (p.HasMarker && !p.HasBox && before.Length == p.PrefixEnd + 1
                && before[p.PrefixEnd] == '[' && typed.Length == 1 && typed[0] == 'x') return null;
        }

        // §2.2 pre = before minus its trailing run of openers. A guillemet opener
        // (« ‹ » ›) that is preceded by WS19, by another opener or by nothing but
        // prefix2 carries the WS19 run after it (French spacing: `« Bonjour »`);
        // a closing `»` or `«` (`Done.» then`, `»Hallo.« dann`) keeps its closer role.
        int preEnd = before.Length;
        bool quoteStripped = false;
        for (;;)
        {
            if (preEnd == 0) break;
            char u = before[preEnd - 1];
            if (IsOpener(u)) { preEnd--; if (IsQuoteOpener(u)) quoteStripped = true; continue; }
            if (u == '!' && preEnd < before.Length && before[preEnd] == '[') { preEnd--; continue; }
            int w = preEnd;
            while (w > 0 && IsWS19(before[w - 1])) w--;
            if (w < preEnd && w > 0 && IsGuillemet(before[w - 1]))
            {
                int g = w - 1;
                if (g == p2 || (g > 0 && (IsWS19(before[g - 1]) || IsOpener(before[g - 1])))) { preEnd = w; continue; }
            }
            break;
        }
        var pre = before.Slice(0, preEnd);

        // §2.4 rule A — line start: pre is exactly prefix2, or pre is empty (the
        // caret is at column 0, ahead of whatever prefix the line has: `|- item`,
        // or a selection that starts at the line start is being replaced)
        if (preEnd == p2 || pre.Length == 0) return upperTyped;

        // §2.5 rule B — sentence start within the line
        {
            int i = pre.Length;
            int wsEnd = i;
            while (i > 0 && IsWS19(pre[i - 1])) i--;
            if (i == wsEnd) return null;                                        // 1. ≥ 1 WS19
            // 1a. direct speech after a colon: `:` before the WS19 run and a quotation
            //     opener among the stripped openers (`Он сказал: «`, `He said: "`)
            if (quoteStripped && pre[i - 1] == ':') return upperTyped;
            bool viaDash = false;                                               // 1b. optional group: WS19+ (dash-run | symbols-run) WS19+
            {
                int j = i;
                while (j > 0 && IsMidLineDashUnit(pre[j - 1])) j--;
                if (j == i) j = SymbolsRunStart(pre, i);                        //    an emoji / keycap between two sentences (`Done. 🎉 next`)
                else viaDash = true;
                if (j < i)
                {
                    int w = j;
                    while (w > 0 && IsWS19(pre[w - 1])) w--;
                    if (w < j) i = w; else viaDash = false;
                }
            }
            int closers = 0;
            for (;;)                                                            // 2. closers, and a footnote reference / citation
            {
                if (i > 0 && pre[i - 1] == ']')                                 //    `text.[^1] Next`, `text.[12] Next` — skipped like a closer
                {
                    int r = ReferenceStart(pre, i);
                    if (r >= 0) { i = r; closers++; continue; }
                }
                if (i > 0 && IsCloser(pre[i - 1])) { i--; closers++; continue; }
                break;
            }
            if (closers > 0) while (i > 0 && IsWS19(pre[i - 1])) i--;          //    French `? »`: WS19 between the terminator and a closer
            int termEnd = i;
            int dots = 0;
            bool bangQ = false;
            while (i > 0 && IsTerminator(pre[i - 1]))
            {
                if (pre[i - 1] == '.') dots++; else bangQ = true;
                i--;
            }
            if (i == termEnd) return null;                                      // 3. run length ≥ 1
            if (dots >= 2) return null;                                         //    ellipsis
            if (viaDash && pre[termEnd - 1] != '.') return null;                //    `? —` / `! —` are dialogue tags
            int tokEnd = i;
            while (i > 0 && !IsWS19(pre[i - 1])) i--;                           // 4. token
            int tokStart = i;
            var tok = StripOpeners(pre.Slice(tokStart, tokEnd - tokStart));
            if (tok.Length == 0 && dots == 0 && tokEnd >= 1 && IsWS19(pre[tokEnd - 1])
                && !(tokEnd >= 2 && IsWS19(pre[tokEnd - 2])))
            {
                // French spacing (`Bonjour !`, `Ça va ?`): exactly one WS19 unit before the
                // run, then the token — never reaching into prefix2
                int j = tokEnd - 1;
                while (j > 0 && !IsWS19(pre[j - 1])) j--;
                if (j >= p2 && j < tokEnd - 1)
                {
                    tokStart = j;
                    tok = StripOpeners(pre.Slice(j, tokEnd - 1 - j));
                }
            }
            if (tok.Length == 0) return null;                                   // (i)
            if (bangQ) return upperTyped;                                       //    a run with `?` or `!` ends the sentence whatever the token
            var tcps = ScalarsOf(tok);
            if (IsSingleLetter(tcps) && tcps[0] != 0x44F)                       // (ii) one Letter (+ Marks)
            {
                if (IsUppercase(tcps[0])) { if (PrevTokenIsInitial(pre, tokStart)) return null; } // `J. R.`, `z. B.`
                else return null;                                               // `p. 42`, `т. е.`, `J.` after a name is Lu
            }
            if (IsInitialsChain(tcps)) return null;                             // (ii′) compact initials `И.И.`, `J.R.R.`, `U.S.A.`
            if (Abbreviations.Contains(Fold(tok))) return null;                 // (iii)
            return upperTyped;
        }
    }
}
