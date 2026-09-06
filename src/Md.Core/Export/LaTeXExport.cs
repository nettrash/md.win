using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Book;
using Md.Core.Markdown;
using Md.Core.Text;

namespace Md.Core.Export;

/// <summary>
/// Serializes the parsed block model to LaTeX source — the <c>.tex</c> export.
/// </summary>
/// <remarks>
/// <para>
/// Every other output turns a formula into a picture or into KaTeX markup. This one hands the
/// mathematics back as the <c>$…$</c> the author wrote, which is why a math span — alone among
/// everything here — is copied through untouched while every other scrap of author text is
/// escaped. Where LaTeX cannot do what the preview does, it says so instead of dropping: a
/// diagram fence keeps its source in <c>verbatim</c> under a comment naming the language, an
/// image it cannot read is named in a comment below its alt text, and a footnote definition
/// nothing references is still printed at the end.
/// </para>
/// <para>
/// Port of <c>md.macOS/md/LaTeXExport.swift</c>, byte-identical in output with the Kotlin and
/// TypeScript editions. C# strings are UTF-16 like theirs, so the ordinal BCL operations are exact
/// where Swift needed <c>ScalarText</c>; the two traps that are .NET's own are culture-sensitive
/// overloads (never used here) and a code-unit regex engine whose <c>\p{L}</c> cannot see a
/// supplementary-plane letter — the two word guards are therefore checked by hand on
/// <see cref="Rune"/>s (see <see cref="Writer.Guarded"/>).
/// </para>
/// </remarks>
public static class LaTeXExport
{
    // MARK: - Entry points

    /// <summary>
    /// One document as a standalone <c>article</c> .tex file. No title block unless the front
    /// matter gives one: an invented <c>\title</c> would be a line the author never wrote.
    /// </summary>
    public static string Document(string source)
    {
        var blocks = MarkdownParser.Parse(WithoutSentinels(source));
        var writer = new Writer(headingOffset: 0);
        var body = writer.RenderUnit(blocks);
        return Assemble("article", writer, TitleBlock(Fields(blocks)), body);
    }

    /// <summary>
    /// The shape the app layer calls with (core-api.md Part B). <paramref name="title"/> is the
    /// file's name and is <b>not</b> written into the document — a file name is not part of a
    /// document, and the Swift never invents a <c>\title</c> from one. It exists so the call site
    /// that also names the <c>.tex</c> can pass what it has.
    /// </summary>
    public static string Document(string source, string title) => Document(source);

    /// <summary>
    /// A whole book as a <c>book</c> .tex file: each chapter a <c>\chapter</c>, each article a
    /// <c>\section</c>, root articles first — the reading order the PDF compile and the EPUB use.
    /// An article's own headings drop one level so they nest under the article's <c>\section</c>.
    /// </summary>
    public static string Book(StructuredBook book)
    {
        var writer = new Writer(headingOffset: 1);
        var parts = new List<string>();

        void Append(BookUnit article)
        {
            parts.Add("\\section{" + Escape(WithoutSentinels(article.Title)) + "}");
            var body = writer.RenderUnit(MarkdownParser.Parse(WithoutSentinels(article.Source)));
            if (body.Length > 0) parts.Add(body);
        }

        foreach (var article in book.FrontUnits) Append(article);
        foreach (var chapter in book.Sections)
        {
            // `book` resets the footnote counter at every chapter, so the numbers
            // `\footnotemark` cites have to restart with it.
            writer.StartChapter();
            parts.Add("\\chapter{" + Escape(WithoutSentinels(chapter.Title)) + "}");
            foreach (var article in chapter.Units) Append(article);
        }

        // A book has a title page in every other export, so it keeps one here. `\date{}`
        // rather than no date: `\maketitle` would otherwise stamp today, which nobody wrote.
        var title = new List<string> { "\\title{" + Escape(WithoutSentinels(book.Title)) + "}", "\\date{}" };
        return Assemble("book", writer, title, string.Join("\n\n", parts));
    }

    // MARK: - Preamble

    /// <summary>
    /// Wrap a rendered body in its class, packages and title block. Only the packages the
    /// document used are loaded, and each flag was set by the renderer as it emitted the command
    /// — never by scanning the finished text: a code block quoting <c>\href</c> is not a link.
    /// </summary>
    private static string Assemble(string documentClass, Writer writer, IReadOnlyList<string> titleBlock, string body)
    {
        var lines = new List<string> { "\\documentclass{" + documentClass + "}", "\\usepackage[utf8]{inputenc}" };

        // T2A carries the Cyrillic glyphs and has to be the *last* encoding listed: fontenc
        // makes the last one the default, and T2A holds Latin as well, so English is untouched
        // while T1 alone would leave Russian to fail character by character.
        if (ContainsCyrillic(body) || titleBlock.Any(ContainsCyrillic))
        {
            lines.Add("\\usepackage[T1,T2A]{fontenc}");
        }
        // Any formula loads amsmath: guessing which constructs the author reached for stops the
        // whole file at the first one that was not guessed.
        if (writer.NeedsAmsmath) lines.Add("\\usepackage{amsmath}");
        if (writer.NeedsGraphicx) lines.Add("\\usepackage{graphicx}");
        // `normalem` is not optional: plain ulem redefines `\emph` to underline.
        if (writer.NeedsUlem) lines.Add("\\usepackage[normalem]{ulem}");
        if (writer.NeedsLongtable) lines.Add("\\usepackage{longtable}");
        // hyperref last — the one package with a documented loading order.
        if (writer.NeedsHyperref) lines.Add("\\usepackage{hyperref}");

        lines.AddRange(titleBlock);
        lines.Add("\\begin{document}");
        if (titleBlock.Count > 0) lines.Add("\\maketitle");
        lines.Add("");
        lines.Add(body);
        lines.Add("");
        lines.Add("\\end{document}");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>Front-matter fields, first occurrence winning — the rule the rest of the app applies to a repeated key.</summary>
    private static Dictionary<string, string> Fields(IReadOnlyList<MarkdownBlock> blocks)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            if (block is not MarkdownBlock.FrontMatter matter) continue;
            foreach (var field in matter.Fields)
            {
                // Swift `lowercased()`: full, locale-free mapping. Never `ToLower()` — a Turkish
                // culture would turn `TITLE` into `tıtle` and the title block would vanish.
                var key = ScalarText.FullLowercase(field.Key);
                if (!fields.ContainsKey(key)) fields[key] = field.Value;
            }
        }
        return fields;
    }

    /// <summary>
    /// <c>\title</c> / <c>\author</c> / <c>\date</c> from front matter. Every other field is
    /// metadata <i>about</i> the document and is dropped the way the HTML and PDF drop it.
    /// </summary>
    private static List<string> TitleBlock(Dictionary<string, string> fields)
    {
        var lines = new List<string>();
        if (fields.TryGetValue("title", out var title)) lines.Add("\\title{" + Escape(title) + "}");
        if (fields.TryGetValue("author", out var author)) lines.Add("\\author{" + Escape(author) + "}");
        if (fields.TryGetValue("date", out var date))
        {
            lines.Add("\\date{" + Escape(date) + "}");
        }
        else if (lines.Count > 0)
        {
            // `\maketitle` prints today's date when none is given — a date nobody wrote.
            lines.Add("\\date{}");
        }
        return lines;
    }

    // MARK: - Sentinels

    private const char SentinelOpen = (char)0xE000;
    private const char SentinelClose = (char)0xE001;

    /// <summary>
    /// The author's text with this writer's own token sentinels (U+E000, U+E001) taken out —
    /// applied to every string that enters the writer, and nowhere later: the working string of
    /// an inline pass is <i>made</i> of these scalars. A document holding them of its own had its
    /// own characters read back as a token index; they are private-use scalars inputenc has no
    /// definition for, so dropping them costs nothing typesettable.
    /// </summary>
    public static string WithoutSentinels(string text)
    {
        var found = false;
        foreach (var c in text)
        {
            if (IsSentinel(c)) { found = true; break; }
        }
        if (!found) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!IsSentinel(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool IsSentinel(char c) => c == SentinelOpen || c == SentinelClose;

    // MARK: - Escaping

    /// <summary>
    /// Escape LaTeX's ten special characters in one pass. One pass rather than a chain of
    /// replacements because three escapes (<c>\</c>, <c>~</c>, <c>^</c>) contain characters that
    /// are themselves special. <c>&lt;</c>, <c>&gt;</c>, <c>|</c> are deliberately left alone —
    /// not TeX specials, and the ports agree character for character on this table.
    /// </summary>
    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length);
        // A code-unit walk is exact: every case is ASCII and a surrogate half equals none of them,
        // so a pair passes through whole and a combining mark never hides the special before it.
        foreach (var c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\textbackslash{}"); break;
                case '~': sb.Append("\\textasciitilde{}"); break;
                case '^': sb.Append("\\textasciicircum{}"); break;
                case '#': case '$': case '%': case '&': case '_': case '{': case '}':
                    sb.Append('\\').Append(c);
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escape a URL or image path for <c>\href</c> / <c>\includegraphics</c>: far less than
    /// <see cref="Escape"/>, because hyperref reads its argument with its own catcodes and
    /// <c>_ ~ &amp;</c> must arrive intact. Only what breaks the <i>argument</i> is escaped.
    /// </summary>
    public static string EscapeUrl(string url)
    {
        var sb = new StringBuilder(url.Length);
        foreach (var c in url)
        {
            switch (c)
            {
                case '\\': sb.Append("\\textbackslash{}"); break;
                case '%': case '#': case '{': case '}':
                    sb.Append('\\').Append(c);
                    break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Percent-decode an image path, or hand back exactly what came in. <c>my%20dir</c> is a
    /// directory on no disk; anything that is not valid percent-encoding — a stray <c>%</c>, a
    /// short escape, bytes that are not UTF-8 — is a file name that happens to contain a <c>%</c>,
    /// returned untouched (never repaired to U+FFFD) for <see cref="UnreadableImage"/> to refuse.
    /// </summary>
    public static string PercentDecoded(string path)
    {
        if (!ScalarText.Contains(path, "%")) return path;
        var bytes = new List<byte>(path.Length);
        Span<byte> scratch = stackalloc byte[4];
        var index = 0;
        while (index < path.Length)
        {
            if (path[index] != '%')
            {
                // One scalar at a time, so a surrogate pair is one 4-byte sequence and never two
                // replacement characters. An unpaired surrogate is not something a Swift string
                // can hold at all; here it is text this function cannot encode, and the rule for
                // everything it cannot handle is the same — hand the path back rather than repair
                // it, because a U+FFFD in a file name is a name nobody has.
                if (Rune.DecodeFromUtf16(path.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
                {
                    return path;
                }
                var written = rune.EncodeToUtf8(scratch);
                for (var b = 0; b < written; b++) bytes.Add(scratch[b]);
                index += consumed;
                continue;
            }
            if (index + 2 >= path.Length) return path;
            var high = HexDigit(path[index + 1]);
            var low = HexDigit(path[index + 2]);
            if (high is null || low is null) return path;
            bytes.Add((byte)((high.Value << 4) | low.Value));
            index += 3;
        }
        try
        {
            return StrictUtf8.GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return path;
        }
    }

    private static int? HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => null,
    };

    /// <summary>
    /// True when <paramref name="text"/> holds a Cyrillic letter — the trigger for T2A. Without
    /// it pdfTeX does not stop: the document compiles and the Russian is simply not in it. A
    /// range scan, not <c>\p{IsCyrillic}</c>: regex engines disagree. All four blocks are BMP and
    /// clear of the surrogate range, so a code-unit walk is exact.
    /// </summary>
    public static bool ContainsCyrillic(string text)
    {
        foreach (var c in text)
        {
            int v = c;
            if ((v >= 0x0400 && v <= 0x04FF)      // Cyrillic
                || (v >= 0x0500 && v <= 0x052F)   // Cyrillic Supplement
                || (v >= 0x2DE0 && v <= 0x2DFF)   // Cyrillic Extended-A
                || (v >= 0xA640 && v <= 0xA69F))  // Cyrillic Extended-B
            {
                return true;
            }
        }
        return false;
    }

    // MARK: - Small structural helpers

    /// <summary>
    /// LaTeX reads a <c>[</c> right after <c>\item</c> or <c>\\</c> as an optional argument, so a
    /// body that begins with one would lose its first words — a task checkbox is literally
    /// <c>[x]</c>. An empty group in front stops the scan. Applied to <i>finished</i> LaTeX only,
    /// never to a string still holding tokens.
    /// </summary>
    private static string BracketGuard(string body) => body.Length > 0 && body[0] == '[' ? "{}" + body : body;

    /// <summary>
    /// A header cell, set bold. <c>\textbf</c> reads its argument with a delimited macro that a
    /// top-level <c>&amp;</c> ends the row out from under; an extra group makes TeX step over it
    /// whole. Written only where there is a live alignment tab to guard.
    /// </summary>
    private static string HeaderCell(string cell) =>
        HoldsAlignmentTab(cell) ? "\\textbf{{" + cell + "}}" : "\\textbf{" + cell + "}";

    /// <summary>True when finished LaTeX holds an <c>&amp;</c> that is an alignment tab: a backslash swallows the character after it (<c>\&amp;</c>, and the second half of <c>\\</c>).</summary>
    private static bool HoldsAlignmentTab(string latex)
    {
        var index = 0;
        while (index < latex.Length)
        {
            if (latex[index] == '\\') index += 2;
            else if (latex[index] == '&') return true;
            else index += 1;
        }
        return false;
    }

    /// <summary>
    /// A formula given an <c>aligned</c> of its own when its <c>\\</c> and <c>&amp;</c> have
    /// nowhere else to live — <c>\[a &amp;= b\]</c> is "Misplaced alignment tab" and a top-level
    /// <c>\\</c> in a table cell ends the row from inside math mode. A formula that opens an
    /// environment of its own already owns its separators and is left as written.
    /// </summary>
    public static string Aligned(string math)
    {
        if (ScalarText.Contains(math, "\\begin{")) return math;
        if (!ScalarText.Contains(math, "\\\\") && !ScalarText.Contains(math, "&")) return math;
        return "\\begin{aligned}" + math + "\\end{aligned}";
    }

    private const string BeginFigure = "\\begin{figure}";
    private const string EndFigure = "\\end{figure}";

    /// <summary>
    /// True when <paramref name="line"/> sets something on the page — anything that is not white
    /// space, a comment or a float. <c>\\</c> after a line that began nothing is "There's no line
    /// here to end", so the soft-break join asks this first. By hand, not regex: this runs on
    /// finished LaTeX, where an escaped <c>\%</c> must not read as a comment — hence no backslash
    /// case, so a <c>\</c> counts as content.
    /// </summary>
    public static bool SetsSomething(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var c = line[index];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
            {
                index += 1;
            }
            else if (c == '%')
            {
                // A comment runs to the end of its physical line.
                index += 1;
                while (index < line.Length && line[index] != '\n') index += 1;
            }
            else if (ScalarText.FirstIndex(line, BeginFigure, index) == index)
            {
                var end = ScalarText.FirstIndex(line, EndFigure, index + BeginFigure.Length);
                if (end is null) return false;
                index = end.Value + EndFigure.Length;
            }
            else
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Why <c>\includegraphics</c> cannot be handed this path, or null when it can. graphicx reads
    /// its argument as a <i>file name</i>: <c>% # { } \</c> have no spelling that works there,
    /// <c>"</c> breaks graphicx's own space-quoting parser, and a URL is a file TeX cannot fetch.
    /// </summary>
    public static string? UnreadableImage(string file)
    {
        foreach (var c in file)
        {
            if (c is '%' or '#' or '{' or '}' or '\\' or '"') return "is not a file name LaTeX can read";
        }
        if (UriScheme(file) is not null) return "is a URL, and LaTeX has nothing to fetch it with";
        return null;
    }

    /// <summary>
    /// The URI scheme of <paramref name="path"/> when it is a URL, null otherwise: a scheme
    /// followed by <c>//</c>, or the schemeless <c>data:</c> form. Deliberately not "letters then a
    /// colon" — <c>C:</c> is a drive and <c>notes:draft.png</c> a file somebody can really have.
    /// </summary>
    private static string? UriScheme(string path)
    {
        var index = 0;
        while (index < path.Length)
        {
            var c = path[index];
            if (c == ':') break;
            var letter = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
            var rest = index > 0 && ((c >= '0' && c <= '9') || c == '+' || c == '-' || c == '.');
            if (!letter && !rest) return null;
            index += 1;
        }
        if (index == 0 || index >= path.Length) return null;
        var scheme = path.Substring(0, index);
        var authority = index + 1 < path.Length && path[index + 1] == '/'
            && index + 2 < path.Length && path[index + 2] == '/';
        if (!authority && ScalarText.FullLowercase(scheme) != "data") return null;
        return scheme;
    }

    // MARK: - Context

    /// <summary>
    /// Where a run of inline text is being written. <see cref="Restricted"/> is a table cell, a
    /// footnote's own text or an image caption — anywhere a float or a display environment stops
    /// the whole file. Only ever added, never lifted: a footnote raised from a table cell is
    /// inside the float too.
    /// </summary>
    private enum Context
    {
        Body,
        Restricted,
    }

    // MARK: - Writer

    /// <summary>
    /// Renders blocks while accumulating what the preamble owes them (package flags) and where
    /// the footnotes are up to. A class because the footnote numbering is a running count that
    /// has to survive nested calls.
    /// </summary>
    private sealed class Writer
    {
        /// <summary>How far to push the author's headings down, so a book's <c>\chapter</c>/<c>\section</c> sit above them.</summary>
        private readonly int headingOffset;

        public bool NeedsAmsmath { get; private set; }
        public bool NeedsGraphicx { get; private set; }
        public bool NeedsHyperref { get; private set; }
        public bool NeedsLongtable { get; private set; }
        public bool NeedsUlem { get; private set; }

        // The current unit's footnote definitions, the order written (to print the uncited), the
        // ids cited, and id -> the number LaTeX will give it. Ours and LaTeX's counters agree
        // because every `\footnote` and `\footnotemark` is emitted in the order it is numbered.
        private Dictionary<string, string> definitions = new(StringComparer.Ordinal);
        private List<string> written = new();
        private HashSet<string> cited = new(StringComparer.Ordinal);
        private Dictionary<string, int> number = new(StringComparer.Ordinal);
        private int counter;
        // Inside a footnote's text (LaTeX cannot nest them), inside a moving argument (a title,
        // a caption), inside a table's header row (the one box whose insertions LaTeX discards).
        private bool inFootnote;
        private bool movingArgument;
        private bool inTableHead;
        /// <summary>The <c>\footnotetext</c> lines the header row being written owes, emitted after its <c>\end{longtable}</c>.</summary>
        private List<string> headerNotes = new();
        private Context context = Context.Body;
        private Spans spans = new();

        public Writer(int headingOffset)
        {
            this.headingOffset = headingOffset;
        }

        /// <summary>Start a chapter: `book` resets the footnote counter, so the numbers `\footnotemark` cites restart — and the id map goes with them.</summary>
        public void StartChapter()
        {
            counter = 0;
            number = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        // MARK: Units

        /// <summary>
        /// One whole document or book article: its blocks, then any footnote definition the text
        /// never referenced. All four stores reset, <c>number</c> included — each article numbers
        /// its own notes from <c>[^1]</c>, and carrying the previous article's numbers over would
        /// make that a <c>\footnotemark</c> citing the wrong note. <c>counter</c> is not reset:
        /// a chapter is one counter however many articles it holds.
        /// </summary>
        public string RenderUnit(IReadOnlyList<MarkdownBlock> blocks)
        {
            definitions = new Dictionary<string, string>(StringComparer.Ordinal);
            written = new List<string>();
            cited = new HashSet<string>(StringComparer.Ordinal);
            number = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var block in blocks)
            {
                if (block is MarkdownBlock.FootnoteDefinition definition && !definitions.ContainsKey(definition.Id))
                {
                    definitions[definition.Id] = definition.Text;
                    written.Add(definition.Id);
                }
            }

            var output = RenderBlocks(blocks);
            // A definition nothing points at is still something the author wrote: printed, with
            // no reference of its own since it has nowhere to point back to.
            var orphans = written.Where(id => !cited.Contains(id)).ToList();
            if (orphans.Count == 0) return output;
            var trailer = new List<string> { "% Footnotes defined but never referenced " + EmDash + " kept so nothing is lost." };
            foreach (var id in orphans)
            {
                counter += 1;
                trailer.Add("\\footnote{" + FootnoteText(definitions[id]) + "}");
            }
            if (output.Length > 0) output += "\n\n";
            return output + string.Join("\n", trailer);
        }

        /// <summary>Blocks, blank-line separated; blocks that render to nothing drop out rather than leaving holes.</summary>
        private string RenderBlocks(IReadOnlyList<MarkdownBlock> blocks) =>
            string.Join("\n\n", blocks.Select(RenderBlock).Where(rendered => rendered.Length > 0));

        // MARK: Blocks

        /// <summary>The five sectioning commands `article` and `book` share; six heading levels map onto them, so the deepest two both land on `\subparagraph`.</summary>
        private static readonly string[] Sections = { "section", "subsection", "subsubsection", "paragraph", "subparagraph" };

        private string SectionCommand(int level)
        {
            var index = Math.Min(Math.Max(level - 1 + headingOffset, 0), Sections.Length - 1);
            return Sections[index];
        }

        private string RenderBlock(MarkdownBlock block)
        {
            switch (block)
            {
                case MarkdownBlock.Heading heading:
                    return "\\" + SectionCommand(heading.Level) + "{" + HeadingInline(heading.Text) + "}";
                case MarkdownBlock.Paragraph paragraph:
                    // Soft line breaks are breaks in the preview, the HTML and the PDF; they stay
                    // breaks here rather than reflowing the way raw LaTeX would.
                    return Inline(paragraph.Text, softBreaks: true);
                case MarkdownBlock.List list:
                    return RenderList(list.Items, list.Ordered);
                case MarkdownBlock.CodeBlock code:
                    return RenderCode(code.Language, code.Code);
                case MarkdownBlock.Quote quote:
                    return "\\begin{quote}\n" + RenderBlocks(quote.Blocks) + "\n\\end{quote}";
                case MarkdownBlock.Table table:
                    return RenderTable(table.Header, table.Alignments, table.Rows);
                case MarkdownBlock.ThematicBreak:
                    return "\\par\\noindent\\hrulefill\\par";
                case MarkdownBlock.PageBreak:
                    return "\\newpage";
                case MarkdownBlock.Note:
                case MarkdownBlock.FrontMatter:
                case MarkdownBlock.FootnoteDefinition:
                    // Private notes never reach a rendered document; front matter is hoisted into
                    // the title block; a definition is inlined at first reference or printed by
                    // RenderUnit if nothing references it.
                    return "";
                default:
                    throw new UnreachableException(block.Kind.ToString());
            }
        }

        /// <summary>A section title is a moving argument, so a footnote inside it needs `\protect`.</summary>
        private string HeadingInline(string text)
        {
            var saved = movingArgument;
            movingArgument = true;
            try
            {
                return Inline(text);
            }
            finally
            {
                movingArgument = saved;
            }
        }

        // MARK: Lists

        /// <summary>
        /// <c>itemize</c> / <c>enumerate</c>, nested by level. The model is flat, so levels are
        /// mapped onto a stack of open environments: LaTeX errors on a <c>\begin{itemize}</c> not
        /// preceded by an <c>\item</c> (a list whose first item is already indented) and refuses
        /// to nest more than four deep. Opening at most one level per item, and stopping at four,
        /// keeps every item in the output.
        /// </summary>
        private string RenderList(IReadOnlyList<ListItem> items, bool ordered)
        {
            var environment = ordered ? "enumerate" : "itemize";
            var lines = new List<string>();
            var open = new List<int>();

            foreach (var item in items)
            {
                while (open.Count > 0 && item.Level < open[^1])
                {
                    lines.Add("\\end{" + environment + "}");
                    open.RemoveAt(open.Count - 1);
                }
                if (open.Count == 0 || (item.Level > open[^1] && open.Count < 4))
                {
                    lines.Add("\\begin{" + environment + "}");
                    open.Add(item.Level);
                }
                var body = Inline(item.Text);
                if (item.Task is bool done)
                {
                    // A literal checkbox rather than a package for one glyph.
                    body = (done ? "[x]" : "[\\,]") + " " + body;
                }
                lines.Add("\\item " + BracketGuard(body));
            }
            for (var i = 0; i < open.Count; i++) lines.Add("\\end{" + environment + "}");
            return string.Join("\n", lines);
        }

        // MARK: Code, diagrams and data

        private const string VerbatimEnd = "\\end{verbatim}";

        /// <summary>
        /// The ten keys of the HTML renderer's <c>graphvizEngines</c> table — the fences that name
        /// a Graphviz layout. Kept here (not read from the HTML writer) so this module has no
        /// dependency on it; the list must stay identical to <c>MarkdownHTML.graphvizEngines</c>.
        /// </summary>
        private static readonly HashSet<string> GraphvizEngines = new(StringComparer.Ordinal)
        {
            "dot", "graphviz", "gv", "neato", "circo", "fdp", "sfdp", "twopi", "osage", "patchwork",
        };

        /// <summary>A fenced block. The info string decides what it is, using the names the HTML renderer answers to, so the two agree about every fence.</summary>
        private string RenderCode(string? language, string code)
        {
            var name = language ?? "";
            var lowered = ScalarText.FullLowercase(name);
            switch (lowered)
            {
                case "mermaid":
                case "plantuml":
                case "puml":
                case "plant-uml":
                    return Diagram(name, code);
                case "plot":
                    // The one rich fence this app can draw without an engine — and it still
                    // travels as its source: `\includegraphics` reads no SVG without a conversion
                    // step a `.tex` cannot carry.
                    return Diagram(name, code);
                case "csv":
                case "tsv":
                {
                    // Already a table everywhere else in the app; the parse and the alignment
                    // rule are shared with the HTML renderer.
                    var table = DelimitedTable.From(code, lowered == "tsv" ? '\t' : ',');
                    if (table is null) return Verbatim(code);
                    return RenderTable(table.Header, table.Alignments, table.Rows);
                }
                case "math":
                case "latex":
                case "tex":
                    // The author's mathematics, untouched — the point of the whole export.
                    return Display("\n" + code + "\n");
                default:
                    if (GraphvizEngines.Contains(lowered)) return Diagram(name, code);
                    return Verbatim(code);
            }
        }

        /// <summary>A diagram fence: LaTeX has no renderer for it, so the source is kept verbatim under a comment naming the language (the author's own spelling).</summary>
        private string Diagram(string language, string code) =>
            "% " + language + " diagram source " + EmDash + " LaTeX has no renderer for it, so it is kept as written.\n" + Verbatim(code);

        /// <summary>
        /// Wrap code in <c>verbatim</c>, unescaped. LaTeX's verbatim scans for the
        /// <i>characters</i> <c>\end{verbatim}</c>, so a block quoting them would close the
        /// environment early and spill the rest as LaTeX to execute; the block is split around
        /// each occurrence and the terminator itself set with <c>\verb</c>.
        /// </summary>
        private static string Verbatim(string code)
        {
            var pieces = ScalarText.Split(code, VerbatimEnd);
            if (pieces.Count == 1) return VerbatimBlock(pieces[0]);
            var output = new List<string>();
            for (var index = 0; index < pieces.Count; index++)
            {
                if (index > 0) output.Add("\\noindent\\verb|" + VerbatimEnd + "|\\par");
                if (pieces[index].Length > 0) output.Add(VerbatimBlock(pieces[index]));
            }
            return string.Join("\n", output);
        }

        private static string VerbatimBlock(string code) => "\\begin{verbatim}\n" + code + "\n" + VerbatimEnd;

        // MARK: Tables

        /// <summary>
        /// A <c>longtable</c>, never a <c>tabular</c> in a <c>table</c> float: a float cannot
        /// break across a page, so a tall table is truncated at exit 0. Not being a float also
        /// lets a body cell keep a plain <c>\footnote</c>. The header row is the exception —
        /// typeset once into the box <c>\endhead</c> reinserts, whose footnote insertions LaTeX
        /// discards — so a note first cited there is split (see <see cref="FootnoteReference"/>)
        /// and its <c>\footnotetext</c> written after <c>\end{longtable}</c>. Width is the widest
        /// row, not the header's.
        /// </summary>
        private string RenderTable(IReadOnlyList<string> header, IReadOnlyList<ColumnAlignment> alignments,
                                   IReadOnlyList<IReadOnlyList<string>> rows)
        {
            NeedsLongtable = true;
            var columns = Math.Max(header.Count, rows.Count == 0 ? 0 : rows.Max(row => row.Count));
            if (columns == 0) return "";

            var spec = new StringBuilder(columns);
            for (var column = 0; column < columns; column++)
            {
                if (column >= alignments.Count) { spec.Append('l'); continue; }
                spec.Append(alignments[column] switch
                {
                    ColumnAlignment.Leading => 'l',
                    ColumnAlignment.Center => 'c',
                    ColumnAlignment.Trailing => 'r',
                    _ => throw new UnreachableException(),
                });
            }

            string Row(IReadOnlyList<string> cells, bool heading)
            {
                var rendered = new string[columns];
                for (var column = 0; column < columns; column++)
                {
                    var cell = column < cells.Count ? Inline(cells[column], context: Context.Restricted) : "";
                    rendered[column] = heading && cell.Length > 0 ? HeaderCell(cell) : cell;
                }
                return BracketGuard(string.Join(" & ", rendered)) + " \\\\";
            }

            // The header row first — its notes take the first numbers — and under `inTableHead`,
            // so a note cited from it is split rather than dropped into the saved head box.
            var savedHead = inTableHead;
            var savedNotes = headerNotes;
            inTableHead = true;
            headerNotes = new List<string>();
            var head = Row(header, heading: true);
            var notes = headerNotes;
            inTableHead = savedHead;
            headerNotes = savedNotes;

            var lines = new List<string> { "\\begin{longtable}{" + spec + "}", "\\hline", head, "\\hline", "\\endhead" };
            foreach (var row in rows) lines.Add(Row(row, heading: false));
            lines.Add("\\hline");
            lines.Add("\\end{longtable}");
            lines.AddRange(notes);
            return string.Join("\n", lines);
        }

        // MARK: Footnotes

        /// <summary>
        /// A reference to <paramref name="id"/>, resolved the moment the reader meets it: the
        /// first reference carries the note's text (that is what a LaTeX footnote is), a later
        /// one cites the number, an undefined one goes back to the text the author typed.
        /// </summary>
        private string FootnoteReference(string id)
        {
            var protection = movingArgument ? "\\protect" : "";
            // LaTeX cannot nest footnotes, so a reference inside a note's own text stays literal.
            if (inFootnote || !definitions.TryGetValue(id, out var text))
            {
                return Escape("[^" + id + "]");
            }
            cited.Add(id);
            if (number.TryGetValue(id, out var existing))
            {
                return protection + "\\footnotemark[" + existing.ToString(CultureInfo.InvariantCulture) + "]";
            }
            counter += 1;
            number[id] = counter;
            // The header row: mark here, text after the table, and the counter stepped by hand
            // because `\footnotemark[n]` does not step it — a body cell further down takes the
            // next number and would otherwise print this one's.
            if (inTableHead)
            {
                var mark = counter.ToString(CultureInfo.InvariantCulture);
                headerNotes.Add("\\footnotetext[" + mark + "]{" + FootnoteText(text) + "}");
                return "\\stepcounter{footnote}" + protection + "\\footnotemark[" + mark + "]";
            }
            return protection + "\\footnote{" + FootnoteText(text) + "}";
        }

        /// <summary>A footnote's own text: inline Markdown with nesting disabled, and restricted — a footnote can no more hold a float than a table cell can.</summary>
        private string FootnoteText(string text)
        {
            var saved = inFootnote;
            inFootnote = true;
            try
            {
                return Inline(text, context: Context.Restricted);
            }
            finally
            {
                inFootnote = saved;
            }
        }

        // MARK: Inline

        private const RegexOptions Opts = RegexOptions.CultureInvariant;

        // ICU's `\s` as NSRegularExpression evaluates it, measured on macOS: the five ASCII
        // controls, NEL and every `Z`; U+200B, U+FEFF and U+180E are *not* in it. Spelled out so
        // the engine's own `\s` (a different set on the JVM, another in ECMAScript) never decides
        // where a link's URL ends. `[\s\S]` below is the union and means "any code unit" whatever
        // `\s` is.
        private const string Ws = @"\t\n\v\f\r\u0085\p{Z}";
        // ICU's `.` without DOTALL, measured the same way: every line terminator excluded, the
        // zero-width characters not. .NET's own `.` excludes only `\n`.
        private const string Dot = @"[^\n\v\f\r\u0085\u2028\u2029]";

        private static readonly Regex CodeSpan = new(@"`([^`]+)`", Opts);
        private static readonly Regex DisplayDollars = new(@"\$\$([\s\S]+?)\$\$", Opts);
        private static readonly Regex DisplayBrackets = new(@"\\\[([\s\S]+?)\\\]", Opts);
        // The two guarded patterns are the Swift's minus their `(?<![\p{L}\p{N}_$])` /
        // `(?![\p{L}\p{N}_])` lookarounds, which are checked by hand in `Guarded`: .NET's regex is
        // code-unit based and its `\p{L}` cannot see a supplementary-plane letter.
        private static readonly Regex InlineDollar = new(@"\$([^$\n]+?)\$", Opts);
        private static readonly Regex InlineParen = new(@"\\\(([^\n]+?)\\\)", Opts);
        private static readonly Regex ImageTitled = new(@"!\[([^\]]*)\]\(([^)" + Ws + @"]+)[" + Ws + @"]+""(" + Dot + @"*?)""\)", Opts);
        private static readonly Regex Image = new(@"!\[([^\]]*)\]\(([^)" + Ws + @"]+)\)", Opts);
        private static readonly Regex LinkTitled = new(@"\[([^\]]+)\]\(([^)" + Ws + @"]+)[" + Ws + @"]+""(" + Dot + @"*?)""\)", Opts);
        private static readonly Regex Link = new(@"\[([^\]]+)\]\(([^)" + Ws + @"]+)\)", Opts);
        private static readonly Regex Footnote = new(@"\[\^([A-Za-z0-9_-]+)\]", Opts);
        private static readonly Regex Bold = new(@"\*\*([^*]+)\*\*", Opts);
        private static readonly Regex BoldUnderscore = new(@"__([^_]+)__", Opts);
        private static readonly Regex Strike = new(@"~~([^~]+)~~", Opts);
        private static readonly Regex Italic = new(@"\*([^*]+)\*", Opts);
        private static readonly Regex ItalicUnderscore = new(@"_([^_]+)_", Opts);

        /// <summary>
        /// Convert a block's inline Markdown to LaTeX. The commands go in first — as tokens, never
        /// as text — so the escaping pass at the end sees author text and nothing else, and markup
        /// nested inside a link label or a bold run is still plain text when the later passes look
        /// for it. The patterns are the HTML renderer's, so the preview and the <c>.tex</c> agree
        /// about what a formula is. <paramref name="context"/> is added to whatever is in force,
        /// never substituted for it.
        /// </summary>
        private string Inline(string text, bool softBreaks = false, Context context = Context.Body)
        {
            // A pass owns its tokens: `Image` starts a second, complete pass over the alt text in
            // the middle of this one, and the two stores must never meet.
            var savedSpans = spans;
            var savedContext = this.context;
            spans = new Spans();
            if (context == Context.Restricted) this.context = Context.Restricted;
            try
            {
                var working = text;

                // 1. Literal spans first: code wins over math, so `$x$` in backticks stays code;
                //    `$…$` keeps its currency guard so "$5 and $10" is prose. `\texttt` rather
                //    than `\verb`: this has to survive in a title, a caption and a cell.
                working = Substitute(CodeSpan, working, null, groups => "\\texttt{" + Escape(groups[1]) + "}");
                working = Substitute(DisplayDollars, working, null, groups => Display(groups[1]));
                working = Substitute(DisplayBrackets, working, null, groups => Display(groups[1]));
                working = Substitute(InlineDollar, working, DollarGuard, groups => InlineMath(groups[1]));
                working = Substitute(InlineParen, working, null, groups => InlineMath(groups[1]));

                // 2. Images before links (image syntax is link syntax with a `!`), links before
                //    emphasis. A title is matched only so it is consumed.
                working = Substitute(ImageTitled, working, null, groups => RenderImage(groups[1], groups[2]));
                working = Substitute(Image, working, null, groups => RenderImage(groups[1], groups[2]));
                working = Wrap(LinkTitled, working, null, "}", groups => Href(groups[2]));
                working = Wrap(Link, working, null, "}", groups => Href(groups[2]));

                // 3. Footnote references after images and links: converting one first would let
                //    an image carry a whole `\footnote` into its caption.
                working = Substitute(Footnote, working, null, groups => FootnoteReference(groups[1]));

                // 4. Emphasis: bold before italic so `**` wins, underscore italic only at word
                //    boundaries so snake_case survives.
                working = Wrap(Bold, working, null, "}", _ => "\\textbf{");
                working = Wrap(BoldUnderscore, working, null, "}", _ => "\\textbf{");
                working = Wrap(Strike, working, null, "}", _ =>
                {
                    NeedsUlem = true;
                    return "\\sout{";
                });
                working = Wrap(Italic, working, null, "}", _ => "\\emph{");
                working = Wrap(ItalicUnderscore, working, UnderscoreGuard, "}", _ => "\\emph{");

                // 5. Everything still in the string is the author's own text. Tokens are
                //    private-use scalars and digits, none special, so they pass through.
                working = Escape(working);

                // 6. Restore the spans, and with them the soft breaks. Lines are split while the
                //    spans are still tokens (a multi-line formula is one token), but the bracket
                //    guard runs on the *restored* line. A `\\` is written only between two lines
                //    that both set something — "There's no line here to end" otherwise.
                if (!softBreaks) return Restored(working);
                var lines = ScalarText.Split(working, "\n").Select(Restored).ToList();
                var output = new StringBuilder(lines[0]);
                var opened = SetsSomething(lines[0]);
                for (var i = 1; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (opened && SetsSomething(line))
                    {
                        output.Append("\\\\\n").Append(BracketGuard(line));
                        opened = true;
                    }
                    else
                    {
                        // Never two newlines in a row: a blank line is a paragraph break, and one
                        // straight after a `\\` is the very error this avoids.
                        if (output.Length == 0 || output[^1] != '\n') output.Append('\n');
                        output.Append(line);
                        opened = opened || SetsSomething(line);
                    }
                }
                return output.ToString();
            }
            finally
            {
                spans = savedSpans;
                this.context = savedContext;
            }
        }

        /// <summary>An inline formula: legal everywhere, but its own `\\` and `&` still need an `aligned`, and the document owes amsmath.</summary>
        private string InlineMath(string math)
        {
            NeedsAmsmath = true;
            return "$" + Aligned(math) + "$";
        }

        /// <summary>A display formula — or an inline one where LaTeX will not open a display at all (`\[` in a table cell stops the file).</summary>
        private string Display(string math)
        {
            NeedsAmsmath = true;
            var body = Aligned(math);
            return context == Context.Restricted ? "$" + body + "$" : "\\[" + body + "\\]";
        }

        private string Href(string url)
        {
            NeedsHyperref = true;
            return "\\href{" + EscapeUrl(url) + "}{";
        }

        /// <summary>
        /// An image: in body text a captioned <c>figure</c>, or the bare graphic with a
        /// <c>\noindent</c> (a <c>\linewidth</c> graphic starting a paragraph is pushed over the
        /// margin by the indent). In a restricted place there is no float to be had, so the
        /// graphic is written bare and the alt text goes beside it in italic rather than being
        /// dropped.
        /// </summary>
        private string RenderImage(string alt, string path)
        {
            var file = PercentDecoded(path);
            var reason = UnreadableImage(file);
            if (reason is not null) return Skipped(file, reason, alt);
            NeedsGraphicx = true;
            var include = "\\includegraphics[width=\\linewidth]{" + EscapeUrl(file) + "}";
            if (context == Context.Restricted)
            {
                return alt.Length == 0 ? include : include + " \\emph{" + AltText(alt) + "}";
            }
            if (alt.Length == 0) return "\\noindent" + include;
            var saved = movingArgument;
            movingArgument = true;
            try
            {
                return "\\begin{figure}[ht]\n\\centering\n" + include + "\n\\caption{" + AltText(alt) + "}\n\\end{figure}";
            }
            finally
            {
                movingArgument = saved;
            }
        }

        /// <summary>
        /// An image LaTeX cannot include: the alt text in its place, and the file named in a
        /// comment that comes <b>last</b> and ends with a newline — <c>%</c> runs to the end of
        /// its physical line, so a comment in front would swallow the words, and one without a
        /// newline would swallow the rest of a table row.
        /// </summary>
        private string Skipped(string file, string reason, string alt)
        {
            var note = "% md: image skipped " + EmDash + " " + file + " " + reason + ".\n";
            if (alt.Length == 0) return note;
            return "\\emph{" + AltText(alt) + "}\n" + note;
        }

        /// <summary>
        /// Alt text as LaTeX: a fresh, complete pass over the Markdown the author wrote. The
        /// captured <paramref name="alt"/> carries this pass's tokens, and re-tokenising a
        /// half-tokenised string would leave them for the outer restore never to see.
        /// </summary>
        private string AltText(string alt) => Inline(RawSource(alt), context: Context.Restricted);

        // MARK: Token plumbing

        /// <summary>The spans one inline pass has lifted out: for each token, the LaTeX it stands for and the Markdown it was made from (the latter for alt text alone).</summary>
        private sealed class Spans
        {
            public List<string> Latex { get; } = new();
            public List<string> Source { get; } = new();
            public int Count => Latex.Count;

            public void Append(string latex, string source)
            {
                Latex.Add(latex);
                Source.Add(source);
            }
        }

        private static string Token(int index) =>
            SentinelOpen + index.ToString(CultureInfo.InvariantCulture) + SentinelClose;

        /// <summary>Put the LaTeX back where the tokens are — newest span first, since a span's replacement can only hold a token from an older pass.</summary>
        private string Restored(string text) => Resolve(text, index => spans.Latex[index]);

        /// <summary>The author's own Markdown for a stretch of the working string.</summary>
        private string RawSource(string text) => Resolve(text, index => spans.Source[index]);

        private string Resolve(string text, Func<int, string> replacement)
        {
            if (text.IndexOf(SentinelOpen) < 0) return text;
            var output = text;
            for (var index = spans.Count - 1; index >= 0; index--)
            {
                output = ScalarText.Replacing(output, Token(index), replacement(index));
            }
            return output;
        }

        /// <summary>
        /// Replace every match with a token standing for <paramref name="transform"/>'s LaTeX.
        /// The transform runs <b>before</b> the span is filed because it may start an inline pass
        /// of its own (a caption) which borrows the store and hands it back.
        /// </summary>
        private string Substitute(Regex pattern, string text, Func<string, Match, bool>? guard, Func<string[], string> transform) =>
            Splice(pattern, text, guard, match =>
            {
                var latex = transform(match.Groups);
                spans.Append(latex, match.Groups[0]);
                return (Token(spans.Count - 1), null);
            });

        /// <summary>Replace every match with an opening token, group 1's text, and a closing token — the command's braces hidden from the escaper, the author's words still matchable.</summary>
        private string Wrap(Regex pattern, string text, Func<string, Match, bool>? guard, string close, Func<string[], string> open) =>
            Splice(pattern, text, guard, match =>
            {
                var latex = open(match.Groups);
                var first = spans.Count;
                spans.Append(latex, match.Before);
                spans.Append(close, match.After);
                return (Token(first), Token(first + 1));
            });

        /// <summary>One match as the passes need it: the groups, and the author's text on either side of group 1 (the delimiters `Wrap` has to be able to give back).</summary>
        private sealed record MatchParts(string[] Groups, string Before, string After);

        /// <summary>
        /// The shared rewrite: match forward, build each replacement in reading order (footnote
        /// numbering follows the order the reader meets the references), then reassemble. The
        /// Swift splices backward to keep its ranges valid; appending the untouched stretches and
        /// the replacements in order gives the identical string.
        /// </summary>
        private string Splice(Regex pattern, string text, Func<string, Match, bool>? guard,
                              Func<MatchParts, (string Open, string? Close)> build)
        {
            var matches = Guarded(pattern, text, guard);
            if (matches.Count == 0) return text;

            var replacements = new string[matches.Count];
            for (var k = 0; k < matches.Count; k++)
            {
                var match = matches[k];
                var groups = new string[match.Groups.Count];
                for (var g = 0; g < groups.Length; g++)
                {
                    groups[g] = match.Groups[g].Success ? match.Groups[g].Value : "";
                }
                var before = "";
                var after = "";
                if (match.Groups.Count > 1 && match.Groups[1].Success)
                {
                    var inner = match.Groups[1];
                    before = text.Substring(match.Index, inner.Index - match.Index);
                    var innerEnd = inner.Index + inner.Length;
                    after = text.Substring(innerEnd, match.Index + match.Length - innerEnd);
                }
                var (open, close) = build(new MatchParts(groups, before, after));
                replacements[k] = close is null ? open : open + (groups.Length > 1 ? groups[1] : "") + close;
            }

            var result = new StringBuilder(text.Length);
            var cursor = 0;
            for (var k = 0; k < matches.Count; k++)
            {
                var match = matches[k];
                result.Append(text, cursor, match.Index - cursor);
                result.Append(replacements[k]);
                cursor = match.Index + match.Length;
            }
            result.Append(text, cursor, text.Length - cursor);
            return result.ToString();
        }

        /// <summary>
        /// Every non-overlapping match, left to right, with the hand-rolled word guard where the
        /// Swift pattern had lookarounds. A rejected match resumes the scan one code unit later,
        /// exactly as ICU does after a failed lookbehind — the delimiter's content excludes the
        /// delimiter, so there is no other candidate close to backtrack to.
        /// </summary>
        private static List<Match> Guarded(Regex pattern, string text, Func<string, Match, bool>? guard)
        {
            var matches = new List<Match>();
            var match = pattern.Match(text);
            while (match.Success)
            {
                if (guard is null || guard(text, match))
                {
                    matches.Add(match);
                    match = pattern.Match(text, match.Index + match.Length);
                }
                else
                {
                    match = pattern.Match(text, match.Index + 1);
                }
            }
            return matches;
        }

        /// <summary>`(?<![\p{L}\p{N}_$])` … `(?![\p{L}\p{N}_$])` around an inline formula.</summary>
        private static bool DollarGuard(string text, Match match) => NotWordAround(text, match, dollarToo: true);

        /// <summary>`(?<![\p{L}\p{N}_])` … `(?![\p{L}\p{N}_])` around an underscore italic.</summary>
        private static bool UnderscoreGuard(string text, Match match) => NotWordAround(text, match, dollarToo: false);

        private static bool NotWordAround(string text, Match match, bool dollarToo)
        {
            if (match.Index > 0 && IsWordScalar(RuneBefore(text, match.Index), dollarToo)) return false;
            var end = match.Index + match.Length;
            if (end < text.Length && IsWordScalar(RuneAt(text, end), dollarToo)) return false;
            return true;
        }

        /// <summary>The scalar ending at <paramref name="index"/>, decoding a surrogate pair whole; null for a lone half (category Cs — neither letter nor number, as ICU sees it).</summary>
        private static Rune? RuneBefore(string text, int index)
        {
            var c = text[index - 1];
            if (char.IsLowSurrogate(c) && index >= 2 && char.IsHighSurrogate(text[index - 2])) return new Rune(text[index - 2], c);
            if (char.IsSurrogate(c)) return null;
            return new Rune(c);
        }

        private static Rune? RuneAt(string text, int index) =>
            Rune.TryGetRuneAt(text, index, out var rune) ? rune : null;

        /// <summary>The spelled-out `[\p{L}\p{N}_]` guard class (plus `$` for the formula pattern) — not `\w`, which is a different set on every engine.</summary>
        private static bool IsWordScalar(Rune? scalar, bool dollarToo)
        {
            if (scalar is not Rune rune) return false;
            if (rune.Value == '_') return true;
            if (dollarToo && rune.Value == '$') return true;
            return Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber;
        }
    }

    /// <summary>U+2014, the dash in the three comments this file writes. Built from its code so no editor can quietly swap it for a hyphen or an en dash.</summary>
    private static readonly string EmDash = new((char)0x2014, 1);
}
