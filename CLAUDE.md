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

The app **launches, the editor works, the preview works and every export works**. All four gates and
`md.exe --selftest` are green on this machine (ARM64, WebView2 Runtime 152.0.4191.66).

### Verified on Windows on 2026-09-07 — do not re-litigate these

1. **The preview origin redesign is correct and running.** With
   `SetVirtualHostNameToFolderMapping` in place, `WebResourceRequested` was **never raised** for the
   top-level document, so the navigation failed (`Unknown`, then `ConnectionAborted`) and the
   preview was blank. Microsoft's how-to claims the event still fires "when a requested resource does
   not exist in the folder that is virtually hosted"; it does not, at least not for the document. See
   WebView2Feedback #2103 and #4201. `src/Md.App/Web/AssetHost.cs` uses **no mapping at all**: one
   filter over `https://md.assets/*`, the document served from memory, `rich/` served off disk
   through `Asset()` with `AssetMime`'s types. `md.log` carries `preview index served from memory,
   N bytes` and `preview navigation ok status=200`; KaTeX (inline, display, mhchem `\ce{}`),
   highlight.js, Mermaid, Graphviz, PlantUML and a `plot` fence all draw.
   **The first session here lost an hour to a stale build**: the fix was committed on the Mac but the
   `bin\` output predated it, so the symptom on screen was the *old* code. Check
   `AppX\md.dll`'s timestamp against the source before believing a symptom.
2. **`IsShown` is fixed and the stale-while-collapsed rule engages.** Measured: Edit → Split with no
   edit in between causes **no** reload; typing while collapsed and then switching back causes
   **exactly one**, with the new HTML.
3. **The window opens in Edit for an empty document, and that is the Mac's rule**
   (`ViewModeRule.OpenViewMode`: `isEmptyDocument` → `Edit`). A collapsed pane is never realised, so
   the WebView2 is not created until the writer first asks for Split or Preview — intended, and it
   holds together because `ArticlePanes` raises `LayoutChanged` before `Loaded`. "No preview at
   startup" is not a bug; "no preview after Ctrl+2" would be.
4. **Every export works, in the self-test and in the real app.** `--selftest` is 44/44: the
   self-contained HTML (stands alone, no engines, no `rich/` URLs), the EPUB (7 real PNG snapshots
   for 7 rich elements, via CDP `Page.captureScreenshot`), and PDF at A4 and 6×9 (correct MediaBox,
   three `\newpage` sections → three pages). Driven from the menu, File ▸ Export ▸ PDF… opened the
   real `FileSavePicker` and wrote a 2-page A4 PDF.

### The bug that hid behind all of that — read before touching an adapter

`ExportPipeline` is pure logic and awaits with `ConfigureAwait(false)` end to end, which is right:
it must run where a Mac test can put it. But every seam it drives is implemented in `Md.App` by a
WinUI control, a WinRT picker or a `ContentDialog`, and all three have thread affinity. The first
genuinely asynchronous step in an export moved the rest of the flow onto a thread-pool thread and
every call after it failed with `RPC_E_WRONG_THREAD` — "The application called an interface that was
marshalled for a different thread." HTML, EPUB and both PDFs failed that way; only the checks that
drive `ExportRenderer` straight from the UI thread passed.

The seams are frozen and the pipeline is right, so the marshalling lives at the **adapter boundary**:
`src/Md.App/Services/UiDispatch.cs` hops onto the owning `DispatcherQueue` (inline when already
there), and `ExportRenderer`, `ExportRendererFactory`, `Pickers`, `WinUiAlerts` and `ShareBridge` all
go through it. **Any new adapter behind a frozen seam must do the same** — it will be called from a
thread-pool continuation sooner or later, and a Mac can never catch it.

### Still never executed on Windows

Print (`ShowPrintUI(Browser)` draws inside the control's own rectangle — the overlay exists for
that), share via `IDataTransferManagerInterop`, the open/folder pickers, file-type activation,
single-instance redirection, `FutureAccessList` for books, the Book window end to end, and the MSIX
itself, which has never been built or opened.

`md.exe --selftest <outDir>` drives the real export pipeline over the fixture documents and writes
`report.json`; CI runs it on `windows-latest` (x64 only — the runner cannot execute the ARM64 binary
it builds). On this ARM64 machine it runs natively:

```powershell
dotnet build src\Md.App\Md.App.csproj -p:Platform=ARM64 -p:RuntimeIdentifier=win-arm64 `
  -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:SelfTest=true `
  -p:EnableWinAppRunSupport=false -p:OutDir=<dir>\
# md.exe is a GUI subsystem binary, so the shell does not wait for it and $LASTEXITCODE stays empty:
Start-Process <dir>\md.exe -ArgumentList "--selftest","<report>" -PassThru -Wait
```

**Run it after any change to an export, a renderer or an adapter** — it exercises more WebView2
surface in one go than clicking will, and it is the only thing that catches the threading class of
bug above.

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
