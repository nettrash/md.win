<!-- The Md.Core API contract the app layer codes against. Part A/A2 = shipped; Part B = names dictated for the modules written after it. Keep in sync with the code. -->

# Md.Core API contract (2026-09-06)

Part A is what Wave A actually shipped (do not rename). Part B is DICTATED for the modules still
being written (Wave B: HTML writer, LaTeX; Wave C: EPUB, HTML export, DiagramSvg, PdfExport,
ExportFileNames, book compile + I/O). App-side code (Md.App.Logic) codes against this file; where
the design (shell-final.md §13.5) spelled a name differently, THIS file wins and the Logic call
site adapts.

## A. Shipped (Wave A)

namespace Md.Core.Text
- static Whitespace: IsWhitespace(char) [Foundation WS, 19 units incl. U+200B]; IsNewline(char);
  IsWhitespaceOrNewline(char); TrimWS/TrimWSNL/TrimLeadingWS/TrimTrailingWS/TrimSpaceTab/
  DropLeadingSpaces/DropSpaceTab(string); NormalizedLines(string) [CR/LF/CRLF]; NewlineSetLines(string).
- static ScalarText (ordinal): Contains, FirstIndex(text, needle, from=0) -> int?, HasPrefix, HasSuffix,
  Split(text, sep), Replacing, DropFirst(text, codePoints), IsMark(Rune), IsEnclosedAlphabetic(Rune),
  FullLowercase(string) [per-rune ToLowerInvariant + U+0130 -> i U+0307; NO final sigma].

namespace Md.Core.Markdown
- enum BlockKind { Heading, Paragraph, List, CodeBlock, Quote, Table, ThematicBreak, PageBreak, Note, FrontMatter, FootnoteDefinition }
- enum ColumnAlignment { Leading, Center, Trailing }
- record MetadataField(string Key, string Value); record ListItem(string Text, int Level, int? Ordinal, bool? Task)
- record OutlineEntry(int Level, string Text, string Slug, int Line); record NoteEntry(string Text, int Line)
- record PlacedBlock(MarkdownBlock Block, int Line)
- abstract record MarkdownBlock { BlockKind Kind } with nested: Heading(int Level, string Text), Paragraph(string Text),
  List(bool Ordered, IReadOnlyList<ListItem> Items), CodeBlock(string? Language, string Code), Quote(IReadOnlyList<MarkdownBlock> Blocks),
  Table(IReadOnlyList<string> Header, IReadOnlyList<ColumnAlignment> Alignments, IReadOnlyList<IReadOnlyList<string>> Rows),
  ThematicBreak(), PageBreak(), Note(string Text), FrontMatter(IReadOnlyList<MetadataField> Fields), FootnoteDefinition(string Id, string Text)
- static MarkdownParser: Parse(string, int quoteDepth=0); ParseWithLines(string); FrontMatter(string) [never null];
  ParseFootnoteDefinition(string line) -> (string Id, string Text)?; Outline(string); Notes(string);
  Slug(string text, IDictionary<string,int> used); IsRawPlantUml(string); IsRawGraphviz(string); Lines(string).
- record DelimitedTable(Header, Alignments, Rows) with static From(string code, char separator) -> DelimitedTable?;
  static IsDecimalNumber(string); static ParseDelimited(string text, char separator).
- static Plot: RenderPlot(string source) -> string [memoised <div class="plot">… container, never throws];
  PlotSvg(string) -> string [throws PlotException]; ParsePlot; ParseExpression; Evaluate; NiceStep; Decade; Ticks;
  FormatLabel; FormatExponential; FormatFixed; MemoStatistics; ClearMemo. class PlotMemo; class PlotException.

namespace Md.Core.Export
- record ZipEntry(string Name, byte[] Data) [structural]; class ZipWriter { Add(name, ReadOnlySpan<byte>); byte[] Finish();
  static byte[] Archive(IEnumerable<ZipEntry>); static uint Crc32(ReadOnlySpan<byte>) } [STORED only]
- static ZipReader { const MaxEntrySize = 128 MiB; Entries(byte[] archive, Func<string,bool>? shouldInflate=null) -> IReadOnlyList<ZipEntry>? }
- static TextBundle { consts InfoJson, TextFileName, InfoFileName, AssetsDirectoryName, BundleExtension, PackExtension;
  record Asset(string Name, byte[] Data); record Rewrite(string Text, IReadOnlyList<Asset> Assets);
  TextFromBundle(IReadOnlyDictionary<string,byte[]>) / TextFromBundle(string bundleDirectory) / TextFromPack(byte[]) -> DecodedText?;
  LooksLikePack(string name, ReadOnlySpan<byte>); IsLocalRelativeReference(string url);
  ExportRewriting(string source, Func<string, byte[]?> resolveAsset) -> Rewrite; BundleWrapper(string text, IReadOnlyList<Asset>);
  ReadAsset(string relativePath, string? documentPath) -> byte[]? }
- class BundleWrapper { byte[] TextBytes, InfoJsonBytes; IReadOnlyList<Asset> Assets; void Write(string bundleDirectory);
  IReadOnlyList<ZipEntry> PackEntries(string folderName); byte[] ToTextPack(string folderName) }

namespace Md.Core.Document
- enum TextEncoding { Utf8, Utf16, WindowsCP1251, IsoLatin1 }; record DecodedText(string Text, TextEncoding Encoding);
  record EncodedText(byte[] Data, TextEncoding Encoding) [structural]
- static PlainTextCodec { Decode(ReadOnlySpan<byte>) / Decode(byte[]) -> DecodedText?; Encode(string, TextEncoding preferred) -> EncodedText }
- enum ViewMode { Edit, Split, Preview }; static ViewModes { All, WindowStateKey="md.viewMode", WindowDefault=Split, RawValue, FromRawValue, Label, CommandKey }
- static ViewModeRule { AvailableModes(bool isWide); EffectiveMode(stored, isWide); DisplayedMode(preferred, navigation, isWide);
  NavigationNudge(displayed, wants); OpenViewMode(remembered, isEmptyDocument, hasFileIdentity, isWide) }
- interface IViewModeStore { string? Load(); void Save(string) }; class InMemoryViewModeStore
- static ViewModeMemory { SettingsKey="md.viewModeMemory", Header="v1", MaxEntries=200; record struct Entry(string Identity, ViewMode Mode);
  IdentityFor(path); CanonicalPath(path[, foldCase]); Sha256Prefix(value); Token; ModeForToken; IsIdentity; Decode; Encode; Touched;
  Entries(store); Lookup(identity, store); Remember(mode, identity, store) }
- static BookArticleOpens { MarkLifetime=10s; Func<DateTime> Now; Mark(path); ClaimOpen(path); Reset() }
- record PageSize(string Id, string Label, double Width, double Height) { statics A4, A5, UsLetter, UsLegal, SixByNine, FiveByEight, Digest;
  All; PreferenceKey="md.pdfPageSize"; DefaultId="a4"; Named(string? id); CssPadding }
- record Example(string FileName, string Name); static ExampleLibrary { FromListing(IEnumerable<string>); IsExampleFile; DisplayName; CompareNatural }

namespace Md.Core.Book
- static BookPaths { Comparison; Standardize; Same; Name; IsInside }
- static BookNaming { ArticleExtensions; IsArticleName; SplitExtension; SplitPrefix; DisplayName; RenamedName(name, stem) }
- static BookOrder { LeadingNumber; NaturalCompare; Compare; Ordered; Comparer }
- record BookArticle(string Path){Name}; record BookChapter(string Path, IReadOnlyList<BookArticle> Articles){Name};
  record Book(string Root, Articles, Chapters){Name; static Empty(root)}; record BookEntry(Name, IsDirectory, IsHidden)
- interface IBookListing { List(folder) }; class DirectoryBookListing; static BookModel { Load(root[, listing]) -> Book?; ReadingOrder(Book) }
- record RenamePair(From, To); record StagedRename(Original, Temp, Target); interface IBookFileMover; class LocalBookFileMover
- static RenumberPlan { Plan(names, from, to); RenumberedName; TempName; Stage; Apply(plan, folder[, mover], out failure) }
- static Destination { Of(path, folder, plan) }
- static WritingStats { Words(string); Characters(string); Segments(string) }
- record struct FileStamp(DateTime ModifiedUtc, long Size); interface IArticleFileSystem; class LocalArticleFileSystem
- enum BookStageKind; record BookStage; class BookFlushGate { static Post(); static FlushEditor(); Vetoed }
- interfaces IDocumentOwnership, IBookAlerts, IAutosaveScheduler; class BookArticleSession (see book module report)
- internal ArticleTextCodec — STAND-IN, to be replaced by PlainTextCodec (Wave C task).

## B. Dictated (Wave B / Wave C) — implement with EXACTLY these names

namespace Md.Core.Markdown  (Wave B, html module)
- static class MarkdownHtml
  - string Document(string source, string title, bool dark, bool export = false)   // whole page; Swift MarkdownHTML.document
  - RenderedBody Body(string source, string title, bool dark)                       // md.vscode renderBody: body markup + needs
  - string Css(bool dark, bool export)                                              // the 101-line stylesheet, three variants
  - string RenderBlocks(IReadOnlyList<MarkdownBlock> blocks, ...)  and string Inline(string text) as the Swift has them (public where Swift is)
  - EngineNeeds Needs(string body)                                                  // five probes on emitted markup
- record RenderedBody(string Html, EngineNeeds Needs); record EngineNeeds(bool Math, bool Mermaid, bool Plantuml, bool Graphviz, bool Highlight)
  (Needs.ToString() for engine-needs.json is the space-joined lowercase list in the order math mermaid plantuml graphviz highlight — the golden file pins it)

namespace Md.Core.Export  (Wave B, latex module)
- static class LaTeXExport { string Document(string source, string title); string Book(StructuredBook book) }
namespace Md.Core.Book  (Wave B, latex module creates it; Wave C reuses)
- record StructuredBook(string Title, IReadOnlyList<BookUnit> FrontUnits, IReadOnlyList<BookSection> Sections)  — mirror Swift's EPUBBook/Part shape
  exactly as export.md/book.md describe (root articles first, then chapters with their articles); record BookSection(string Title, IReadOnlyList<BookUnit> Units);
  record BookUnit(string Title, string Source). If the Swift shape has different fields, follow the Swift and document.

namespace Md.Core.Export  (Wave C)
- static class PdfExport { string StyledForExport(string html, PageSize pageSize) }   // the print/paper HTML: export stylesheet + page CSS
- static class HtmlExport { string PreparePage(string capturedHtml, Func<string, byte[]?> readRichAsset, bool hasMath) ; string Finish(...) }
  — the pure steps of the self-contained HTML export (DOCTYPE prepend, page-break swap, KaTeX CSS inlining woff2-only when math, notices)
- static class DiagramSvg { IReadOnlyList<Diagram> Diagrams(string source); record Diagram(int Ordinal, string Engine, string Source, string MenuTitle);
  string StandaloneDocument(string svgOuterHtml) }
- static class EpubExport { string DocumentTitle(string source, string fileName); byte[] BuildDocument(string source, string title, Func<…> snapshots…);
  byte[] Build(StructuredBook book, …) } — snapshots are supplied by the app as bytes; Core does OPF/nav/XHTML fixer/zip
- static class ExportFileNames { string Sanitized(string title, string extension) }

namespace Md.Core.Book  (Wave C)
- static class BookFolder { CreateChapter, CreateArticle, RenameItem, DeleteItem, DeletionNeighbor, RelativePath — the I/O the Swift BookNavigator does }
- static class BookCompiler { string CompileBookSource(Book book, Func<string, string> readArticle) ; StructuredBook ReadStructuredBook(Book book, …) }
- Replace ArticleTextCodec stand-in with PlainTextCodec (5 call sites in BookArticleSession).

## A2. Shipped after the Wave A refuters (2026-09-06, later)
namespace Md.Core.Book
- static BookCompiler { const string PageSeparator = "\n\n\\newpage\n\n"; string Compile(Book book, Func<string,string> readArticle …) } — port of BookLibrary.compile
  (title page + every part on a fresh page); record BookPart (Chapter/Article). Wave C's compile/ReadStructuredBook work BUILDS ON THIS FILE (do not create a second compiler).
- static BookPaths gained DeletingPathExtension(name) and PathExtension(name) (Foundation-faithful: dots-only stem keeps the whole name; an extension containing U+0020 is empty).
- BookOrder.NaturalCompare is now the Foundation-faithful two-walk algorithm (same as ExampleLibrary.CompareNatural), not the Kotlin stand-in.
- WritingStats follows ICU root tailorings (colon family not MidLetter; fullwidth digits Numeric; SA scripts as one word).
- LocalArticleFileSystem.FileExists is true for directories too (Swift fileExists(atPath:)).
- MarkdownParser.cs: the blank-line predicate is TrimWS(line).Length == 0 — a live mutant (line.Length == 0) shipped from the implementer's harness and hung the parser on whitespace-only lines; fixed by the refuter, pinned by ParserAdversarialTests.WhitespaceOnlyLineIsABlankLineAndNeverHangsTheParser.
- Test-isolation: BookArticleSession tests are in [Collection("BookArticleSession")] because BookFlushGate.Requested is a static event.

## A3. Shipped after Wave B/C (2026-09-06) — the HTML writer, LaTeX, book I/O and DiagramSvg

namespace Md.Core.Markdown
- record EngineNeeds(bool Math, bool Mermaid, bool Plantuml, bool Graphviz, bool Highlight) { static None; ToString() = the space-joined
  lowercase list in probe order "math mermaid plantuml graphviz highlight" — the engine-needs.json golden pins it }
- record RenderedBody(string Html, EngineNeeds Needs)
- static MarkdownHtml { IReadOnlyDictionary<string,string> GraphvizEngines (10 keys, 8 engines, ordinal);
  string Document(string source, string title, bool dark, bool export = false); RenderedBody Body(string source, string title, bool dark);
  string Css(bool dark, bool export); EngineNeeds Needs(string body); string RenderBlocks(IReadOnlyList<MarkdownBlock>);
  string RenderBlock(MarkdownBlock); string Inline(string text, bool softBreaks = false); string Escape(string) }
  (internal MarkdownCss.Stylesheet, MarkdownInlineHtml — not for callers.)
  NOTE Css(dark: true, export: true) returns the EXPORT sheet: `dark && !export` is applied inside, as in the Swift and in md.vscode.

namespace Md.Core.Book
- record BookUnit(string Title, string Source); record BookSection(string Title, IReadOnlyList<BookUnit> Units);
  record StructuredBook(string Title, IReadOnlyList<BookUnit> FrontUnits, IReadOnlyList<BookSection> Sections)  — Swift's EPUBBook
- static BookCompiler gained ReadStructuredBook(...) alongside Compile(...)
- static BookFolder { CreateChapter, CreateArticle, RenameItem, DeleteItem, DeletionNeighbor, RelativePath, OrderIndex, IsValidName … }
- BookArticleSession now decodes through Md.Core.Document.PlainTextCodec (ArticleTextCodec.cs is DELETED)

namespace Md.Core.Export
- static LaTeXExport { string Document(string source, string title); string Book(StructuredBook book) }
- static DiagramSvg { IReadOnlyList<Diagram> Diagrams(string source); record Diagram(int Ordinal, string Engine, string Source, string MenuTitle);
  string StandaloneDocument(string svgOuterHtml) }
- static ExportFileNames { string Sanitized(string title, string extension) } — also refuses the Windows-illegal characters,
  trailing dots/spaces and the reserved device names (documented in the file as the Windows-specific part)

Md.Core suite after integration: **1163 tests, 0 failures, 0 warnings**. The 25 golden HTML bodies, engine-needs.json,
document.html and the three stylesheets are byte-exact; testdata/test.html and document.html were regenerated for the Windows
test.md and differ from md.vscode's originals only in the four documented hunks (recorded at the top of GoldenTests.cs).

## B2. Still to write (Wave D)
namespace Md.Core.Export
- static PdfExport { string StyledForExport(string html, PageSize pageSize) }
- static HtmlExport { the pure steps of the self-contained HTML export: DOCTYPE prepend, page-break swap, KaTeX CSS inlining
  (woff2-only data: faces, only when the document has math), the MIT + OFL notices verbatim }
- static EpubExport { string DocumentTitle(string source, string fileName); byte[] BuildDocument(...); byte[] Build(StructuredBook, ...) }
  — container.xml/OPF/nav/XHTML fixer/UUIDv5 identifier/stored zip in Core; the rich-element PNG snapshots come from the app as bytes

## A4. Shipped in Wave D (2026-09-06) — the export pipeline's pure half

Md.Core never touches a browser. The app loads the page, waits for `data-md-render-complete`,
captures the DOM, snapshots rich elements to PNG and drives `PrintToPdfAsync` itself, then hands
the results here as strings and byte arrays. Each module's own hand-off report follows, including
the app-side contract it requires. Md.Core suite after Wave D: **1271 tests, 0 failures, 0 warnings**.


### pdfhtml

## 3. Public API as implemented

```csharp
namespace Md.Core.Export;

public static class PdfExport
{
    public const  string BodyPaddingRule = "padding: 48px 56px;";
    public static string StyledForExport(string html, PageSize pageSize);   // Swift-exact, FIRST occurrence only
    public static string PageBoxCss(PageSize pageSize);                     // md.vscode's @page block — opt-in
    public static string WithPageBox(string html, PageSize pageSize);       // appends it after the first </style>
}

public static class HtmlExport
{
    public static readonly string CaptureScript;      // the 9-line DOM read, joined with "\n"
    public const  string Doctype = "<!DOCTYPE html>\n";
    public const  string KatexCssAsset = "rich/katex.min.css";
    public const  string KatexFontsFolder = "rich/fonts/";
    public const  string ExportPageBreakRule;         // ".md-pagebreak { height: 0; margin: 0; break-after: page; }"
    public const  string ScreenPageBreakRule;         // ".md-pagebreak { border-top: 2px dashed rgba(43,38,32,0.16); margin: 1.6em 0; }"
    public const  string MermaidContainerNeedle = "class=\"mermaid\"";
    public static readonly string KatexNotice;        // 7 lines, verbatim MIT + OFL
    public static readonly string MermaidNotice;      // 5 lines, verbatim MIT

    public static string  ExportDocument(string source, string title);      // step 1+2 — what the app LOADS
    public static string  VisiblePageBreaks(string document);               // ALL occurrences
    public static bool    NeedsKatex(string exportDocument);                // gate on the INPUT
    public static string? EmbeddedKatexCss(Func<string, byte[]?> readRichAsset);
    public static string  WithEmbeddedKatex(string page, string css);
    public static string  WithMermaidNotice(string page);
    public static string  PreparePage(string capturedHtml, Func<string, byte[]?> readRichAsset, bool hasMath);
    public static string  PreparePage(string capturedHtml, string exportDocument, Func<string, byte[]?> readRichAsset);
}
```
`PreparePage(captured, reader, hasMath)` is the signature `docs/core-api.md` Part B dictated; the three-string overload was added so the math gate cannot be pointed at the wrong string. `Finish(...)` from the B sketch is not needed — suggested B2 edit: replace the `HtmlExport { PreparePage…; Finish(…) }` line with the list above.

**What scales with the paper and what does not** (documented in the file, pinned by `TheMarginIsTheOnlyThingThatScalesWithThePaper`): exactly one declaration — the body `padding`. The 11pt body size, `#FFFFFF` paper, `color-scheme: light`, `pre { white-space: pre-wrap; overflow-wrap: anywhere; }`, `break-after: page`, the print-color-adjust rule and every colour are the `export: true` sheet and identical on all seven sizes. Pinned by putting A4's rule back and asserting byte equality with the input, and by matching `<style>` against `golden/stylesheet-export.css` with one declaration rewritten. **Preview / HTML export / EPUB are unaffected** (`ThePdfPageSizeReachesNeitherThePreviewNorTheHtmlExportNorTheSheet`): all three sheets carry A4's `48px 56px`, and `HtmlExport.ExportDocument` keeps it whatever `md.pdfPageSize` says.

## 4. App-side contract (Md.App / Md.App.Logic)

**PDF (Share ▸ Rendered PDF, Export ▸ PDF, and the two book equivalents):**
1. `html = MarkdownHtml.Document(source, title, dark, export: true)`.
2. `paper = PdfExport.StyledForExport(html, PageSize.Named(LocalSettings["md.pdfPageSize"]))`. Do **not** also call `WithPageBox`.
3. Load `paper` offscreen (virtual host for `rich/`, in-memory `index.html`), poll `data-md-render-complete` 480 × 250 ms, **timeout is success**.
4. `PrintToPdfAsync` with `Orientation = Portrait`, `PageWidth = Width/72`, `PageHeight = Height/72`, `ScaleFactor = 1`, `ShouldPrintBackgrounds = true`, `ShouldPrintHeaderAndFooter = false`, margins **0.5 in** all round (Android's reason: the body padding only wraps the whole document, so a middle page would touch the paper edge). The paper geometry lives in the print settings, never in the document.
5. Print is always A4 and ignores the setting.

**HTML (Export ▸ HTML):**
1. `document = HtmlExport.ExportDocument(source, title)` — the app must load **this** string, not `MarkdownHtml.Document(...)`, or the author's page breaks vanish from the file.
2. Load offscreen, wait for render-complete.
3. `ExecuteScriptAsync(HtmlExport.CaptureScript)` → **JSON-decode** the result. `null` / `"null"` / empty ⇒ show `Could not export HTML` / `The rendered page could not be captured.` and write nothing.
4. `page = HtmlExport.PreparePage(captured, document, ReadRichAsset)` — pass the same `document` from step 1; that is the math gate.
5. Write `page` as UTF-8 **without BOM** to the `FileSavePicker` destination, suggested name `ExportFileNames.Sanitized(title, ".html")`.

**`Func<string, byte[]?> ReadRichAsset` contract:** the key is always slash-separated and is exactly one of `rich/katex.min.css` or `rich/fonts/<face>.woff2` with `<face>` matching `[A-Za-z0-9_-]+`. Return the file's bytes, or `null` when it is not in the package. **Never throw** (map `Path.Combine(AppContext.BaseDirectory, key.Replace('/', Path.DirectorySeparatorChar))` and `File.Exists`). A zero-length array means "an empty file that exists", not "missing". At most 21 calls (1 sheet + 20 faces); **zero calls for a document without math** (pinned). `null` for the sheet ⇒ the export still produces a file, unstyled formulas and no OFL notice.

**Count mismatch:** there is none in this module — nothing here pairs a list of PNGs with a list of containers (that is the EPUB's invariant). The only input that can fail is the capture, and it fails loudly: `PreparePage` throws `ArgumentException` on an empty capture / `ArgumentNullException` on null, rather than writing a file that is a doctype and nothing else.


### epub

**(3) Public API as implemented** — namespace `Md.Core.Export`

Types: `RichElement(int Start, int Length, bool IsMath){ End, Alt }`; `RichSnapshot(byte[] Png, double Width){ DisplayWidth }` (structural equality); `EpubImage(string File, byte[] Data)` (structural); `EpubUnit(File, Title, Xhtml)`; `NavEntry(string Title, string File, IReadOnlyList<NavEntry>? Children = null)`; `EpubUnitPlan(int Index, string File, string Title, string Document, string Body, IReadOnlyList<RichElement> RichElements, bool IsHeadingPage)`; `EpubPlan(string Title, string Identifier, IReadOnlyList<EpubUnitPlan> Units, IReadOnlyList<NavEntry> NavEntries){ RichUnits }`; `sealed class EpubSnapshotCountException : InvalidOperationException { UnitIndex, UnitTitle, Expected, Actual }`.

`static class EpubExport`:
```
const Mimetype = "application/epub+zip"; const ContentFileName = "content.xhtml";
const RichSelector = ".md-mathi, .md-mathd, .mermaid, .plantuml, .graphviz"; const ContainerXml
string DocumentTitle(string source, string fileName)
string DocumentTitle(IReadOnlyList<MetadataField> frontMatter, string fileName)
bool   ContainsRichContent(string html);  string BodyHtml(string document);  string XhtmlBody(string html)
IReadOnlyList<RichElement> RichElements(string html)
string ReplacingRichElements(string html, IReadOnlyList<string> tags)
string ImageTag(string file, bool isMath, int width); string ImageFileName(int unit, int element); string UnitFileName(int index)
string XhtmlDocument(string title, string body);  string Nav(string title, IReadOnlyList<NavEntry> entries)
string Opf(title, identifier, modified, IReadOnlyList<string> units, IReadOnlyList<string> images, IReadOnlyCollection<int>? svgUnits = null)
string Stylesheet();  string StableIdentifier(string title);  string ModifiedNow();  string Modified(DateTimeOffset when)
EpubPlan PlanDocument(string source, string title);   EpubPlan PlanBook(StructuredBook book)
byte[]  Assemble(EpubPlan plan, IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? snapshots = null, string? modified = null, string? identifier = null)
IReadOnlyList<ZipEntry> Entries(EpubPlan plan, …same…)
IReadOnlyList<ZipEntry> Entries(string title, IReadOnlyList<EpubUnit> units, IReadOnlyList<NavEntry> navEntries, IReadOnlyList<EpubImage> images, string identifier, string modified)
IReadOnlyList<ZipEntry> DocumentEntries(string title, string body, IReadOnlyList<EpubImage> images, IReadOnlyList<OutlineEntry> outline, string modified, string? identifier = null)
byte[]  BuildDocument(string source, string title, IReadOnlyList<RichSnapshot>? snapshots = null, string? modified = null, string? identifier = null)
byte[]  Build(StructuredBook book, IReadOnlyDictionary<int, IReadOnlyList<RichSnapshot>>? snapshots = null, string? modified = null, string? identifier = null)
```

**Parameter-shape decision.** The app supplies **only PNG snapshots**, never a captured DOM string. EPUB needs no capture: every rich container becomes an `<img>`, a `plot` is already a finished `<svg>` in the writer's own markup, and code blocks stay uncoloured because highlight.js only ever painted the live DOM (the Swift comment in `MarkdownHTML.css` says so explicitly). That is the "or the Core-rendered body when no capture is needed" branch of the brief. Snapshots are keyed **by unit index**, not by a flat sequence, so a book's photographs cannot drift onto the wrong article.

**App-side contract (Md.App.Logic / WebView2), exactly:**

1. `var plan = EpubExport.PlanDocument(source, title)` (title from `EpubExport.DocumentTitle(source, fileName)`) or `EpubExport.PlanBook(book)` from `BookCompiler.ReadStructuredBook`.
2. For each `unit in plan.RichUnits` **only** (every other unit needs no browser at all; a heading page's `Document` is deliberately `""`):
   a. `await surface.LoadAsync(unit.Document, ct)` with `RenderKind.Export` — pure Core HTML, never the Windows Georgia style — waiting for `data-md-render-complete`; a timeout counts as success, as on macOS.
   b. Grow to content (`scrollHeight` → `SetHeightAsync`) and let it repaint (macOS sleeps 300 ms): the capture is in view coordinates, so every element must lie inside.
   c. `EvalAsync` over `document.querySelectorAll('<EpubExport.RichSelector>')` returning, per element and in DOM order, `[left+scrollX, top+scrollY, width, height, isMath?1:0]`. Keep degenerate rects — dropping one shifts every later image.
   d. **The DOM count must equal `unit.RichElements.Count`.** If it does not, abort with "Could not export EPUB" — do not call Core (Core would throw anyway).
   e. Per rect in order: `CaptureRegionPngAsync(rect, scale: 2.0)` with width/height clamped to ≥ 1; build `new RichSnapshot(png, rect.Width)` — Core rounds the width half-away-from-zero.
   f. `snapshots[unit.Index] = thatList`.
3. `var bytes = EpubExport.Assemble(plan, snapshots)`, or the one-call `BuildDocument` / `Build`.
4. Count mismatch anywhere ⇒ `EpubSnapshotCountException` carrying `UnitIndex`, `UnitTitle`, `Expected`, `Actual`; map it to the "Could not export EPUB" alert. Never a partial book.
5. Save with `ExportFileNames.Sanitized(title, ".epub")`. Book: **panel first, then build** (photographing takes a while and the user may cancel). Document: title → panel → build, so the suggested name matches. Book export first goes through `BookFlushGate.FlushEditor()`.
6. Pass `modified` only for determinism; the default is `ModifiedNow()` (UTC, second precision — epubcheck rejects fractions).

**(4) Divergences decided, each with its pinning test**

| Decision | Test |
| --- | --- |
| Marker scan (Kotlin/TS `RICH_MARKERS`, prefix-tolerant on Graphviz) instead of the Swift `NSRegularExpression`. Identical on every byte this writer emits; differs only on combinations no port can produce (`<div class="md-mathi">`). `ContainsRichContent` stays the wider Swift test, so such markup still opens a renderer and then trips the count check rather than shipping a shifted book. | `TheMarkerScanTakesOnlyTheSixShapesTheWriterEmits` |
| Swift's `zip` pairing kept in `ReplacingRichElements` **and** Android's count check added on the way in — the invariant that otherwise corrupts a book silently. | `EmptyTagListLeavesTheMarkupAloneAndAShortListPairsUpToTheShorter`, `AMismatchedSnapshotCountIsRefusedByName` |
| `documentTitle` trims with `Whitespace.TrimWSNL`, **not** `string.Trim()`. export.md §6.4 says `Trim()` is "correct HERE"; it is not — Foundation's frozen tables keep U+200B in `.whitespacesAndNewlines` and `char.IsWhiteSpace` does not, so a title of three zero-width spaces would survive as a blank title. | `DocumentTitleTrimsFoundationWhitespaceNotDotNetWhitespace` |
| `BodyHtml` returns the input when `</body>` precedes the body tag (Swift traps on the range; md.vscode guards it the same way). | `BodyExtractionHandsBackTheInputWhenTheMarkersAreMissingOrReversed` |
| **A book with no articles and no chapters keeps macOS's empty `<nav>` (no `<ol>`) — invalid EPUB 3.** Android adds the title page as the one entry there; the single-document fallback exists on every port and is implemented. Reproduced as macOS bytes per the brief, and flagged: **nettrash's call whether to adopt Android's fallback** (a four-repo decision). | `ABookWithNothingButATitlePageGetsTheSwiftsEmptyNav` |
| `Int(width.rounded())` = `MidpointRounding.AwayFromZero`, not .NET's banker's default. | `SnapshotWidthRoundsHalfAwayFromZeroNotToEven` |
| Everything else is macOS where the three ports drift: `<meta charset="utf-8"/>` in both heads, the book/document title in the nav `<title>`/`<h1>` (not "Contents"), the export CSS + `body { padding: 0.5em 5%; }` stylesheet, `unit-000` = title page, ids `bookid`/`u0`/`i0`, `images/unit-%03d-rich-%d.png`, `<img src alt width>` only, the blank line before `</body>`, all-STORED zip. | `XhtmlDocumentCarriesTheCharsetMetaAndTheSharedStylesheet`, `NavTitleIsTheBookTitleAndTheHeadIsMacosSpelled`, `StylesheetIsTheExportCssPlusThePaddingOverride`, `BookUnitsAreTheTitlePageThenRootArticlesThenChapters`, `OpfCarriesMetadataManifestAndSpine`, `ImageTagAndFileNamesFollowTheMacosSpelling`, `BodyExtractionTrimsAndKeepsTheScriptForTheFixerToRemove` |

Two C# specifics worth recording: the UUIDv5 is formatted from raw hex (never `System.Guid`, which is mixed-endian) and is pinned against **Python's `uuid.uuid5(uuid.NAMESPACE_URL, name)`** for five names including `""` and a Cyrillic one built from code points — an oracle outside this family (`StableIdentifierMatchesTheRfc4122Oracle`, `StableIdentifierHashesTheTitleAsUtf8`). The only regex whitespace class is spelled out as `[\t\n\v\f\r\x85\p{Z}]` (ICU's `\s`); `[\s\S]` survives once as the tautological "any character" set, commented as exempt.

Every EPUB row of the catalogue is ported: `XhtmlFixerMakesWellFormedMarkup`, `RichElementsBecomeImages`, `GraphvizContainersAreSnapshottedWhateverTheirEngine` (plus all ten fence aliases), `OpfCarriesMetadataManifestAndSpine`, `NavListsRootArticlesThenNestedChapters`, `EpubIdentifierIsStableForTheSameBook`, `DocumentEpubTitlePrefersFrontMatterThenFileName`, `DocumentEpubIsAValidStoredZipWithMimetypeFirst`, `DocumentEpubOpfReferencesResolveWithOneRealUnitAndNoTitlePage`, `DocumentEpubNavListsHeadingsWithSlugsMatchingTheContentAnchors`, `DocumentEpubOfAHeadinglessDocumentHasAValidNav`, `DocumentEpubIdentifierIsStableAcrossTwoExports`, `EpubRoundTripsThroughAZipReader`. `testZipWriterProducesValidStoredArchive` / `testZipWriterCRC32MatchesKnownVector` were already shipped in `ZipTests`.

