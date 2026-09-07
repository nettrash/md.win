# CLAUDE.md — md for Windows

Guidance for Claude Code working in this repository. The whole port was written on a Mac and has
only just started running on Windows, so the most valuable thing a session here can do is **execute
things and report what actually happens** — that is the one thing the Mac could never do.

## What this is

`md.win` is the fifth port of the md Markdown editor: C# / .NET 10 / WinUI 3 on Windows App SDK
2.4.0, Windows 11 only, packaged as MSIX for the Microsoft Store, version 1.0 with feature parity to
md 1.4. Its siblings are `md` (iOS/iPadOS), `md.macOS`, `md.Android` and `md.vscode`, all under
`~/Develop/nettrash.me/`. **md.macOS is the source of truth** for behaviour.

**Byte parity is the contract, not an aspiration.** The rendered HTML, the stylesheet, the plot SVG,
the EPUB and the LaTeX must match what the other four ports produce, byte for byte. That is pinned by
`tests/Md.Core.Tests/GoldenTests.cs` against a corpus shared with md.vscode. Fed md.vscode's own
`test.md`, this renderer reproduces md.vscode's `test.html` exactly. Never "improve" a rendering
detail: if it changes a golden, it is a bug in the change, not in the golden.

## Layout

| Path | What | Builds on |
| --- | --- | --- |
| `src/Md.Core` | Parser, HTML writer + CSS, plot engine, LaTeX/EPUB/HTML/TextBundle/PDF-styling exports, zip, text codec, book model, view-mode memory. No Windows dependency. | any OS |
| `src/Md.App.Logic` | Everything testable that is not WinUI: commands, documents, view modes, preview coordination, export pipelines, books. Talks to the app through the frozen `Seams/`. | any OS |
| `src/Md.App` | The thin WinUI 3 layer: windows, controls, WebView2 hosting, pickers, share, MSIX manifest. | Windows only |
| `tests/Md.Core.Tests`, `tests/Md.App.Logic.Tests` | xUnit. | any OS |
| `tools/xamlcheck` | Lints XAML against real WinUI metadata and shadow-compiles `src/Md.App/**/*.cs` — how the Mac type-checked the app. | any OS |
| `docs/` | `shell-design.md` is law; `core-api.md` (Md.Core API + each exporter's app-side contract), `app-api.md` (what each work package published), `windows-facts.md` (toolchain and Store facts). | — |
| `store/` | Partner Center copy, one file per field, counted against the limits. | — |

## The four gates

Run all four after any change; they were green at the last Mac commit.

```powershell
dotnet test tests/Md.Core.Tests/Md.Core.Tests.csproj        # 1271 passed
dotnet test tests/Md.App.Logic.Tests/Md.App.Logic.Tests.csproj  # 1215 passed
dotnet build src/Md.App/Md.App.csproj -p:Platform=ARM64     # or x64 — Windows only
bash tools/xamlcheck/run.sh                                 # 7 XAML files, 70 named elements, 0 errors
```

On Windows the third gate is a **real build**, which the Mac never had — it runs the XAML compiler and
MakePri. Expect it to find things `xamlcheck` structurally cannot: resource lookups, `x:Class`
mismatches, the two-prefix `ui:Grid.Row` usage in `DocumentWindow.xaml`, generated-code conflicts.
Those are real findings, not noise.

## Running it

A build alone is not something you can double-click, and getting this wrong looks exactly like "the
app does nothing":

```powershell
dotnet run --project src\Md.App\Md.App.csproj -p:Platform=ARM64
```

`WindowsPackageType` is at its default (`MSIX`), so the SDK compiles in the **Deployment Manager**
auto-initializer — a module initializer whose WinRT class needs package identity. Start
`bin\…\md.exe` directly and it dies with `REGDB_E_CLASSNOTREG` **before `Main`**, so nothing in this
repo can catch or report it. `dotnet run` registers a debug identity; `-p:WindowsPackageType=None`
builds a genuinely unpackaged binary (Bootstrap initializer instead) and is what CI's `--selftest`
leg uses. README's *Running it on Windows* has all three routes.

**The log is the first thing to read when anything misbehaves:**

```powershell
Get-ChildItem "$env:LOCALAPPDATA\Packages\*md*\LocalState\md.log","$env:LOCALAPPDATA\md\md.log" `
  -ErrorAction SilentlyContinue | Get-Content -Tail 40
```

It records startup failures after `Main`, single-instance redirects, the preview's asset root, whether
the document was served, and every navigation failure with its `WebErrorStatus`.

## State as of 2026-09-07 — read this before anything else

The app **launches and the editor works**. Everything below is live.

**Just changed on the Mac, committed or not, and NEVER RUN on Windows** — verifying these is the next
job:

1. **The preview origin was redesigned.** The first Windows run proved the design's core assumption
   false: with `SetVirtualHostNameToFolderMapping` in place, `WebResourceRequested` was **never
   raised** for the top-level document, so the navigation failed (`Unknown`, then
   `ConnectionAborted`) and the preview was blank. Microsoft's how-to claims the event still fires
   "when a requested resource does not exist in the folder that is virtually hosted"; it does not,
   at least not for the document. See WebView2Feedback #2103 and #4201.
   `src/Md.App/Web/AssetHost.cs` now uses **no mapping at all**: one filter over
   `https://md.assets/*`, the document served from memory, `rich/` served off disk through
   `Asset()` with `AssetMime`'s types. Same origin, so relative `rich/…`,
   `import('./plantuml.js')` and KaTeX's `url(fonts/…)` still resolve.
   **Verify:** the preview renders text at all; `md.log` carries `preview index served from memory,
   N bytes`; math, Mermaid, Graphviz, PlantUML, highlight.js and a `plot` fence all draw (open
   `Examples ▸ 07-Diagrams` and `08-Plots`).
2. **`DocumentWindow.Publish()` now waits for a `_ready` flag** set at the end of the constructor. A
   `TextBox` raises `SelectionChanged` while it is initialising, which reached `Build()` on a
   half-built window and threw `NullReferenceException` onto the XAML dispatcher. **Which member was
   null was inferred, not observed** — if it recurs, the fresh `md.log` entry names it.
3. **`IsShown` was wrong and is fixed.** `ArticlePanes.Apply()` collapses `PreviewSlot`, but
   `IsShown` read the host's and the WebView2's own `Visibility`, which WinUI does not inherit — so
   it always answered "shown" and the "stale while collapsed" rule never engaged. It now walks the
   visual tree. **Verify:** switching to Edit and back to Split reloads the preview exactly once.

**Nothing in the WinUI layer beyond launch and the editor has ever been executed.** In rough order of
how much is riding on it: the preview (above), Print (`ShowPrintUI(Browser)` draws inside the
control's own rectangle — the overlay exists for that), PDF export via `PrintToPdfAsync`, the EPUB
snapshot path via CDP `Page.captureScreenshot`, share via `IDataTransferManagerInterop`, the pickers,
file-type activation, single-instance redirection, `FutureAccessList` for books, and the MSIX itself,
which has never been built or opened.

`md.exe --selftest <outDir>` (built with `-p:SelfTest=true -p:WindowsPackageType=None`) drives the
real export pipeline over the fixture documents and writes `report.json`. It is the only automated
proof these paths can get; CI runs it on `windows-latest`. **Run it early** — it exercises more
WebView2 surface in one go than clicking will.

## Rules that are not negotiable

- **No `x:Bind`, no `{Binding}`.** XAML carries static structure and `x:Name` only; everything is
  wired from code-behind. `x:Bind` also defeats `tools/xamlcheck` on non-Windows machines.
- **Never edit `src/Md.App.Logic/Seams/`.** Those interfaces are frozen; every package codes against
  them, and the fakes in `tests/Md.App.Logic.Tests/Fakes/` implement them.
- **Window classes live in namespace `Md.App`, not `Md.App.Windows`.** A `Md.App.Windows` namespace
  shadows the global `Windows` namespace and breaks `Windows.Storage` / `Windows.UI` in four
  unrelated files. The folder is still `Windows/`.
- **Logic in `Md.App.Logic`, wiring in `Md.App`.** If a rule can be tested without Windows, it
  belongs behind a seam with a test. `Md.App` may not be unit-tested at all, which is exactly why it
  must stay thin.
- **No third-party NuGet.** WinUI 3 / Windows App SDK / WinRT and the BCL only — no CommunityToolkit.
  This is a house rule across the md family.
- User-facing strings live in `src/Md.App.Logic/Strings.cs`; append, never rewrite.
- Ordinal string comparisons and `InvariantCulture` numerics everywhere. Grammar code must not use
  `char.IsWhiteSpace`, `Trim()`, `Split()` or `\s\w\d` — use `Md.Core.Text.Whitespace` /
  `ScalarText`, or spell the class out. These are not style preferences: each one is a
  cross-port divergence the other four ports already paid for.
- **`ScreenHtml.WithWindowsFonts` (the Georgia stand-in for American Typewriter) must never reach an
  export.** Screen and paper get it; HTML, EPUB and SVG get pure Md.Core output. A test pins it —
  that is how byte parity would die quietly.
- Never weaken or delete a test to make something pass.

## Traps already paid for

- **libm is per-platform.** `PlotTests` allows ≤ 1 ulp only for IEEE-754 *recommended* operations
  (`asin`, `atanh`, `exp`, `pow`, …); everything else stays bit-exact. glibc and UCRT each disagree
  with the Darwin-recorded oracle in one row, in opposite directions. Do not widen this.
- **A mutation harness needs a timeout and a proven restore.** A harness without both once left a
  live mutant in the parser that hung the whole suite. Restore, then `cmp`, then `touch` — MSBuild
  skips a rebuild when a restored file has an older mtime, which leaves the mutant running.
- **A fake `IUiThread` must queue, not run inline.** `DispatcherQueue.TryEnqueue` marshals; a fake
  that calls the action on the caller's thread invents races the product cannot have.
- **`<Content>` needs `CopyToOutputDirectory`** or it reaches neither the output directory nor the
  MSIX payload. CI has two steps that unzip the package and check.

## Working agreements

- **nettrash commits and pushes himself.** Do not commit, do not push, do not create or push tags.
  Leave the tree clean and say what changed.
- The author is `nettrash <nettrash@nettrash.me>`; MIT, © 2026 nettrash.
- Store copy must never claim "no third-party dependencies", "zero permissions" or "no network
  access", must not use `<` or `>` in `store/*.txt`, and describes the bundled engines as "open
  source and bundled" — never that their licence texts are published. `store/README.md` has the field
  limits and the submission checklist.
- Still open and only doable here or in Partner Center: screenshots (≥ 1366×768, at least one), the
  IARC questionnaire, reserving the product name `md`, swapping the `Identity` placeholders in
  `Package.appxmanifest`, and deploying nettrash.me so the mandatory privacy URL answers 200.
