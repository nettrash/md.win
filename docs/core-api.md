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
