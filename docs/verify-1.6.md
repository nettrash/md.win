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
