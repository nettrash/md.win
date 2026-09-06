# md for Windows

[![build](https://github.com/nettrash/md.win/actions/workflows/windows.yml/badge.svg)](https://github.com/nettrash/md.win/actions/workflows/windows.yml)

The simplest Markdown editor for Windows 11. Write Markdown on one side and
see it rendered on the other — or switch to a full-window **Edit** or
**Preview**. Built in C# on .NET 10 and WinUI 3 (the Windows App SDK), with
a hand-written Markdown renderer, and packaged as MSIX for the Microsoft
Store. **No accounts, no servers** — your files live wherever you keep them
(on disk, or in your own cloud-synced folders). The .NET side is
dependency-light: nothing beyond Microsoft's Windows App SDK and Windows SDK
build tools (and xUnit for the tests), and the only vendored code is the
offline math / diagram engines under `src/Md.App/rich/` (KaTeX with the
mhchem chemistry extension, Mermaid, Graphviz, PlantUML, and highlight.js
for code).

> This is the Windows port of [**md**](https://github.com/nettrash/md), the
> iPhone / iPad editor, and of its native
> [macOS](https://github.com/nettrash/md.macOS),
> [Android](https://github.com/nettrash/md.Android) and
> [VS Code](https://github.com/nettrash/md.vscode) siblings. All five share
> the same hand-written block parser, renderer, themed HTML export and the
> HTML / EPUB / SVG / LaTeX / TextBundle writers — pinned to one set of
> golden fixtures byte for byte — and this port reimplements them in C#, in
> `src/Md.Core`. The difference worth knowing is where the bytes stop:
> Windows has neither American Typewriter nor WebKit. Everything Core
> *writes* is the family's output to the byte, but the on-screen preview and
> the printed page set the prose in **Georgia** (the family's stand-in, as
> md.vscode uses it) through a style rule the app appends to the screen and
> paper pages only, and PDFs are paginated by Chromium inside WebView2 —
> same page sizes, same content, same line-aware breaks, a different
> rasterizer. And Windows has no `NSDocument`, so what the Mac gets from its
> title bar — **Rename**, **Move To**, **Duplicate**, **Revert to Saved** —
> is written into the File menu here, autosave is a one-second timer that
> writes the file in place behind a clobber guard, and macOS *Versions* has
> no analogue and is not promised.

## Features

- **Document-based, the Windows way.** Open, edit and save `.md` /
  `.markdown` (and `.mdown`, `.markdn`, `.mdtext`) files anywhere through
  the standard Open / Save dialogs, with autosave — one second after the last
  keystroke, written **in place** so the file keeps its identity, ACLs and
  cloud-sync state, and only after checking that nothing else changed the
  file first. **Rename…**, **Move To…**, **Duplicate** and **Revert to
  Saved** sit in the File menu; **Open Recent** is Windows' own recent-items
  list, so the taskbar Jump List works too; files can be dropped on any
  window; and a double-click in File Explorer lands in the running app — md
  is a single instance, and a file that is already open brings its window
  forward instead of opening twice. Plain-text files open too and keep their
  extension, and every file is saved back in the encoding and line endings
  it arrived in (UTF-8 with or without a byte-order mark, UTF-16 only behind
  one, Windows-1251). A **TextBundle** (`.textbundle` — a folder, so it opens
  through **File ▸ Open TextBundle Folder…** or by dropping the folder on a
  window; Windows cannot associate a folder with an app) or **TextPack**
  (`.textpack`, by double-click) — the Markdown-with-images container
  Ulysses, iA Writer and Bear write — opens too, imported as its text for
  editing (the bundle's own `assets/` images aren't shown in the preview, and
  the bundle is never written back, so its assets are never lost).
- **Nothing lost.** If a file changes on disk under unsaved edits nothing is
  clobbered: a bar offers **Reload from Disk** or **Keep My Version**. If a
  save fails while a window is closing, the text is kept beside the
  document as `<name> (rescued)` with the original extension — `Notes
  (rescued).md` — and the window says so. On a
  plain launch md reopens the saved documents you had open, each in its
  layout and where its window was. Untitled drafts are the one thing not
  autosaved — Windows has no equivalent of the Mac's Autosave Information —
  so a dirty untitled window asks before it closes.
- **Live preview.** A built-in renderer covers the everyday Markdown you
  actually write:
  - Headings (`#`–`######`)
  - **Bold**, *italic*, `inline code`, [links](https://nettrash.me) and
    ~~strikethrough~~
  - Bullet, numbered and **task lists** (`- [ ]` / `- [x]`), with nesting
  - Fenced code blocks (```` ``` ```` and `~~~`), with horizontal scroll —
    **syntax-highlighted** in md's own quiet paper palette when the fence
    names a language (`csharp`, `js`, …); a bare fence stays plain
  - Block quotes (including nested)
  - GitHub-style tables, with column alignment
  - **CSV / TSV blocks** (` ```csv `, ` ```tsv `) — data pasted straight
    out of a spreadsheet drawn as a table, quoted fields and all, with
    all-number columns lined up on the right; the source stays the data,
    so it can be replaced wholesale when the numbers change
  - Thematic breaks (`---`) and page breaks (`\newpage` / `\pagebreak`)
  - YAML / TOML **front matter** (`---` … `---` or `+++` … `+++`) at the
    very top of a file — recognised as metadata and hidden from the page,
    print and PDF, instead of showing up as a rule and stray text
  - **Footnotes** (`[^id]` in the text, `[^id]: the note` on a line of its
    own) — gathered under a rule at the foot of the rendered page and
    numbered in the order a reader meets them, each reference linking down
    to its note and each cited note linking back
- **A deliberate subset, not a CommonMark engine.** The renderer is the
  family's renderer and inherits its omissions on purpose: raw HTML is
  escaped rather than passed through, there are no reference-style links, a
  four-space indent is a paragraph continuation rather than a code block,
  and a single `$` in prose is left alone (`$5 and $10` is money, not math).
  Each is a decision four shipping ports already made; changing one here
  would make five documents out of one.
- **Math and diagrams.** TeX/LaTeX math (`$…$`, `$$…$$` and ` ```math `) —
  with **chemistry** notation (`\ce{…}` / `\pu{…}`) via the bundled mhchem
  extension — plus **Mermaid** (` ```mermaid `), **Graphviz** (` ```dot `, ` ```graphviz `
  or ` ```gv `, and every layout program — `neato`, `circo`, `fdp`, `sfdp`,
  `twopi`, `osage`, `patchwork` — usable as the block language) and
  **PlantUML** (` ```plantuml `), all drawn on your PC by the vendored
  engines inside WebView2 and carried through to print and PDF. A raw
  `.puml`, `.plantuml` or `.gv` file opens and renders as the diagram it
  describes, source still editable. `.dot` is deliberately unclaimed —
  Windows, like macOS, treats a `.dot` as a Word template — so rename such a
  file to `.gv`; a ` ```dot ` fence inside a document is unaffected.
- **Plots** (` ```plot `). Write a function and md draws it — one line per
  curve, with optional `x` / `y` ranges, `title`, `xlabel`, `ylabel`,
  `legend`, `grid`, `axes`, `width`, `height` and `samples` above them.
  Curves can be named (`envelope = exp(-abs(x)/5)`, which is what the legend
  shows), parametric (`(cos(t), sin(t)) for t in 0..2*pi`) or plain data
  (`measured = points: 0,0 1,2 2,1`), over the usual arithmetic with `pi`,
  `e`, the trigonometric, hyperbolic, logarithmic and rounding functions you
  would expect, `atan2`, `pow`, `hypot`, and comparisons that are numbers,
  so `(x > 0) * sqrt(x)` draws exactly the half it names. Alone among the
  rich blocks it bundles **no engine at all**: the chart is a real vector
  `<svg>` in the page before any script runs, which is why it works in the
  preview, in print, in PDF, in an exported HTML page, in an EPUB (as
  vector, not a photograph of one) and in Export Diagram as SVG… A block
  that cannot be read keeps its source under one `plot: …` line.
- **Three layouts.** *Edit*, *Split* (side by side, re-rendering as you
  type and scrolling as one — it stacks vertically when the window is
  narrower than 640 pixels) and *Preview*, chosen from the **View** menu
  (Ctrl+1 / Ctrl+2 / Ctrl+3). The layout is remembered **for each file**,
  so a document comes back in the one you left it in; the 200 most recently
  opened are kept, on this PC, and nothing is sent anywhere. A file md has
  not seen before opens in Split; a new document opens in Edit; saving an
  untitled document for the first time keeps the mode you were in; renaming
  or moving a file in File Explorer lets it start over as unseen (Rename…
  and Move To… inside md re-run the same rule for the new name). Books are
  left out on purpose — a book keeps one layout throughout, so stepping
  between chapters never changes the view under you — and an article opened
  in its own window records nothing. Only a mode you *pick* is remembered:
  jumping to a note brings the editor up, but that is a move, not a choice.
- **Zen mode.** **View ▸ Zen Mode** (Ctrl+Shift+Enter) takes the window
  full screen and leaves one column two-thirds of the screen wide on bare
  paper, with a quiet floating switch between writing and reading that
  fades when the pointer rests; Ctrl+1 / Ctrl+3 do the same, Esc or leaving
  full screen (F11) drops it. It is per window.
- **Writer tools.** **Go ▸ Contents** lists every heading and jumps both
  panes to it; **Go ▸ Notes** lists your private author notes — an HTML
  comment on a line of its own whose text begins with `note:` — and jumps
  the editor to one; they never reach the preview, the PDF or the printout
  (inline, a comment renders as text). A live **word · character** count
  sits under every page, counted with the ICU word rules Windows ships in
  `icu.dll`, so CJK text counts as it does on the Mac and Android. A
  **Find** bar (Ctrl+F, F3 / Shift+F3, Ctrl+E for the selection) is a
  Windows addition: the WinUI `TextBox` ships without one.
- **Typewriter feel.** Warm paper background (light "fresh paper" / dark
  "carbon paper", following the system theme) with prose set in
  **Georgia** and code in **Courier New** — the Windows stand-ins for the
  family's American Typewriter, which Windows does not ship and md does not
  bundle. The shared stylesheet stays byte-identical with the other ports;
  the app appends the Georgia rule to the screen and print pages only, so
  every export carries the family's font stack untouched.
- **Editing you'd expect.** A plain, undo-aware `TextBox` editor driven by
  the standard **Edit ▸ Undo / Redo**, Tab inserting a tab, Markdown
  punctuation left literal (no smart quotes, no dash substitution, no
  spell-check underlines), and every keystroke flowing to the one-second
  autosave.
- **Print & share.** Print the *rendered* document (**File ▸ Print…**,
  Ctrl+P) through Chromium's print preview inside the window — paper and
  margins are the dialog's; untick its "Headers and footers" once and it
  stays off. Export it as a PDF at A4 (the default), A5, US Letter or
  US Legal, or a print-on-demand trim size (6 × 9″, 5 × 8″, 5.5 × 8.5″), the
  choice remembered and applied to the book compile too — real pages, white
  paper and dark ink whatever the theme, 11 pt, half-inch margins, breaks
  that fall between lines of text, `\newpage` starting a fresh page. Export
  it as one self-contained `.html` file that opens anywhere with nothing
  beside it (diagrams as drawings, formulas as selectable text), as an
  **EPUB** e-book with the document's own headings as its table of contents,
  as LaTeX `.tex` source (formulas as the `$…$` you typed rather than a
  picture of them; diagram and plot sources kept under a comment), as a
  single **diagram** (Mermaid, Graphviz, PlantUML or a plot — math is HTML
  text, not a drawing, so it isn't offered) in a standalone `.svg`, or as a
  **TextBundle** folder with any local images it references gathered into
  the bundle's `assets/` (you pick the parent folder, and md asks before
  replacing an existing bundle). **Share ▸ Source…** and **Share ▸ Rendered
  PDF…** hand the file to the Windows Share pane.
- **Writer mode: books.** A book is a folder — subfolders are chapters,
  Markdown files are articles, ordered by a numeric prefix and then by name.
  **Book ▸ New Book…**, **Open Book…**, **Show Book** (Ctrl+Shift+B) and
  **Close Book**; the book window keeps the structure in a slim sidebar and
  the selected article beside it in Edit / Split / Preview, with its own
  Contents, Notes and word count, saving as you type. Right-click to
  **Rename**, **Move Up**, **Move Down** or **Delete** chapters and articles
  — reordering renumbers the group and writes the order back into the file
  names, so it stays true in File Explorer. Walk the manuscript with
  Ctrl+Alt+↑ / Ctrl+Alt+↓, take it full screen (F11) for a writing room,
  and compile the whole book to one PDF at the chosen page size, print it,
  or export it as **EPUB 3** or a `book`-class LaTeX file. The book is
  remembered across launches through Windows' future-access list; **Open
  Articles in Separate Windows** hands each article to its own document
  window, and while one is open there the book pane steps aside so the two
  never write the same file.
- **Light / dark and text selection** throughout. The only thing md fetches
  from the network is an image your own document points at by URL — see
  [PRIVACY.md](PRIVACY.md).

## Platforms

- Windows **11** (build 22000) or later, **x64** and **ARM64**.
- No Windows 10 package: Windows 11 ships the WebView2 Evergreen runtime in
  the box, so there is nothing to bootstrap, and no 32-bit Windows 11
  exists, so there is no x86 build.

## Build

The parity core and the shell's logic are plain .NET and build wherever the
.NET 10 SDK runs (`global.json` pins it); only the WinUI app needs Windows,
because the XAML compiler is a Windows executable.

```bash
# The parity core — parser, renderer, exports — against the shared golden fixtures. Any OS.
dotnet test tests/Md.Core.Tests/Md.Core.Tests.csproj

# The shell's logic — documents, commands, view modes, export pipeline. Any OS.
dotnet test tests/Md.App.Logic.Tests/Md.App.Logic.Tests.csproj

# The WinUI app. Windows only.
dotnet build src/Md.App/Md.App.csproj -p:Platform=x64

# On a Mac or Linux: type-check the app's code-behind and XAML against the real WinUI API surface.
tools/xamlcheck/run.sh
```

The MSIX is produced by CI (`.github/workflows/windows.yml`), which builds
`src/Md.App` with `msbuild … -p:GenerateAppxPackageOnBuild=true` for x64 and
ARM64 and uploads the packages **unsigned** — the Store signs what it
publishes. There is no auto-incremented build number as on iOS, macOS and
Android: the MSIX `Version` in `Package.appxmanifest`
(`Major.Minor.Build.Revision`, Revision 0 for the Store) *is* the build
number, and the family's `1.0` ships as `1.0.0.0`.

## Project layout

| Path | What lives there |
| --- | --- |
| `src/Md.Core` | The parity core, pure .NET: block parser and inline renderer, the themed HTML writer and its stylesheet, the plot engine, the HTML / EPUB / LaTeX / TextBundle exporters, the book model, the per-file view-mode memory and the text codec. Pinned byte for byte against the fixtures shared with the other ports. |
| `src/Md.App.Logic` | The shell's logic without a line of WinUI: the command table and shortcuts, activation routing, the document session (autosave, clobber guard, rescue copies, rename validation), the view-mode and Zen controllers, the export pipeline, the book navigator, the settings keys and the palette. Testable on any OS. |
| `src/Md.App` | The thin WinUI 3 layer: windows and menus, the `TextBox` editor, the WebView2 preview and export renderers, pickers, Share, print — plus `rich/` (the engines), `Examples/`, `Assets/` and `Package.appxmanifest`. |
| `tests/Md.Core.Tests` | xUnit golden-fixture suite for the core (`Fixtures/` is the family's shared set). |
| `tests/Md.App.Logic.Tests` | xUnit suite for the shell's logic — documents, commands, view modes, export pipeline — driven through fakes of the `Seams/` interfaces; runs on any OS. |
| `tools/xamlcheck` | The off-Windows compile check: a shadow library build plus a XAML lint against the real WinUI metadata. |
| `store/` | Microsoft Store listing copy, one plain-text file per Partner Center field, with the limits table in its README. |

## License

MIT — see [LICENSE](LICENSE). © 2026 nettrash.
