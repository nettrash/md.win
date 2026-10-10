# Verifying 1.6 on Windows

What the Mac cannot check. `tools/verify-on-windows.ps1` covers the test suites, the
build and the self-test; this list is the hands-on part for the 1.6 changes, one
section per issue. Tick each on a Windows 11 machine (x64 or ARM64) with a
Release build, then note the date and build here.

## #1 Large PlantUML diagrams

1. Open a document with a ```plantuml sequence diagram of 48 participants
   (`participant P1` … `participant P48`, then `P1 -> P48 : hi`).
   - Preview: the diagram draws, scaled to the page — no "Diagram too large for
     browser rendering" error.
   - Export as HTML, PDF and SVG: the diagram is in each.
   - Export as EPUB: the snapshot is present and not blank (it is drawn at the
     page width, then scaled by the reader).
2. A 3-participant diagram looks exactly as it did in 1.5.

## #2 EPUB metadata and cover

Core and App.Logic carry the rules and the reading (EPUBCheck 5.1.0 passed on
Core's output on macOS); what is left is the real file system, the network and
a real reader.

1. Save `C:\tmp\book\notes.md` with front matter `author`, `language: de`,
   `date: 2026-10-10`, `series: S`, `series_index: 2` and `cover: cover.png`,
   with a PNG `cover.png` beside it. Export ▸ EPUB…: the book opens in a reader
   (e.g. Thorium) with that cover, author and series.
2. Change to `cover: https://<any https PNG/JPEG>` — the cover appears; with the
   network off the export still succeeds, without a cover, after at most 15 s.
3. `cover: ../outside.png` (a file one folder up): no cover, no error.
4. Open the folder as a book, export the article holding `cover: cover.png`
   (beside the article): the cover appears.
5. A document with no front matter exports exactly as in 1.5.

## #3 Formatting commands and chords

Core and App.Logic carry the rules (137 shared vectors), the command table, the
AltGr decision and the setting; the shadow build compiles the WinUI side. The
self-test now checks Format ▸ Bold through the dispatcher (wrap + one undo) and
the third Typing toggle. Left for a person:

1. Every chord on a fresh document: Ctrl+B, Ctrl+I, Ctrl+K, Ctrl+Shift+C/X/L/T/Q,
   Ctrl+Alt+1–6, Ctrl+] and Ctrl+[ — each makes the change docs/formatting.md
   says, and **one** Ctrl+Z undoes it. The same rows from the Format menu (the
   editor keeps the focus afterwards). The same in the Book window's article.
2. The chords from the **preview** in Split (focus in the WebView2): they act on
   the editor's selection, as the menu would.
3. **AltGr**: add German and Polish. German AltGr+2 types ², AltGr+3 types ³ —
   no heading; **left** Ctrl+Alt+2 makes Heading 2. US layout: Ctrl+Alt+2 (either
   Alt) is Heading 2. Check that letting the key through really delivers the
   character to the TextBox (the guard sets `Handled = false`).
4. **Wrap**: select a word, type `*` → `*word*`, the word still selected; the same
   for `_`, `` ` ``, `$`. Ctrl+Z once → `*` alone in its place, again → the word.
   With an IME composing, and for a paste of `*`, nothing wraps. Edit ▸ Typing ▸
   Wrap Selection When Typing … off → typing replaces the selection again; the
   tick follows in every window.
5. Ctrl+Shift+C must not reach WebView2 DevTools, and Ctrl+[ / Ctrl+] work on
   the layouts where those keys are not brackets (they follow the key position,
   VK_OEM_4 / VK_OEM_6).
