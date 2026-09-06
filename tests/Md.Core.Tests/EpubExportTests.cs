using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Md.Core.Book;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// The EPUB 3 package: the container shape, the package document, the navigation, the XHTML fixer,
/// the rich-element swap, the title-derived identifier and the stored archive. Port of the macOS
/// <c>EPUBExport</c> tests in <c>mdTests.swift</c> plus the invariants only md.win states —
/// the snapshot count check, and every XML file in a finished archive really parsing.
/// </summary>
public class EpubExportTests
{
    private const string Modified = "2026-07-24T00:00:00Z";

    private static string Text(byte[] bytes) => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

    private static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);

    /// <summary>
    /// The single-document entries for <paramref name="source"/>, assembled exactly as the export
    /// does for a document with no rich blocks: the body is
    /// <c>XhtmlBody(BodyHtml(the export HTML))</c> — the same form a book article is wrapped in —
    /// and the outline is read from the same source, so nav slugs and content ids come from the real
    /// code paths rather than from fixtures. (Swift's test-private <c>documentEpubEntries</c>.)
    /// </summary>
    private static IReadOnlyList<ZipEntry> DocumentEpubEntries(string source, string title)
    {
        var html = MarkdownHtml.Document(source, title, dark: false, export: true);
        var body = EpubExport.XhtmlBody(EpubExport.BodyHtml(html));
        return EpubExport.DocumentEntries(title, body, [], MarkdownParser.Outline(source), Modified);
    }

    private static byte[]? Entry(IReadOnlyList<ZipEntry> entries, string name) =>
        entries.FirstOrDefault(entry => string.Equals(entry.Name, name, StringComparison.Ordinal))?.Data;

    private static string EntryText(IReadOnlyList<ZipEntry> entries, string name)
    {
        var data = Entry(entries, name);
        Assert.NotNull(data);
        return Text(data);
    }

    /// <summary>A one-pixel-ish PNG stand-in: the app's bytes are opaque to Core, only their identity matters.</summary>
    private static byte[] Png(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker];

    // MARK: - The XHTML fixer

    [Fact]
    public void XhtmlFixerMakesWellFormedMarkup()
    {
        var fixedUp = EpubExport.XhtmlBody(string.Join("\n",
            "<p>a<br>b</p>",
            "<hr>",
            "<img src=\"x.png\" alt=\"pic\">",
            "<div class=\"md-list\"><span class=\"md-marker\">&bull;</span></div>",
            "<script type=\"module\" src=\"rich/md-init.js\"></script>"));
        Assert.Contains("<br/>", fixedUp, StringComparison.Ordinal);
        Assert.Contains("<hr/>", fixedUp, StringComparison.Ordinal);
        Assert.Contains("<img src=\"x.png\" alt=\"pic\"/>", fixedUp, StringComparison.Ordinal);
        // Named entities become numeric — XML predefines only amp/lt/gt/quot/apos.
        Assert.Contains("&#8226;", fixedUp, StringComparison.Ordinal);
        Assert.DoesNotContain("&bull;", fixedUp, StringComparison.Ordinal);
        // Engine references are stripped.
        Assert.DoesNotContain("<script", fixedUp, StringComparison.Ordinal);
    }

    [Fact]
    public void XhtmlFixerIsIdempotentAndKeepsAngleBracketsInsideAttributes()
    {
        // Already self-closed voids survive a second pass unchanged.
        Assert.Equal("<br/>", EpubExport.XhtmlBody("<br/>"));
        Assert.Equal("<img src=\"a.png\"/>", EpubExport.XhtmlBody("<img src=\"a.png\"/>"));
        Assert.Equal("<hr/>", EpubExport.XhtmlBody(EpubExport.XhtmlBody("<hr>")));
        // Group 2 walks quoted values as units, so a `>` inside an attribute cannot end the tag
        // early — the whole alt text stays on the one element.
        Assert.Equal(
            "<img src=\"x.png\" alt=\"a > b\"/>",
            EpubExport.XhtmlBody("<img src=\"x.png\" alt=\"a > b\">"));
        // Trailing whitespace before the slash is swallowed, as in the Swift.
        Assert.Equal("<br/>", EpubExport.XhtmlBody("<br  >"));
    }

    [Fact]
    public void XhtmlFixerStripsWholeScriptElementsIncludingTheirBody()
    {
        Assert.Equal(
            "<p>a</p>\n<p>b</p>",
            EpubExport.XhtmlBody("<p>a</p>\n<script>var x = 1 < 2;\nvar y = 3;</script><p>b</p>"));
    }

    // MARK: - Rich elements

    [Fact]
    public void RichElementsBecomeImages()
    {
        const string html = "<p>x <span class=\"md-mathi\">a^2</span> y</p>\n<pre class=\"mermaid\">graph TD</pre>";
        var output = EpubExport.ReplacingRichElements(html, [
            "<img src=\"images/m0.png\" alt=\"formula\"/>",
            "<img src=\"images/m1.png\" alt=\"diagram\"/>",
        ]);
        Assert.Equal(
            "<p>x <img src=\"images/m0.png\" alt=\"formula\"/> y</p>\n<img src=\"images/m1.png\" alt=\"diagram\"/>",
            output);
    }

    [Fact]
    public void RichElementScanReportsKindsAndSpansInDocumentOrder()
    {
        const string html = "<p>x <span class=\"md-mathi\">a^2</span> y</p>\n<pre class=\"mermaid\">graph TD</pre>";
        var elements = EpubExport.RichElements(html);
        Assert.Equal(2, elements.Count);
        Assert.True(elements[0].IsMath);
        Assert.Equal("formula", elements[0].Alt);
        Assert.Equal("<span class=\"md-mathi\">a^2</span>", html[elements[0].Start..elements[0].End]);
        Assert.False(elements[1].IsMath);
        Assert.Equal("diagram", elements[1].Alt);
        Assert.Equal("<pre class=\"mermaid\">graph TD</pre>", html[elements[1].Start..elements[1].End]);
    }

    [Fact]
    public void RichElementScanCoversAllSixContainersAndNeverThePlot()
    {
        var html = string.Join("\n",
            "<span class=\"md-mathi\">i</span>",
            "<span class=\"md-mathd\">d</span>",
            "<div class=\"md-mathd\">D</div>",
            "<pre class=\"mermaid\">m</pre>",
            "<div class=\"plantuml\">p</div>",
            "<div class=\"graphviz\" data-engine=\"neato\">g</div>",
            "<div class=\"plot\"><svg viewBox=\"0 0 1 1\"></svg></div>");
        var elements = EpubExport.RichElements(html);
        Assert.Equal(6, elements.Count);
        Assert.Equal([true, true, true, false, false, false], elements.Select(e => e.IsMath));
        // The plot is deliberately not a rich container: it is already an <svg>, so it travels into
        // the book as vector rather than as a photograph of itself.
        Assert.DoesNotContain("plot", string.Concat(elements.Select(e => html[e.Start..e.End])), StringComparison.Ordinal);
        Assert.False(EpubExport.ContainsRichContent("<div class=\"plot\"><svg/></div>"));
    }

    [Fact]
    public void EmptyTagListLeavesTheMarkupAloneAndAShortListPairsUpToTheShorter()
    {
        const string html = "<p><span class=\"md-mathi\">a</span><span class=\"md-mathi\">b</span></p>";
        Assert.Equal(html, EpubExport.ReplacingRichElements(html, []));
        // Swift pairs with `zip`: a missing snapshot leaves the trailing element as source. md.win
        // keeps that behaviour here and refuses the mismatch one level up, in Entries.
        Assert.Equal(
            "<p>TAG<span class=\"md-mathi\">b</span></p>",
            EpubExport.ReplacingRichElements(html, ["TAG"]));
    }

    [Fact]
    public void GraphvizContainersAreSnapshottedWhateverTheirEngine()
    {
        // A ```dot block in a book article must reach the EPUB as a PNG, not as its DOT source. The
        // container names its layout program in the tag, so both the gate that decides whether the
        // renderer runs at all and the replacement scan have to tolerate that extra attribute.
        const string source = "```dot\ndigraph { a -> b }\n```\n\n```twopi\ngraph { a -- b }\n```";
        var body = EpubExport.BodyHtml(MarkdownHtml.Document(source, "t", dark: false, export: true));
        Assert.Contains("data-engine=\"twopi\"", body, StringComparison.Ordinal);
        Assert.True(EpubExport.ContainsRichContent(body), "a DOT diagram must trigger the snapshot pass");

        var output = EpubExport.ReplacingRichElements(body, [
            "<img src=\"images/g0.png\" alt=\"diagram\"/>",
            "<img src=\"images/g1.png\" alt=\"diagram\"/>",
        ]);
        Assert.Contains("images/g0.png", output, StringComparison.Ordinal);
        Assert.Contains("images/g1.png", output, StringComparison.Ordinal);
        // Both containers are gone — no leftover open tag, no raw DOT.
        Assert.DoesNotContain("class=\"graphviz\"", output, StringComparison.Ordinal);
        Assert.DoesNotContain("digraph", output, StringComparison.Ordinal);
        Assert.DoesNotContain("data-engine", output, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryGraphvizAliasReachesTheSameReplacement()
    {
        // The eight layout programs the writer names, through all ten fence aliases.
        foreach (var alias in MarkdownHtml.GraphvizEngines.Keys)
        {
            var body = EpubExport.BodyHtml(MarkdownHtml.Document(
                "```" + alias + "\ndigraph { a -> b }\n```", "t", dark: false, export: true));
            Assert.True(EpubExport.ContainsRichContent(body), alias);
            Assert.Single(EpubExport.RichElements(body));
            var replaced = EpubExport.ReplacingRichElements(body, ["IMG"]);
            Assert.StartsWith("IMG", replaced, StringComparison.Ordinal);
            Assert.DoesNotContain("digraph", replaced, StringComparison.Ordinal);
            Assert.DoesNotContain("data-engine", replaced, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContainsRichContentNamesTheFiveClassesAndStopsAtTheClosingQuote()
    {
        Assert.True(EpubExport.ContainsRichContent("<span class=\"md-mathi\">x</span>"));
        Assert.True(EpubExport.ContainsRichContent("<div class=\"md-mathd\">x</div>"));
        Assert.True(EpubExport.ContainsRichContent("<pre class=\"mermaid\">x</pre>"));
        Assert.True(EpubExport.ContainsRichContent("<div class=\"plantuml\">x</div>"));
        Assert.True(EpubExport.ContainsRichContent("<div class=\"graphviz\" data-engine=\"osage\">x</div>"));
        Assert.False(EpubExport.ContainsRichContent("<p>plain</p>"));
    }

    [Fact]
    public void TheDomSelectorAndTheMarkerTableNameTheSameContainers()
    {
        // The app queries the live DOM with this exact string; the marker scan walks the same
        // markup. One kind counted here and not there shifts every later image onto the wrong
        // element, which is why the Swift states the invariant three separate times.
        Assert.Equal(".md-mathi, .md-mathd, .mermaid, .plantuml, .graphviz", EpubExport.RichSelector);
        Assert.Equal("application/epub+zip", EpubExport.Mimetype);
        Assert.Equal("content.xhtml", EpubExport.ContentFileName);
        // Not the diagram-SVG set: that one has div.plot and no formulas. Do not derive one from
        // the other.
        Assert.NotEqual(DiagramSvg.DomSelector, EpubExport.RichSelector);
    }

    [Fact]
    public void TheMarkerScanTakesOnlyTheSixShapesTheWriterEmits()
    {
        // DECIDED DIVERGENCE, pinned here: macOS scans with the regex
        // `<(span|div|pre) class="(?:md-mathi|md-mathd|mermaid|plantuml|graphviz)"[^>]*>…</\1>`,
        // which would also match combinations the writer never emits — a `<div class="md-mathi">`,
        // a `<span class="mermaid">`. md.win follows Kotlin and TypeScript and lists the six real
        // shapes instead, so the two agree byte-for-byte on this writer's output and differ only on
        // markup no port can produce. `containsRichContent` is the wider test on all four ports, so
        // such markup would still open a renderer — and then fail the snapshot count check rather
        // than ship a shifted book.
        const string unreal = "<div class=\"md-mathi\">x</div>";
        Assert.Empty(EpubExport.RichElements(unreal));
        Assert.Equal(unreal, EpubExport.ReplacingRichElements(unreal, ["IMG"]));
        Assert.True(EpubExport.ContainsRichContent(unreal));
    }

    [Fact]
    public void ImageTagAndFileNamesFollowTheMacosSpelling()
    {
        Assert.Equal("images/unit-000-rich-0.png", EpubExport.ImageFileName(0, 0));
        Assert.Equal("images/unit-012-rich-3.png", EpubExport.ImageFileName(12, 3));
        Assert.Equal("unit-000.xhtml", EpubExport.UnitFileName(0));
        Assert.Equal("unit-007.xhtml", EpubExport.UnitFileName(7));
        Assert.Equal(
            "<img src=\"images/unit-000-rich-0.png\" alt=\"formula\" width=\"84\"/>",
            EpubExport.ImageTag("images/unit-000-rich-0.png", isMath: true, width: 84));
        Assert.Equal(
            "<img src=\"images/unit-001-rich-2.png\" alt=\"diagram\" width=\"420\"/>",
            EpubExport.ImageTag("images/unit-001-rich-2.png", isMath: false, width: 420));
    }

    [Fact]
    public void SnapshotWidthRoundsHalfAwayFromZeroNotToEven()
    {
        // Swift's `Int(rect.width.rounded())`. .NET's Math.Round defaults to banker's rounding,
        // which would make 84.5 → 84 and 85.5 → 86 — two different rules in one column.
        Assert.Equal(85, new RichSnapshot(Png(1), 84.5).DisplayWidth);
        Assert.Equal(86, new RichSnapshot(Png(1), 85.5).DisplayWidth);
        Assert.Equal(84, new RichSnapshot(Png(1), 84.4).DisplayWidth);
        Assert.Equal(1, new RichSnapshot(Png(1), 0.5).DisplayWidth);
    }

    // MARK: - The package document

    [Fact]
    public void OpfCarriesMetadataManifestAndSpine()
    {
        var opf = EpubExport.Opf("A & B", "urn:uuid:TEST", "2026-07-10T00:00:00Z",
            ["unit-000.xhtml", "unit-001.xhtml"], ["images/unit-001-rich-0.png"]);
        Assert.Contains("<dc:title>A &amp; B</dc:title>", opf, StringComparison.Ordinal);
        Assert.Contains("<dc:language>en</dc:language>", opf, StringComparison.Ordinal);
        Assert.Contains("<dc:identifier id=\"bookid\">urn:uuid:TEST</dc:identifier>", opf, StringComparison.Ordinal);
        Assert.Contains("<meta property=\"dcterms:modified\">2026-07-10T00:00:00Z</meta>", opf, StringComparison.Ordinal);
        Assert.Contains("properties=\"nav\"", opf, StringComparison.Ordinal);
        Assert.Contains("<item id=\"u1\" href=\"unit-001.xhtml\" media-type=\"application/xhtml+xml\"/>", opf, StringComparison.Ordinal);
        Assert.Contains("<item id=\"i0\" href=\"images/unit-001-rich-0.png\" media-type=\"image/png\"/>", opf, StringComparison.Ordinal);
        // The spine follows the unit order — the reading order.
        var first = opf.IndexOf("<itemref idref=\"u0\"/>", StringComparison.Ordinal);
        var second = opf.IndexOf("<itemref idref=\"u1\"/>", StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.True(second >= 0);
        Assert.True(first < second);
    }

    [Fact]
    public void OpfFlagsOnlyTheUnitsThatCarryAnSvg()
    {
        // EPUB 3 requires the reserved property on every content document holding an <svg>;
        // EPUBCheck reports OPF-014 without it. Today that means a ```plot fence.
        var opf = EpubExport.Opf("T", "urn:uuid:TEST", Modified,
            ["unit-000.xhtml", "unit-001.xhtml", "unit-002.xhtml"], [], new HashSet<int> { 1 });
        Assert.Contains("<item id=\"u0\" href=\"unit-000.xhtml\" media-type=\"application/xhtml+xml\"/>", opf, StringComparison.Ordinal);
        Assert.Contains("<item id=\"u1\" href=\"unit-001.xhtml\" media-type=\"application/xhtml+xml\" properties=\"svg\"/>", opf, StringComparison.Ordinal);
        Assert.Contains("<item id=\"u2\" href=\"unit-002.xhtml\" media-type=\"application/xhtml+xml\"/>", opf, StringComparison.Ordinal);
    }

    [Fact]
    public void OpfWithNoUnitsStillParses()
    {
        // The shape the identifier test drives: an empty manifest tail and an empty spine.
        var opf = EpubExport.Opf("Empty", "urn:uuid:TEST", Modified, [], []);
        Assert.Contains("<manifest>\n<item id=\"nav\"", opf, StringComparison.Ordinal);
        Assert.Contains("media-type=\"text/css\"/>\n</manifest>", opf, StringComparison.Ordinal);
        Assert.Contains("<spine>\n\n</spine>", opf, StringComparison.Ordinal);
    }

    // MARK: - The navigation document

    [Fact]
    public void NavListsRootArticlesThenNestedChapters()
    {
        var nav = EpubExport.Nav("Book", [
            new NavEntry("Intro", "unit-001.xhtml"),
            new NavEntry("Chapter One", "unit-002.xhtml", [new NavEntry("First", "unit-003.xhtml")]),
        ]);
        Assert.Contains("epub:type=\"toc\"", nav, StringComparison.Ordinal);
        Assert.Contains("<a href=\"unit-001.xhtml\">Intro</a>", nav, StringComparison.Ordinal);
        // The chapter's articles nest inside the chapter's own item.
        Assert.Contains("<li><a href=\"unit-002.xhtml\">Chapter One</a>\n<ol>", nav, StringComparison.Ordinal);
        Assert.Contains("<a href=\"unit-003.xhtml\">First</a>", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void NavTitleIsTheBookTitleAndTheHeadIsMacosSpelled()
    {
        // macOS puts the book's own title in the <title> and the <h1>; Android writes the literal
        // "Contents". macOS wins, and the charset meta is macOS-only too.
        var nav = EpubExport.Nav("A & B", [new NavEntry("x", "unit-001.xhtml")]);
        Assert.Contains("<meta charset=\"utf-8\"/>", nav, StringComparison.Ordinal);
        Assert.Contains("<title>A &amp; B</title>", nav, StringComparison.Ordinal);
        Assert.Contains("<h1>A &amp; B</h1>", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("Contents", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void XhtmlDocumentCarriesTheCharsetMetaAndTheSharedStylesheet()
    {
        var page = EpubExport.XhtmlDocument("A \"quoted\" & <angled> title", "<p>body</p>");
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE html>\n", page, StringComparison.Ordinal);
        Assert.Contains("<html xmlns=\"http://www.w3.org/1999/xhtml\">", page, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\"/>", page, StringComparison.Ordinal);
        Assert.Contains("<title>A &quot;quoted&quot; &amp; &lt;angled&gt; title</title>", page, StringComparison.Ordinal);
        Assert.Contains("<link rel=\"stylesheet\" type=\"text/css\" href=\"style.css\"/>", page, StringComparison.Ordinal);
        Assert.EndsWith("<body>\n<p>body</p>\n</body>\n</html>", page, StringComparison.Ordinal);
    }

    // MARK: - Body extraction

    [Fact]
    public void BodyExtractionTrimsAndKeepsTheScriptForTheFixerToRemove()
    {
        var document = MarkdownHtml.Document("# Alpha\n\nText.\n", "Alpha", dark: false, export: true);
        var body = EpubExport.BodyHtml(document);
        Assert.StartsWith("<h1 id=\"alpha\">Alpha</h1>", body, StringComparison.Ordinal);
        Assert.EndsWith("<script type=\"module\" src=\"rich/md-init.js\"></script>", body, StringComparison.Ordinal);
        // Stripping the script leaves the newline that preceded it — this is where the blank line
        // before </body> in every content file comes from. Android cuts at the script and has none.
        Assert.EndsWith("<p>Text.</p>\n", EpubExport.XhtmlBody(body), StringComparison.Ordinal);
        var page = EpubExport.XhtmlDocument("Alpha", EpubExport.XhtmlBody(body));
        Assert.Contains("<p>Text.</p>\n\n</body>", page, StringComparison.Ordinal);
    }

    [Fact]
    public void BodyExtractionHandsBackTheInputWhenTheMarkersAreMissingOrReversed()
    {
        Assert.Equal("no body here", EpubExport.BodyHtml("no body here"));
        Assert.Equal("<body", EpubExport.BodyHtml("<body"));
        // A close tag before the open one: the Swift traps on the range, md.vscode guards it.
        Assert.Equal("</body>x<body >", EpubExport.BodyHtml("</body>x<body >"));
        // The LAST close tag wins, so a document that somehow held two yields the whole body.
        Assert.Equal("a</body>b", EpubExport.BodyHtml("<body>a</body>b</body>"));
    }

    // MARK: - The identifier

    [Fact]
    public void EpubIdentifierIsStableForTheSameBook()
    {
        // A fresh random UUID per export made every export a *different* publication: re-exporting
        // after fixing a typo stacked up beside the old file in a reader's library.
        var first = EpubExport.StableIdentifier("My Book");
        var second = EpubExport.StableIdentifier("My Book");
        Assert.Equal(first, second);
        Assert.NotEqual(first, EpubExport.StableIdentifier("Other Book"));

        Assert.StartsWith("urn:uuid:", first, StringComparison.Ordinal);
        var uuid = first["urn:uuid:".Length..];
        var groups = uuid.Split('-');
        Assert.Equal([8, 4, 4, 4, 12], groups.Select(group => group.Length));
        Assert.All(uuid, c => Assert.True(Uri.IsHexDigit(c) || c == '-'));
        Assert.Equal('5', groups[2][0]);                     // version 5 — name-based, not random
        Assert.Contains(groups[3][0], "89ab");               // RFC 4122 variant

        // And it is the value that actually reaches the package document.
        var opf = EpubExport.Opf("My Book", first, Modified, [], []);
        Assert.Contains("<dc:identifier id=\"bookid\">" + first + "</dc:identifier>", opf, StringComparison.Ordinal);
    }

    [Theory]
    // Independently computed with Python's uuid.uuid5(uuid.NAMESPACE_URL, name) — the same RFC 4122
    // v5 construction the four ports share, so this pins the namespace, the SHA-1 and the hex layout
    // against something outside this family. Never "upgrade" the hash: it would orphan every book
    // already in a reader's library.
    [InlineData("My Book", "urn:uuid:9905e6b1-7ffd-5c7f-9cef-1d878db12e9d")]
    [InlineData("Other Book", "urn:uuid:07addc7e-aaf2-52ce-8dff-3367db92aca3")]
    [InlineData("The Book", "urn:uuid:8f60e4b5-88ee-5555-a8df-3fb31302834d")]
    [InlineData("Round Trip", "urn:uuid:b662c790-6033-5d05-a67f-244c745d5c0c")]
    [InlineData("", "urn:uuid:1b4db7eb-4057-5ddf-91e0-36dec72071f5")]
    public void StableIdentifierMatchesTheRfc4122Oracle(string title, string expected) =>
        Assert.Equal(expected, EpubExport.StableIdentifier(title));

    [Fact]
    public void StableIdentifierHashesTheTitleAsUtf8()
    {
        // The encoding RFC 4122 §4.3 names, and the only one that gives a non-ASCII title the same
        // UUID here as on Apple and Android. Built from code points rather than typed, so the
        // assertion cannot quietly depend on how this file happens to be encoded.
        var cyrillic = new string([(char)0x041F, (char)0x0440, (char)0x0438, (char)0x0432, (char)0x0435, (char)0x0442]);
        Assert.Equal("urn:uuid:b9be3511-b3be-5e1d-b703-42c05088d0fa", EpubExport.StableIdentifier(cyrillic));
        // UTF-16 would hash different bytes entirely; this is the value Python's
        // uuid.uuid5(uuid.NAMESPACE_URL, name) gives for the same name.
        Assert.NotEqual(EpubExport.StableIdentifier(cyrillic), EpubExport.StableIdentifier("Privet"));
    }

    // MARK: - Single document

    [Fact]
    public void DocumentEpubTitlePrefersFrontMatterThenFileName()
    {
        // The front-matter `title:` wins when present…
        Assert.Equal("My Essay", EpubExport.DocumentTitle(
            MarkdownParser.FrontMatter("---\ntitle: My Essay\n---\n\n# H"), "notes"));
        // …the key is matched case-insensitively, and the value is trimmed…
        Assert.Equal("Spaced", EpubExport.DocumentTitle(
            [new MetadataField("Title", "  Spaced  ")], "notes"));
        // …an empty value falls through to the file name…
        Assert.Equal("notes", EpubExport.DocumentTitle([new MetadataField("title", "  ")], "notes"));
        // …and with no front matter at all the file name is the title.
        Assert.Equal("My File", EpubExport.DocumentTitle(
            MarkdownParser.FrontMatter("# Just a heading"), "My File"));
        // The source overload is the same rule with the parse folded in.
        Assert.Equal("My Essay", EpubExport.DocumentTitle("---\ntitle: My Essay\n---\n\n# H", "notes"));
        Assert.Equal("My File", EpubExport.DocumentTitle("# Just a heading", "My File"));
    }

    [Fact]
    public void DocumentTitleTrimsFoundationWhitespaceNotDotNetWhitespace()
    {
        // Foundation's .whitespacesAndNewlines has U+200B in it (the frozen tables still call it Zs)
        // and .NET's char.IsWhiteSpace does not — so string.Trim() would keep a title of nothing but
        // zero-width spaces and hand a reader a blank one.
        var zeroWidth = new string((char)0x200B, 3);
        Assert.Equal("notes", EpubExport.DocumentTitle([new MetadataField("title", zeroWidth)], "notes"));
        Assert.Equal("T", EpubExport.DocumentTitle([new MetadataField("title", zeroWidth + "T" + zeroWidth)], "notes"));
        // U+0085 NEL is in the set on both sides, so it trims either way.
        Assert.Equal("notes", EpubExport.DocumentTitle([new MetadataField("title", ((char)0x0085).ToString())], "notes"));
    }

    [Fact]
    public void DocumentEpubIsAValidStoredZipWithMimetypeFirst()
    {
        var entries = DocumentEpubEntries("# Alpha\n\nText.", "Alpha");
        // The pure entries list already puts `mimetype` first…
        Assert.Equal("mimetype", entries[0].Name);
        Assert.Equal("application/epub+zip", Text(entries[0].Data));

        // …and once archived (through the very zip writer the book export uses) it is a valid stored
        // zip whose sniffable magic is exactly what a reader checks before unzipping.
        var archive = ZipWriter.Archive(entries);
        Assert.Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 }, archive[..4]);
        Assert.Equal(0, archive[8]);      // stored method, not deflated
        Assert.Equal(0, archive[9]);
        Assert.Equal("mimetype", Text(archive[30..38]));
        Assert.Equal("application/epub+zip", Text(archive[38..58]));
    }

    [Fact]
    public void DocumentEpubOpfReferencesResolveWithOneRealUnitAndNoTitlePage()
    {
        var entries = DocumentEpubEntries("# Alpha\n\nText.\n\n## Beta\n\nMore.", "Alpha");
        var names = entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var opf = EntryText(entries, "OEBPS/content.opf");

        // Every href the manifest names must resolve to a packed file — nav, stylesheet and the one
        // content unit alike, no dangling reference.
        var hrefs = Regex.Matches(opf, "href=\"([^\"]+)\"", RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value).ToList();
        Assert.Contains("content.xhtml", hrefs);
        foreach (var href in hrefs) Assert.Contains("OEBPS/" + href, names);

        // Exactly one content unit — content.xhtml — and no phantom title page: the book path makes
        // unit-000 an <h1>title</h1> page and starts its nav past it, and that unit must not exist here.
        var contentUnits = names
            .Where(name => name.EndsWith(".xhtml", StringComparison.Ordinal)
                && !string.Equals(name, "OEBPS/nav.xhtml", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(["OEBPS/content.xhtml"], contentUnits);

        // The spine is that one unit, once.
        Assert.Single(Regex.Matches(opf, "<itemref", RegexOptions.CultureInvariant));
        Assert.Contains("<itemref idref=\"u0\"/>", opf, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentEpubNavListsHeadingsWithSlugsMatchingTheContentAnchors()
    {
        // Two headings that would collide on slug, so the dedup ("beta", "beta-1") is exercised on
        // both sides at once.
        const string source = "# Alpha\n\nText.\n\n## Beta\n\nMore.\n\n## Beta\n\nEnd.";
        var entries = DocumentEpubEntries(source, "Alpha");
        var outline = MarkdownParser.Outline(source);
        Assert.Equal(["alpha", "beta", "beta-1"], outline.Select(entry => entry.Slug));

        var nav = EntryText(entries, "OEBPS/nav.xhtml");
        var content = EntryText(entries, "OEBPS/content.xhtml");
        Assert.Contains("epub:type=\"toc\"", nav, StringComparison.Ordinal);

        // Every heading is a nav entry pointing at its anchor inside the one content file, and that
        // anchor really exists — the nav slug and the id are the same rule, so a tap lands on the
        // section rather than on nothing.
        foreach (var slug in outline.Select(entry => entry.Slug))
        {
            Assert.Contains("href=\"content.xhtml#" + slug + "\"", nav, StringComparison.Ordinal);
            Assert.Contains("id=\"" + slug + "\"", content, StringComparison.Ordinal);
        }
        Assert.Equal(3, Regex.Matches(nav, "content\\.xhtml#", RegexOptions.CultureInvariant).Count);
    }

    [Fact]
    public void DocumentEpubOfAHeadinglessDocumentHasAValidNav()
    {
        // A document with no headings has an empty outline, and a toc <nav> whose <ol> holds no <li>
        // is not valid EPUB 3. The fallback is a single entry — the whole document under its title,
        // linking to the content file itself.
        var entries = DocumentEpubEntries("Just a paragraph, no headings.", "Untitled");
        var nav = EntryText(entries, "OEBPS/nav.xhtml");
        Assert.DoesNotContain("<ol>\n\n</ol>", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("<ol></ol>",
            nav.Replace(" ", string.Empty, StringComparison.Ordinal)
               .Replace("\n", string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
        Assert.Contains("<li>", nav, StringComparison.Ordinal);
        Assert.Contains("href=\"content.xhtml\"", nav, StringComparison.Ordinal);
        Assert.Contains(">Untitled</a>", nav, StringComparison.Ordinal);
    }

    [Fact]
    public void DocumentEpubIdentifierIsStableAcrossTwoExports()
    {
        // Two exports of the same document reach the same dc:identifier, so re-exporting after an
        // edit replaces the file in a reader's library instead of stacking a second copy beside it.
        static string Identifier(IReadOnlyList<ZipEntry> entries)
        {
            var opf = EntryText(entries, "OEBPS/content.opf");
            const string open = "<dc:identifier id=\"bookid\">";
            var start = opf.IndexOf(open, StringComparison.Ordinal);
            Assert.True(start >= 0);
            var end = opf.IndexOf("</dc:identifier>", start, StringComparison.Ordinal);
            Assert.True(end >= 0);
            return opf[(start + open.Length)..end];
        }

        var first = DocumentEpubEntries("# Title\n\nBody v1.", "The Same Doc");
        var second = DocumentEpubEntries("# Title\n\nBody v2, edited.", "The Same Doc");
        Assert.Equal(Identifier(first), Identifier(second));
        Assert.Equal(EpubExport.StableIdentifier("The Same Doc"), Identifier(first));
        Assert.NotEqual(Identifier(first), Identifier(DocumentEpubEntries("# X", "Another Doc")));
    }

    // MARK: - The stylesheet

    [Fact]
    public void StylesheetIsTheExportCssPlusThePaddingOverride()
    {
        var css = EpubExport.Stylesheet();
        // The whole light export sheet, verbatim — quirks and all (the body padding and 11pt size
        // are intentional-by-omission on macOS; Android and md.vscode each diverge differently).
        Assert.Contains("padding: 48px 56px;", css, StringComparison.Ordinal);
        Assert.Contains("font-size: 11pt;", css, StringComparison.Ordinal);
        Assert.Contains("html, body { background: #FFFFFF; }", css, StringComparison.Ordinal);
        Assert.Contains(".md-pagebreak { height: 0; margin: 0; break-after: page; }", css, StringComparison.Ordinal);
        Assert.DoesNotContain("<style>", css, StringComparison.Ordinal);
        Assert.DoesNotContain("</style>", css, StringComparison.Ordinal);
        // …then the one override the reader needs.
        Assert.EndsWith("\n/* EPUB: the reader owns pages and margins. */\nbody { padding: 0.5em 5%; }\n", css, StringComparison.Ordinal);
        Assert.Equal(MarkdownHtml.Css(dark: false, export: true) + "\n/* EPUB: the reader owns pages and margins. */\nbody { padding: 0.5em 5%; }\n", css);
    }

    // MARK: - Timestamps and the container

    [Fact]
    public void ModifiedIsUtcSecondPrecisionWithNoFraction()
    {
        // epubcheck rejects fractional seconds in dcterms:modified.
        Assert.Equal("2026-07-24T00:00:00Z", EpubExport.Modified(
            new DateTimeOffset(2026, 7, 24, 0, 0, 0, TimeSpan.Zero)));
        // A non-UTC input is converted, not relabelled.
        Assert.Equal("2026-07-23T22:30:45Z", EpubExport.Modified(
            new DateTimeOffset(2026, 7, 24, 0, 30, 45, 500, TimeSpan.FromHours(2))));
        var now = EpubExport.ModifiedNow();
        Assert.Equal(20, now.Length);
        Assert.EndsWith("Z", now, StringComparison.Ordinal);
        Assert.DoesNotContain(".", now, StringComparison.Ordinal);
    }

    [Fact]
    public void ContainerXmlPointsAtThePackageDocument()
    {
        Assert.Equal(EpubExport.ContainerXml, string.Join("\n",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>",
            "<container version=\"1.0\" xmlns=\"urn:oasis:names:tc:opendocument:xmlns:container\">",
            "<rootfiles>",
            "<rootfile full-path=\"OEBPS/content.opf\" media-type=\"application/oebps-package+xml\"/>",
            "</rootfiles>",
            "</container>"));
        Assert.DoesNotContain("\n\n", EpubExport.ContainerXml, StringComparison.Ordinal);
    }

    // MARK: - Planning and the snapshot contract

    [Fact]
    public void PlanDocumentDescribesTheOnePageTheAppMustRender()
    {
        var plan = EpubExport.PlanDocument("$x^2$ and\n\n```mermaid\ngraph TD\n```\n", "Doc");
        var unit = Assert.Single(plan.Units);
        Assert.Equal(0, unit.Index);
        Assert.Equal("content.xhtml", unit.File);
        Assert.False(unit.IsHeadingPage);
        // The page the app loads is the very HTML the writer produces for a paper export.
        Assert.Equal(MarkdownHtml.Document("$x^2$ and\n\n```mermaid\ngraph TD\n```\n", "Doc", dark: false, export: true), unit.Document);
        Assert.Equal([true, false], unit.RichElements.Select(element => element.IsMath));
        Assert.Equal(EpubExport.StableIdentifier("Doc"), plan.Identifier);
        Assert.Single(plan.RichUnits);
    }

    [Fact]
    public void PlanDocumentOfAPlainDocumentAsksForNoRendering()
    {
        var plan = EpubExport.PlanDocument("# Alpha\n\nText.\n", "Alpha");
        Assert.Empty(Assert.Single(plan.Units).RichElements);
        Assert.Empty(plan.RichUnits);
    }

    [Fact]
    public void AMismatchedSnapshotCountIsRefusedByName()
    {
        var plan = EpubExport.PlanDocument("$a$ and $b$\n", "Doc");
        Assert.Equal(2, plan.Units[0].RichElements.Count);

        // One short: Swift's `zip` would silently leave the second formula as TeX source.
        var thrown = Assert.Throws<EpubSnapshotCountException>(() =>
            EpubExport.BuildDocument("$a$ and $b$\n", "Doc", [new RichSnapshot(Png(1), 40)], Modified));
        Assert.Equal(0, thrown.UnitIndex);
        Assert.Equal("Doc", thrown.UnitTitle);
        Assert.Equal(2, thrown.Expected);
        Assert.Equal(1, thrown.Actual);
        Assert.Contains("2 rich element(s)", thrown.Message, StringComparison.Ordinal);

        // None at all, for a document that needs two.
        Assert.Throws<EpubSnapshotCountException>(() => EpubExport.BuildDocument("$a$ and $b$\n", "Doc", null, Modified));

        // And one too many, for a document that needs none.
        var extra = Assert.Throws<EpubSnapshotCountException>(() =>
            EpubExport.BuildDocument("Plain.\n", "Doc", [new RichSnapshot(Png(1), 40)], Modified));
        Assert.Equal(0, extra.Expected);
        Assert.Equal(1, extra.Actual);
    }

    [Fact]
    public void SnapshotsBecomeImagesInTheArchiveAndInTheManifest()
    {
        const string source = "Before $a^2$ after.\n\n```mermaid\ngraph TD\n```\n";
        var bytes = EpubExport.BuildDocument(source, "Doc",
            [new RichSnapshot(Png(1), 84.5), new RichSnapshot(Png(2), 300)], Modified);
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);

        var content = Text(Entry(entries, "OEBPS/content.xhtml")!);
        Assert.Contains("<img src=\"images/unit-000-rich-0.png\" alt=\"formula\" width=\"85\"/>", content, StringComparison.Ordinal);
        Assert.Contains("<img src=\"images/unit-000-rich-1.png\" alt=\"diagram\" width=\"300\"/>", content, StringComparison.Ordinal);
        Assert.DoesNotContain("md-mathi", content, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"mermaid\"", content, StringComparison.Ordinal);

        Assert.Equal(Png(1), Entry(entries, "OEBPS/images/unit-000-rich-0.png"));
        Assert.Equal(Png(2), Entry(entries, "OEBPS/images/unit-000-rich-1.png"));
        var opf = Text(Entry(entries, "OEBPS/content.opf")!);
        Assert.Contains("<item id=\"i0\" href=\"images/unit-000-rich-0.png\" media-type=\"image/png\"/>", opf, StringComparison.Ordinal);
        Assert.Contains("<item id=\"i1\" href=\"images/unit-000-rich-1.png\" media-type=\"image/png\"/>", opf, StringComparison.Ordinal);
    }

    [Fact]
    public void APlotStaysVectorAndFlagsItsContentDocument()
    {
        // The one rich block that is not photographed: it is already an <svg>, so it goes in as
        // markup — and the content document that holds it must carry properties="svg" (OPF-014).
        var bytes = EpubExport.BuildDocument("```plot\nsin(x)\n```\n", "Plotted", null, Modified);
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);
        var content = Text(Entry(entries, "OEBPS/content.xhtml")!);
        Assert.Contains("<div class=\"plot\">", content, StringComparison.Ordinal);
        Assert.Contains("<svg", content, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", content, StringComparison.Ordinal);
        var opf = Text(Entry(entries, "OEBPS/content.opf")!);
        Assert.Contains("<item id=\"u0\" href=\"content.xhtml\" media-type=\"application/xhtml+xml\" properties=\"svg\"/>", opf, StringComparison.Ordinal);
        // A plot-only document never has to open a renderer at all.
        Assert.Empty(EpubExport.PlanDocument("```plot\nsin(x)\n```\n", "Plotted").RichUnits);
    }

    // MARK: - Whole book

    private static StructuredBook SampleBook() => new(
        "The Book",
        [new BookUnit("Preface", "Opening words.")],
        [
            new BookSection("One", [new BookUnit("First", "# Inner\n\nA."), new BookUnit("Second", "B.")]),
            new BookSection("Two", [new BookUnit("Third", "C.")]),
        ]);

    [Fact]
    public void BookUnitsAreTheTitlePageThenRootArticlesThenChapters()
    {
        var plan = EpubExport.PlanBook(SampleBook());
        Assert.Equal(
            ["unit-000.xhtml", "unit-001.xhtml", "unit-002.xhtml", "unit-003.xhtml", "unit-004.xhtml", "unit-005.xhtml", "unit-006.xhtml"],
            plan.Units.Select(unit => unit.File));
        Assert.Equal(
            ["The Book", "Preface", "One", "First", "Second", "Two", "Third"],
            plan.Units.Select(unit => unit.Title));
        // The title page and the two chapter openers are heading pages — nothing to render.
        Assert.Equal([true, false, true, false, false, true, false], plan.Units.Select(unit => unit.IsHeadingPage));
        Assert.Equal("<h1>The Book</h1>", plan.Units[0].Body);
        Assert.Empty(plan.RichUnits);
    }

    [Fact]
    public void BookNavListsRootArticlesThenChaptersWithTheirArticlesNested()
    {
        var bytes = EpubExport.Build(SampleBook(), null, Modified);
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);
        var nav = Text(Entry(entries, "OEBPS/nav.xhtml")!);
        Assert.Contains("<h1>The Book</h1>", nav, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"unit-001.xhtml\">Preface</a></li>", nav, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"unit-002.xhtml\">One</a>\n<ol>\n"
            + "<li><a href=\"unit-003.xhtml\">First</a></li>\n"
            + "<li><a href=\"unit-004.xhtml\">Second</a></li>\n</ol>\n</li>", nav, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"unit-005.xhtml\">Two</a>\n<ol>\n"
            + "<li><a href=\"unit-006.xhtml\">Third</a></li>\n</ol>\n</li>", nav, StringComparison.Ordinal);
        // The title page is a spine entry, never a nav row.
        Assert.DoesNotContain("unit-000.xhtml", nav, StringComparison.Ordinal);

        var opf = Text(Entry(entries, "OEBPS/content.opf")!);
        Assert.Contains("<dc:title>The Book</dc:title>", opf, StringComparison.Ordinal);
        Assert.Contains("<dc:identifier id=\"bookid\">" + EpubExport.StableIdentifier("The Book") + "</dc:identifier>", opf, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Matches(opf, "<itemref", RegexOptions.CultureInvariant).Count);
        Assert.Contains("<itemref idref=\"u0\"/>\n<itemref idref=\"u1\"/>", opf, StringComparison.Ordinal);
    }

    [Fact]
    public void BookArchiveHoldsEveryUnitAndEveryManifestHrefResolves()
    {
        var bytes = EpubExport.Build(SampleBook(), null, Modified);
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);
        Assert.Equal(
            ["mimetype", "META-INF/container.xml", "OEBPS/content.opf", "OEBPS/nav.xhtml", "OEBPS/style.css",
             "OEBPS/unit-000.xhtml", "OEBPS/unit-001.xhtml", "OEBPS/unit-002.xhtml", "OEBPS/unit-003.xhtml",
             "OEBPS/unit-004.xhtml", "OEBPS/unit-005.xhtml", "OEBPS/unit-006.xhtml"],
            entries.Select(entry => entry.Name));
        var names = entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        var opf = Text(Entry(entries, "OEBPS/content.opf")!);
        foreach (Match match in Regex.Matches(opf, "href=\"([^\"]+)\"", RegexOptions.CultureInvariant))
        {
            Assert.Contains("OEBPS/" + match.Groups[1].Value, names);
        }
        // The title page really is a level-1 heading page under the book's name.
        Assert.Contains("<body>\n<h1>The Book</h1>\n</body>", Text(Entry(entries, "OEBPS/unit-000.xhtml")!), StringComparison.Ordinal);
    }

    [Fact]
    public void BookSnapshotsAreKeyedByUnitIndex()
    {
        var book = new StructuredBook("Rich Book", [new BookUnit("Plain", "Nothing here.")],
            [new BookSection("Ch", [new BookUnit("Mathy", "Text $a^2$ text.\n")])]);
        var plan = EpubExport.PlanBook(book);
        // unit-000 title page, unit-001 Plain, unit-002 the chapter opener, unit-003 Mathy.
        var rich = Assert.Single(plan.RichUnits);
        Assert.Equal(3, rich.Index);

        var bytes = EpubExport.Build(book,
            new Dictionary<int, IReadOnlyList<RichSnapshot>> { [3] = [new RichSnapshot(Png(7), 40)] },
            Modified);
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);
        Assert.Equal(Png(7), Entry(entries, "OEBPS/images/unit-003-rich-0.png"));
        Assert.Contains("<img src=\"images/unit-003-rich-0.png\" alt=\"formula\" width=\"40\"/>",
            Text(Entry(entries, "OEBPS/unit-003.xhtml")!), StringComparison.Ordinal);
        // Keyed by unit index, so the photograph lands on the article that needs it and nowhere else.
        Assert.DoesNotContain("<img", Text(Entry(entries, "OEBPS/unit-001.xhtml")!), StringComparison.Ordinal);
    }

    [Fact]
    public void ABookWithNothingButATitlePageGetsTheSwiftsEmptyNav()
    {
        // KNOWN, deliberate: the macOS book nav has no fallback for a book with neither articles nor
        // chapters, so its <nav> has no <ol> at all — which is not valid EPUB 3. Android adds the
        // title page as the single entry there; the single-document path (above) has the fallback on
        // every port. Byte parity with macOS wins here, and this test is the flag on it.
        var nav = Text(Entry(ZipReader.Entries(EpubExport.Build(new StructuredBook("Bare", [], []), null, Modified))!,
            "OEBPS/nav.xhtml")!);
        Assert.Contains("<nav epub:type=\"toc\">\n<h1>Bare</h1></nav>", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("<ol>", nav, StringComparison.Ordinal);
    }

    // MARK: - The finished archive

    [Fact]
    public void EpubRoundTripsThroughAZipReader()
    {
        // The macOS PDFExportTests shell out to /usr/bin/unzip; the portable equivalent is to open
        // the bytes with the framework's own reader and read the mimetype back out.
        var book = new StructuredBook("Round Trip",
            [new BookUnit("Intro", "# Intro\n\nHello *there*.")],
            [new BookSection("One", [new BookUnit("First", "Body text.")])]);
        var bytes = EpubExport.Build(book, null, Modified);

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal("mimetype", archive.Entries[0].FullName);
        using var reader = new StreamReader(archive.Entries[0].Open(), new UTF8Encoding(false));
        Assert.Equal("application/epub+zip", reader.ReadToEnd());
        Assert.Contains(archive.Entries, entry => string.Equals(entry.FullName, "OEBPS/content.opf", StringComparison.Ordinal));
        // Every member is stored, so every entry's compressed length equals its real length.
        Assert.All(archive.Entries, entry => Assert.Equal(entry.Length, entry.CompressedLength));

        // …and the app's own reader agrees, byte for byte.
        var entries = ZipReader.Entries(bytes);
        Assert.NotNull(entries);
        Assert.Equal(Utf8("application/epub+zip"), entries[0].Data);
    }

    [Theory]
    [InlineData("# Alpha\n\nText.\n\n- one\n- two\n\n---\n\n![pic](x.png)\n\n| a | b |\n| - | - |\n| 1 | 2 |\n")]
    [InlineData("```plot\nsin(x)\n```\n\n> A quote with `code` & \"quotes\" <angles>.\n")]
    [InlineData("Just a paragraph, no headings.")]
    public void EveryXmlFileInADocumentArchiveIsWellFormed(string source)
    {
        // The whole point of the XHTML fixer: no browser here, but a conforming XML parser is the
        // same gate EPUBCheck applies first. Named entities, unclosed <br>/<img> or a stray <script>
        // would all fail this.
        AssertArchiveXmlIsWellFormed(EpubExport.BuildDocument(source, "Doc", null, Modified));
    }

    [Fact]
    public void EveryXmlFileInABookArchiveIsWellFormedIncludingSnapshottedUnits()
    {
        var book = new StructuredBook("A & B \"Book\"",
            [new BookUnit("Pre<face>", "Words & more.\n\n![pic](x.png)\n")],
            [new BookSection("Ch & Co", [new BookUnit("Rich", "Text $a^2$ text.\n\n```mermaid\ngraph TD\n```\n")])]);
        var plan = EpubExport.PlanBook(book);
        var rich = Assert.Single(plan.RichUnits);
        var bytes = EpubExport.Build(book,
            new Dictionary<int, IReadOnlyList<RichSnapshot>>
            {
                [rich.Index] = [new RichSnapshot(Png(1), 40), new RichSnapshot(Png(2), 200)],
            },
            Modified);
        AssertArchiveXmlIsWellFormed(bytes);
    }

    private static void AssertArchiveXmlIsWellFormed(byte[] archive)
    {
        var entries = ZipReader.Entries(archive);
        Assert.NotNull(entries);
        var settings = new XmlReaderSettings
        {
            // <!DOCTYPE html> with no subset: skip it rather than resolve anything off the machine.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
        };
        var checkedAny = false;
        foreach (var entry in entries)
        {
            if (!entry.Name.EndsWith(".xhtml", StringComparison.Ordinal)
                && !entry.Name.EndsWith(".xml", StringComparison.Ordinal)
                && !entry.Name.EndsWith(".opf", StringComparison.Ordinal)) continue;
            checkedAny = true;
            using var stream = new MemoryStream(entry.Data);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read()) { }   // throws XmlException on anything malformed
        }
        Assert.True(checkedAny);
    }
}
