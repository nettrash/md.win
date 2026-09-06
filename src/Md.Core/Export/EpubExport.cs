using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Md.Core.Book;
using Md.Core.Markdown;
using Md.Core.Text;

namespace Md.Core.Export;

/// <summary>
/// One rich container as it sits in the rendered markup: where it starts, how long it is, and
/// whether it is mathematics (which decides the replacement image's <c>alt</c> text).
/// </summary>
/// <remarks>
/// The order of these is the order <c>querySelectorAll(<see cref="EpubExport.RichSelector"/>)</c>
/// reports the same elements in the live DOM, which is the whole pairing contract with the app.
/// </remarks>
public sealed record RichElement(int Start, int Length, bool IsMath)
{
    /// <summary>One past the last character of the element (its close tag included).</summary>
    public int End => Start + Length;

    /// <summary>The <c>alt</c> text the replacement image carries.</summary>
    public string Alt => IsMath ? "formula" : "diagram";
}

/// <summary>
/// One photographed rich element handed back by the app: the PNG bytes and the element's layout
/// width in CSS px.
/// </summary>
/// <remarks>
/// The PNG is captured at 2× so a formula stays crisp; <see cref="DisplayWidth"/> pins the
/// displayed size back to the layout width, and the stylesheet's <c>max-width: 100%</c> still
/// shrinks it on a narrow reader. The rounding is Swift's <c>Int(width.rounded())</c> — half away
/// from zero, <b>not</b> .NET's banker's default.
/// </remarks>
public sealed record RichSnapshot(byte[] Png, double Width)
{
    /// <summary>The <c>width</c> attribute's value: the layout width, rounded half away from zero.</summary>
    public int DisplayWidth => (int)Math.Round(Width, MidpointRounding.AwayFromZero);

    public bool Equals(RichSnapshot? other) =>
        other is not null && Width.Equals(other.Width) && Png.AsSpan().SequenceEqual(other.Png);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Width);
        hash.AddBytes(Png);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"RichSnapshot({Png.Length} bytes, {Width})");
}

/// <summary>One image resource inside <c>OEBPS/</c>: its archive-relative name and its bytes.</summary>
public sealed record EpubImage(string File, byte[] Data)
{
    public bool Equals(EpubImage? other) =>
        other is not null
        && string.Equals(File, other.File, StringComparison.Ordinal)
        && Data.AsSpan().SequenceEqual(other.Data);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(File, StringComparer.Ordinal);
        hash.AddBytes(Data);
        return hash.ToHashCode();
    }

    public override string ToString() => $"EpubImage({File}, {Data.Length} bytes)";
}

/// <summary>One finished spine entry: its file name inside <c>OEBPS/</c> and its XHTML document.</summary>
public sealed record EpubUnit(string File, string Title, string Xhtml);

/// <summary>
/// One table-of-contents row. Chapters carry their articles as <see cref="Children"/>, nested one
/// level like the book itself; a single document's rows are heading anchors and never nest.
/// </summary>
public sealed record NavEntry(string Title, string File, IReadOnlyList<NavEntry>? Children = null);

/// <summary>
/// One planned unit: everything the app needs to photograph it, and everything
/// <see cref="EpubExport.Assemble"/> needs to finish it once the photographs come back.
/// </summary>
/// <remarks>
/// <see cref="Document"/> is the full standalone page the app must load in its offscreen renderer
/// (empty for a heading page, which has nothing to render). <see cref="Body"/> is the extracted
/// body markup — rich containers still holding their source, the md-init script still on the end;
/// <see cref="EpubExport.Assemble"/> does the replacement and the XHTML fix-up, in that order.
/// </remarks>
public sealed record EpubUnitPlan(
    int Index,
    string File,
    string Title,
    string Document,
    string Body,
    IReadOnlyList<RichElement> RichElements,
    bool IsHeadingPage);

/// <summary>
/// An export that is complete but for the photographs: the identifier, the reading order and the
/// navigation are all fixed here, so nothing a browser does can change the shape of the book.
/// </summary>
public sealed record EpubPlan(
    string Title,
    string Identifier,
    IReadOnlyList<EpubUnitPlan> Units,
    IReadOnlyList<NavEntry> NavEntries)
{
    /// <summary>The units that carry rich content, in reading order — the ones the app must render.</summary>
    public IReadOnlyList<EpubUnitPlan> RichUnits =>
        Units.Where(unit => unit.RichElements.Count > 0).ToList();
}

/// <summary>
/// The app handed back a number of snapshots that does not match the number of rich elements in
/// the unit's markup — the one failure that would otherwise corrupt a book silently, shifting
/// every later image onto the wrong element.
/// </summary>
/// <remarks>
/// Swift's <c>zip(matches, tags)</c> pairs up to the shorter list and leaves the rest as source,
/// which loses a diagram without saying so; Android aborts the export on exactly this count check.
/// md.win takes Swift's replacement semantics (see <see cref="EpubExport.ReplacingRichElements"/>,
/// which the tests pin) and Android's guard on the way in.
/// </remarks>
public sealed class EpubSnapshotCountException : InvalidOperationException
{
    public EpubSnapshotCountException(int unitIndex, string unitTitle, int expected, int actual)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "EPUB unit {0} (\"{1}\") has {2} rich element(s) in its markup but {3} snapshot(s) were supplied.",
            unitIndex, unitTitle, expected, actual))
    {
        UnitIndex = unitIndex;
        UnitTitle = unitTitle;
        Expected = expected;
        Actual = actual;
    }

    /// <summary>The unit's index in the spine.</summary>
    public int UnitIndex { get; }

    /// <summary>The unit's display title.</summary>
    public string UnitTitle { get; }

    /// <summary>How many rich containers the markup holds.</summary>
    public int Expected { get; }

    /// <summary>How many snapshots the app supplied.</summary>
    public int Actual { get; }
}

/// <summary>
/// The EPUB 3 package: container, OPF, navigation document, the XHTML fixer, the rich-element to
/// image swap, the title-derived identifier and the stored-zip assembly. Port of macOS
/// <c>EPUBExport</c> in <c>DocumentExport.swift</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches a browser. The app's half is exactly one thing: a PNG per rich element,
/// in document order, for each unit this module says has any. It gets the page to load from
/// <see cref="EpubUnitPlan.Document"/>, finds the elements with
/// <c>querySelectorAll(<see cref="RichSelector"/>)</c>, and hands the bytes back — see
/// <see cref="Assemble"/>. A count that does not match the markup throws
/// <see cref="EpubSnapshotCountException"/> rather than shipping a book with its pictures shifted.
/// </para>
/// <para>
/// There is deliberately no captured-DOM input. The EPUB body is the writer's own markup: every
/// rich container becomes an <c>&lt;img&gt;</c>, a <c>```plot</c> fence is already a finished
/// <c>&lt;svg&gt;</c>, and code blocks stay plain because highlight.js only ever coloured the live
/// DOM. The self-contained HTML export is the path that needs a capture; this one does not.
/// </para>
/// <para>
/// Byte parity is with macOS, which the three shipping ports do not have with each other:
/// <c>&lt;meta charset="utf-8"/&gt;</c> in every head (Android and md.vscode omit it), the book's own
/// title in the nav (Android writes the literal <c>Contents</c>), the export stylesheet plus a
/// padding override (Android ships a hand-written sheet), <c>unit-000</c> for the title page
/// (Android starts at <c>unit-001</c>), manifest ids <c>bookid</c> / <c>u0</c> / <c>i0</c>, image
/// names <c>images/unit-000-rich-0.png</c>, and the blank line before <c>&lt;/body&gt;</c> that
/// falls out of stripping the md-init script from an already-trimmed body.
/// </para>
/// </remarks>
public static class EpubExport
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // MARK: - Fixed strings

    /// <summary>The container identity — the FIRST zip entry, stored uncompressed, exactly these bytes.</summary>
    public const string Mimetype = "application/epub+zip";

    /// <summary>The single document's one content file.</summary>
    public const string ContentFileName = "content.xhtml";

    /// <summary>
    /// The DOM query that finds the same containers <see cref="RichElements"/> finds in the markup,
    /// in the same order. This, <see cref="ContainsRichContent"/> and the marker table must be
    /// changed together: a class one of them misses either skips the snapshot pass entirely or
    /// shifts every later image onto the wrong element. It is deliberately a different set from
    /// <see cref="DiagramSvg.DomSelector"/> — that one has <c>div.plot</c> and no formulas.
    /// </summary>
    public const string RichSelector = ".md-mathi, .md-mathd, .mermaid, .plantuml, .graphviz";

    /// <summary>Points the reader at the package document. No trailing newline.</summary>
    public const string ContainerXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
        + "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">\n"
        + "<rootfiles>\n"
        + "<rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>\n"
        + "</rootfiles>\n"
        + "</container>";

    // MARK: - Titles

    /// <summary>
    /// The EPUB title for a single document: the front-matter <c>title:</c> field if the author gave
    /// a non-empty one, else the file name.
    /// </summary>
    /// <remarks>
    /// A book takes its title from its folder name; a lone document has no folder, so the file name
    /// is the closest thing to a title it has — and the title is also what
    /// <see cref="StableIdentifier"/> hashes, so two exports of the same document reach the same
    /// identifier. The key match is case-insensitive because generators write <c>title:</c> and
    /// <c>Title:</c> alike; the first non-empty one wins.
    /// </remarks>
    public static string DocumentTitle(string source, string fileName) =>
        DocumentTitle(MarkdownParser.FrontMatter(source), fileName);

    /// <summary>The same, for a caller that has already parsed the front matter (Swift's shape).</summary>
    public static string DocumentTitle(IReadOnlyList<MetadataField> frontMatter, string fileName)
    {
        foreach (var field in frontMatter)
        {
            // The ASCII key: OrdinalIgnoreCase is Foundation's caseInsensitiveCompare here.
            if (!string.Equals(field.Key, "title", StringComparison.OrdinalIgnoreCase)) continue;
            // Foundation's .whitespacesAndNewlines, which is NOT string.Trim(): the frozen tables
            // put U+200B in the set and .NET's char.IsWhiteSpace does not.
            var value = Whitespace.TrimWSNL(field.Value);
            if (value.Length > 0) return value;
        }
        return fileName;
    }

    // MARK: - Body extraction and the XHTML fixer

    /// <summary>
    /// Whether the rendered markup carries anything the engines must typeset — the trigger for the
    /// snapshot pass.
    /// </summary>
    /// <remarks>
    /// Each test stops at the class value's closing quote, so the Graphviz container matches
    /// whatever else its tag carries (<c>&lt;div class="graphviz" data-engine="neato"&gt;</c>).
    /// <c>class="plot"</c> is deliberately absent: a <c>```plot</c> fence is finished
    /// <c>&lt;svg&gt;</c> by the time this runs, so a plot-only document never opens a renderer —
    /// and travels into the book as vector rather than as a photograph of itself.
    /// </remarks>
    public static bool ContainsRichContent(string html) =>
        html.Contains("class=\"md-mathi\"", StringComparison.Ordinal)
        || html.Contains("class=\"md-mathd\"", StringComparison.Ordinal)
        || html.Contains("class=\"mermaid\"", StringComparison.Ordinal)
        || html.Contains("class=\"plantuml\"", StringComparison.Ordinal)
        || html.Contains("class=\"graphviz\"", StringComparison.Ordinal);

    /// <summary>
    /// The renderer's <c>&lt;body&gt;</c> content: everything between the body tag and the LAST
    /// <c>&lt;/body&gt;</c>, trimmed. The md-init script is still on the end —
    /// <see cref="XhtmlBody"/> takes it off and leaves the newline that preceded it, which is where
    /// every content file's blank line before <c>&lt;/body&gt;</c> comes from.
    /// </summary>
    /// <remarks>
    /// Searched backwards because a code block quoting <c>&lt;/body&gt;</c> is escaped and cannot
    /// match. Anything missing hands the input back untouched; so does a close tag before the open
    /// one, where the Swift would trap on the range (md.vscode guards it the same way).
    /// </remarks>
    public static string BodyHtml(string document)
    {
        var start = document.IndexOf("<body", StringComparison.Ordinal);
        if (start < 0) return document;
        var open = document.IndexOf('>', start + "<body".Length);
        if (open < 0) return document;
        var end = document.LastIndexOf("</body>", StringComparison.Ordinal);
        if (end < open + 1) return document;
        return Whitespace.TrimWSNL(document[(open + 1)..end]);
    }

    // "any character at all" — a tautological class, not a whitespace decision, so it is exempt
    // from the spell-it-out rule (its two halves are complements whatever \s means).
    private static readonly Regex ScriptElement =
        new("<script[^>]*>[\\s\\S]*?</script>", RegexOptions.CultureInvariant);

    // The void elements the writer emits. Group 2 walks attribute values as units so a `>` inside
    // a quoted value cannot end the tag early. The run before the optional slash is ICU's `\s`
    // spelled out — .NET's `\s` happens to be the same nine-ish set, but the family rule is to
    // never let a regex decide what whitespace is.
    private static readonly Regex VoidElement =
        new("<(br|hr|img)((?:[^>\"]|\"[^\"]*\")*?)[\\t\\n\\v\\f\\r\\x85\\p{Z}]*/?>", RegexOptions.CultureInvariant);

    /// <summary>
    /// Post-process rendered HTML into well-formed XHTML: scripts stripped, void elements
    /// self-closed, and the one named entity the writer uses replaced with its numeric form — XML
    /// predefines only amp / lt / gt / quot / apos.
    /// </summary>
    public static string XhtmlBody(string html)
    {
        var output = ScriptElement.Replace(html, string.Empty);
        output = VoidElement.Replace(output, "<$1$2/>");
        return output.Replace("&bull;", "&#8226;", StringComparison.Ordinal);
    }

    // MARK: - Rich elements

    // The writer's six rich containers: opener, close tag, and whether it is mathematics. Their
    // content is escaped text, so the first close tag is always the element's own.
    //
    // The Graphviz opener stops at the class attribute's closing quote rather than at the tag's
    // `>`: the writer names the layout program in the tag as well, so a whole-tag literal would
    // recognise none of the eight engines and every DOT diagram would ship as raw source.
    private static readonly (string Open, string Close, bool IsMath)[] RichMarkers =
    [
        ("<span class=\"md-mathi\">", "</span>", true),
        ("<span class=\"md-mathd\">", "</span>", true),
        ("<div class=\"md-mathd\">", "</div>", true),
        ("<pre class=\"mermaid\">", "</pre>", false),
        ("<div class=\"plantuml\">", "</div>", false),
        ("<div class=\"graphviz\"", "</div>", false),
    ];

    /// <summary>
    /// Every rich element in <paramref name="html"/>, in document order — the same order the DOM
    /// query reports, so captures and replacements pair up by index.
    /// </summary>
    /// <remarks>
    /// From a cursor, take the earliest of the six openers, find its close after it, emit the span
    /// and move past it. Non-overlapping by construction, and an unclosed opener ends the scan (as
    /// in Kotlin and TypeScript) rather than skipping ahead to a later element.
    /// </remarks>
    public static IReadOnlyList<RichElement> RichElements(string html)
    {
        var found = new List<RichElement>();
        var index = 0;
        while (true)
        {
            var start = -1;
            var open = string.Empty;
            var close = string.Empty;
            var isMath = false;
            foreach (var (candidateOpen, candidateClose, candidateIsMath) in RichMarkers)
            {
                var at = html.IndexOf(candidateOpen, index, StringComparison.Ordinal);
                if (at < 0 || (start >= 0 && at >= start)) continue;
                start = at;
                open = candidateOpen;
                close = candidateClose;
                isMath = candidateIsMath;
            }
            if (start < 0) return found;
            var closeAt = html.IndexOf(close, start + open.Length, StringComparison.Ordinal);
            if (closeAt < 0) return found;
            var end = closeAt + close.Length;
            found.Add(new RichElement(start, end - start, isMath));
            index = end;
        }
    }

    /// <summary>
    /// Replace each rich element in <paramref name="html"/> — in document order — with its image
    /// tag. Empty <paramref name="tags"/> leaves the markup alone; a short list pairs up to the
    /// shorter of the two, exactly as Swift's <c>zip</c> does.
    /// </summary>
    public static string ReplacingRichElements(string html, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0) return html;
        var elements = RichElements(html);
        if (elements.Count == 0) return html;
        var output = new StringBuilder(html.Length);
        var cursor = 0;
        var pairs = Math.Min(elements.Count, tags.Count);
        for (var index = 0; index < pairs; index++)
        {
            var element = elements[index];
            output.Append(html, cursor, element.Start - cursor);
            output.Append(tags[index]);
            cursor = element.End;
        }
        output.Append(html, cursor, html.Length - cursor);
        return output.ToString();
    }

    /// <summary>The replacement tag for one photographed element.</summary>
    public static string ImageTag(string file, bool isMath, int width) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "<img src=\"{0}\" alt=\"{1}\" width=\"{2}\"/>",
            file, isMath ? "formula" : "diagram", width);

    /// <summary>Where one unit's photographs live: <c>images/unit-000-rich-0.png</c>.</summary>
    public static string ImageFileName(int unitIndex, int elementIndex) =>
        string.Format(CultureInfo.InvariantCulture, "images/unit-{0:D3}-rich-{1}.png", unitIndex, elementIndex);

    /// <summary>A book unit's file name: <c>unit-000.xhtml</c> is always the title page.</summary>
    public static string UnitFileName(int index) =>
        string.Format(CultureInfo.InvariantCulture, "unit-{0:D3}.xhtml", index);

    // MARK: - The XML documents

    /// <summary>One XHTML5 content document, in the EPUB's shared stylesheet.</summary>
    public static string XhtmlDocument(string title, string body) => string.Join("\n",
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE html>",
        "<html xmlns=\"http://www.w3.org/1999/xhtml\">",
        "<head>",
        "<meta charset=\"utf-8\"/>",
        "<title>" + XmlEscape(title) + "</title>",
        "<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>",
        "</head>",
        "<body>",
        body,
        "</body>",
        "</html>");

    /// <summary>
    /// The EPUB 3 navigation document: root articles first, then each chapter with its articles
    /// nested one level below it. A single document's entries are heading anchors into its one
    /// content file, so they never nest.
    /// </summary>
    public static string Nav(string title, IReadOnlyList<NavEntry> entries) => string.Join("\n",
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
        "<!DOCTYPE html>",
        "<html xmlns=\"http://www.w3.org/1999/xhtml\" xmlns:epub=\"http://www.idpf.org/2007/ops\">",
        "<head>",
        "<meta charset=\"utf-8\"/>",
        "<title>" + XmlEscape(title) + "</title>",
        "<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>",
        "</head>",
        "<body>",
        "<nav epub:type=\"toc\">",
        "<h1>" + XmlEscape(title) + "</h1>" + NavList(entries) + "</nav>",
        "</body>",
        "</html>");

    private static string NavList(IReadOnlyList<NavEntry>? entries)
    {
        if (entries is null || entries.Count == 0) return string.Empty;
        var items = string.Join("\n", entries.Select(entry =>
            "<li><a href=\"" + entry.File + "\">" + XmlEscape(entry.Title) + "</a>"
            + NavList(entry.Children) + "</li>"));
        return "\n<ol>\n" + items + "\n</ol>\n";
    }

    /// <summary>
    /// The package document: metadata, a manifest of every file, and the spine in reading order.
    /// </summary>
    /// <remarks>
    /// <paramref name="svgUnits"/> names the content documents that hold an <c>&lt;svg&gt;</c>.
    /// EPUB 3 requires the reserved manifest property <c>svg</c> on each of them — EPUBCheck reports
    /// OPF-014 without it and the book is invalid. Until the <c>```plot</c> fence no EPUB this
    /// family produced had ever contained one, since the engines all arrive as PNG snapshots.
    /// The identifier and the timestamp are generated, so they are not escaped; the title is.
    /// <c>dc:language</c> is hard-coded <c>en</c> — a product fact, not an oversight.
    /// </remarks>
    public static string Opf(
        string title,
        string identifier,
        string modified,
        IReadOnlyList<string> units,
        IReadOnlyList<string> images,
        IReadOnlyCollection<int>? svgUnits = null)
    {
        var manifest = new StringBuilder();
        manifest.Append("<item id=\"nav\" href=\"nav.xhtml\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>\n");
        manifest.Append("<item id=\"css\" href=\"style.css\" media-type=\"text/css\"/>\n");
        for (var index = 0; index < units.Count; index++)
        {
            var properties = svgUnits is not null && svgUnits.Contains(index) ? " properties=\"svg\"" : string.Empty;
            manifest.Append(CultureInfo.InvariantCulture,
                $"<item id=\"u{index}\" href=\"{units[index]}\" media-type=\"application/xhtml+xml\"{properties}/>\n");
        }
        for (var index = 0; index < images.Count; index++)
        {
            manifest.Append(CultureInfo.InvariantCulture,
                $"<item id=\"i{index}\" href=\"{images[index]}\" media-type=\"image/png\"/>\n");
        }
        var spine = string.Join("\n", Enumerable.Range(0, units.Count).Select(index =>
            string.Format(CultureInfo.InvariantCulture, "<itemref idref=\"u{0}\"/>", index)));
        return string.Join("\n",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<package xmlns=\"http://www.idpf.org/2007/opf\" version=\"3.0\" unique-identifier=\"bookid\">",
            "<metadata xmlns:dc=\"http://purl.org/dc/elements/1.1/\">",
            "<dc:identifier id=\"bookid\">" + identifier + "</dc:identifier>",
            "<dc:title>" + XmlEscape(title) + "</dc:title>",
            "<dc:language>en</dc:language>",
            "<meta property=\"dcterms:modified\">" + modified + "</meta>",
            "</metadata>",
            "<manifest>",
            manifest.ToString() + "</manifest>",
            "<spine>",
            spine,
            "</spine>",
            "</package>");
    }

    /// <summary>
    /// The book's stylesheet: the export CSS the PDFs use, pulled out of a rendered document, with
    /// a light EPUB override — the reader owns pages and margins.
    /// </summary>
    /// <remarks>
    /// A quirk to preserve rather than fix: because this is the export CSS verbatim, every EPUB page
    /// carries <c>body { padding: 48px 56px }</c> before the override and <c>font-size: 11pt</c>.
    /// That is intentional-by-omission on macOS, and the other two ports each diverge differently —
    /// Android ships its own sheet, md.vscode appends a page-break and an image rule instead.
    /// </remarks>
    public static string Stylesheet()
    {
        var document = MarkdownHtml.Document(string.Empty, "style", dark: false, export: true);
        var css = string.Empty;
        var start = document.IndexOf("<style>", StringComparison.Ordinal);
        var end = document.IndexOf("</style>", StringComparison.Ordinal);
        if (start >= 0 && end >= start + "<style>".Length) css = document[(start + "<style>".Length)..end];
        return css + "\n/* EPUB: the reader owns pages and margins. */\nbody { padding: 0.5em 5%; }\n";
    }

    /// <summary>
    /// A stable identifier for a book, derived from its title — an RFC 4122 version 5 (name-based)
    /// UUID in the standard URL namespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// EPUB's <c>dc:identifier</c> is what a reader uses to decide whether two files are the same
    /// publication. A fresh random UUID on every export means every export is a <i>different</i>
    /// book: re-exporting after fixing a typo stacks up beside the old one in the library instead of
    /// replacing it, and a store that expects a stable identifier across releases cannot accept the
    /// file at all. Renaming the book does change it, which is the right answer.
    /// </para>
    /// <para>
    /// SHA-1 because v5 <i>is</i> SHA-1 — not a security decision, and it must never be "upgraded":
    /// that would orphan every book this family has already issued. The 16 bytes are formatted as
    /// hex by hand: <c>new Guid(byte[])</c> is mixed-endian and would reorder the first three groups.
    /// </para>
    /// </remarks>
    public static string StableIdentifier(string title)
    {
        // The URL namespace from RFC 4122 Appendix C.
        ReadOnlySpan<byte> namespaceBytes =
        [
            0x6b, 0xa7, 0xb8, 0x11, 0x9d, 0xad, 0x11, 0xd1,
            0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8,
        ];
        // The name is hashed as UTF-8 — the encoding RFC 4122 §4.3 names, and the only one that
        // gives a non-ASCII title the same UUID here as on Apple.
        var name = Utf8.GetBytes(title);
        var input = new byte[namespaceBytes.Length + name.Length];
        namespaceBytes.CopyTo(input);
        name.CopyTo(input, namespaceBytes.Length);

        var bytes = SHA1.HashData(input)[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);   // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);   // RFC 4122 variant

        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return "urn:uuid:" + hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..];
    }

    /// <summary>The <c>dcterms:modified</c> timestamp: UTC, second precision (EPUBCheck rejects fractions).</summary>
    public static string ModifiedNow() => Modified(DateTimeOffset.UtcNow);

    /// <summary>The same, for a caller that wants a deterministic archive.</summary>
    public static string Modified(DateTimeOffset when) =>
        when.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string XmlEscape(string s) => s
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    // MARK: - Planning (pure; what the app must render and photograph)

    /// <summary>
    /// Plan a single document: one content file, and a navigation built from the document's own
    /// outline.
    /// </summary>
    /// <remarks>
    /// The trap a book export carries and a document must not: <see cref="PlanBook"/> makes its
    /// first unit a title page and counts the nav from the units past it, so reusing that path with
    /// the title page removed would leave the nav pointing one file short. Here there is exactly one
    /// unit and every nav row is a heading anchor into it — the slug
    /// <see cref="MarkdownParser.Outline"/> assigns is the same <c>id</c> the HTML writer gives that
    /// heading, so a nav tap lands on the section rather than on nothing. A document with no
    /// headings would leave the toc list empty, which is not valid EPUB 3, so it falls back to one
    /// entry: the whole document, under its title, linking at the content file.
    /// </remarks>
    public static EpubPlan PlanDocument(string source, string title)
    {
        var document = MarkdownHtml.Document(source, title, dark: false, export: true);
        var body = BodyHtml(document);
        var elements = ContainsRichContent(body) ? RichElements(body) : [];
        var unit = new EpubUnitPlan(0, ContentFileName, title, document, body, elements, IsHeadingPage: false);
        return new EpubPlan(title, StableIdentifier(title), [unit], DocumentNavEntries(title, MarkdownParser.Outline(source)));
    }

    /// <summary>
    /// Plan a whole book: a title page, then each root article, then per chapter a heading page
    /// followed by its articles — the same reading order the PDF compile uses.
    /// </summary>
    public static EpubPlan PlanBook(StructuredBook book)
    {
        var units = new List<EpubUnitPlan>();
        var entries = new List<NavEntry>();

        EpubUnitPlan HeadingUnit(string title)
        {
            var index = units.Count;
            return new EpubUnitPlan(
                index, UnitFileName(index), title,
                Document: string.Empty,
                Body: "<h1>" + XmlEscape(title) + "</h1>",
                RichElements: [],
                IsHeadingPage: true);
        }

        EpubUnitPlan ArticleUnit(BookUnit article)
        {
            var index = units.Count;
            var document = MarkdownHtml.Document(article.Source, article.Title, dark: false, export: true);
            var body = BodyHtml(document);
            var elements = ContainsRichContent(body) ? RichElements(body) : [];
            return new EpubUnitPlan(index, UnitFileName(index), article.Title, document, body, elements, IsHeadingPage: false);
        }

        units.Add(HeadingUnit(book.Title));                     // unit-000 — the title page
        foreach (var article in book.FrontUnits)
        {
            var unit = ArticleUnit(article);
            units.Add(unit);
            entries.Add(new NavEntry(article.Title, unit.File));
        }
        foreach (var section in book.Sections)
        {
            var heading = HeadingUnit(section.Title);
            units.Add(heading);
            var children = new List<NavEntry>();
            foreach (var article in section.Units)
            {
                var unit = ArticleUnit(article);
                units.Add(unit);
                children.Add(new NavEntry(article.Title, unit.File));
            }
            entries.Add(new NavEntry(section.Title, heading.File, children));
        }
        return new EpubPlan(book.Title, StableIdentifier(book.Title), units, entries);
    }

    private static IReadOnlyList<NavEntry> DocumentNavEntries(string title, IReadOnlyList<OutlineEntry> outline) =>
        outline.Count == 0
            ? [new NavEntry(title, ContentFileName)]
            : outline.Select(entry => new NavEntry(entry.Text, ContentFileName + "#" + entry.Slug)).ToList();

    // MARK: - Assembly

    /// <summary>
    /// Finish a plan with the app's photographs and pack the archive.
    /// </summary>
    /// <param name="plan">A plan from <see cref="PlanDocument"/> or <see cref="PlanBook"/>.</param>
    /// <param name="snapshots">
    /// One list of PNGs per unit that has rich elements, keyed by <see cref="EpubUnitPlan.Index"/>
    /// and in document order within the unit. Units with no rich elements may be omitted.
    /// </param>
    /// <param name="modified">The <c>dcterms:modified</c> stamp; <see cref="ModifiedNow"/> when null.</param>
    /// <param name="identifier">Overrides the title-derived identifier (a test seam).</param>
    /// <exception cref="EpubSnapshotCountException">
    /// A unit's snapshot count does not match its rich-element count.
    /// </exception>
    public static byte[] Assemble(
        EpubPlan plan,
        IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? snapshots = null,
        string? modified = null,
        string? identifier = null) =>
        ZipWriter.Archive(Entries(plan, snapshots, modified, identifier));

    /// <summary>The archive's members, in order — the seam the tests drive and the packing runs through.</summary>
    public static IReadOnlyList<ZipEntry> Entries(
        EpubPlan plan,
        IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? snapshots = null,
        string? modified = null,
        string? identifier = null)
    {
        var units = new List<EpubUnit>(plan.Units.Count);
        var images = new List<EpubImage>();
        foreach (var unit in plan.Units)
        {
            var taken = snapshots is not null && snapshots.TryGetValue(unit.Index, out var found)
                ? found
                : (IReadOnlyList<RichSnapshot>)[];
            if (taken.Count != unit.RichElements.Count)
            {
                throw new EpubSnapshotCountException(unit.Index, unit.Title, unit.RichElements.Count, taken.Count);
            }
            var body = unit.Body;
            if (unit.RichElements.Count > 0)
            {
                var tags = new List<string>(taken.Count);
                for (var element = 0; element < taken.Count; element++)
                {
                    var file = ImageFileName(unit.Index, element);
                    images.Add(new EpubImage(file, taken[element].Png));
                    tags.Add(ImageTag(file, unit.RichElements[element].IsMath, taken[element].DisplayWidth));
                }
                body = ReplacingRichElements(body, tags);
            }
            // A heading page's body is already the finished markup; an article's still holds the
            // md-init script the fixer strips (and the newline before it, which stays).
            var xhtml = XhtmlDocument(unit.Title, unit.IsHeadingPage ? body : XhtmlBody(body));
            units.Add(new EpubUnit(unit.File, unit.Title, xhtml));
        }
        return Entries(plan.Title, units, plan.NavEntries, images,
            identifier ?? plan.Identifier, modified ?? ModifiedNow());
    }

    /// <summary>
    /// The finished container's members: <c>mimetype</c> first (stored, per the OCF spec), then
    /// META-INF and the OEBPS payload.
    /// </summary>
    public static IReadOnlyList<ZipEntry> Entries(
        string title,
        IReadOnlyList<EpubUnit> units,
        IReadOnlyList<NavEntry> navEntries,
        IReadOnlyList<EpubImage> images,
        string identifier,
        string modified)
    {
        // Whichever units still hold an `<svg>` of their own — today that means a ```plot fence,
        // since every engine has become a PNG by now — carry properties="svg".
        var svgUnits = new HashSet<int>();
        for (var index = 0; index < units.Count; index++)
        {
            if (units[index].Xhtml.Contains("<svg", StringComparison.Ordinal)) svgUnits.Add(index);
        }
        var opf = Opf(title, identifier, modified,
            units.Select(unit => unit.File).ToList(),
            images.Select(image => image.File).ToList(),
            svgUnits);
        var entries = new List<ZipEntry>(6 + units.Count + images.Count)
        {
            new("mimetype", Utf8.GetBytes(Mimetype)),
            new("META-INF/container.xml", Utf8.GetBytes(ContainerXml)),
            new("OEBPS/content.opf", Utf8.GetBytes(opf)),
            new("OEBPS/nav.xhtml", Utf8.GetBytes(Nav(title, navEntries))),
            new("OEBPS/style.css", Utf8.GetBytes(Stylesheet())),
        };
        foreach (var unit in units) entries.Add(new ZipEntry("OEBPS/" + unit.File, Utf8.GetBytes(unit.Xhtml)));
        foreach (var image in images) entries.Add(new ZipEntry("OEBPS/" + image.File, image.Data));
        return entries;
    }

    /// <summary>
    /// The package entries for a single document, <c>mimetype</c> first — the pure seam the macOS
    /// tests drive. <paramref name="body"/> is the already XHTML-fixed body
    /// (<c>XhtmlBody(BodyHtml(document))</c>), the same form a book article is wrapped in.
    /// </summary>
    public static IReadOnlyList<ZipEntry> DocumentEntries(
        string title,
        string body,
        IReadOnlyList<EpubImage> images,
        IReadOnlyList<OutlineEntry> outline,
        string modified,
        string? identifier = null)
    {
        var unit = new EpubUnit(ContentFileName, title, XhtmlDocument(title, body));
        return Entries(title, [unit], DocumentNavEntries(title, outline), images,
            identifier ?? StableIdentifier(title), modified);
    }

    // MARK: - Entry points

    /// <summary>
    /// Build the EPUB 3 of one open document. <paramref name="snapshots"/> is one PNG per rich
    /// element in document order — <see cref="PlanDocument"/> says how many, and how to find them.
    /// </summary>
    /// <exception cref="EpubSnapshotCountException">The snapshot count does not match the markup.</exception>
    public static byte[] BuildDocument(
        string source,
        string title,
        IReadOnlyList<RichSnapshot>? snapshots = null,
        string? modified = null,
        string? identifier = null) =>
        Assemble(PlanDocument(source, title), Single(snapshots), modified, identifier);

    /// <summary>
    /// Build the EPUB 3 of a whole book. <paramref name="snapshots"/> maps a unit index (as
    /// <see cref="PlanBook"/> assigned it) to that unit's PNGs in document order.
    /// </summary>
    /// <exception cref="EpubSnapshotCountException">A unit's snapshot count does not match its markup.</exception>
    public static byte[] Build(
        StructuredBook book,
        IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? snapshots = null,
        string? modified = null,
        string? identifier = null) =>
        Assemble(PlanBook(book), snapshots, modified, identifier);

    private static IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? Single(IReadOnlyList<RichSnapshot>? snapshots) =>
        snapshots is null || snapshots.Count == 0
            ? null
            : new Dictionary<int, IReadOnlyList<RichSnapshot>> { [0] = snapshots };
}
