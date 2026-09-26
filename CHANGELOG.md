# Changelog

All notable changes to this project are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The number this file tracks is set by hand — there is no `agvtool bump` as
on iOS and macOS and no Gradle `versionCode` finalizer as on Android. What
the Store sees is the MSIX `Version` in `src/Md.App/Package.appxmanifest`:
four parts, `Major.Minor.Build.Revision`, with Revision fixed at 0 because
the Store reserves it, so the family's two-part `1.5` became `1.5.0.0`. Its
third part is what moves on its own — `BumpPackageVersion` in
`Md.App.csproj` raises it on every build, which is how a resubmission is
always strictly greater than the last; by the time 1.5 was committed it had
climbed to `1.5.7.0` through the builds of the release verification. A resubmission that changes no
behaviour is not tracked here.

md.win began at its own 1.0, as every md port has, and said it would join
the family's number at the next family release. That release is 1.5, and
the promise is kept: **1.5 follows 1.0 directly** because the number is
the family's, shared by every md edition, and not a count of this port's
releases. Nothing is missing between the two — 1.0 already arrived, last
of the five, with everything the other editions had learned through their
own 1.4 — and from here md.win is versioned with them.

## [1.5] — 2026-09-23

### Added

- **Replace, in the find bar.** The find bar (**Edit ▸ Find…**, Ctrl+F) now
  carries a replacement field beside the query, and **Replace** and **Replace
  All** beside Next and Previous; **Ctrl+H** (**Edit ▸ Replace…**) opens the
  same bar with the caret already in the replacement field, and a non-empty
  selection seeds the query there as it does for Find. Matching is the rule
  the find has always used — case-insensitive, wrapping round the end of the
  file, and **no regular expressions**: a query is the characters you typed.
  What is searched is the text you are editing, never the rendered preview
  beside it. Replace rewrites the hit the editor is standing on and then
  moves to the next, so pressing it repeatedly walks the document; with the
  caret somewhere else it is simply a Find Next. Enter in the replacement
  field is Replace and Shift+Enter is Replace All. After a Replace focus
  stays in the field, so a run of Enters never reaches the text, and the hit
  the next press will replace stays visibly selected in the page behind the
  bar. **Replace All is one press and one undo step**: every hit goes in as a
  single edit, focus returns to the editor with the rewritten span selected,
  and one Ctrl+Z puts the lot back. Replacements go in exactly as typed —
  Capitalize Sentences never touches them — and the word count, the autosave,
  the dirty marker and the clobber guard see them like any other edit. The
  bar leaves the screen with the editor: switch to Preview and it closes,
  like the Find… and Replace… rows that open it. As with Find, the bar
  belongs to a document window; a book's article editor has none.
- **Enter carries the list on, and sentences capitalize themselves.** Enter
  on a list item — a bullet, a number, a task box, a quote line — starts the
  next one with the same marker, numbers counting on; on an item you left
  empty it ends the list; on a table row it starts the next row. The first
  letter of a line, and the first letter after a sentence ends, is
  capitalized as it is typed, in every cased script, and never inside a code
  fence, a table, inline code or math, a link address or a path;
  abbreviations (`e.g.`, `т.д.`, `p.m.`) are not sentence ends. Delete the
  capital md wrote and type the letter again and it stays lowercase — that is
  how `md` or `iOS` start a sentence — and Ctrl+Z immediately after a capital
  restores the lowercase letter as its own undo step. Shift+Enter is always a
  plain newline; Undo, Redo and Paste put text back exactly as it was and are
  never capitalized on the way, so a Redo restores every step and **Edit ▸
  Paste** is as literal as Ctrl+V. Both helpers work in a document window and
  in a book's article editor, and both are switches under **Edit ▸ Typing** —
  **Continue Lists and Tables** and **Capitalize Sentences**, on by default,
  stored as `md.continueLists` and `md.capitalizeSentences` and listed in
  PRIVACY.md — which turn on or off in every open window at once. The rules
  are the family's shared SmartTyping specification, pinned by the same 1350
  vectors (512 for Enter, 838 for the capital) on every md port.
- **Every Markdown spelling is md's.** Beside `.md`, `.markdown`, `.mdown`,
  `.markdn` and `.mdtext`, the package now owns `.mdtxt`, `.mkd`, `.mkdn`,
  `.mdwn` and `.mkdown` — the full set every md port declares — so a file
  under any of them opens by double-click, is listed in the Open dialog, and
  is saved back in place under its own name. PlantUML gains `.iuml` and `.pu`
  beside `.puml` and `.plantuml`, in the Open and Save dialogs and in File
  Explorer alike. Graphviz is still `.gv` alone, `.dot` deliberately
  unclaimed because Windows calls it a Word template. The manifest's
  associations and the loader's own extension lists are now one set, pinned
  to each other by a test, so the Open dialog cannot offer a type Windows was
  never told about — which is exactly what the bug below was.

### Fixed

- **A `.text` file now opens by double-click.** The Open dialog listed the
  extension and the editor had always been able to save one, but the package
  never registered it, so File Explorer handed such a file to another app.
- **Find Next and Find Previous no longer do nothing in Preview.** With the
  editor off screen — View ▸ Preview, or the reading half of Zen — F3 and
  Shift+F3 stayed live, searched the hidden text and selected a hit nobody
  could see. They are greyed there now, like Find… and Replace…, and come back
  with the editor, the query still in the bar.
- **Find…, Replace… and Use Selection for Find are greyed in Zen.** Zen shows
  no find bar, so the three rows that open it did nothing while their
  shortcuts were swallowed; Ctrl+F, Ctrl+H and Ctrl+E now pass through
  instead. F3 still walks the text in Zen's writing half.
- **Use Selection for Find no longer opens the find bar over Preview.** A
  selection left behind in the hidden editor kept the row live, and Ctrl+E
  opened the bar with no editor under it.
- **Cut, Copy and Delete are greyed when nothing is selected**, in a document
  window and in a book's article editor, rather than lighting up and doing
  nothing — the book window now refreshes its Edit menu as the selection moves.
- **A menu tick no longer goes missing when you click an already-ticked row.**
  Choosing View ▸ Edit while in Edit left no mode ticked, and choosing another
  window in the Window menu left two windows ticked, until something else
  changed; the tick now always shows the state.
- **Enter's list continuation is decided before the editor inserts its own
  newline.** It was read after the key had reached the editor, where a
  multi-line text box is free to have handled Enter first — which would have
  meant no continuation at all, or one after a stray blank line.
- **The Windows self-test proves what it runs.** CI started the self-test
  without waiting for it — PowerShell does not wait for a windowed program — so
  the step passed before a single check had run; it now waits, fails without a
  report, and prints every failed check. The self-test also gained real
  document windows and real keystrokes: menus, Find, Replace, the Typing
  switches, file associations, capitals and list continuation, typed into the
  editor. `tools/verify-on-windows.ps1` runs all of it, the test suites and the
  package checks in one command, and the off-Windows compile check now covers
  the self-test's own code.

## [1.0] — 2026-09-06

### Added

- **The whole md surface, on Windows 11.** Everything the iPhone, iPad,
  Mac, Android and VS Code editions learned through their 1.4 releases,
  reimplemented in C# on .NET 10 and WinUI 3: the hand-written Markdown
  renderer — headings, lists and task lists, fenced code with
  syntax-highlighting, block quotes, tables, CSV / TSV blocks drawn as
  tables, thematic and page breaks, images, footnotes gathered under a rule
  and numbered in reading order, YAML / TOML front matter recognised and
  kept off the page — with TeX/LaTeX math and mhchem chemistry, Mermaid,
  Graphviz and PlantUML diagrams drawn on the PC by the bundled engines
  (KaTeX 0.17.0 with mhchem, Mermaid 11.16.0, Graphviz 14.1.1 through
  Viz.js 3.24.0, PlantUML 1.2026.4beta4, highlight.js 11.11.1), and the
  ` ```plot ` block that needs none of them: a chart drawn by the app as a
  real vector `<svg>` from a formula, a parametric curve or a list of
  points, with `x` / `y` ranges, `title`, `xlabel`, `ylabel`, `legend`,
  `grid`, `axes`, `width`, `height` and `samples` as optional directives.
  The parser, the HTML writer and its stylesheet, the plot engine and the
  HTML / EPUB / SVG / LaTeX / TextBundle writers live in `Md.Core` and are
  pinned byte for byte to the fixtures the other ports pin — the same
  document exported here and on a Mac is one file. Raw HTML is escaped, a
  four-space indent continues a paragraph, there are no reference-style
  links and a lone `$` is prose, exactly as in the other four.
- **Documents.** Open, edit and save `.md`, `.markdown`, `.mdown`,
  `.markdn` and `.mdtext` through the standard dialogs, plus `.txt`,
  `.puml` / `.plantuml` and `.gv` as an alternate handler (a raw diagram
  file renders as the diagram it describes, source still editable; `.dot`
  is left unclaimed because Windows calls it a Word template). Files are
  saved back in the encoding and line endings they arrived in: UTF-8 with
  or without its byte-order mark, UTF-16 only when a BOM says so, and
  Windows-1251 — a Cyrillic file without a BOM opens as Cyrillic, never as
  UTF-16 mojibake. A `.textpack` opens by double-click and a `.textbundle`
  folder through **File ▸ Open TextBundle Folder…** or by dropping it on a
  window; both are imported as their text, titled after the bundle, and
  never written back, so a bundle's `assets/` are never lost. Files can be
  dropped on any window, and md runs as a single instance: a double-click
  in File Explorer lands in the running app, and a file that is already
  open brings its window forward instead of opening twice.
- **Autosave that cannot clobber.** One second after the last keystroke
  the file is written **in place** — never write-then-rename, so the file
  keeps its identity, ACLs, hard links and cloud-sync placeholder state —
  and only after checking that its modification time and size are the ones
  md last saw. If another program changed the file under unsaved edits,
  autosave stops and a bar in the window offers **Reload from Disk** or
  **Keep My Version**; a clean document simply reloads. The title's
  "— Edited" mark is cleared only by an explicit **Save**, as on the Mac.
  If a save fails while a window is closing, the text is written beside the
  document as `<name> (rescued)` with the original extension — `Notes
  (rescued).md`, then `(rescued 2)`, … — and the window says where it
  went, so a full disk or a locked file never costs a paragraph.
- **Rename…, Move To…, Duplicate and Revert to Saved** in the File menu —
  the commands the Mac inherits from `NSDocument` and Windows has to be
  given by hand. Rename keeps the extension; Move To picks a folder;
  Duplicate opens a new untitled window titled `<name> copy`; Revert puts
  back the last explicitly saved text. Every name prompt (Rename, New
  Chapter, New Article) rejects the characters Windows cannot store
  (`\ / : * ? " < > |`), a trailing dot or space, and the reserved device
  names, with one plain message.
- **Where you left off.** The layout is remembered **per file**: the 200
  most recently opened documents keep their Edit / Split / Preview, on this
  PC, keyed by a short hash of the path rather than the path itself. A file
  md has not seen opens in Split, a new document in Edit, a first save keeps
  the mode you were in, and renaming or moving a file — in File Explorer or
  through md's own Rename… / Move To… — lets it start over as unseen. Books
  are excluded entirely and keep one layout, and an article opened in its
  own window records nothing; only a mode you pick is remembered, so
  jumping to a note from **Go ▸ Notes** brings the editor up without
  rewriting anything. On a plain launch md restores the saved documents you
  had open — each in its mode, Zen state and window placement — from a
  small `session.json`; untitled drafts are not autosaved and are asked
  about before their window closes.
- **Three layouts, Zen, and the writer's tools.** **View ▸ Edit / Split /
  Preview** (Ctrl+1 / Ctrl+2 / Ctrl+3); Split re-renders as you type,
  scrolls both panes as one and stacks vertically when the window is
  narrower than 640 pixels. **View ▸ Zen Mode** (Ctrl+Shift+Enter) takes
  the window full screen and leaves one column two-thirds of the screen
  wide, with a floating switch between writing and reading that fades when
  the pointer rests; Esc, F11 or leaving full screen by any means drops it.
  **Go ▸ Contents** lists every heading and jumps both panes; **Go ▸ Notes**
  lists private author notes — an HTML comment on its own line whose text
  begins with `note:` — which never reach the preview, the PDF or the
  printout. A live **words · characters** footer sits under every page,
  counted with the ICU word-break rules Windows ships in `icu.dll`, so CJK
  text counts as it does on the Mac and on Android.
- **Find.** A find bar (**Edit ▸ Find…**, Ctrl+F; F3 / Shift+F3 for next
  and previous; Ctrl+E puts the selection in the search field) — the one
  editor feature the family gets from its platform text views and the
  WinUI `TextBox` does not have.
- **Print and PDF.** **File ▸ Print…** (Ctrl+P) shows Chromium's print
  preview inside the window, with the printer's own paper and margins;
  **Export ▸ PDF…** and **Share ▸ Rendered PDF…** write real pages at
  **A4** (the default), **A5**, **US Letter**, **US Legal** or a paperback
  trim — **6 × 9″**, **5 × 8″**, **5.5 × 8.5″** — chosen under **Export ▸
  PDF Page Size**, remembered between exports and applied to the book
  compile too. Pages are white with dark ink whatever the theme, set at
  11 pt with half-inch margins; breaks fall between lines of text, and a
  line holding only `\newpage` or `\pagebreak` starts a fresh page. The
  preview, the HTML export and the EPUB never see the page-size setting,
  and a paper printout uses whatever the printer holds.
- **Exports.** **Export ▸ HTML…** writes one self-contained `.html` — every
  diagram already drawn, every formula typeset as selectable text, the
  KaTeX fonts embedded only when the document has formulas — that opens
  anywhere with nothing beside it. **Export ▸ EPUB…** makes a standard
  EPUB 3 whose contents are the document's own headings, with an identifier
  derived from the title so re-exporting updates a reader's copy; math and
  diagrams travel as images and plots as vector. **Export ▸ LaTeX…** writes
  `.tex` in which your mathematics is still the `$…$` you typed, tables
  become `longtable`, footnotes are set at their first citation, front
  matter becomes the title block, and Mermaid / Graphviz / PlantUML / plot
  sources are kept under a comment — there is no chart in the `.tex`.
  **Export ▸ Diagram as SVG ▸** offers one row per Mermaid, Graphviz,
  PlantUML or plot block (math is HTML text, not a drawing, so it is not
  offered) and never writes an empty file. **Export ▸ TextBundle…** writes
  `text.md`, `info.json` and `assets/` into a folder you pick, gathering any
  image linked by a plain relative name beside the document and asking
  before replacing an existing bundle. **Share ▸ Source…** and **Share ▸
  Rendered PDF…** hand the file to the Windows Share pane.
- **Writer mode: books.** A book is a folder — subfolders are chapters,
  Markdown files are articles, ordered by a numeric prefix and then by name,
  no hidden files, no sidecar metadata. **Book ▸ New Book…**, **Open
  Book…**, **Show Book** (Ctrl+Shift+B) and **Close Book**; the book window
  keeps the structure in a slim sidebar and the selected article beside it
  in Edit / Split / Preview, with its own Contents, Notes, word count and
  autosave, and reopens on the last article across launches. Right-click a
  chapter or article to **Rename…**, **Move Up**, **Move Down** or
  **Delete…**, or **Open in New Window**; reordering renumbers the whole
  group with tidy `01-`, `02-` prefixes written back to the file names.
  **Ctrl+Alt+↑ / Ctrl+Alt+↓** walk the manuscript; F11 makes the window a
  writing room. **Share Book as PDF**, **Print Book…** and **Export Book ▸
  PDF… / EPUB… / LaTeX…** compile the whole book — a title page, then every
  chapter and article on a fresh page, at the chosen page size, or as one
  `book`-class `.tex` with chapters as `\chapter` and articles as
  `\section`. **Open Articles in Separate Windows** hands each article to
  its own document window; while one is open there the book pane steps
  aside so two windows never write the same file.
- **Examples.** **File ▸ Examples** opens Welcome, Formatting, Tables,
  Code, Images, Math, Diagrams, Plots and Writer Tools as fresh untitled
  documents of your own, and **Example Book…** unpacks the sample book to a
  folder you choose and opens it. The documents are byte-identical with the
  other editions'.
- **A Window menu** (Minimize, Zoom, and one row per open window) and
  **Help ▸ md Help**, **Privacy Policy** and **About md**, which names the
  version and every bundled engine.

### Changed

Everything below is a deliberate departure from how the macOS app does the
same thing, with the platform reason; whatever is not listed works as it
does on the Mac.

- **The menu bar lives in every window.** Windows has no global menu bar,
  so each document window and the book window carries `File · Edit · View ·
  Book · Go · Window · Help` as its first row; there is still no toolbar on
  a document window. The Mac's **md** menu is gone with it: About is under
  **Help**, Quit is **File ▸ Exit**.
- **Shortcuts are Windows chords.** Ctrl+1 / Ctrl+2 / Ctrl+3 for the
  layouts, Ctrl+Shift+Enter for Zen, Ctrl+Shift+B for Show Book,
  Ctrl+Alt+↑ / Ctrl+Alt+↓ between articles, Ctrl+P to print, and **F11**
  for full screen, the Windows convention.
- **No Versions, no document browser, no autosaved drafts.** macOS
  *Versions* has no Windows analogue and is not promised; a plain launch
  restores the last session or opens one untitled window instead of a
  document browser; untitled drafts are not autosaved (Windows has nothing
  like the Mac's Autosave Information) and are asked about on close. The
  process exits with its last window rather than lingering without a menu
  bar.
- **Lucida Sans Typewriter stands in for American Typewriter** in the editor,
  the preview and on paper, with Courier New for code. Windows does not ship
  the Apple face and md bundles no font, so md.win uses the typewriter face
  Windows does ship — Regular, Bold and Italic all in the box — rather than
  the family's Georgia screen fallback. The rule is appended by the app to the
  screen and print pages only, so the shared stylesheet and every export stay
  byte-identical with the other ports.
- **PDFs are Chromium's.** WebView2 lays pages out at 96 CSS pixels per
  inch where WebKit paginated at 72, in Lucida Sans Typewriter, with
  half-inch margins on every side (the Mac inherits its Page Setup) — same
  page sizes, same
  content, same line-aware breaks, a different rasterizer, so a PDF from
  Windows and a PDF from a Mac are not the same bytes. The HTML, EPUB, SVG,
  LaTeX and TextBundle exports are.
- **Print is Chromium's preview dialog** shown inside the window, with a
  **Done** button because the dialog reports no completion. Its "Headers
  and footers" option starts on and would print the title and the page
  address; untick it once and the choice sticks.
- **TextBundle is a folder, and Windows cannot associate a folder.**
  `.textbundle` opens through **File ▸ Open TextBundle Folder…** or a folder
  drop rather than by double-click; `.textpack` is associated as on the Mac.
  Export writes a `.textbundle` folder through the folder picker and asks
  **Replace** itself, since a save dialog cannot create a folder.
- **Autosave is a timer, and the clobber guard is a stamp.** Windows has no
  `NSFilePresenter` / `NSFileCoordinator`, so the one-second autosave
  writes in place and a change is detected by comparing the file's
  modification time and size with the ones md last wrote, backed by a
  `FileSystemWatcher`; there is no "will terminate" hook either, so the
  loss window at a forced shutdown is that one second.
- **Open Recent is Windows' recent-items list** (25 entries, also feeding
  Windows' Recent and the taskbar Jump List) instead of the Mac's ten.
- **A book's remembered folder is a Windows future-access grant** plus its
  path, not a security-scoped bookmark; **Close Book** removes both. The
  sidebar is a fixed 240 pixels (WinUI has no splitter without a
  third-party toolkit); unnumbered chapter and article names sort by the
  family's natural order rather than Finder's, matching Android.
- **A book compiles the way it edits.** Compile, EPUB and LaTeX read every
  article through the same text decoder the editor uses, so a Windows-1251
  article that edits correctly also compiles correctly — the Mac decodes
  UTF-8 then Latin-1 there, and would compile it as mojibake. UTF-8 books
  are byte-identical either way.
- **The preview's context menu is Copy and Select All**, and Ctrl+wheel
  zoom is off: Reload, Back, Print and Save make no sense for a live
  preview. A `javascript:` link in a document is blocked by a click guard
  as well as by the navigation policy, because Chromium runs one in-page
  without any navigation event.
- **The caret is ink, not accent** — the WinUI `TextBox` has no caret
  brush.

### Fixed

- **A folder whose name ends in `.md` is not an article.** Inside a chapter
  only files are listed, so a stray `Notes.md` folder no longer appears in a
  book's sidebar as an unreadable article, as it can on the Mac.
