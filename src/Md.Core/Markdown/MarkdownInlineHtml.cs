using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Text;

namespace Md.Core.Markdown;

/// <summary>
/// The span pass — the port of the private <c>inline</c> / <c>protect</c> / <c>token</c> /
/// <c>mathSpan</c> / <c>escape</c> / <c>replace</c> family in <c>MarkdownHTML.swift</c>. This
/// function, not a browser delimiter scan, decides what is a formula; that is why
/// <c>rich/auto-render.min.js</c> is bundled by the apps and referenced by nothing.
/// </summary>
/// <remarks>
/// <para>THE PHASE ORDER IS THE SPECIFICATION. Any reordering silently changes real documents:</para>
/// <list type="number">
/// <item>protect: code spans → <c>$$…$$</c> → <c>\[…\]</c> → <c>$…$</c> (currency guard) → <c>\(…\)</c></item>
/// <item>escape the whole remaining literal text (<c>&amp; &lt; &gt; "</c>)</item>
/// <item>spans: img(title) → img → link(title) → link → footnote ref → <c>**</c> → <c>__</c> → <c>~~</c> → <c>*</c> → <c>_</c></item>
/// <item>soft breaks → <c>&lt;br&gt;\n</c> (paragraphs only)</item>
/// <item>restore the protected spans, ascending, by literal replace</item>
/// </list>
/// <para>
/// Everything is escaped before phase 3, so every later pattern speaks in <i>escaped</i> terms — a
/// link title reads <c>&amp;quot;…&amp;quot;</c>.
/// </para>
/// <para>
/// The regex dialect is ICU's (NSRegularExpression), and three of its classes are not .NET's, so
/// none is left to the engine: <c>\w</c> is <c>[\p{Alphabetic}\p{M}\p{Nd}\p{Pc} U+200C U+200D]</c>
/// (.NET's <c>\w</c> lacks Nl, Mc, Me and the joiners; no <c>\p{Alphabetic}</c> exists here), so the
/// two guarded patterns are hand-rolled scanners that ask <see cref="IsWord"/> of the whole code
/// point on each side — a regex lookbehind sees a surrogate half where ICU sees an astral letter;
/// <c>\s</c> is <c>[\t\n\v\f\r\x85\p{Z}]</c> (measured; NEL and VT in, U+200B and U+FEFF out);
/// <c>.</c> excludes the seven line terminators, not just <c>\n</c>. Kotlin spells the word guard
/// <c>[\p{L}\p{N}_$]</c> and both LaTeX writers <c>[\p{L}\p{N}_]</c>; they disagree with the Swift
/// on <c>²$x$</c> and <c>é$x$</c> (NFD). This file follows the Swift HTML writer, whose bytes the
/// golden corpus was captured from — port spec OQ-3, recorded, not resolved.
/// </para>
/// </remarks>
internal static class MarkdownInlineHtml
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    // Phase 1. `.dotMatchesLineSeparators` is what the Swift passes to every protect pattern —
    // inert here (none uses `.`), but it is the option the source asks for.
    private static readonly Regex CodeSpan = new("`([^`]+)`", Options | RegexOptions.Singleline);
    private static readonly Regex DisplayDollars = new(@"\$\$([\s\S]+?)\$\$", Options | RegexOptions.Singleline);
    private static readonly Regex DisplayBrackets = new(@"\\\[([\s\S]+?)\\\]", Options | RegexOptions.Singleline);
    private static readonly Regex InlineParens = new(@"\\\(([^\n]+?)\\\)", Options | RegexOptions.Singleline);

    /// <summary>ICU's <c>\s</c>, spelled out: <c>White_Space</c> = the five ASCII controls, NEL and every Z*.</summary>
    private const string Space = @"\t\n\v\f\r\x85\p{Z}";

    /// <summary>ICU's <c>.</c> without DOTALL: anything but the seven line terminators (U+2028 is the only Zl, U+2029 the only Zp).</summary>
    private const string Dot = @"[^\n\r\v\f\x85\p{Zl}\p{Zp}]";

    // Phase 3, in order. Titled forms first or the untitled pattern stops at the first `)` and the
    // title leaks; images before links because image syntax is link syntax with a leading `!`.
    private static readonly Regex ImageTitled =
        new(@"!\[([^\]]*)\]\(([^)" + Space + "]+)[" + Space + "]+&quot;(" + Dot + "*?)&quot;\\)", Options);
    private static readonly Regex Image = new(@"!\[([^\]]*)\]\(([^)" + Space + @"]+)\)", Options);
    private static readonly Regex LinkTitled =
        new(@"\[([^\]]+)\]\(([^)" + Space + "]+)[" + Space + "]+&quot;(" + Dot + "*?)&quot;\\)", Options);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)" + Space + @"]+)\)", Options);
    // ASCII ids because the parser accepts exactly that set for a definition: nothing to escape
    // into an `id` attribute, and no definition can exist that no reference can name.
    private static readonly Regex FootnoteRef = new(@"\[\^([A-Za-z0-9_-]+)\]", Options);
    private static readonly Regex StrongStars = new(@"\*\*([^*]+)\*\*", Options);
    private static readonly Regex StrongUnderscores = new("__([^_]+)__", Options);
    private static readonly Regex Deleted = new("~~([^~]+)~~", Options);
    private static readonly Regex EmphasisStars = new(@"\*([^*]+)\*", Options);

    private const char TokenOpen = (char)0xE000;
    private const char TokenClose = (char)0xE001;

    public static string Inline(string text, bool softBreaks)
    {
        var protectedSpans = new List<string>();
        var working = text;

        // 1. Protect. Code wins over math, so `$x$` inside backticks stays literal code; the
        //    inline `$…$` form carries the currency guard so "$5 and $10" is left as prose. A
        //    footnote reference needs no protection: `[^id]` has no `](`, so no link or image
        //    pattern can match it, and one inside backticks is already a code token by now.
        working = Protect(CodeSpan.Matches(working), working, protectedSpans, static c => "<code>" + Escape(c) + "</code>");
        working = Protect(DisplayDollars.Matches(working), working, protectedSpans, static c => MathSpan(c, true));
        working = Protect(DisplayBrackets.Matches(working), working, protectedSpans, static c => MathSpan(c, true));
        working = Protect(InlineDollarMatches(working), working, protectedSpans, static c => MathSpan(c, false));
        working = Protect(InlineParens.Matches(working), working, protectedSpans, static c => MathSpan(c, false));

        // 2. Escape the literal text; the private-use tokens pass through untouched.
        working = Escape(working);

        // 3. Span syntax → tags, every one a replace-all. Nothing in these templates is a .NET
        //    substitution other than the group numbers.
        working = ImageTitled.Replace(working, "<img src=\"$2\" alt=\"$1\" title=\"$3\">");
        working = Image.Replace(working, "<img src=\"$2\" alt=\"$1\">");
        working = LinkTitled.Replace(working, "<a href=\"$2\" title=\"$3\">$1</a>");
        working = Link.Replace(working, "<a href=\"$2\">$1</a>");
        //    Footnote references after images and links, and must: their markup is full of quotes
        //    and angle brackets, so converting first would carry it into an `alt` or nest `<a>` in
        //    `<a>`. Running last, a reference inside a link label merely stops the label being a
        //    link. The number is not known yet — it depends on first-reference order across the
        //    whole document — so this leaves a placeholder for `WithFootnotes`.
        working = FootnoteRef.Replace(working, "<sup class=\"md-fnref\" data-fn=\"$1\"></sup>");
        working = StrongStars.Replace(working, "<strong>$1</strong>");
        working = StrongUnderscores.Replace(working, "<strong>$1</strong>");
        working = Deleted.Replace(working, "<del>$1</del>");
        working = EmphasisStars.Replace(working, "<em>$1</em>");
        working = EmphasisUnderscores(working);

        // 4. Soft breaks (paragraphs only), before restore so a multi-line display formula keeps
        //    its own newlines rather than getting `<br>`s injected.
        if (softBreaks) working = working.Replace("\n", "<br>\n", StringComparison.Ordinal);

        // 5. Restore, ascending, by LITERAL replace — never a regex replace, or a protected `$$`
        //    would be read as a template.
        //
        //    KNOWN LATENT BUG, REPRODUCED DELIBERATELY: a later protect pass can swallow an earlier
        //    pass's token (a code span inside a math span), and because restore runs ascending the
        //    enclosed token is restored before its enclosing HTML is back in `working`, so the raw
        //    U+E000 / U+E001 survive: `$$ `x` $$` → `<span class="md-mathd"> U+E000 0 U+E001 </span>`.
        //    Every shipping app does this; byte parity is the contract (OQ-6).
        for (var index = 0; index < protectedSpans.Count; index++)
        {
            working = working.Replace(Token(index), protectedSpans[index], StringComparison.Ordinal);
        }
        return working;
    }

    /// <summary>
    /// The four-character HTML escape, <c>&amp;</c> first. <c>'</c> is deliberately not escaped;
    /// <c>"</c> is, so a link URL landing in a double-quoted <c>href</c> cannot break out — that one
    /// line is the whole XSS story here. No raw-HTML passthrough, no autolinks, anywhere.
    /// </summary>
    public static string Escape(string s) =>
        s.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);

    /// <summary>A KaTeX target. The LaTeX is escaped for HTML; KaTeX reads the decoded <c>textContent</c>.</summary>
    private static string MathSpan(string latex, bool display) =>
        "<span class=\"md-math" + (display ? "d" : "i") + "\">" + Escape(latex) + "</span>";

    /// <summary>U+E000, the decimal index, U+E001. Private-use, so `Escape` never touches it and no span pattern matches it.</summary>
    private static string Token(int index) =>
        string.Concat(TokenOpen.ToString(), index.ToString(CultureInfo.InvariantCulture), TokenClose.ToString());

    private readonly record struct Span(int Start, int Length, string Content);

    private static IReadOnlyList<Span> Spans(MatchCollection matches)
    {
        var spans = new List<Span>(matches.Count);
        foreach (Match match in matches)
        {
            // Swift guards with `numberOfRanges >= 2`; every pattern here has its group 1.
            if (match.Groups.Count < 2) continue;
            spans.Add(new Span(match.Index, match.Length, match.Groups[1].Value));
        }
        return spans;
    }

    private static string Protect(MatchCollection matches, string text, List<string> store, Func<string, string> transform) =>
        Protect(Spans(matches), text, store, transform);

    /// <summary>
    /// Replace every match with a unique token, appending <c>transform(group1)</c> to the store.
    /// Rewritten BACK-TO-FRONT so earlier ranges stay valid, while the index counts forward — so
    /// the <i>last</i> match in the text takes the <i>lowest</i> index of the pass. Kotlin walks
    /// forward; the difference is visible only through the token leak, where the leaked digits
    /// differ. Swift and TypeScript are followed.
    /// </summary>
    private static string Protect(IReadOnlyList<Span> spans, string text, List<string> store, Func<string, string> transform)
    {
        var result = text;
        for (var i = spans.Count - 1; i >= 0; i--)
        {
            var span = spans[i];
            var index = store.Count;
            store.Add(transform(span.Content));
            result = string.Concat(result.AsSpan(0, span.Start), Token(index), result.AsSpan(span.Start + span.Length));
        }
        return result;
    }

    // MARK: - The two `\w`-guarded patterns, hand-rolled

    /// <summary>
    /// <c>(?&lt;![\w$])\$([^$\n]+?)\$(?![\w$])</c> — the currency guard, the most load-bearing
    /// pattern in the file. A delimiting <c>$</c> may be neither preceded nor followed by a word
    /// character or another <c>$</c>; no newline inside. Because the content class excludes
    /// <c>$</c>, the only possible close is the first <c>$</c> after the opener, so the scan is
    /// exactly the regex: a failed guard moves on one unit, a match resumes after its close.
    /// </summary>
    private static IReadOnlyList<Span> InlineDollarMatches(string text)
    {
        var spans = new List<Span>();
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '$' || (i > 0 && (text[i - 1] == '$' || IsWordBefore(text, i))))
            {
                i++;
                continue;
            }
            var close = i + 1;
            while (close < text.Length && text[close] != '$' && text[close] != '\n') close++;
            if (close >= text.Length || text[close] != '$' || close == i + 1
                || (close + 1 < text.Length && (text[close + 1] == '$' || IsWordAfter(text, close + 1))))
            {
                i++;
                continue;
            }
            spans.Add(new Span(i, close - i + 1, text.Substring(i + 1, close - i - 1)));
            i = close + 1;
        }
        return spans;
    }

    /// <summary>
    /// <c>(?&lt;![\w])_([^_]+)_(?![\w])</c> → <c>&lt;em&gt;$1&lt;/em&gt;</c>, replace-all: the
    /// word-boundary guard that lets <c>snake_case</c> survive. <c>__bold__</c> was consumed by the
    /// pass before. The content class excludes <c>_</c>, so the close is the first <c>_</c> after
    /// the opener and backtracking cannot rescue a failed guard.
    /// </summary>
    private static string EmphasisUnderscores(string text)
    {
        StringBuilder? builder = null;
        var last = 0;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] != '_' || (i > 0 && IsWordBefore(text, i)))
            {
                i++;
                continue;
            }
            var close = i + 1;
            while (close < text.Length && text[close] != '_') close++;
            if (close >= text.Length || close == i + 1 || (close + 1 < text.Length && IsWordAfter(text, close + 1)))
            {
                i++;
                continue;
            }
            builder ??= new StringBuilder(text.Length + 16);
            builder.Append(text, last, i - last).Append("<em>").Append(text, i + 1, close - i - 1).Append("</em>");
            last = close + 1;
            i = close + 1;
        }
        if (builder is null) return text;
        builder.Append(text, last, text.Length - last);
        return builder.ToString();
    }

    /// <summary>The code point ending just before <paramref name="index"/> is a word character.</summary>
    private static bool IsWordBefore(string text, int index)
    {
        var start = index - 1;
        if (char.IsLowSurrogate(text[start]) && start > 0 && char.IsHighSurrogate(text[start - 1])) start--;
        return Rune.TryGetRuneAt(text, start, out var rune) && IsWord(rune);
    }

    /// <summary>The code point starting at <paramref name="index"/> is a word character.</summary>
    private static bool IsWordAfter(string text, int index) =>
        Rune.TryGetRuneAt(text, index, out var rune) && IsWord(rune);

    /// <summary>
    /// ICU's <c>\w</c>: <c>Alphabetic</c> (L*, Nl, the marks and the enclosed Latin letters that
    /// carry <c>Other_Alphabetic</c>), M*, Nd, Pc, ZWNJ and ZWJ. <c>ф</c>, <c>٣</c>, a combining
    /// acute, <c>_</c> and <c>Ⅹ</c> are word characters; <c>½</c> (No), <c>²</c> (No) and <c>-</c>
    /// are not. Asked of a <see cref="Rune"/>, so <c>𝐀</c> counts as ICU and TypeScript count it.
    /// </summary>
    private static bool IsWord(Rune rune)
    {
        if (Rune.IsLetter(rune) || ScalarText.IsMark(rune) || ScalarText.IsEnclosedAlphabetic(rune)) return true;
        if (rune.Value is 0x200C or 0x200D) return true;
        return Rune.GetUnicodeCategory(rune) is UnicodeCategory.LetterNumber
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation;
    }
}
