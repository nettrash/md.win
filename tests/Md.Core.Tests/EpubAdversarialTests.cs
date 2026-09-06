using System.Text;
using System.Xml.Linq;
using Md.Core.Book;
using Md.Core.Export;
using Md.Core.Markdown;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial cover for <see cref="EpubExport"/>: the structural invariants of the OCF container
/// and the EPUB 3 package that <see cref="EpubExportTests"/> asserts on by substring but never
/// links up, plus the two places md.win's marker scan deliberately parts company with macOS's
/// <c>NSRegularExpression</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every expectation here comes from the EPUB 3 / OCF specification or from a run of the real
/// Swift <c>EPUBExport</c> pure half (compiled out of
/// <c>md.macOS/md/DocumentExport.swift</c> and driven over the same vectors), never from what this
/// C# happens to print. The Swift values quoted in the comments are that run's output.
/// </para>
/// <para>
/// Each of the first four tests was written to kill a surviving mutant: the OPF's
/// <c>unique-identifier</c> pointing at nothing, <c>nav.xhtml</c> leaving the XHTML namespace, the
/// navigation document's stylesheet link dangling, and the nav manifest item claiming
/// <c>text/html</c>. All four produce a file every reader rejects and every earlier assertion still
/// passed.
/// </para>
/// </remarks>
public class EpubAdversarialTests
{
    private const string Modified = "2026-07-24T00:00:00Z";
    private const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string OpfNamespace = "http://www.idpf.org/2007/opf";
    private const string DcNamespace = "http://purl.org/dc/elements/1.1/";
    private const string OpsNamespace = "http://www.idpf.org/2007/ops";

    private static byte[] Png(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, marker];

    private static string Text(byte[] bytes) => new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);

    private static StructuredBook SampleBook() => new(
        "A & B \"Book\"",
        [new BookUnit("Preface", "Opening words.\n")],
        [
            new BookSection("One", [
                new BookUnit("First", "# Inner\n\nText $a^2$ text.\n\n```mermaid\ngraph TD\n```\n"),
                new BookUnit("Second", "B.\n"),
            ]),
            new BookSection("Two", [new BookUnit("Third", "```plot\nsin(x)\n```\n")]),
        ]);

    /// <summary>The sample book, photographed exactly as the app must photograph it.</summary>
    private static byte[] SampleBookArchive()
    {
        var plan = EpubExport.PlanBook(SampleBook());
        var snapshots = plan.RichUnits.ToDictionary(
            unit => unit.Index,
            unit => (IReadOnlyList<RichSnapshot>)unit.RichElements
                .Select((_, index) => new RichSnapshot(Png((byte)index), 120 + index)).ToList());
        return EpubExport.Assemble(plan, snapshots, Modified);
    }

    private static IReadOnlyList<ZipEntry> Read(byte[] archive)
    {
        var entries = ZipReader.Entries(archive);
        Assert.NotNull(entries);
        return entries;
    }

    private static XDocument Parse(IReadOnlyList<ZipEntry> entries, string name)
    {
        var entry = entries.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
        Assert.NotNull(entry);
        // LoadOptions.None: the <!DOCTYPE html> has no internal subset, and nothing is fetched.
        return XDocument.Parse(Text(entry.Data), LoadOptions.None);
    }

    // MARK: - The package document really is a package

    [Fact]
    public void ThePackageUniqueIdentifierNamesADcIdentifierThatIsReallyThere()
    {
        // EPUB 3 §3.4.3: package/@unique-identifier MUST be the id of a dc:identifier in the
        // metadata. A package whose attribute names nothing is rejected outright (EPUBCheck
        // RSC-005), and no substring assertion notices — both halves can be spelled correctly and
        // still not refer to each other. Swift writes `unique-identifier="bookid"` on the package
        // and `<dc:identifier id="bookid">` in the metadata; this pins the LINK, not the spelling.
        foreach (var archive in new[] { SampleBookArchive(), EpubExport.BuildDocument("# Alpha\n\nText.\n", "Doc", null, Modified) })
        {
            var entries = Read(archive);
            var opf = Parse(entries, "OEBPS/content.opf");
            var package = opf.Root;
            Assert.NotNull(package);
            Assert.Equal(XName.Get("package", OpfNamespace), package.Name);
            Assert.Equal("3.0", package.Attribute("version")?.Value);

            var unique = package.Attribute("unique-identifier")?.Value;
            Assert.False(string.IsNullOrEmpty(unique));
            var identifiers = package
                .Elements(XName.Get("metadata", OpfNamespace))
                .Elements(XName.Get("identifier", DcNamespace))
                .Where(element => string.Equals(element.Attribute("id")?.Value, unique, StringComparison.Ordinal))
                .ToList();
            var identifier = Assert.Single(identifiers);
            // …and the value it resolves to is the title-derived one, not a fresh UUID.
            Assert.StartsWith("urn:uuid:", identifier.Value, StringComparison.Ordinal);
            var title = Assert.Single(package
                .Elements(XName.Get("metadata", OpfNamespace))
                .Elements(XName.Get("title", DcNamespace)));
            Assert.Equal(EpubExport.StableIdentifier(title.Value), identifier.Value);
        }
    }

    [Fact]
    public void EveryManifestItemIsReferencedByTheSpineOrIsAReservedResource()
    {
        // Two directions at once: every spine itemref resolves to a manifest item (a dangling
        // idref is EPUBCheck RSC-005), and every content document in the manifest carries the
        // media type a reader dispatches on. The nav document in particular must be
        // application/xhtml+xml with properties="nav" — calling it text/html leaves the package
        // parseable, the hrefs resolvable, and the book unopenable.
        var entries = Read(SampleBookArchive());
        var opf = Parse(entries, "OEBPS/content.opf");
        var manifest = opf.Root!.Element(XName.Get("manifest", OpfNamespace))!;
        var items = manifest.Elements(XName.Get("item", OpfNamespace)).ToList();
        var byId = items.ToDictionary(item => item.Attribute("id")!.Value, StringComparer.Ordinal);

        var navItems = items
            .Where(item => (item.Attribute("properties")?.Value ?? string.Empty).Split(' ').Contains("nav"))
            .ToList();
        var nav = Assert.Single(navItems);
        Assert.Equal("nav.xhtml", nav.Attribute("href")!.Value);
        Assert.Equal("application/xhtml+xml", nav.Attribute("media-type")!.Value);

        foreach (var item in items)
        {
            var href = item.Attribute("href")!.Value;
            var expected = href.EndsWith(".xhtml", StringComparison.Ordinal) ? "application/xhtml+xml"
                : href.EndsWith(".css", StringComparison.Ordinal) ? "text/css"
                : href.EndsWith(".png", StringComparison.Ordinal) ? "image/png"
                : null;
            Assert.Equal(expected, item.Attribute("media-type")!.Value);
        }

        var spine = opf.Root!.Element(XName.Get("spine", OpfNamespace))!;
        var refs = spine.Elements(XName.Get("itemref", OpfNamespace)).Select(x => x.Attribute("idref")!.Value).ToList();
        Assert.NotEmpty(refs);
        foreach (var idref in refs)
        {
            Assert.True(byId.ContainsKey(idref), "spine idref " + idref + " has no manifest item");
            Assert.Equal("application/xhtml+xml", byId[idref].Attribute("media-type")!.Value);
        }
        // The nav document is the one manifest item that is deliberately NOT in the spine on macOS.
        Assert.DoesNotContain(nav.Attribute("id")!.Value, refs);
        // Every other content document is, in reading order, exactly once.
        Assert.Equal(refs, refs.Distinct().ToList());
    }

    // MARK: - Every packed XHTML is a content document a reader can open

    [Fact]
    public void EveryPackedXhtmlSitsInTheXhtmlNamespaceAndTheNavDeclaresTheOpsPrefix()
    {
        // Swift's literals, checked against the compiled Swift:
        //   xhtmlDocument: <html xmlns="http://www.w3.org/1999/xhtml">
        //   nav:           <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops">
        // An XHTML file in the wrong namespace still parses as XML — the suite's well-formedness
        // gate cannot see it — but it is not an EPUB content document.
        foreach (var archive in new[] { SampleBookArchive(), EpubExport.BuildDocument("# Alpha\n\nText.\n", "Doc", null, Modified) })
        {
            var entries = Read(archive);
            var pages = entries.Where(entry => entry.Name.EndsWith(".xhtml", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(pages);
            foreach (var page in pages)
            {
                var doc = XDocument.Parse(Text(page.Data), LoadOptions.None);
                Assert.Equal(XName.Get("html", XhtmlNamespace), doc.Root!.Name);
                Assert.Single(doc.Root.Elements(XName.Get("head", XhtmlNamespace)));
                Assert.Single(doc.Root.Elements(XName.Get("body", XhtmlNamespace)));
            }
            // The toc lives in the OPS namespace, on a <nav> that is really a nav element.
            var navDoc = Parse(entries, "OEBPS/nav.xhtml");
            var toc = Assert.Single(navDoc.Descendants(XName.Get("nav", XhtmlNamespace)));
            Assert.Equal("toc", toc.Attribute(XName.Get("type", OpsNamespace))?.Value);
        }
    }

    [Fact]
    public void EveryLocalReferenceInEveryPackedPageResolvesToAPackedMember()
    {
        // The stylesheet link, the nav's own links and the snapshot images all point at files this
        // export writes; a typo in any of them is EPUBCheck RSC-007 and a reader showing an
        // unstyled page or a broken image. Author-supplied image sources are deliberately out of
        // scope — macOS packs only the snapshot PNGs too, so `![pic](local.png)` dangles on every
        // port — hence a corpus that references nothing of its own.
        var archives = new[]
        {
            SampleBookArchive(),
            EpubExport.BuildDocument("# Alpha\n\nText.\n\n## Beta\n\nMore.\n", "Doc", null, Modified),
            EpubExport.BuildDocument("Just a paragraph, no headings.", "Untitled", null, Modified),
        };
        foreach (var archive in archives)
        {
            var entries = Read(archive);
            var names = entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
            var seen = 0;
            foreach (var page in entries.Where(entry => entry.Name.EndsWith(".xhtml", StringComparison.Ordinal)))
            {
                var doc = XDocument.Parse(Text(page.Data), LoadOptions.None);
                foreach (var attribute in doc.Descendants().Attributes()
                    .Where(attribute => attribute.Name.LocalName is "href" or "src"))
                {
                    var value = attribute.Value;
                    if (value.Length == 0 || value[0] == '#' || value.Contains("://", StringComparison.Ordinal)) continue;
                    seen++;
                    var target = "OEBPS/" + value.Split('#')[0];
                    Assert.True(names.Contains(target),
                        page.Name + " references " + value + ", which is not packed");
                }
            }
            // Every page links the stylesheet, so this can never be a vacuous pass.
            Assert.True(seen >= entries.Count(entry => entry.Name.EndsWith(".xhtml", StringComparison.Ordinal)));
            // …and the anchors the nav points into really exist in the file it names.
            var navDoc = Parse(entries, "OEBPS/nav.xhtml");
            foreach (var href in navDoc.Descendants(XName.Get("a", XhtmlNamespace))
                .Select(anchor => anchor.Attribute("href")!.Value)
                .Where(href => href.Contains('#', StringComparison.Ordinal)))
            {
                var parts = href.Split('#');
                var page = Parse(entries, "OEBPS/" + parts[0]);
                Assert.Contains(page.Descendants(), element =>
                    string.Equals(element.Attribute("id")?.Value, parts[1], StringComparison.Ordinal));
            }
        }
    }

    // MARK: - The two decided partings from the macOS regex

    [Fact]
    public void AnUnclosedRichContainerStopsTheScanWhereTheSwiftRegexWouldSkipPastIt()
    {
        // DECIDED DIVERGENCE, from Kotlin's and TypeScript's marker scan. Measured on the compiled
        // Swift pure half over these exact vectors:
        //   in  "<span class=\"md-mathi\">unclosed <pre class=\"mermaid\">graph</pre>"
        //   Swift richElements -> one match at 32 length 32; replacingRichElements(["ONLY"])
        //         -> "<span class=\"md-mathi\">unclosed ONLY"
        //   md.win           -> no elements; the markup is handed back untouched.
        // The writer closes every container it opens, so this shape cannot be produced — and if it
        // ever were, `containsRichContent` (the wider test on all four ports) still says yes, the
        // app still photographs what the DOM reports, and the count check refuses the export rather
        // than shipping a book whose pictures have slid onto the wrong elements.
        const string unclosedFirst = "<span class=\"md-mathi\">unclosed <pre class=\"mermaid\">graph</pre>";
        Assert.Empty(EpubExport.RichElements(unclosedFirst));
        Assert.Equal(unclosedFirst, EpubExport.ReplacingRichElements(unclosedFirst, ["ONLY"]));
        Assert.True(EpubExport.ContainsRichContent(unclosedFirst));

        const string unclosedPre = "<pre class=\"mermaid\">unclosed <span class=\"md-mathi\">a</span>";
        Assert.Empty(EpubExport.RichElements(unclosedPre));
        Assert.Equal(unclosedPre, EpubExport.ReplacingRichElements(unclosedPre, ["ONLY"]));

        // A closed container after a closed container is unaffected: the halt is the unclosed case
        // alone, not a one-element cap.
        const string two = "<pre class=\"mermaid\">a</pre><span class=\"md-mathi\">b</span>";
        Assert.Equal(2, EpubExport.RichElements(two).Count);
    }

    [Fact]
    public void TheWriterStillEmitsTheBareContainersTheMarkerTableIsSpelledFor()
    {
        // Only the Graphviz marker is a PREFIX (`<div class="graphviz"`, no `>`), because the
        // writer names the layout program in the same tag. The other five markers are whole tags,
        // so an attribute added to any of them would make it invisible to the scan while macOS's
        // `[^>]*>` would still match it — measured on the compiled Swift:
        //   "<span class=\"md-mathi\" data-x=\"a>b\">m</span>" -> Swift one match at 0 length 44,
        //   md.win none. That is the exact failure the Swift comment warns about for Graphviz
        //   ("leaving every DOT diagram in the book as raw source, and shifting the snapshots onto
        //   the wrong elements"), so this test guards the assumption instead of the symptom: the
        // day the writer adds an attribute to a math, Mermaid or PlantUML container, it fires here
        // and the marker for that container has to become a prefix too.
        var samples = new (string Source, string Opener)[]
        {
            ("Text $a^2$ text.\n", "<span class=\"md-mathi\">"),
            ("Text $$a^2$$ text.\n", "<span class=\"md-mathd\">"),
            ("```latex\na^2\n```\n", "<div class=\"md-mathd\">"),
            ("```mermaid\ngraph TD\n```\n", "<pre class=\"mermaid\">"),
            ("```plantuml\n@startuml\n@enduml\n```\n", "<div class=\"plantuml\">"),
        };
        foreach (var (source, opener) in samples)
        {
            var body = EpubExport.BodyHtml(MarkdownHtml.Document(source, "t", dark: false, export: true));
            Assert.Contains(opener, body, StringComparison.Ordinal);
            var element = Assert.Single(EpubExport.RichElements(body));
            Assert.StartsWith(opener, body[element.Start..element.End], StringComparison.Ordinal);
        }
        // Graphviz is the one that does carry an extra attribute, and its marker is the prefix.
        var dot = EpubExport.BodyHtml(MarkdownHtml.Document("```dot\ndigraph { a -> b }\n```\n", "t", dark: false, export: true));
        Assert.Contains("<div class=\"graphviz\" data-engine=\"dot\">", dot, StringComparison.Ordinal);
        Assert.Single(EpubExport.RichElements(dot));
    }

    [Fact]
    public void TitlesAreEscapedForTheFourCharactersMacosEscapesAndTheApostropheIsLeftAlone()
    {
        // export.md §6.8 states the rule ("& → &amp;  < → &lt;  > → &gt;  \" → &quot;   (never ')")
        // and no test pinned the negative half. It is reachable on the first apostrophe in a book
        // title, and it is not cosmetic: XML predefines &apos;, so "fixing" it would still parse
        // everywhere and silently break byte parity with the two Apple editions and with md.vscode.
        // Measured on the compiled Swift over the same title:
        //   xhtmlDocument(title: "a'b", …) -> "<title>a'b</title>"
        //   nav(title: "a'b", …)           -> "<title>a'b</title>" and "<h1>a'b</h1>"
        const string tricky = "Alice's & Bob's \"<Book>\"";
        const string escaped = "Alice's &amp; Bob's &quot;&lt;Book&gt;&quot;";

        var page = EpubExport.XhtmlDocument(tricky, "<p>x</p>");
        Assert.Contains("<title>" + escaped + "</title>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("&apos;", page, StringComparison.Ordinal);
        Assert.DoesNotContain("&#39;", page, StringComparison.Ordinal);

        var nav = EpubExport.Nav(tricky, [new NavEntry(tricky, "unit-001.xhtml")]);
        Assert.Contains("<h1>" + escaped + "</h1>", nav, StringComparison.Ordinal);
        Assert.Contains(">" + escaped + "</a>", nav, StringComparison.Ordinal);
        Assert.DoesNotContain("&apos;", nav, StringComparison.Ordinal);

        // The escaper runs & first, so an already-escaped entity is escaped again rather than
        // passed through — a title that literally reads "&amp;" must survive a round trip.
        Assert.Contains("<dc:title>&amp;amp;</dc:title>",
            EpubExport.Opf("&amp;", "urn:uuid:TEST", Modified, [], []), StringComparison.Ordinal);

        // And it holds through a real archive, where the title reaches four separate files.
        var entries = Read(EpubExport.BuildDocument("# H\n", tricky, null, Modified));
        foreach (var name in new[] { "OEBPS/content.opf", "OEBPS/nav.xhtml", "OEBPS/content.xhtml" })
        {
            var text = Text(entries.First(entry => string.Equals(entry.Name, name, StringComparison.Ordinal)).Data);
            Assert.Contains(escaped, text, StringComparison.Ordinal);
            Assert.DoesNotContain("&apos;", text, StringComparison.Ordinal);
        }
    }

    // MARK: - The container's byte layout, read back

    [Fact]
    public void TheArchiveIsTheStoredOcfLayoutFromItsFirstByteToItsEndRecord()
    {
        // The OCF rule a reader sniffs before it unzips anything: `mimetype` is the first member,
        // stored, no extra field, so its payload lands at a fixed offset. Then every other member
        // is stored too (macOS never deflates; Android deflates all but the mimetype) and every
        // local header carries the fixed 1980-01-01 date that makes the archive a pure function of
        // its payloads.
        var archive = SampleBookArchive();
        var entries = Read(archive);

        Assert.Equal("mimetype", entries[0].Name);
        Assert.Equal([0x50, 0x4B, 0x03, 0x04], archive[..4]);
        Assert.Equal(0, BitConverter.ToUInt16(archive, 6));    // flags: bit 11 (UTF-8 names) clear
        Assert.Equal(0, BitConverter.ToUInt16(archive, 8));    // method: stored
        Assert.Equal("mimetype", Text(archive[30..38]));
        Assert.Equal("application/epub+zip", Text(archive[38..58]));
        Assert.Equal(EpubExport.Mimetype, Text(entries[0].Data));

        // Walk every local header the central directory names and check the same three fields.
        var end = archive.Length - 22;
        Assert.Equal([0x50, 0x4B, 0x05, 0x06], archive[end..(end + 4)]);
        int count = BitConverter.ToUInt16(archive, end + 10);
        Assert.Equal(entries.Count, count);
        var directory = (int)BitConverter.ToUInt32(archive, end + 16);
        var cursor = directory;
        for (var index = 0; index < count; index++)
        {
            Assert.Equal([0x50, 0x4B, 0x01, 0x02], archive[cursor..(cursor + 4)]);
            Assert.Equal(0, BitConverter.ToUInt16(archive, cursor + 10));           // method: stored
            Assert.Equal(0, BitConverter.ToUInt16(archive, cursor + 12));           // DOS time
            Assert.Equal(0x0021, BitConverter.ToUInt16(archive, cursor + 14));      // DOS date 1980-01-01
            var compressed = BitConverter.ToUInt32(archive, cursor + 20);
            var uncompressed = BitConverter.ToUInt32(archive, cursor + 24);
            Assert.Equal(uncompressed, compressed);                                  // stored: same size
            var nameLength = BitConverter.ToUInt16(archive, cursor + 28);
            var local = (int)BitConverter.ToUInt32(archive, cursor + 42);
            var name = Text(archive[(cursor + 46)..(cursor + 46 + nameLength)]);
            Assert.Equal(entries[index].Name, name);
            Assert.Equal((uint)entries[index].Data.Length, uncompressed);
            // The central record's offset really points at that member's local header.
            Assert.Equal([0x50, 0x4B, 0x03, 0x04], archive[local..(local + 4)]);
            Assert.Equal(name, Text(archive[(local + 30)..(local + 30 + nameLength)]));
            cursor += 46 + nameLength
                + BitConverter.ToUInt16(archive, cursor + 30)   // extra
                + BitConverter.ToUInt16(archive, cursor + 32);  // comment
        }
        Assert.Equal(directory + (int)BitConverter.ToUInt32(archive, end + 12), cursor);

        // Two builds of the same book are byte-identical: nothing in here is a wall clock.
        Assert.Equal(archive, SampleBookArchive());
    }
}
