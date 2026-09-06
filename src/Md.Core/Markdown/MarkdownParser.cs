using System.Globalization;
using System.Text;
using Md.Core.Text;
using static Md.Core.Text.Whitespace;

namespace Md.Core.Markdown;

/// <summary>
/// The block-level Markdown parser: a pragmatic subset of CommonMark plus the GitHub extensions
/// (fences, task lists, tables, footnotes) and md's own additions (front matter, page breaks,
/// author notes). Line-oriented, single pass, cheap enough to re-run on every keystroke.
/// </summary>
/// <remarks>
/// <para>
/// This is upstream of everything — preview, HTML, PDF, EPUB, LaTeX, outline, notes — so a
/// one-line divergence here is "the same document is a different document on Windows". The branch
/// order in <see cref="Scan"/> is normative, the three continuation break-sets are deliberately
/// different, and every documented wart is kept (see the tests): an unclosed fence at EOF gains a
/// trailing newline, <c>&lt;!-- c --&gt; text</c> drops the text, <c>outline()</c> lists a phantom
/// setext heading over a table header, tables cannot interrupt paragraphs but can interrupt list
/// items, heading indentation is unbounded while a fence's is capped at three, and quote nesting
/// stops at depth 32 as part of the output shape.
/// </para>
/// <para>
/// Character model: the scanner walks UTF-16 code units and compares single ASCII chars, exactly
/// as the Kotlin and TypeScript ports do — every delimiter is one ASCII scalar and no surrogate
/// half equals one. Only <see cref="Slug"/> and the DOT name scan walk code points. Every string
/// comparison is ordinal; trims use the sets in <see cref="Whitespace"/>; digits are ASCII
/// <c>'0'..'9'</c>; nothing normalises.
/// </para>
/// </remarks>
public static class MarkdownParser
{
    /// <summary>Block quotes recurse once per marker; past this depth the remainder stays a paragraph. The cap shapes the output (33 nested quotes for a run of 5000 markers) and must not move.</summary>
    private const int QuoteDepthLimit = 32;

    // MARK: - Entry points

    /// <summary>Parse Markdown source into a flat list of blocks. <paramref name="quoteDepth"/> is internal to the recursion.</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string source, int quoteDepth = 0)
    {
        var placed = Scan(source, quoteDepth);
        var blocks = new List<MarkdownBlock>(placed.Count);
        foreach (var item in placed) blocks.Add(item.Block);
        return blocks;
    }

    /// <summary>
    /// The same walk as <see cref="Parse"/>, recording the 0-based line each top-level block started
    /// on. <c>Parse(s)</c> equals <c>ParseWithLines(s).Select(p =&gt; p.Block)</c> by construction:
    /// both are projections of one scanner.
    /// </summary>
    public static IReadOnlyList<PlacedBlock> ParseWithLines(string source) => Scan(source, 0);

    /// <summary>The document's front matter, or an empty list. Runs a full parse so the accessor cannot disagree with the document.</summary>
    public static IReadOnlyList<MetadataField> FrontMatter(string source)
    {
        foreach (var block in Parse(source))
        {
            if (block is MarkdownBlock.FrontMatter matter) return matter.Fields;
        }
        return [];
    }

    // MARK: - The scanner

    private static List<PlacedBlock> Scan(string source, int quoteDepth)
    {
        var lines = NormalizedLines(source);
        var blocks = new List<PlacedBlock>();
        var i = 0;

        // Front matter: only at the very start of the document, never inside a quote. Without it
        // the opening `---` reads as a rule and the metadata as prose.
        if (quoteDepth == 0 && ParseFrontMatter(lines) is { } matter)
        {
            blocks.Add(new PlacedBlock(new MarkdownBlock.FrontMatter(matter.Fields), 0));
            i = matter.Next;
        }

        while (i < lines.Count)
        {
            var line = lines[i];
            // Captured before any branch moves the cursor: the paragraph branch reads it after
            // its own loop, and a setext heading takes the line of its text, not its underline.
            var start = i;

            // 1. Blank line — separator, nothing to emit. WS set: a lone U+200B is blank.
            if (line.Length == 0)
            {
                i++;
                continue;
            }

            // 2. Fenced code. Wins over everything: inside a fence `---`, `# x`, `> x`, `- x` are code.
            var fence = FenceMarker.From(line);
            if (fence is not null)
            {
                var code = new List<string>();
                i++;
                while (i < lines.Count)
                {
                    if (fence.Closes(lines[i]))
                    {
                        i++;
                        break;
                    }
                    code.Add(fence.StripIndent(lines[i]));
                    i++;
                }
                // Unclosed → consumes to EOF, and because NormalizedLines always appends a final
                // element, a source ending in a newline gives the code a trailing "\n". Wart, kept.
                blocks.Add(new PlacedBlock(new MarkdownBlock.CodeBlock(fence.Language, string.Join("\n", code)), start));
                continue;
            }

            // 3. Thematic break — before the list branch, so `- - -` and `* * *` are rules.
            if (IsThematicBreak(line))
            {
                blocks.Add(new PlacedBlock(new MarkdownBlock.ThematicBreak(), start));
                i++;
                continue;
            }

            // 4. Page break: `\newpage` / `\pagebreak` alone on a line.
            if (IsPageBreak(line))
            {
                blocks.Add(new PlacedBlock(new MarkdownBlock.PageBreak(), start));
                i++;
                continue;
            }

            // 5. Footnote definition, with soft-wrapped continuation absorbed. Wins over heading,
            // table, quote and list. This loop carries the footnote check and NO table lookahead.
            if (ParseFootnoteDefinition(line) is { } definition)
            {
                var text = new StringBuilder(definition.Text);
                i++;
                while (i < lines.Count)
                {
                    var l = lines[i];
                    if (TrimWS(l).Length == 0) break;
                    if (ParseFootnoteDefinition(l) is not null || FenceMarker.From(l) is not null
                        || IsThematicBreak(l) || ParseHeading(l) is not null || IsQuote(l)
                        || IsPageBreak(l) || IsCommentStart(l) || ListMarker(l) is not null)
                    {
                        break;
                    }
                    text.Append(' ').Append(TrimWS(l));
                    i++;
                }
                blocks.Add(new PlacedBlock(new MarkdownBlock.FootnoteDefinition(definition.Id, text.ToString()), start));
                continue;
            }

            // 6. HTML comment, possibly spanning lines. `<!-- note: … -->` becomes a note; any other
            // comment is dropped. The WHOLE closing line is consumed (text after `-->` is lost) and
            // an unclosed comment swallows to EOF — both kept.
            if (IsCommentStart(line))
            {
                var raw = new List<string>();
                while (i < lines.Count)
                {
                    raw.Add(lines[i]);
                    var closed = ScalarText.Contains(lines[i], "-->");
                    i++;
                    if (closed) break;
                }
                var note = NoteText(string.Join("\n", raw));
                if (note is not null) blocks.Add(new PlacedBlock(new MarkdownBlock.Note(note), start));
                continue;
            }

            // 7. ATX heading — before the table branch, so `# a | b` over `---|---` is a heading.
            if (ParseHeading(line) is { } heading)
            {
                blocks.Add(new PlacedBlock(new MarkdownBlock.Heading(heading.Level, heading.Text), start));
                i++;
                continue;
            }

            // 8. GFM table — the only branch with lookahead. Wins over quote and list.
            if (i + 1 < lines.Count && ParseTable(line, lines[i + 1]) is { } table)
            {
                var rows = new List<IReadOnlyList<string>>();
                i += 2;
                while (i < lines.Count && lines[i].Contains('|') && TrimWS(lines[i]).Length != 0)
                {
                    rows.Add(SplitTableRow(lines[i], table.Header.Count));
                    i++;
                }
                blocks.Add(new PlacedBlock(new MarkdownBlock.Table(table.Header, table.Alignments, rows), start));
                continue;
            }

            // 9. Block quote: the run of `>` lines, one marker stripped, parsed recursively. No lazy
            // continuation. The depth cap keeps a line of thousands of `>` off the stack — and is
            // part of the output shape.
            if (IsQuote(line))
            {
                var inner = new List<string>();
                while (i < lines.Count && IsQuote(lines[i]))
                {
                    inner.Add(StripQuoteMarker(lines[i]));
                    i++;
                }
                var innerText = string.Join("\n", inner);
                IReadOnlyList<MarkdownBlock> innerBlocks = quoteDepth < QuoteDepthLimit
                    ? Parse(innerText, quoteDepth + 1)
                    : [new MarkdownBlock.Paragraph(innerText)];
                blocks.Add(new PlacedBlock(new MarkdownBlock.Quote(innerBlocks), start));
                continue;
            }

            // 10. List: consecutive items, each absorbing its continuation lines. This loop carries
            // the table lookahead and NO footnote check ("- item\n[^a]: note" is one item).
            if (ListMarker(line) is not null)
            {
                var items = new List<ListItem>();
                var ordered = false;
                while (i < lines.Count && ListMarker(lines[i]) is { } marker)
                {
                    ordered = ordered || marker.Ordinal is not null;
                    var text = new StringBuilder(marker.Text);
                    i++;
                    while (i < lines.Count)
                    {
                        var l = lines[i];
                        if (TrimWS(l).Length == 0) break;
                        if (ListMarker(l) is not null || FenceMarker.From(l) is not null
                            || IsThematicBreak(l) || ParseHeading(l) is not null || IsQuote(l)
                            || IsPageBreak(l) || IsCommentStart(l))
                        {
                            break;
                        }
                        if (i + 1 < lines.Count && ParseTable(l, lines[i + 1]) is not null) break;
                        text.Append(' ').Append(TrimWS(l));
                        i++;
                    }
                    items.Add(new ListItem(text.ToString(), marker.Level, marker.Ordinal, marker.Task));
                }
                blocks.Add(new PlacedBlock(new MarkdownBlock.List(ordered, items), start));
                continue;
            }

            // 11. Paragraph: raw lines until a blank or a block start. Setext is recognised only
            // here, only with exactly one buffered line, and before the break set — so `Title\n---`
            // is one heading and never heading + rule. No table lookahead: a table cannot interrupt
            // a paragraph. The first line is always appended, since every predicate in the break set
            // was already refused above.
            var paragraph = new List<string>();
            var emittedHeading = false;
            while (i < lines.Count)
            {
                var l = lines[i];
                if (TrimWS(l).Length == 0) break;
                if (paragraph.Count == 1 && SetextUnderline(l) is { } level)
                {
                    blocks.Add(new PlacedBlock(new MarkdownBlock.Heading(level, TrimWS(paragraph[0])), start));
                    i++;
                    emittedHeading = true;
                    break;
                }
                if (FenceMarker.From(l) is not null || IsThematicBreak(l) || ParseHeading(l) is not null
                    || IsQuote(l) || ListMarker(l) is not null || IsPageBreak(l) || IsCommentStart(l))
                {
                    break;
                }
                paragraph.Add(l);
                i++;
            }
            if (!emittedHeading && paragraph.Count > 0)
            {
                blocks.Add(new PlacedBlock(new MarkdownBlock.Paragraph(string.Join("\n", paragraph)), start));
            }
        }

        return blocks;
    }

    // MARK: - Front matter

    /// <summary>
    /// A front-matter block if the document opens with one: <c>---</c> (YAML, closed by <c>---</c> or
    /// <c>...</c>) or <c>+++</c> (TOML, closed by <c>+++</c>). Three guards, each load-bearing because
    /// the YAML opener is spelled like a thematic break and getting this wrong hides the reader's
    /// prose: the block must be closed, the line after the opener must not be blank, and at least
    /// one <c>key: value</c> field must be read. Nothing is committed until all three pass.
    /// </summary>
    private static (IReadOnlyList<MetadataField> Fields, int Next)? ParseFrontMatter(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return null;
        string[] closers;
        char separator;
        switch (TrimWS(lines[0]))
        {
            case "---": closers = ["---", "..."]; separator = ':'; break;
            case "+++": closers = ["+++"]; separator = '='; break;
            default: return null;
        }

        if (lines.Count <= 1 || TrimWS(lines[1]).Length == 0) return null;

        var close = -1;
        for (var index = 1; index < lines.Count; index++)
        {
            if (Array.IndexOf(closers, TrimWS(lines[index])) >= 0)
            {
                close = index;
                break;
            }
        }
        if (close < 0) return null;

        var fields = new List<MetadataField>();
        for (var index = 1; index < close; index++)
        {
            var trimmed = TrimWS(lines[index]);
            // Comments and list items are skipped; the block is still consumed. The scan is flat,
            // so an indented child that carries its own separator is read as a field of its own.
            if (trimmed.Length == 0 || trimmed[0] == '#' || trimmed[0] == '-') continue;
            var split = trimmed.IndexOf(separator);
            if (split < 0) continue;
            var key = TrimWS(trimmed.Substring(0, split));
            if (key.Length == 0) continue;
            var value = TrimWS(trimmed.Substring(split + 1));
            // One layer of matching quotes, `"` tried before `'`. Counting units is the Swift's
            // scalar count here: an astral char is two units but is neither quote.
            foreach (var quote in "\"'")
            {
                if (value.Length >= 2 && value[0] == quote && value[value.Length - 1] == quote)
                {
                    value = value.Substring(1, value.Length - 2);
                    break;
                }
            }
            fields.Add(new MetadataField(key, value));
        }
        if (fields.Count == 0) return null;
        return (fields, close + 1);
    }

    // MARK: - Footnote definitions

    /// <summary>
    /// <c>[^id]: the note</c> → (id, text), or null. Identifiers are ASCII <c>[A-Za-z0-9_-]</c> —
    /// narrower than Pandoc on purpose: it is exactly the set the HTML writer's reference pattern
    /// accepts, so no definition can exist that no reference can name. The <c>:</c> must follow
    /// <c>]</c> immediately; the text is WS-trimmed.
    /// </summary>
    public static (string Id, string Text)? ParseFootnoteDefinition(string line)
    {
        var trimmed = TrimWS(line);
        if (!trimmed.StartsWith("[^", StringComparison.Ordinal)) return null;
        var close = trimmed.IndexOf(']', 2);
        if (close < 0) return null;
        var id = trimmed.Substring(2, close - 2);
        if (id.Length == 0) return null;
        foreach (var c in id)
        {
            if (!IsFootnoteIdentifier(c)) return null;
        }
        if (close + 1 >= trimmed.Length || trimmed[close + 1] != ':') return null;
        return (id, TrimWS(trimmed.Substring(close + 2)));
    }

    private static bool IsFootnoteIdentifier(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-' || c == '_';

    // MARK: - Headings

    /// <summary>ATX heading: any number of leading spaces (unbounded, unlike a fence), 1–6 <c>#</c>, then U+0020 or end of line. A tab after the run does not count.</summary>
    private static (int Level, string Text)? ParseHeading(string line)
    {
        var p = 0;
        while (p < line.Length && line[p] == ' ') p++;
        if (p >= line.Length || line[p] != '#') return null;
        var level = 0;
        while (p < line.Length && line[p] == '#' && level < 7)
        {
            level++;
            p++;
        }
        if (level < 1 || level > 6) return null;
        if (p < line.Length && line[p] != ' ') return null;
        return (level, StripClosingHashes(TrimWS(line.Substring(p))));
    }

    /// <summary>Remove a closing <c>#</c> run only when preceded by SPTAB, per CommonMark — so <c>C#</c> and <c>F#</c> keep theirs; a text of only <c>#</c>s becomes empty.</summary>
    private static string StripClosingHashes(string text)
    {
        var end = text.Length;
        while (end > 0 && text[end - 1] == '#') end--;
        if (end == text.Length) return text;
        if (end == 0) return "";
        var before = text[end - 1];
        if (before != ' ' && before != '\t') return text;
        return TrimWS(text.Substring(0, end));
    }

    /// <summary>A non-empty WS-trimmed line of only <c>=</c> (1) or only <c>-</c> (2).</summary>
    private static int? SetextUnderline(string line)
    {
        var t = TrimWS(line);
        if (t.Length == 0) return null;
        var allEquals = true;
        var allDashes = true;
        foreach (var c in t)
        {
            if (c != '=') allEquals = false;
            if (c != '-') allDashes = false;
        }
        if (allEquals) return 1;
        if (allDashes) return 2;
        return null;
    }

    // MARK: - Page breaks & comments

    private static bool IsPageBreak(string line)
    {
        var t = TrimWS(line);
        return t == "\\newpage" || t == "\\pagebreak";
    }

    /// <summary>Leading U+0020 only (a tab disqualifies), then <c>&lt;!--</c>.</summary>
    private static bool IsCommentStart(string line) =>
        DropLeadingSpaces(line).StartsWith("<!--", StringComparison.Ordinal);

    /// <summary>
    /// <c>&lt;!-- note: … --&gt;</c> → the note text (possibly empty); any other comment → null.
    /// <c>--&gt;</c> is searched from index 0, not from the opener, so a stray <c>--&gt;</c> before
    /// <c>&lt;!--</c> drops the whole comment — wart, kept. The body is the only WSNL trim in the
    /// parser (U+0085 is whitespace here). The prefix is tested on a lowercased copy and dropped
    /// from the original: five units, since no lowercase mapping yields any of <c>note:</c> from fewer.
    /// </summary>
    private static string? NoteText(string comment)
    {
        var open = ScalarText.FirstIndex(comment, "<!--");
        if (open is null) return null;
        var close = ScalarText.FirstIndex(comment, "-->") ?? comment.Length;
        if (open.Value + 4 > close) return null;
        var body = TrimWSNL(comment.Substring(open.Value + 4, close - open.Value - 4));
        if (!body.ToLowerInvariant().StartsWith("note:", StringComparison.Ordinal)) return null;
        return TrimWSNL(ScalarText.DropFirst(body, 5));
    }

    // MARK: - Outline, notes & anchors

    /// <summary>
    /// Every ATX / setext heading outside a fence and outside front matter, with its 0-based line
    /// and the same slug the HTML writer assigns — the two share one counter walked in document
    /// order, so any line this counts as a heading and <c>Parse</c> does not (or the reverse) drifts
    /// every later anchor. Front matter and footnote definitions are therefore skipped exactly as
    /// <c>Parse</c> skips them. A table header row and a list continuation line are <i>not</i>
    /// excluded from plain text, so <c>| a |\n---</c> yields a phantom entry — shipped divergence,
    /// replicated by every port.
    /// </summary>
    public static IReadOnlyList<OutlineEntry> Outline(string source)
    {
        var lines = NormalizedLines(source);
        var entries = new List<OutlineEntry>();
        var used = new Dictionary<string, int>(StringComparer.Ordinal);
        FenceMarker? fence = null;
        (string Text, int Line)? previousPlain = null;
        // `parse` treats an underline as setext only when exactly ONE line is buffered.
        var plainRun = 0;
        var i = 0;
        if (ParseFrontMatter(lines) is { } matter) i = matter.Next;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (fence is not null)
            {
                if (fence.Closes(line)) fence = null;
                previousPlain = null;
                plainRun = 0;
                i++;
                continue;
            }
            if (FenceMarker.From(line) is { } open)
            {
                fence = open;
                previousPlain = null;
                plainRun = 0;
                i++;
                continue;
            }
            if (IsCommentStart(line))
            {
                // A different loop shape from `parse`, same net cursor; an unclosed comment ends
                // at lines.Count + 1, which the while guard tolerates.
                while (i < lines.Count && !ScalarText.Contains(lines[i], "-->")) i++;
                previousPlain = null;
                plainRun = 0;
                i++;
                continue;
            }
            if (ParseHeading(line) is { } heading)
            {
                entries.Add(new OutlineEntry(heading.Level, heading.Text, Slug(heading.Text, used), i));
                previousPlain = null;
                plainRun = 0;
                i++;
                continue;
            }
            if (previousPlain is { } previous && plainRun == 1 && SetextUnderline(line) is { } level)
            {
                entries.Add(new OutlineEntry(level, previous.Text, Slug(previous.Text, used), previous.Line));
                previousPlain = null;
                plainRun = 0;
                i++;
                continue;
            }
            var trimmed = TrimWS(line);
            var isPlain = trimmed.Length != 0 && !IsThematicBreak(line) && !IsQuote(line)
                && ListMarker(line) is null && !IsPageBreak(line)
                && ParseFootnoteDefinition(line) is null;
            plainRun = isPlain ? plainRun + 1 : 0;
            previousPlain = isPlain ? (trimmed, i) : null;
            i++;
        }
        return entries;
    }

    /// <summary>Every author note outside a fence and outside front matter, with the 0-based line its comment opens on.</summary>
    public static IReadOnlyList<NoteEntry> Notes(string source)
    {
        var lines = NormalizedLines(source);
        var entries = new List<NoteEntry>();
        FenceMarker? fence = null;
        var i = 0;
        if (ParseFrontMatter(lines) is { } matter) i = matter.Next;
        while (i < lines.Count)
        {
            var line = lines[i];
            if (fence is not null)
            {
                if (fence.Closes(line)) fence = null;
                i++;
                continue;
            }
            if (FenceMarker.From(line) is { } open)
            {
                fence = open;
                i++;
                continue;
            }
            if (IsCommentStart(line))
            {
                var start = i;
                var raw = new List<string>();
                while (i < lines.Count)
                {
                    raw.Add(lines[i]);
                    var closed = ScalarText.Contains(lines[i], "-->");
                    i++;
                    if (closed) break;
                }
                var note = NoteText(string.Join("\n", raw));
                if (note is not null) entries.Add(new NoteEntry(note, start));
                continue;
            }
            i++;
        }
        return entries;
    }

    /// <summary>
    /// GitHub-style anchor slug, unique within one document through the caller's
    /// <paramref name="used"/> counts (<c>title</c>, <c>title-1</c>, …). The whole string is
    /// lowercased first with the full mapping (<see cref="ScalarText.FullLowercase"/>), then walked
    /// by code point: letters, numbers, marks, <c>_</c> and <c>-</c> survive, U+0020 alone becomes
    /// <c>-</c>, everything else (tab, NBSP, ZWJ, punctuation) is dropped; empty → <c>section</c>.
    /// </summary>
    /// <remarks>
    /// Code points, not units: a surrogate half is neither a letter nor a mark, so a UTF-16 walk
    /// would drop <c>𝐀</c>. Marks are kept even on a character the slug drops (<c>a -́b</c> →
    /// <c>a--́b-c</c>), which is what Android's code-point walk always did. The counter counts
    /// the bare slug, so <c>Same</c>, <c>Same 1</c>, <c>Same</c> give <c>same</c>, <c>same-1</c>,
    /// <c>same-1</c> — a duplicate id, as on GitHub; replicated, not fixed. Never normalise: NFC and
    /// NFD headings are different anchors on every platform.
    /// </remarks>
    public static string Slug(string text, IDictionary<string, int> used)
    {
        var builder = new StringBuilder(text.Length);
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in ScalarText.FullLowercase(text).EnumerateRunes())
        {
            if (IsSlugKept(rune))
            {
                var written = rune.EncodeToUtf16(buffer);
                builder.Append(buffer[..written]);
            }
            else if (rune.Value == ' ')
            {
                builder.Append('-');
            }
        }
        var slug = builder.Length == 0 ? "section" : builder.ToString();
        used.TryGetValue(slug, out var seen);
        used[slug] = seen + 1;
        return seen == 0 ? slug : slug + "-" + seen.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Swift: <c>isAlphabetic || numericType != nil || Mn/Mc/Me || _ -</c>. Here: letters (L*),
    /// numbers (Nd, Nl, No — which is <c>numericType != nil</c> except the CJK numeral ideographs,
    /// which are Lo and letters anyway), marks, the enclosed Latin letters that make up the rest of
    /// <c>Alphabetic</c>, and the two punctuation marks GitHub keeps.
    /// </summary>
    private static bool IsSlugKept(Rune rune) =>
        rune.Value == '_' || rune.Value == '-'
        || Rune.IsLetter(rune) || Rune.IsNumber(rune)
        || ScalarText.IsMark(rune) || ScalarText.IsEnclosedAlphabetic(rune);

    // MARK: - Raw diagram documents

    /// <summary>
    /// True when <paramref name="source"/> is a bare PlantUML file rather than Markdown: its first
    /// non-blank, non-<c>'</c>-comment line starts with <c>@start</c> (any diagram: uml, mindmap,
    /// gantt, json, …). The first qualifying line decides, so prose mentioning <c>@startuml</c>
    /// stays Markdown. Lines come from the wide newline set; the trim is WSNL.
    /// </summary>
    public static bool IsRawPlantUml(string source)
    {
        foreach (var rawLine in Lines(source))
        {
            var line = TrimWSNL(rawLine);
            if (line.Length == 0 || line[0] == '\'') continue;
            return line.StartsWith("@start", StringComparison.Ordinal);
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="source"/> is a bare Graphviz DOT file: its first non-blank,
    /// non-comment (<c>//</c>, <c>/*</c>) line matches <c>[strict] (graph|digraph) [ID] '{'</c>,
    /// keywords case-insensitive, the brace allowed on a later line. A <c>#</c> line is deliberately
    /// not a comment here — every Markdown heading starts with one, and skipping it would let the
    /// probe read past a title into prose. The <c>{</c> pre-guard is cheap, not sufficient.
    /// </summary>
    public static bool IsRawGraphviz(string source)
    {
        if (!source.Contains('{')) return false;
        foreach (var rawLine in Lines(source))
        {
            var line = TrimWSNL(rawLine);
            if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }
            var head = line.ToLowerInvariant();
            if (head.StartsWith("strict ", StringComparison.Ordinal)) head = TrimWS(head.Substring(7));
            // `digraph` first: it contains `graph`, and the shorter prefix would misread it.
            foreach (var keyword in (string[])["digraph", "graph"])
            {
                if (head.StartsWith(keyword, StringComparison.Ordinal)) return IsGraphHeader(head.Substring(keyword.Length));
            }
            return false;
        }
        return false;
    }

    /// <summary>The wide, Foundation-<c>.newlines</c> splitter the two raw-diagram probes use — not the block scanner's.</summary>
    public static IReadOnlyList<string> Lines(string source) => NewlineSetLines(source);

    /// <summary>
    /// What follows the keyword: nothing, <c>{</c>, or a name (bare or quoted) separated from the
    /// keyword by SPTAB and then nothing or <c>{</c>. The separator rule is what keeps
    /// <c>digraphs</c> and <c>graphviz</c> words; the empty/brace short-circuit before it is what
    /// lets <c>digraph{a}</c> through.
    /// </summary>
    private static bool IsGraphHeader(string tail)
    {
        var rest = DropSpaceTab(tail);
        if (rest.Length == 0 || rest[0] == '{') return true;
        if (tail[0] != ' ' && tail[0] != '\t') return false;
        if (rest[0] == '"')
        {
            var close = rest.IndexOf('"', 1);
            if (close < 0) return false;
            rest = rest.Substring(close + 1);
        }
        else
        {
            // By code point, keeping marks: Swift asks `isLetter` of a whole grapheme, so an NFD
            // `café` is one letter there; walking units, U+0301 is a mark and nothing else.
            // Runes also keep an astral letter, which a `char` scan would end the name at.
            var length = 0;
            foreach (var rune in rest.EnumerateRunes())
            {
                if (!IsGraphNameRune(rune)) break;
                length += rune.Utf16SequenceLength;
            }
            if (length == 0) return false;
            rest = rest.Substring(length);
        }
        rest = DropSpaceTab(rest);
        return rest.Length == 0 || rest[0] == '{';
    }

    private static bool IsGraphNameRune(Rune rune) =>
        rune.Value == '_' || Rune.IsLetter(rune) || Rune.IsNumber(rune) || ScalarText.IsMark(rune);

    // MARK: - Thematic break

    /// <summary>Three or more of one of <c>-</c>, <c>*</c>, <c>_</c> with only SPTAB between — narrower than every other predicate, so <c>U+00A0---</c> is a paragraph.</summary>
    private static bool IsThematicBreak(string line)
    {
        var marker = '\0';
        var count = 0;
        foreach (var c in line)
        {
            if (c == ' ' || c == '\t') continue;
            if (c != '-' && c != '*' && c != '_') return false;
            if (count == 0) marker = c;
            else if (c != marker) return false;
            count++;
        }
        return count >= 3;
    }

    // MARK: - Block quote

    private static bool IsQuote(string line)
    {
        var p = 0;
        while (p < line.Length && line[p] == ' ') p++;
        return p < line.Length && line[p] == '>';
    }

    /// <summary>Leading spaces, one <c>&gt;</c>, and exactly one optional space — a mark after that stays.</summary>
    private static string StripQuoteMarker(string line)
    {
        var p = 0;
        while (p < line.Length && line[p] == ' ') p++;
        if (p < line.Length && line[p] == '>') p++;
        if (p < line.Length && line[p] == ' ') p++;
        return line.Substring(p);
    }

    // MARK: - Lists

    private readonly record struct Marker(int Level, int? Ordinal, string Text, bool? Task);

    /// <summary>
    /// <c>-</c> / <c>*</c> / <c>+</c> or 1–9 ASCII digits then <c>.</c> / <c>)</c>, followed by U+0020
    /// or end of line (a tab does not count; a bare <c>-</c> is an item with empty text). Indentation
    /// counts spaces and tabs to the next 4-column stop — the only place a tab indents — and
    /// <c>Level</c> is <c>indent / 2</c>. Ten or more digits is not a marker; leading zeros are lost.
    /// </summary>
    private static Marker? ListMarker(string line)
    {
        var indent = 0;
        var start = 0;
        while (start < line.Length)
        {
            var c = line[start];
            if (c == ' ') indent++;
            else if (c == '\t') indent += 4 - indent % 4;
            else break;
            start++;
        }
        if (start >= line.Length) return null;

        var first = line[start];
        int? ordinal = null;
        int restStart;
        if (first == '-' || first == '*' || first == '+')
        {
            restStart = start + 1;
        }
        else if (IsAsciiDigit(first))
        {
            var d = start;
            while (d < line.Length && IsAsciiDigit(line[d])) d++;
            var digits = d - start;
            if (digits > 9) return null;
            if (d >= line.Length) return null;
            var delim = line[d];
            if (delim != '.' && delim != ')') return null;
            ordinal = int.Parse(line.AsSpan(start, digits), NumberStyles.None, CultureInfo.InvariantCulture);
            restStart = d + 1;
        }
        else
        {
            return null;
        }

        if (restStart < line.Length && line[restStart] != ' ') return null;
        var t = restStart;
        while (t < line.Length && line[t] == ' ') t++;
        var text = line.Substring(t);

        bool? task = null;
        if (text.StartsWith("[ ] ", StringComparison.Ordinal) || text == "[ ]")
        {
            task = false;
            text = UncheckedText(text);
        }
        else
        {
            var lower = text.ToLowerInvariant();
            if (lower.StartsWith("[x] ", StringComparison.Ordinal) || lower == "[x]")
            {
                task = true;
                text = UncheckedText(text);
            }
        }
        return new Marker(indent / 2, ordinal, text, task);
    }

    /// <summary>ASCII only — CommonMark's marker, and the set on which every port agrees (<c>char.IsDigit('٣')</c> is true).</summary>
    private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

    /// <summary>The three-unit box and the spaces after it; a mark the author put on the next character stays.</summary>
    private static string UncheckedText(string text) => DropLeadingSpaces(ScalarText.DropFirst(text, 3));

    // MARK: - Tables (GFM)

    /// <summary>Header row plus delimiter row (<c>| :--- | :---: | ---: |</c>), or null. Every delimiter cell must be <c>:?-+:?</c>; the header must have exactly as many cells.</summary>
    private static (IReadOnlyList<string> Header, IReadOnlyList<ColumnAlignment> Alignments)? ParseTable(string header, string delimiter)
    {
        if (!header.Contains('|')) return null;
        if (!TrimWS(delimiter).Contains('-')) return null;
        var delimCells = SplitTableRow(delimiter, null);
        if (delimCells.Count == 0) return null;
        var alignments = new List<ColumnAlignment>(delimCells.Count);
        foreach (var cell in delimCells)
        {
            var c = TrimWS(cell);
            if (c.Length == 0) return null;
            var sawDash = false;
            foreach (var ch in c)
            {
                if (ch == '-') sawDash = true;
                else if (ch != ':') return null;
            }
            if (!sawDash) return null;
            var left = c[0] == ':';
            var right = c[c.Length - 1] == ':';
            alignments.Add(left && right ? ColumnAlignment.Center : right ? ColumnAlignment.Trailing : ColumnAlignment.Leading);
        }
        var headerCells = SplitTableRow(header, null);
        if (headerCells.Count != alignments.Count) return null;
        return (headerCells, alignments);
    }

    /// <summary>
    /// One row into WS-trimmed cells. Optional leading and trailing pipe (the trailing test runs
    /// after the leading drop, so a row of a single <c>|</c> is one empty cell); <c>\|</c> is a
    /// literal pipe, <c>\x</c> stays <c>\x</c>, a trailing lone backslash is kept. With
    /// <paramref name="columns"/>, padded with <c>""</c> or truncated to that width.
    /// </summary>
    private static List<string> SplitTableRow(string row, int? columns)
    {
        var trimmed = TrimWS(row);
        var start = 0;
        var end = trimmed.Length;
        if (start < end && trimmed[start] == '|') start++;
        if (end > start && trimmed[end - 1] == '|') end--;

        var cells = new List<string>();
        var current = new StringBuilder();
        var escaped = false;
        for (var i = start; i < end; i++)
        {
            var ch = trimmed[i];
            if (escaped)
            {
                if (ch != '|') current.Append('\\');
                current.Append(ch);
                escaped = false;
            }
            else if (ch == '\\')
            {
                escaped = true;
            }
            else if (ch == '|')
            {
                cells.Add(TrimWS(current.ToString()));
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        if (escaped) current.Append('\\');
        cells.Add(TrimWS(current.ToString()));

        if (columns is { } width)
        {
            while (cells.Count < width) cells.Add("");
            if (cells.Count > width) cells.RemoveRange(width, cells.Count - width);
        }
        return cells;
    }

    // MARK: - Fence helper

    /// <summary>
    /// A fenced-code delimiter: up to three leading spaces (four or a tab is not a fence — there is
    /// no indented-code branch, so the line falls through to a paragraph), a run of at least three
    /// <c>`</c> or <c>~</c>, and an info string whose first U+0020-delimited word is the language.
    /// A backtick fence's info string may not contain a backtick; a tilde fence's may.
    /// </summary>
    private sealed class FenceMarker
    {
        private readonly char character;
        private readonly int count;
        private readonly int indent;

        public string? Language { get; }

        private FenceMarker(char character, int count, int indent, string? language)
        {
            this.character = character;
            this.count = count;
            this.indent = indent;
            Language = language;
        }

        public static FenceMarker? From(string line)
        {
            var indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent > 3 || indent >= line.Length) return null;
            var first = line[indent];
            if (first != '`' && first != '~') return null;
            var run = indent;
            while (run < line.Length && line[run] == first) run++;
            var count = run - indent;
            if (count < 3) return null;
            // WS trim, then split on U+0020 alone: "```js\tfoo" has language "js\tfoo".
            var info = TrimWS(line.Substring(run));
            if (first == '`' && info.Contains('`')) return null;
            var space = info.IndexOf(' ');
            var language = space < 0 ? info : info.Substring(0, space);
            return new FenceMarker(first, count, indent, language.Length == 0 ? null : language);
        }

        /// <summary>Same char, at least as long, indented arbitrarily (no 3-space cap), nothing but WS after.</summary>
        public bool Closes(string line)
        {
            var p = 0;
            while (p < line.Length && line[p] == ' ') p++;
            var run = p;
            while (run < line.Length && line[run] == character) run++;
            if (run - p < count) return false;
            return TrimWS(line.Substring(run)).Length == 0;
        }

        /// <summary>Remove up to the opening fence's indentation — U+0020 only, stopping at the first non-space.</summary>
        public string StripIndent(string line)
        {
            var removed = 0;
            while (removed < indent && removed < line.Length && line[removed] == ' ') removed++;
            return removed == 0 ? line : line.Substring(removed);
        }
    }
}
