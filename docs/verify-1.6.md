# Verifying 1.6 on Windows

What the Mac cannot check. `tools/verify-on-windows.ps1` covers the test suites, the
build and the self-test; this list is the hands-on part for the 1.6 changes, one
section per issue. Tick each on a Windows 11 machine (x64 or ARM64) with a
Release build, then note the date and build here.

## #1 Large PlantUML diagrams

1. Open a document with a `plantuml` fenced sequence diagram of 48 participants
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
