<!-- Snapshot of the shell design the Windows port was implemented from (2026-09-06). Where the code and this document disagree, the code and its tests win; §14 lists the deliberate deviations from md.macOS. -->

# Md.App shell design — FINAL

The design for `src/Md.App` (WinUI 3, Windows App SDK 2.4.0, .NET 10, `net10.0-windows10.0.26100.0`,
Windows 11 minimum, MSIX for the Microsoft Store) and for the plain-.NET library that carries its
logic, `src/Md.App.Logic`. Feature target: parity with md.macOS 1.4. Written for engineers who cannot
compile or run the app on their machine: every WinUI / WinRT / WebView2 / Win32 type and member named
below was checked against learn.microsoft.com on 2026-09-06 (Appendix A lists what was checked and
what it corrected in the three candidate designs).

Starting point: the fidelity-first design (highest total score) with every graft the three judges
listed and every defect they found fixed. Where the three designs disagreed on a platform fact, the
documentation decided.

Sources: `understand/shell.md`, `rich.md`, `export.md`, `book.md`, `product.md` (requirements),
`parser.md` / `html.md` / `plot.md` / `latex.md` / `tests.md` (Core surface), `facts/windows-facts.md`
(toolchain, Store, typography decision), the three candidate designs and the judges' verdicts, and the
files already in `md.win` (`Md.App.csproj`, `Package.appxmanifest`, `app.manifest`, `Md.Core.csproj`,
`tools/xamlcheck`, `.github/workflows/windows.yml`).

Conventions. "Mac" = md.macOS 1.4, the source of truth. WinUI types are `Microsoft.UI.Xaml.*` unless
another namespace is written; WebView2 types are the **WinRT** projection `Microsoft.Web.WebView2.Core`
(the one a WinUI 3 app compiles against — `IAsyncOperation<T>` results, `IRandomAccessStream`
content, `CreateWithOptionsAsync`), not the WPF `.NET` flavour. "epx" = XAML effective pixels
(1/96 in); the Mac's points (1/72 in) convert as `epx = pt × 4 / 3`. Shortcuts are written the Windows
way (`Ctrl+1`). Strings in quotes are copied verbatim from the Mac and ship byte-identical (U+2026
ellipsis, U+2014 em dash, U+00B7 middle dot, U+201C/U+201D curly quotes where the Mac has them).

---

## 0. Decisions in one screen

| Topic | Decision | Why (one line) |
| --- | --- | --- |
| Process / window model | Single instance (`AppInstance.FindOrRegisterForKey("me.nettrash.md")` + redirection); one `DocumentWindow` per file; one app-wide `BookWindow` | The NSDocument shape; every window-scoped Mac behaviour (per-window mode, Zen, focused values, "Open Articles in Separate Windows") ports without renaming a string |
| Chrome | `MenuBar` as the first row of every window under the **standard system title bar**, tinted paper/ink through `AppWindowTitleBar` colour properties; no toolbar on document windows; the Book window keeps its detail `CommandBar` | The Mac's whole chrome is the menu bar; the standard title bar needs no drag-region maths that only a Windows run can verify (judges, risk-first) |
| Commands | One declarative `CommandTable` in `Md.App.Logic`; menus built in code from it; every chord registered once as a `KeyboardAccelerator` on the window root with a per-chord 150 ms debounce | Fires while the `TextBox` or the `WebView2` has focus; the debounce absorbs the documented double-fire when WebView2 forwards accelerators (microsoft-ui-xaml #6231) |
| Menu items the Mac gets from NSDocument | **Rename…, Move To…, Duplicate, Revert to Saved** implemented (`StorageFile.RenameAsync` / `MoveAsync`, a new untitled window, the last-explicit-save snapshot) | product.md §1.1: "Windows must supply Save As / Rename"; Save As alone is not a rename |
| Editor | `TextBox`, **Georgia 20 epx** (= 15 pt), `PlaceholderText "# Start writing…"`, Tab inserted in `KeyDown` (no `AcceptsTab` exists), `\r` normalised in the session | facts: Georgia is the family's stand-in; sizes are epx not pt |
| Preview origin | **No virtual-host mapping.** One `WebResourceRequested` filter over `https://md.assets/*` (3-argument, `Context.All`): `index.html` from memory (`Cache-Control: no-store`), `rich/…` off `<install>\web` through `AssetMime`; `Reload()` re-requests it | Byte-identical HTML with relative `rich/…`; `web\` (not the install root) so `md.dll` is not fetchable by the page. A mapping was the first design and does **not** work — see §4.2 |
| Two HTML entry points | **Screen/paper** HTML = Core HTML + the app-appended `<style id="md-win-fonts">` (live preview, Print, PDF); **Export** HTML = pure Core HTML (HTML/EPUB/SVG exports) | facts "Typography decision": Georgia on screen and paper, byte-pure exports |
| Re-render | 350 ms trailing debounce → `window.scrollY` → `Reload()` → restore in `NavigationCompleted`; first load and token change immediate; stale-while-collapsed | Verbatim Mac policy |
| Links | Pure `LinkPolicy.Decide` (host check **before** the http(s) branch: `https://md.assets/other` → Cancel) + injected capture-phase click/auxclick guard for everything that is not `#…` or http(s) | Chromium runs a clicked `javascript:` href in-page without any navigation event |
| Render-complete | Poll `data-md-render-complete` every 250 ms, 480 attempts, JSON `"1"` with quotes, timeout = success | `md-init.js` stays byte-identical |
| Export renderer | Fresh `WebView2` per export inside the requesting window's `ExportCanvas` (`Canvas.Left = -10000`, `Visibility.Visible`), DPI pinned with CDP `Emulation.setDeviceMetricsOverride` (595×842, dsf 1); one shared `CoreWebView2Environment` with `--disable-background-timer-throttling`; fallback: a window shown with `AppWindow.Show(false)` | Chromium throttles hidden pages by `IsVisible`, not screen position; a never-activated `Window` is never shown (fidelity defect fixed) |
| Print | In-window **PrintOverlay** (visible `WebView2`, paper HTML) → `ShowPrintUI(CoreWebView2PrintDialogKind.Browser)`; **Done** button; `System` kind as fallback | The Browser preview draws inside the WebView2's bounds and is not displayed for a hidden control (WebView2Feedback #3361); `ShowPrintUI` has no completion event |
| PDF | `PrintToPdfAsync(tempPath, settings)`: `PageWidth/Height = pt / 72`, portrait, scale 1, backgrounds on, header/footer off, **0.5 in margins**; then copied over the picker's file | Margins are a decision (Mac inherits Page Setup, Android 0.5, VS Code 0); pinned by a MediaBox/page-count self-test |
| EPUB snapshots | CDP `Page.captureScreenshot {clip, scale:2, captureBeyondViewport:true}` after the Mac's grow-to-content + 300 ms settle; DOM-count vs markup-count mismatch throws | Photographs KaTeX like `WKSnapshotConfiguration`; Mermaid's `max-width:100%` layout must be final before rects are read |
| Documents | Pickers for consent, `System.IO` on `StorageFile.Path`; autosave 1 s after the last keystroke with the (mtime,size) clobber guard; **in-place `FileStream` overwrite**; "— Edited" cleared only by an explicit Save | NSDocument autosave-in-place made honest; write-then-rename would break file identity and fire our own watcher (risk-first defect fixed) |
| TextBundle | Import `.textpack` (association/picker/drop) and `.textbundle` folders (File ▸ Open TextBundle Folder…, folder drop) as **read-only documents titled after the bundle**; export a **`.textbundle` folder** via `FolderPicker` + Replace dialog | Product parity (the Mac product is a folder bundle) |
| Per-window state & restoration | `LocalFolder\session.json` restores saved documents with mode/Zen/ZenReading and placement on a plain launch | The real Windows home for `@SceneStorage` and window frames (native's idea) |
| Word count | `IcuWordCounter` over Windows' in-box `icu.dll` (`ubrk_open(UBRK_WORD)`) behind `IWordCounter`; `SimpleWordCounter` only as a fallback | The footer is a user-visible number; ICU is the only way to match macOS/Android for CJK |
| Book compile decoder | Unified on `PlainTextCodec.Decode` for compile/EPUB/LaTeX — a deliberate, recorded divergence for legacy CP1251 books | book.md §7.2 asks for an explicit decision |
| Logic home | `src/Md.App.Logic` (plain `net10.0`, no WinUI/WinRT types) + `tests/Md.App.Logic.Tests` | The task's requirement; Md.App cannot be tested off Windows |
| Capabilities / Store | `runFullTrust` only; privacy policy URL mandatory (policy 10.5.1); PRIVACY enumerates every key, file and the WebView2/Store telemetry | facts/windows-facts.md |

---

## 1. Application and window model; activation

### 1.1 `Program.Main`

`Md.App.csproj` defines `DISABLE_XAML_GENERATED_MAIN` (`<DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>`)
and `<StartupObject>Md.App.Program</StartupObject>`. Note for 2.3.1+ (release notes): the switch no
longer deletes the generated entry point, it **renames** it to `XamlGeneratedProgram.XamlGeneratedMain()`;
our `Program.Main` is therefore the only `Main`, and `StartupObject` makes that explicit.

```csharp
// src/Md.App/Program.cs
internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);          // CP1251 for Md.Core.Text.PlainTextCodec

        var current    = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent();
        var activation = current.GetActivatedEventArgs();                        // AppActivationArguments
        var main       = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("me.nettrash.md");
        if (!main.IsCurrent)
        {
            Redirection.RedirectAndWait(main, activation);                       // §1.1.1
            return 0;
        }

        Microsoft.UI.Xaml.Application.Start(_ =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            new App(activation);
        });
        return 0;
    }
}
```

`DispatcherQueueSynchronizationContext` is the C#-projection helper the XAML-generated `Main` installs
(`App.g.i.cs`); without `SetSynchronizationContext` every `await` after a picker or a WebView2 call
would resume on a thread-pool thread (risk-first and fidelity omitted it — fixed).

#### 1.1.1 `Redirection.RedirectAndWait` — the documented pattern

`RedirectActivationToAsync` must not be awaited on the STA thread. The App SDK instancing article's C#
sample does the redirect on a worker thread and waits on a semaphore; that is what we use (no P/Invoke):

```csharp
static void RedirectAndWait(AppInstance target, AppActivationArguments args)
{
    var done = new SemaphoreSlim(0, 1);
    Task.Run(() => { target.RedirectActivationToAsync(args).AsTask().Wait(); done.Release(); });
    done.Wait();
}
```

Contingency (written, off): the Windows Developer Blog "single-instanced, part 3" variant with
`CreateEvent`/`SetEvent` + `ole32!CoWaitForMultipleObjects` (P/Invoke) if the semaphore wait ever deadlocks
on a machine. Both are pre-window, pre-pump; no XAML exists yet.

After the main instance receives a redirected activation it activates the target window **and** calls
`user32!SetForegroundWindow(hwnd)` (P/Invoke, `[LibraryImport]`) — a process that is not foreground
cannot bring its window forward without it (the classic "the file opened but the window stayed behind").

### 1.2 Activation routing

`App(AppActivationArguments first)` handles the first activation in `OnLaunched` (ignoring the
`LaunchActivatedEventArgs` parameter — it is not the real activation) and subscribes
`AppInstance.GetCurrent().Activated += OnRedirected` (raised on a background thread → `DispatcherQueue.TryEnqueue`).

`Md.App.Logic.Activation.ActivationRouter.Route(ActivationDescription) → IReadOnlyList<ActivationAction>` (pure):

| `ExtendedActivationKind` | Data | Actions |
| --- | --- | --- |
| `File` | `((IFileActivatedEventArgs)args.Data).Files` | per `StorageFile`: `.textpack` → `ImportTextPack(path)`; else `OpenPath(path)`; a `StorageFolder` whose name ends in `.textbundle` → `ImportTextBundleFolder(path)`; other folders ignored |
| `Launch`, non-empty `ILaunchActivatedEventArgs.Arguments` | shell-quoted paths (unpackaged/debug) | one `OpenPath` per argument |
| `Launch`, no arguments, **first** activation | — | `RestoreSession` if `session.json` lists saved documents that still exist, else `OpenUntitled` |
| `Launch`, no arguments, redirected | — | `OpenUntitled` (what clicking the Dock icon gives on the Mac: ⌘N) |
| anything else | — | treated as `Launch` |

`WindowManager` (Md.App) executes the actions: a path already open (`WindowRegistry.FindByPath`,
canonical, `OrdinalIgnoreCase`) → activate that window instead of opening a second (NSDocumentController
behaviour); the same *untitled* example twice → two windows (as on the Mac).

### 1.3 Windows

Two `Microsoft.UI.Xaml.Window` subclasses, each hosting one root `Grid` built in code (§11 for the XAML):

| | `DocumentWindow` | `BookWindow` |
| --- | --- | --- |
| Content rows | `MenuBar` · content (§5.3) · `FindBar` · `InfoBar` · footer · `ExportCanvas` (off-canvas) · overlays (`ZenGrid`, `PrintOverlay`) | `MenuBar` · `SplitView` (sidebar + detail) · footer · `InfoBar` · `ExportCanvas` · `PrintOverlay` |
| Default client size | **900 × 640 epx** | **1000 × 700 epx** |
| Minimum | **480 × 320 epx** (`OverlappedPresenter.PreferredMinimumWidth/Height`, windowed non-Zen only) | 700 × 400 with a book, 260 × 320 in the empty state |
| Count | one per document | exactly one (created on demand by Show Book, `Activate()`d) |
| `Window.Title` | `"{name}"` or `"{name} — Edited"` (§6.7) | `book?.Name ?? "Book"`, plus `" — {session.Title}"` while editing |
| Placement | from `session.json` when restoring; else cascaded +24/+24 epx from the last active document window at the last saved size (`md.win.windowSize.document`) | `md.win.windowSize.book` |

Sizing: `AppWindow.ResizeClient(new SizeInt32(w, h))` takes **physical pixels of the client area**;
`w = (int)Math.Round(900 * scale)` with `scale = GetDpiForWindow(hwnd) / 96.0` (`user32`, P/Invoke)
taken before the window is shown (`XamlRoot.RasterizationScale` is the same number once content is
loaded and is used for later checks). Restored placements are clamped into
`DisplayArea.GetFromRect(rect, DisplayAreaFallback.Nearest).WorkArea`.

Title bar: the **standard** system title bar (`Window.ExtendsContentIntoTitleBar` stays `false`; no
`SetTitleBar`). It is tinted paper/ink through `AppWindow.TitleBar` after
`AppWindowTitleBar.IsCustomizationSupported()`: `BackgroundColor`, `ForegroundColor`,
`InactiveBackgroundColor`, `InactiveForegroundColor`, `ButtonBackgroundColor`, `ButtonForegroundColor`,
`ButtonHoverBackgroundColor`, `ButtonHoverForegroundColor`, `ButtonPressedBackgroundColor`,
`ButtonPressedForegroundColor`, `ButtonInactiveBackgroundColor`, `ButtonInactiveForegroundColor`
(the docs: with `ExtendsContentIntoTitleBar == false` the alpha channel is ignored — pass opaque
colours). `AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode` keeps the caption
glyphs right in dark mode. Re-tinted in `ActualThemeChanged`. The Mac's window has no menu bar in its
title row either — the Mac bar is global; ours is the first content row.

Full screen: View ▸ Enter Full Screen (`F11`) → `AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen)`;
Exit → `SetPresenter(AppWindowPresenterKind.Overlapped)`. `AppWindow.Changed` with
`AppWindowChangedEventArgs.DidPresenterChange` → `AppWindow.Presenter.Kind` keeps the menu text and
Zen (§5.4) in sync.

### 1.4 Closing — three routes, one policy

`AppWindow.Closing` is documented as "Occurs when a window is being closed through a system affordance"
(close button, Alt+F4, taskbar) and explicitly does **not** occur for `AppWindow.Destroy`; the WinUI
`Window.Closed` event carries `WindowEventArgs.Handled`. The design therefore never assumes that
`Window.Close()` re-enters `Closing`:

1. **System affordance** → `AppWindow.Closing`: `args.Cancel = true`, then `await RequestCloseAsync()`;
   if it returns `true`, call `Window.Close()`.
2. **File ▸ Close (Ctrl+W), File ▸ Exit, Close Book, an activation that closes a window** → call
   `RequestCloseAsync()` directly; on `true` → `Window.Close()`.
3. **`Window.Closed`** → cleanup only: unregister from `WindowRegistry`, `WebView2.Close()` on every
   WebView2 in the window, stop watchers/timers, write `session.json`. (`Handled` is left `false`;
   the decision was already made.) The last window closing ends the app (documented `Closed` remark).

`RequestCloseAsync()` (DocumentWindow) is the §6.3 close policy: untitled && dirty → the Save / Don't
Save / Cancel dialog; saved → `FlushNow(explicit: false)`, rescue copy on failure; an export or print
in flight → awaited (bounded 10 s) before the window goes. Exit runs the policy on every window in
z-order; the first Cancel aborts Exit.

### 1.5 App lifetime

The process exits when the last `Window` closes (WinUI default; a menu-bar-less ghost process would be
worse than the Mac's "keeps running"). Every helper surface — export renderer, print overlay, dialogs
— lives inside an existing window, so nothing keeps the process alive by accident.

### 1.6 Session restore (`LocalFolder\session.json`)

`Md.App.Logic.Windows.SessionStore` serialises (`System.Text.Json`):

```json
{ "v": 1,
  "windows": [ { "path": "C:\\Docs\\a.md", "mode": "split", "zen": false, "zenReading": false,
                 "x": 120, "y": 80, "w": 900, "h": 640, "maximized": false } ],
  "book": { "open": true, "x": 200, "y": 100, "w": 1000, "h": 700, "maximized": false } }
```

Written on every window close and on Exit; read on a plain first launch. Only **saved** documents are
listed (untitled drafts are not autosaved — §14). Missing files are skipped silently. This is where the
Mac's `@SceneStorage("md.viewMode" / "md.zen" / "md.zenReading")` and window frames live on Windows;
`LocalSettings` never holds per-window state (8 KB per value, and it is not per window).

### 1.7 Shared services created in `App`

`WebViewEnvironment` (§4.1), `LocalSettingsStore : ISettingsStore` (§9), `WindowManager` +
`WindowRegistry` (§8.6), `BookLibraryHost` (§8.2), `RecentFiles` (§6.6), `WinUiAlerts : IAlerts` (§7.9),
`ExampleLibrary` (§2.3), `IcuWordCounter` (§5.5).

---

## 2. The command surface

### 2.1 Structure

The Mac bar reads `md · File · Edit · View · Book · Go · Window · Help`. Windows has no app menu, so
About → Help ▸ About md and Quit → File ▸ Exit. Every window carries the **same seven menus**; items
are enabled by *that window's* `ShellSnapshot` (the Mac's focused-scene values become "the window owns
its state"; the enablement and equality rules are kept because they decide when the Go and Diagram
submenus rebuild).

Legend for *Enabled*: `doc` = the window publishes an active document (document windows always; the
Book window only while `session.Stage == Editing`); `viewMode` = same set; `zen` = document windows
only; `saved` = the document has a path; `dirty` = differs from the last explicit save/open; `book` =
`md.bookBookmark` non-empty; `stepper` = Book window only. **(Win)** marks items that exist only on
Windows.

### 2.2 File

| Item | Shortcut | Enabled | Action |
| --- | --- | --- | --- |
| New | Ctrl+N | always | `WindowManager.OpenUntitled()` — mode Edit |
| Open… | Ctrl+O | always | `FileOpenPicker` (multi-select; filter §6.1) → one window per file |
| Open Recent ▸ | — | entries exist | rows from `MostRecentlyUsedList.Entries` newest first (file name, folder as tooltip) · divider · **Clear Menu** |
| Open TextBundle Folder… **(Win)** | — | always | `FolderPicker` → import (§6.5) |
| Examples ▸ | — | always | nine rows (§2.3) · divider · **Example Book…** → `BookLibraryHost.UnpackExampleBookAsync()` then Show Book |
| (divider) | | | |
| Close | Ctrl+W | always | `RequestCloseAsync()` → `Window.Close()` (§1.4) |
| Save | Ctrl+S | `doc` | untitled → Save As; saved → `FlushNow(explicit: true)` |
| Save As… | Ctrl+Shift+S | `doc` | `FileSavePicker` (§6.4) |
| Duplicate | — | `doc` | new untitled window with the same text, title `"{name} copy"`, dirty |
| Rename… | — | `saved` | name dialog → `StorageFile.RenameAsync(newName, NameCollisionOption.FailIfExists)`; identity change re-runs the §5.2 rule |
| Move To… | — | `saved` | `FolderPicker` → `StorageFile.MoveAsync(folder, name, NameCollisionOption.FailIfExists)`; identity change re-runs §5.2 |
| Revert to Saved | — | `saved && dirty` | `Text = LastSavedText`, write, replace editor text, `IsDirty = false` |
| (divider) | | | |
| Print… | Ctrl+P | `doc` | §7.2 |
| (divider) | | | |
| Share ▸ | — | `doc` (whole submenu) | |
| Share ▸ Source… | — | | §7.8 — the real file when saved, else a temp `.md` |
| Share ▸ Rendered PDF… | — | | render at `PageSize.Named(md.pdfPageSize)`, share the temp file |
| Export ▸ | — | **never disabled** | |
| Export ▸ PDF… / HTML… / EPUB… / LaTeX… / TextBundle… | — | `doc` | §7.3–§7.7 |
| Export ▸ (divider) | | | |
| Export ▸ Diagram as SVG ▸ | — | `DiagramCount > 0` | one row per `DiagramSvg.Diagram`, title `MenuTitle`; rebuilt on snapshot change (§2.9) |
| Export ▸ (divider) | | | |
| Export ▸ PDF Page Size ▸ | — | always | seven `RadioMenuFlyoutItem` (`GroupName = "PdfPageSize"`), `Text = PageSize.All[i].Label`, checked = `md.pdfPageSize` |
| (divider) | | | |
| Exit **(Win)** | — (no chord; Alt+F4 closes a *window*, so it is not displayed here) | always | close every window through §1.4, then exit |

### 2.3 Examples rows

`ExampleLibrary` (Md.App.**Services**) enumerates `<install>\Examples\*.md`
(`SearchOption.TopDirectoryOnly` — `Example Book\` is not listed) and hands the bare file names to
Core's `ExampleLibrary.FromListing`, which is what both sorts and titles them: `CompareNatural`
(Finder's `localizedStandardCompare`, hand-written so no culture or ICU table is consulted) and
`DisplayName(fileName)` — strip `^[0-9]+-` **only if something remains** (`[0-9]`, never `\d`, and
the `-` must be a single grapheme, so a dash wearing a combining mark is not a separator).

> Earlier drafts of this section named `Md.App.Logic.Text.ExampleName.DisplayName` and a
> `BookOrdering.NaturalCompare`. Neither exists: `ExampleName` was a second, subtly different copy of
> the prefix rule that nothing called (it compared `char`s where Core compares grapheme clusters) and
> was deleted; the sorter is `ExampleLibrary.CompareNatural`. `docs/core-api.md` is the authority for
> both (see its `ExampleLibrary` line and the `BookOrder.NaturalCompare` note). Expected rows: Welcome, Formatting, Tables, Code, Images, Math, Diagrams, Plots, Writer Tools.
Opening reads UTF-8 and creates a new **untitled, dirty** window with the text (title "Untitled — Edited";
mode Split by the open rule because the text is non-empty).

### 2.4 Edit

The `TextBox` implements these natively; the items call its methods and are enabled while the editor
pane is visible (Edit, Split, Zen writing). Ctrl+Z/Y/X/C/V/A are **not** root accelerators — they stay
with the focused control (the `WebView2` handles Ctrl+C on a preview selection).

| Item | Shortcut | Enabled | Action |
| --- | --- | --- | --- |
| Undo / Redo | Ctrl+Z / Ctrl+Y | editor visible && `CanUndo` / `CanRedo` | `TextBox.Undo()` / `Redo()` |
| (divider) | | | |
| Cut / Copy / Paste / Delete / Select All | Ctrl+X / Ctrl+C / Ctrl+V / Del / Ctrl+A | editor visible | `CutSelectionToClipboard()` / `CopySelectionToClipboard()` / `PasteFromClipboard()` / `SelectedText = ""` / `SelectAll()` |
| (divider) | | | |
| Find… **(Win)** | Ctrl+F | editor visible | shows `FindBar` (§3.5); root accelerator |
| Find Next / Find Previous **(Win)** | F3 / Shift+F3 | find bar has a query | `FindBar.Next()` / `Previous()` |
| Use Selection for Find **(Win)** | Ctrl+E | selection non-empty | copies the selection into the find bar |

The Mac gets Find from `NSTextView`; the WinUI `TextBox` has none. Spelling items are omitted — spell
check is off (§3.1).

### 2.5 View

| Item | Shortcut | Enabled | Behaviour |
| --- | --- | --- | --- |
| Edit (`ToggleMenuFlyoutItem`) | Ctrl+1 | `viewMode` | `ViewModeController.Select(Edit)`; checked when the displayed mode is Edit |
| Split | Ctrl+2 | `viewMode` | `Select(Split)` |
| Preview | Ctrl+3 | `viewMode` | `Select(Preview)` |
| Zen Mode (toggle) | Ctrl+Shift+Enter | `zen` (present but disabled in the Book window) | `ZenController.Toggle()` |
| (divider) | | | |
| Show Sidebar (toggle) **(Book window only)** | — | Book window | `SplitView.IsPaneOpen` |
| Enter Full Screen / Exit Full Screen | F11 | always | presenter toggle (§1.3) |

In Zen the published mode is `zenReading ? Preview : Edit`; `Select(m)` sets `zenReading = (m == Preview)`
— Ctrl+1 and Ctrl+2 both mean write, Ctrl+3 read; Split is never checked in Zen (Mac §2.2).

### 2.6 Book

| Item | Shortcut | Enabled | Action |
| --- | --- | --- | --- |
| New Book… | — | always | §8.2 → Show Book |
| Open Book… | — | always | `FolderPicker` → store → Show Book |
| Show Book | Ctrl+Shift+B | always | create/activate `BookWindow` |
| Close Book | — | `book` | `BookLibraryHost.CloseBook()`; close the Book window |
| (divider) | | | |
| Share Book as PDF | — | `book` | `BookOutput.SharePdf(PageSize.Named(md.pdfPageSize))` |
| Print Book… | — | `book` | `BookOutput.PrintBook()` |
| Export Book ▸ PDF… / EPUB… / LaTeX… | — | submenu disabled unless `book` | `BookOutput.ExportPdf(size)` / `ExportEpub()` / `ExportLaTeX()` |

### 2.7 Go

| Item | Shortcut | Enabled | Action |
| --- | --- | --- | --- |
| Previous Article | Ctrl+Alt+Up | `stepper.CanPrevious` | `stepper.Previous()` |
| Next Article | Ctrl+Alt+Down | `stepper.CanNext` | `stepper.Next()` |
| (divider) | | | |
| Contents ▸ | — | `Outline.Count > 0` | one row per `OutlineEntry`: `new string(' ', 2 * Math.Max(0, level - 1)) + text` → `JumpToHeading(entry)` |
| Notes ▸ | — | `Notes.Count > 0` | one row per `NoteEntry`: `NotePreview.Of(note.Text)` → `JumpToNote(note)` |

Ctrl+Alt+↑/↓ is the closest chord to ⌃⌘↑/↓ that neither the `TextBox` nor Windows claims. Known
collision: legacy Intel graphics drivers bound Ctrl+Alt+Arrow to display rotation (off by default on
current drivers); the fallback is Alt+↑/↓ (one row of `CommandTable`), and the Book toolbar chevrons
always work.

### 2.8 Window (Win) and Help

**Window**: Minimize (`OverlappedPresenter.Minimize()`), Zoom (`Maximize()` / `Restore()` toggle),
divider, one `ToggleMenuFlyoutItem` per open window (document titles + "Book"), checked = this window,
click → `Activate()`. Rebuilt from `WindowRegistry` on snapshot change.

**Help**: **md Help** (`F1`) → `Launcher.LaunchUriAsync(new Uri("https://nettrash.me/msstore/md/support.html"))`;
**Privacy Policy** → `https://nettrash.me/msstore/md/privacy.html` (the same URL Partner Center gets —
policy 10.5.1); divider; **About md** → `ContentDialog` with the icon, "md", the version from
`Windows.ApplicationModel.Package.Current.Id.Version`, "© 2026 nettrash. MIT licensed.", and the engine
list (KaTeX 0.17.0 + mhchem, Mermaid 11.16.0, Graphviz 14.1.1 via Viz.js 3.24.0, PlantUML 1.2026.4beta4,
highlight.js 11.11.1). Never the phrase "no third-party dependencies".

### 2.9 Implementation

- `Md.App.Logic.Commands.CommandTable` — `IReadOnlyList<CommandSpec>` where
  `record CommandSpec(CommandId Id, string Title, Chord? Chord, MenuPath Path, CommandKind Kind, bool WindowsOnly)`
  and `Chord(KeyCode Key, KeyModifiers Modifiers)` uses the library's own enums (no WinRT). `DisplayText`
  ("Ctrl+Shift+Enter") is a pure function.
- `CommandEnablement.IsEnabled(CommandId, ShellSnapshot)` / `IsChecked(CommandId, ShellSnapshot)` — the
  tables above as code. `ShellSnapshot` is a record the window publishes on every relevant change:
  `HasDocument, IsBookWindow, IsEditingArticle, IsSaved, IsDirty, HasBook, DisplayedMode, ZenActive,
  ZenReading, Outline, Notes, Diagrams, CanPrevious, CanNext, EditorVisible, CanUndo, CanRedo,
  RecentEntries, WindowTitles, HasSelection, HasFindQuery, PdfPageSizeId`.
- `Md.App.Menus.MenuBarBuilder` turns the table into `MenuBarItem` → `MenuFlyoutItem` /
  `MenuFlyoutSubItem` / `ToggleMenuFlyoutItem` / `RadioMenuFlyoutItem` / `MenuFlyoutSeparator`, sets
  `KeyboardAcceleratorTextOverride` from `Chord.DisplayText`, wires `Click` → `CommandDispatcher.Execute(id)`.
  **Dynamic submenus (Open Recent, Contents, Notes, Diagram as SVG, Window) are rebuilt when the snapshot
  changes** — `MenuFlyoutSubItem` declares only `Icon`, `Items` and `Text`, and `MenuBarItem` only `Items`
  and `Title`; neither exposes a flyout or an `Opening` event (both other designs assumed one). The
  rebuild is cheap (≤ 60 items) and `Diagrams` arrives with the 250 ms `DerivedText` tick computed off the
  UI thread (§5.5), so the Mac's "recomputed on every menu build" costs nothing per keystroke.
- `Md.App.Menus.AcceleratorInstaller` adds one `KeyboardAccelerator { Key, Modifiers }` per chord to the
  root `Grid`'s `KeyboardAccelerators`; `Invoked` → `args.Handled = dispatcher.TryInvoke(id)`. `TryInvoke`
  returns `false` when `IsEnabled` says so, so a disabled chord falls through to the control.
  **Double-fire guard**: `CommandDispatcher` ignores a second invocation of the same `CommandId` within
  150 ms — microsoft-ui-xaml #6231 documents accelerators firing twice ~100 ms apart while a WebView2 has
  focus. No `KeyDown` re-dispatch anywhere (that would triple-fire), and no `CoreWebView2Controller`
  bridge (the WinUI `WebView2` does not expose its controller — its members are `CoreWebView2`,
  `Source`, `CanGoBack/Forward`, `DefaultBackgroundColor`, `Close()`, the three `EnsureCoreWebView2Async`
  overloads, `ExecuteScriptAsync`, `GoBack/Forward`, `NavigateToString`, `Reload`, and the events
  `CoreProcessFailed`, `CoreWebView2Initialized`, `NavigationStarting/Completed`, `WebMessageReceived`).
- `Escape` is a root accelerator whose `IsEnabled` is `true` only while Zen is active or the find bar is
  open, so the editor keeps Esc otherwise.
- `CoreWebView2Settings.AreBrowserAcceleratorKeysEnabled = false` so Chromium does not act on Ctrl+P,
  Ctrl+F, F5 itself; the WinUI control forwards accelerator keys to XAML (which is what #6231 reports).
  Contingency (written, off): `KeyRelay` — an injected `keydown` → `chrome.webview.postMessage` relay for
  Ctrl/Alt chords — enabled only if a day-1 Windows check shows root accelerators *not* firing with the
  preview focused; it must never run alongside the forwarded path.

---

## 3. The editor (`Md.App.Controls.EditorPane`, one `TextBox`)

### 3.1 `TextBox` configuration

| Mac (`NSTextView`) | WinUI `TextBox` |
| --- | --- |
| plain text, no rich text / graphics / font panel | default `TextBox` (plain) |
| `allowsUndo` with the document's undo manager | built-in undo (`CanUndo`, `Undo()`, `CanRedo`, `Redo()`); the Book pane calls `ClearUndoRedoHistory()` on every article switch (the Mac's fresh `UndoManager`) |
| American Typewriter 15 pt | `FontFamily = new FontFamily("Georgia")`, `FontSize = 20` (epx = 15 pt × 4/3) |
| ink text, accent caret | `Foreground = PaperInkBrush`; the caret follows `Foreground` (no caret brush on the WinUI `TextBox` — §12) |
| clear backgrounds, paper behind | `Background = Transparent`, `BorderThickness = 0`, `BorderBrush = Transparent`; the pane `Grid` paints paper. The focus underline/background of the default template is removed by overriding the `TextControlBackground*` / `TextControlBorderBrush*` theme resources in `App.xaml` (resource keys, not a template) |
| smart quotes/dashes/replacement/spelling off | `IsSpellCheckEnabled = false`, `IsTextPredictionEnabled = false` — the only two auto-correct sources WinUI has |
| `textContainerInset` 16 × 16, `lineFragmentPadding` 0 | `Padding = new Thickness(16)` |
| word wrap, vertical scroll only | `TextWrapping = TextWrapping.Wrap`, `AcceptsReturn = true`; `ScrollViewer.SetHorizontalScrollBarVisibility(box, Disabled)`, `SetVerticalScrollBarVisibility(box, Auto)` |
| selection colour | `SelectionHighlightColor = new SolidColorBrush(accent)` |
| placeholder `"# Start writing…"` at the inset | `PlaceholderText = "# Start writing…"`, `PlaceholderForeground = PaperInkTertiaryBrush` — same face, size and padding as the text, so it lands exactly where the Mac's overlay does |
| Tab inserts a tab | **no `AcceptsTab` exists** (WPF only). `KeyDown`: `if (e.Key == VirtualKey.Tab && !shift) { box.SelectedText = "\t"; box.SelectionStart += 1; box.SelectionLength = 0; e.Handled = true; }` |

### 3.2 Line endings — the WinUI fact that shapes the data flow

`TextBox.Text` reports every line break as `\r`, whatever was assigned. Contract:

- The model (`DocumentSession.Text`) is always **LF**. `EditorText.FromTextBox(s)` = `s.Replace("\r\n", "\n").Replace('\r', '\n')`;
  `EditorText.ToTextBox(s)` is the identity (the control normalises on assignment). Pinned by tests with
  mixed input; correct whether or not a given WinUI build converts.
- The file's convention is remembered in `TextFileDressing.NewLine` (`Lf` | `CrLf`; first line break wins;
  none → `Lf`) at load and re-applied at save. New documents write LF (the Mac writes `\n`). Lone-CR files
  become LF (accepted).
- `TextChanged` → `if (normalized != session.Text) session.Edit(normalized)` (the Mac's `sync()` guard).
- External replace (Revert, Reload from Disk, example load): only when `FromTextBox(box.Text) != newText`;
  assign, then restore the selection clamped (`start = Math.Min(start, len)`, `length = Math.Min(length, len - start)`).
- Caret offsets: `LineOffsets.OffsetOfLine(line, text)` is the Swift UTF-16 routine verbatim (`\n`, `\r`,
  `\r\n` counted; U+2028/2029 not) and runs over **`box.Text` as reported** (with `\r`), because
  `SelectionStart` indexes that string; lines are 1:1 between the two forms.

### 3.3 Caret jumps

`EditorJump(Guid Id, int Line)` performed once per id (`lastJumpId`), deferred one dispatcher turn
(`DispatcherQueue.TryEnqueue`) because a Notes jump may have just switched Preview → Edit and the pane is
not laid out yet: `box.Focus(FocusState.Programmatic); box.Select(offset, 0);` — the `TextBox` scrolls the
caret into view when focused. Then `OnJumpHandled(id)`; the owner clears the request only if the id still
matches.

### 3.4 Editor half of the scroll sync

After `Loaded`, find the template `ScrollViewer` (`VisualTreeHelper` walk for a `ScrollViewer` named
`ContentElement`; fallback: the first `ScrollViewer` descendant). `ViewChanged` → if `!applyingRemote && ScrollableHeight > 0`
→ `scrollSync.EditorDidScroll(Math.Clamp(VerticalOffset / ScrollableHeight, 0, 1))`.
`ApplyFraction(f)`: `applyingRemote = true; sv.ChangeView(null, f * sv.ScrollableHeight, null, disableAnimation: true); applyingRemote = false`.
If `ViewChanged` proves asynchronous on Windows, `ScrollSyncGuard` switches to a 50 ms timestamp window
(one line, same shape as the web side's 300 ms).

### 3.5 Find bar (Win)

A one-row `Grid` above the footer, hidden by default: query `TextBox` (Georgia 17.3 epx = 13 pt), "Next",
"Previous", "Done". `TextSearch.Next(text, query, from)` / `Previous(...)` (Md.App.Logic): ordinal,
case-insensitive (`OrdinalIgnoreCase`), wrapping, returns `(index, length)` in the `TextBox`'s own string;
the pane calls `box.Select(index, length)` and focuses the editor. Enter = next, Shift+Enter = previous,
Esc closes and returns focus. No replace in 1.0 (§12).

---

## 4. The preview host (`Md.App.Controls.PreviewHost` around `Microsoft.UI.Xaml.Controls.WebView2`)

### 4.1 Environment (one per process) — `Md.App.Web.WebViewEnvironment`

```csharp
var options = new CoreWebView2EnvironmentOptions            // WinRT: parameterless ctor
{
    AdditionalBrowserArguments = "--disable-background-timer-throttling",
    AreBrowserExtensionsEnabled = false,
    EnableTrackingPrevention = false,                       // nothing to track; skips the list machinery
};
Environment = await CoreWebView2Environment.CreateWithOptionsAsync(
    browserExecutableFolder: null,                          // Evergreen runtime, in-box on Windows 11
    userDataFolder: Path.Combine(ApplicationData.Current.LocalCacheFolder.Path, "WebView2"),
    options);
```

Every `WebView2` control is initialised with `await webView.EnsureCoreWebView2Async(Environment)`
(the `(CoreWebView2Environment)` overload exists on the WinUI control). One environment and one user-data
folder for preview and export renderers: all WebView2s sharing a UDF must share browser arguments, and
the throttling flag is what keeps a minimised preview and the off-canvas export renderer honest
(rich.md §6 item 9). The property, not the `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` environment variable —
it is type-checked and documented; the env var stays a diagnostic override.

### 4.2 Origin and `index.html` — `Md.App.Web.AssetHost.Attach(CoreWebView2 core, Func<string> html)`

```csharp
// NO SetVirtualHostNameToFolderMapping. One filter over the whole origin; we serve every byte.
core.AddWebResourceRequestedFilter("https://md.assets/*",
    CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.Document);
core.WebResourceRequested += (s, e) =>
{
    if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri)) return;
    if (IsIndex(uri))                                                         // the document, from memory
    {
        var bytes  = Encoding.UTF8.GetBytes(html());
        var stream = new InMemoryRandomAccessStream();                        // Windows.Storage.Streams
        using (var w = new DataWriter(stream.GetOutputStreamAt(0))) { w.WriteBytes(bytes); w.StoreAsync().GetResults(); }
        stream.Seek(0);
        e.Response = core.Environment.CreateWebResourceResponse(stream, 200, "OK",
            "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        return;
    }
    e.Response = Asset(core, uri);                                            // rich/… off disk, AssetMime
};
core.Navigate(IndexUrl);   // "https://md.assets/index.html"
```

**Why no mapping — measured on Windows, not reasoned about.** The mapping was the original design,
with `index.html` served from memory on the same host, resting on Microsoft's how-to: the event is
still raised "when a requested resource does not exist in the folder that is virtually hosted". It is
not, at least not for the top-level document. The first Windows run of this app logged
`preview navigation FAILED Unknown` then `ConnectionAborted` with **no request reaching the handler**
and no `preview index served from memory` line: the mapping took the navigation, found no
`<install>\web\index.html` on disk, and failed it. (Undocumented in the reference; MicrosoftEdge/
WebView2Feedback #2103 and #4201.) Serving every byte ourselves loses nothing — the document and
`rich/` still arrive on the *same* origin, which is the only thing the design needs — and it promotes
what this section called a contingency: **`AssetMime` is now load-bearing**, because without a mapping
Chromium has no file extension to infer a content type from.

- `WebRoot = Path.Combine(AppContext.BaseDirectory, "web")`. `Md.App.csproj` carries the engines as
  `<Content Include="rich\**\*" Link="web\rich\%(RecursiveDir)%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" />`
  so the package holds `web\rich\…` and the page can fetch **only** `rich/` — never `md.dll`, `Examples\` or any assembly
  (mapping the install root, as two designs did, exposed them). `AppContext.BaseDirectory` is right both
  packaged and unpackaged (`Package.Current` throws unpackaged — never use it for paths).
  **`CopyToOutputDirectory` is load-bearing, not decoration.** MSBuild lifts a `ContentWithTargetPath`
  item into `AllItemsFullPathWithTargetPath` only when that metadata is `Always` / `PreserveNewest` /
  `IfDifferent` (`Microsoft.Common.CurrentVersion.targets`,
  `_GetCopyToOutputDirectoryItemsFromThisProject`), and that item set is exactly what
  `GetCopyToOutputDirectoryItemsOutputGroup` hands to the MSIX tooling's `GetPackagingOutputs`
  (`Microsoft.Windows.SDK.BuildTools.MSIX.Packaging.targets`, which never calls
  `ContentFilesProjectOutputGroup`). Without it the engines reach **neither** the output directory nor
  the package: `web\` is empty beside `md.exe`, and §11.4's unpackaged `--selftest` leg
  (`-p:WindowsPackageType=None`) finds no engines at all. The same holds for `Examples\` (§2.3, read
  from `AppContext.BaseDirectory`) and `Assets\`.
- Same host for page and assets is what makes `href="rich/katex.min.css"`, `src="rich/md-init.js"`,
  `import('./plantuml.js')` inside `md-init.js`, `url(fonts/…)` inside `katex.min.css` and `href="#slug"`
  all resolve with the generated HTML **unchanged** — no `<base href>`, no string rewriting, no edit to
  `md-init.js`; the bytes stay the golden bytes.
- Nothing but `md.assets` ever loads, and every fetch on it is same-origin. `https://` gives a secure
  context (the engines' WASM is happier; matches Android's `appassets.androidplatform.net`).
  `Asset()` keeps the old mapping's containment guarantee itself: a resolved path that does not start
  with `WebRoot + "\"` is a 404, never a read, so `rich/` remains the only prefix the page can reach.
- The **three-argument** `AddWebResourceRequestedFilter` (the two-argument overload is documented as
  deprecated). The filter is matched against the URI *without* fragment, so `index.html#slug` also matches.
- Why not a temp file on disk: the MSIX install folder is read-only, and copying 13 MB of engines into
  `LocalFolder` to co-host one HTML file is waste. Why not `NavigateToString`: opaque origin.
- `Reload()` re-requests `index.html` and picks up the new HTML — the debounce relies on this;
  `Cache-Control: no-store` guarantees Chromium never serves its cache.
- **`AssetMime` is the mechanism, not a contingency.** With no mapping there is nothing for Chromium to
  infer a type from, so every `rich/…` response names one from the Mac's exact table (`js, mjs →
  text/javascript; css → text/css; html → text/html; json → application/json; svg → image/svg+xml;
  woff2 → font/woff2; woff → font/woff; ttf → font/ttf; else application/octet-stream`). Pure table +
  tests in Md.App.Logic. **Verified on Windows 2026-09-07**: KaTeX (inline, display and `\ce{}` via
  mhchem), highlight.js, Mermaid, Graphviz and PlantUML all draw in the live preview, as does a `plot`
  fence, with `preview index served from memory` and `preview navigation ok status=200` in `md.log`.

### 4.3 Two HTML entry points (the typography decision)

`Md.App.Logic.Preview.ScreenHtml.WithWindowsFonts(string coreHtml)` inserts, before the first `</head>`:

```
\n<style id="md-win-fonts">body{font-family:Georgia,"Courier New",serif;}</style>
```

The shared stylesheet's `code, pre { font-family: "Courier New", monospace; }` rule is untouched, so code
stays Courier New; KaTeX, Mermaid, Graphviz and PlantUML output carry their own faces. Idempotent (a second
call finds the id and returns the input). Used by exactly three callers: the live preview
(`PreviewCoordinator` — `RenderKind.Screen`), Print and PDF (`ExportPipeline.PaperHtml` — `RenderKind.Paper`).
HTML / EPUB / SVG exports go through `RenderKind.Export` and load the pure Core HTML, so the captured
`outerHTML`, the EPUB body and the diagram SVGs carry nothing Windows-specific. Tests pin: the exact
inserted bytes; the export pipelines never contain `md-win-fonts`; the shared stylesheet bytes are
unchanged.

### 4.4 Settings on `CoreWebView2.Settings`

`AreBrowserAcceleratorKeysEnabled = false`, `IsZoomControlEnabled = false`, `IsPinchZoomEnabled = false`,
`IsSwipeNavigationEnabled = false`, `IsStatusBarEnabled = false`, `AreDevToolsEnabled = false` (Debug: true),
`AreDefaultScriptDialogsEnabled = false`, `IsGeneralAutofillEnabled = false`, `IsPasswordAutosaveEnabled = false`,
`AreHostObjectsAllowed = false`, `IsBuiltInErrorPageEnabled = false`, `IsWebMessageEnabled = true`,
`IsScriptEnabled = true`. `AreDefaultContextMenusEnabled = true` with `ContextMenuRequested` pruning
`e.MenuItems` to the entries whose `CoreWebView2ContextMenuItem.Name` is `copy` or `selectAll`
(WKWebView offers Copy; back/forward/reload/print/save/inspect go). `core.Profile.PreferredColorScheme`
follows `ActualTheme` (`CoreWebView2PreferredColorScheme.Light`/`Dark`) so scrollbars match.

Background: `webView.DefaultBackgroundColor = <paper colour of the current theme>` (a `Windows.UI.Color`;
the property is on the WinUI control), set **before** `EnsureCoreWebView2Async` and switched in the
`ActualThemeChanged` handler before the re-render, so neither the runtime-startup gap nor a reload ever
shows white; the CSS paints the identical value on `html, body`.

### 4.5 Re-render policy — `Md.App.Logic.Preview.PreviewCoordinator` (pure; the Mac coordinator verbatim)

```
Update(text, title, dark, token):
    newDocument = token != lastToken; lastToken = token
    key = (dark, title, text); if key == lastKey && !newDocument return; lastKey = key
    surface.Html = ScreenHtml.WithWindowsFonts(MarkdownHtml.Document(text, title, dark))     // export:false
    debounce.Cancel()
    if !surface.IsShown: stale = true; return                                                 // collapsed pane: reload on next Show()
    if !loadedOnce || newDocument: loadedOnce = true; savedScrollY = 0; surface.Navigate(IndexUrl)
    else debounce.Start(350 ms) → ReloadPreservingScroll
ReloadPreservingScroll: savedScrollY = await surface.EvalNumber("window.scrollY") ?? 0; surface.Reload()
OnNavigationCompleted(ok): if !ok: log; return
    if savedScrollY > 0: surface.Eval("window.__mdScrollTo?.(" + savedScrollY.ToString(InvariantCulture) + ")")
    if parkedNavigation != null: Navigate(parked); parked = null                             // Android's rule
Show(): if stale: stale = false; loadedOnce = true; savedScrollY = 0; surface.Navigate(IndexUrl)
Navigate(nav): if nav.Id == lastNavigationId return; lastNavigationId = nav.Id
    if loading: parkedNavigation = nav; return
    surface.Eval("window.__mdMarkProgrammatic?.(); document.getElementById('" + nav.Slug + "')?.scrollIntoView(true)")
    scheduler.Post(() => onHandled(nav.Id))
```

`IPreviewSurface` (`Html` setter, `IsShown`, `Navigate`, `Reload`, `Eval`, `EvalNumber`) is the seam;
`WebView2Surface` implements it. `dark` comes from the root `Grid`'s `ActualTheme`; `ActualThemeChanged`
→ `Update(...)` with the new flag — a new page with the other `data-md-dark`, so Mermaid/PlantUML redraw.
Document windows pass `token = null`; the Book pane passes the article path. In Edit mode the `WebView2`
stays in the tree (`Visibility.Collapsed`) and the coordinator only records `stale`; the next show reloads
once — no control re-creation per Ctrl+1/Ctrl+2 round-trip, and the Mac's "fresh page on return" look.
`ExecuteScriptAsync` results are JSON (`"1"` with quotes, `null` as four characters): `JsonScript.Number`
/ `.String` decode with `System.Text.Json`; numbers into scripts use `CultureInfo.InvariantCulture`.

### 4.6 Injected scripts (`AddScriptToExecuteOnDocumentCreatedAsync`, once per `CoreWebView2`)

Both run at **document start** (the Mac's `WKUserScript` ran at document end), so neither may touch the
DOM at injection time; both only register listeners and define functions.

1. **Scroll sync** — the Mac script byte-for-byte except
   `window.webkit?.messageHandlers?.mdScroll?.postMessage(obj)` → `window.chrome.webview.postMessage(obj)`
   (pinned by a test that diffs `Scripts.ScrollSync` against the Mac text with the one substitution).
   `WebMessageReceived` → `JsonDocument.Parse(e.WebMessageAsJson)` → `{fraction, echo}`; drop
   `echo == true`; else `scrollSync.PreviewDidScroll(fraction)`. Editor → preview:
   `Eval("window.__mdSyncScrollTo(" + f.ToString("R", InvariantCulture) + ")")`.
2. **Link guard** (`Scripts.LinkGuard`): capture phase, `click` **and** `auxclick`, on `document`:
   ```js
   (function () {
     function guard(e) {
       var a = e.target && e.target.closest ? e.target.closest('a[href]') : null;
       if (!a) return;
       var href = (a.getAttribute('href') || '').trim().toLowerCase();
       if (href.charAt(0) === '#') return;                                          // same-document
       if (href.indexOf('http://') === 0 || href.indexOf('https://') === 0) return;  // NavigationStarting decides
       e.preventDefault(); e.stopImmediatePropagation();                            // javascript:, data:, file:, mailto:, relative
     }
     document.addEventListener('click', guard, true);
     document.addEventListener('auxclick', guard, true);
   })();
   ```
   Registered scripts are not part of the DOM, so exports never carry them. Belt and braces: no
   `AddHostObjectToScript` is ever called, so even a guard failure could only post scroll messages.

### 4.7 Navigation policy — `Md.App.Logic.Preview.LinkPolicy.Decide(Uri uri, bool isUserInitiated, Uri indexUrl) → Allow | Cancel | OpenExternally`

| Case | Decision |
| --- | --- |
| `uri` without fragment equals `indexUrl` (our `Navigate`/`Reload`; a fragment hop that surfaces) | Allow |
| any other `https://md.assets/…` (`[x](other.md)` resolved against the host) | **Cancel** (the Mac cancels a non-fragment `mdassets` URL) |
| scheme `http` / `https`, another host | OpenExternally (`e.Cancel = true; Launcher.LaunchUriAsync(uri)`) |
| anything else (`file:`, `data:`, `mailto:`, dropped files) | Cancel |

Applied in `CoreWebView2.NavigationStarting` (`e.Uri`, `e.IsUserInitiated`, `e.Cancel`) and
`NewWindowRequested` (`e.Handled = true`; launch if http(s) on another host). The host check comes
**before** the http(s) branch — two designs launched the browser for relative links (fixed). Same-document
`#slug` hops raise no `NavigationStarting`. `CoreWebView2.ProcessFailed` (and the control's
`CoreProcessFailed`) → `EnsureCoreWebView2Async(env)` again, re-attach §4.2/§4.6, `Navigate(IndexUrl)`.

### 4.8 Render-complete (exports only; the live preview never waits — Mac)

`RenderCompletePoller` (pure, over `IRenderSurface`): `ExecuteScriptAsync("document.documentElement.getAttribute('data-md-render-complete')")`
every **250 ms**, up to **480** attempts; JSON result `"1"` (three characters, quotes included) or
exhaustion → proceed; `NavigationCompleted.IsSuccess == false` → throw. **Timeout is success** (a failed
diagram has restored its own source). `md-init.js` stays byte-identical.

### 4.9 Images beside the document

Not resolved — parity. A relative `photo.png` becomes `https://md.assets/photo.png` → 404 (broken image),
as on every port; `http(s)://` and `data:` images load (the one network fetch PRIVACY describes). Only
Export ▸ TextBundle… reads local images (§7.7).

### 4.10 PlantUML timer throttling

Chromium throttles timers of *hidden* pages, and page visibility follows the controller's `IsVisible`,
which the WinUI control derives from XAML `Visibility` — not from screen position. Two measures: the
export renderer is `Visible` off-canvas (§7.1), and the environment carries
`--disable-background-timer-throttling` (§4.1). A minimised live preview is throttled and simply finishes
on restore.

---

## 5. View modes, per-file memory, Zen

### 5.1 Per-window state — `Md.App.Logic.View.DocumentWindowState`

`StoredMode` (raw, default Split), `ZenActive`, `ZenReading`, `LastIdentity` (`IdentityMemo`: never-ran /
ran-with-null / ran-with-id), `IsBookArticle` (sticky), `NavigationMode` (transient nudge),
`PreviewNavigation?`, `EditorJump?`, `Derived`. Persisted only through `session.json` (§1.6); never in
`md.viewModeMemory`. `EffectiveMode = ViewModeRule.DisplayedMode(preferred: StoredMode, navigation: NavigationMode, isWide: true)`
— a desktop window is always wide; Split handles narrowness by stacking (§5.3).

### 5.2 `ViewModeController` — the Mac's `setMode` / `applyViewModeMemory`, line for line

```
Select(mode):   if ZenActive: ZenReading = (mode == Preview); return           // stores nothing
                SetMode(mode)
SetMode(mode):  NavigationMode = null; StoredMode = mode
                if !IsBookArticle && identity != null: memory.Remember(mode, identity)
ApplyMemory(identity?):                                  // on window appearance and every identity change
    if identity != null && BookArticleOpens.ClaimOpen(identity): IsBookArticle = true
    if IsBookArticle: LastIdentity = Some(identity); return
    if LastIdentity ran && LastIdentity.Value == identity: return
    hadNoIdentity = LastIdentity ran && LastIdentity.Value == null; LastIdentity = Some(identity)
    if hadNoIdentity && identity != null: memory.Remember(StoredMode, identity); return     // first save: migrate, raw
    NavigationMode = null
    SetMode(ViewModeRule.OpenViewMode(memory.Lookup(identity), isEmptyDocument: text.Length == 0,
                                      hasFileIdentity: identity != null, isWide: true))
```

`memory` = `Md.Core.ViewModeMemory` over `ISettingsStore["md.viewModeMemory"]`. `identity` =
`ViewModeMemory.Identity("file:" + FileIdentity.Canonical(path))` where `FileIdentity.Canonical`
(Md.App.Logic, Windows-only P/Invoke) opens the file (`File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)`),
calls `kernel32!GetFinalPathNameByHandleW(handle, buffer, len, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS)`,
strips `\\?\` (and turns `\\?\UNC\server\share` into `\\server\share`); falls back to `Path.GetFullPath`
when the file cannot be opened. No lowercasing — the final path carries on-disk casing, so `C:\Docs\A.md`
and `c:\docs\a.md` hash identically; junctions and symlinks resolve like `resolvingSymlinksInPath`.
Consequences reproduced exactly: first appearance decides and stores; Ctrl+S of an untitled document
migrates; Save As / Rename / Move re-run the open rule for the new identity; saving over the same path is a
no-op; Examples have content and no file → Split. Tests: the seven Swift cases plus "two spellings of one
path yield one identity" (Windows leg) and a junction-equality integration test.

### 5.3 Layout (`ArticlePanes`, code-behind)

`SplitLayout.Arrange(mode, contentWidth) → EditorOnly | PreviewOnly | SideBySide | Stacked` (pure):
`SideBySide` when width ≥ **640 epx**, else `Stacked`. Code-behind sets the `Grid`'s column/row
definitions (two `*` tracks and a 1-epx `Auto` divider, `PaperBorderBrush`) and `Grid.SetColumn/SetRow`
— equal halves, no splitter (the Mac has none). Edit collapses the preview control; Preview collapses the
editor. The window's `MinWidth 480 / MinHeight 320` applies only in the windowed non-Zen layout.

### 5.4 Zen — `Md.App.Logic.View.ZenController` + `DocumentWindow.ZenGrid`

- Toggle (Ctrl+Shift+Enter, the capsule's exit button, Esc): `toggling = true; AppWindow.SetPresenter(FullScreen | Overlapped); toggling = false`.
  `AppWindow.Changed` with `DidPresenterChange`: `controller.PresenterChanged(AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen)`
  → if `Active && !toggling && !isFullScreen` → `Active = false` (leaving full screen by any means drops Zen).
- Geometry: the whole window is paper; the `MenuBar` row, footer, find bar and divider collapse; a `Grid`
  with `ColumnDefinitions 1*,4*,1*` and `RowDefinitions 4*,92*,4*` places the single pane (editor when
  `!ZenReading`, preview when `ZenReading`) in the centre cell — width 2/3, height 92 %, the Mac's
  fractions, DPI-free. No minimum size.
- Capsule: `Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(6), Background = capsuleBrush }`
  at `VerticalAlignment.Top`, `Margin 0,10,0,0`, holding `StackPanel(Horizontal, Spacing 2)`: write
  (`FontIcon` `\uE70F`, tooltip "Write"), read (`\uE7B3`, "Read"), a 14-epx vertical `Rectangle`, exit
  (`\uE73F`, "Exit Zen Mode", `Padding 6,2`). `capsuleBrush = new AcrylicBrush { TintColor = paperSecondary, TintOpacity = 0.8, FallbackColor = paperSecondary }`
  built in code (no `ThemeResource` lookup for xamlcheck to miss). Selected switch tinted accent, others
  `PaperInkSecondaryBrush`; buttons `Padding 8,2`.
- Fade: `PointerMoved` over the column → `Reveal()`: `Opacity = 1`, `IsHitTestVisible = true`, restart a
  `DispatcherQueueTimer` (`Interval` 2.5 s, `IsRepeating = false`) → `Opacity = 0`, `IsHitTestVisible = false`.
  `capsule.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(300) }` gives the
  ease. Shown on entering Zen.
- Neither `ZenActive` nor `ZenReading` touches `md.viewModeMemory` or `StoredMode`.

### 5.5 Footer and derived text

`Border` (`PaperBackgroundSecondaryBrush`, `Padding 12,5`) → right-aligned `TextBlock` Georgia **14.7 epx**
(11 pt), `PaperInkSecondaryBrush`, `Typography.SetNumeralAlignment(block, FontNumeralAlignment.Tabular)`
(the attached property — there is no `FontNumeralAlignment` property on `TextBlock`), text
`$"{words} words · {characters} characters"` (U+00B7, no pluralisation). Hidden in Zen.

`DerivedTextScheduler` (pure): the first computation is immediate; later ones await **250 ms**
(cancelled by the next edit) and compute on the thread pool: `Words = wordCounter.Count(text)`,
`Characters = new StringInfo(text).LengthInTextElements` (grapheme clusters, the Mac's `count`),
`Outline = MarkdownParser.Outline(text)`, `Notes = MarkdownParser.Notes(text)`,
`Diagrams = DiagramSvg.Diagrams(text)` (feeds the Export ▸ Diagram as SVG rows).

`IWordCounter`: **`IcuWordCounter`** (Md.App.Logic, `[SupportedOSPlatform("windows")]`) P/Invokes the
in-box `icu.dll` (Windows 10 1903+; exported names are unversioned): `ubrk_open(UBRK_WORD /*1*/, null, text, length, out status)`,
`ubrk_first`, `ubrk_next`, `ubrk_getRuleStatus` — a segment counts when its rule status is ≥ 100
(`UBRK_WORD_NUMBER`; letters 200, kana 300, ideographs 400 — the ICU definition of "word", which is what
`enumerateSubstrings(.byWords)` and Kotlin's `BreakIterator` implement), `ubrk_close`. `SimpleWordCounter`
(runs of `\p{L}\p{N}` joined across `'`/U+2019) is the fallback if `icu.dll` fails to load. Both pass the
five pinned vectors (`""`→0, `"   \n\n"`→0, `"Hello, world!"`→2, `"it's — done"`→2, `"One\ntwo\n\nthree"`→3);
the Windows CI leg adds a CJK vector for ICU.

### 5.6 Contents / Notes jumps (Mac §6.9)

```
JumpToHeading(e): if EffectiveMode != Edit:    PreviewNavigation = new(Guid.NewGuid(), e.Slug)
                  if EffectiveMode != Preview: EditorJump = new(Guid.NewGuid(), e.Line)
JumpToNote(n):    nudge = ViewModeRule.NavigationNudge(displayed: EffectiveMode, wants: Edit); if nudge != null: NavigationMode = nudge
                  EditorJump = new(Guid.NewGuid(), n.Line)
```

Headings never nudge; a note nudges Preview → Edit only; nudges are never stored. `NotePreview.Of(text)`
(Md.App.Logic, shared with the Book window): first non-empty line (split on `\n`, `\r`, `\r\n`, U+000B,
U+000C, U+0085, U+2028, U+2029), whitespace runs collapsed (`char.IsWhiteSpace`), `"(empty note)"`, cap at
**50 text elements** (`StringInfo`) then trim trailing spaces/tabs and append `…`.

---

## 6. Documents

### 6.1 Types, pickers, activation

`Package.appxmanifest` already declares the associations (Markdown owner: `.md .markdown .mdown .markdn .mdtext`;
alternates `.puml .plantuml`, `.gv`, `.textpack`, `.txt`). `.dot` stays unclaimed (Word template).
`.textbundle` is a folder and cannot be associated.

Pickers are the WinRT `Windows.Storage.Pickers` classes initialised with
`WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window))`:

- `FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary }`,
  `FileTypeFilter` = `.md .markdown .mdown .markdn .mdtext .txt .text .puml .plantuml .gv .textpack`;
  `PickMultipleFilesAsync()` → one window each.
- `FileSavePicker.FileTypeChoices` (the Mac's writable types, in order): "Markdown Document" → the five
  Markdown extensions; "Plain Text" → `.txt`, `.text`; "PlantUML Diagram" → `.puml`, `.plantuml`;
  "Graphviz DOT Graph" → `.gv`. Never a bundle type. `SuggestedFileName` = current base name or "Untitled";
  `DefaultFileExtension` = the document's extension (or `.md`). **`PickSaveFileAsync` returns a created,
  empty file** (documented) — write through `FileStream(file.Path, FileMode.Create)`; on a failed write
  `await file.DeleteAsync()` so no empty file is left behind. (Only the newer `Microsoft.Windows.Storage.Pickers`
  stopped creating the file in 2.0.1; we use the WinRT pickers.)
- `FolderPicker` needs `FileTypeFilter.Add("*")` on desktop (documented sample).

Drag & drop onto any window root (`AllowDrop = true`; `DragOver` → `e.AcceptedOperation = DataPackageOperation.Copy`
when `e.DataView.Contains(StandardDataFormats.StorageItems)`; `Drop` → `await e.DataView.GetStorageItemsAsync()`):
files open as documents (`.textpack` imports), a `.textbundle` **folder** imports, other folders are ignored.

### 6.2 File I/O

Pickers and activation deliver `StorageFile`s; everything after that uses `System.IO` on `StorageFile.Path`
(a `runFullTrust` package runs with the user's rights). Reasons: the close path needs a **synchronous**
flush, `FileSystemWatcher` needs a path, the (mtime, size) stamp needs `File.GetLastWriteTimeUtc` +
`FileInfo.Length`. `StorageFile` APIs remain for what only they do: `RenameAsync`, `MoveAsync`,
MRU/FutureAccessList tokens, sharing (`StorageFile.GetFileFromPathAsync` when only a path is known). A
provider returning an empty `Path` (rare) → the session falls back to `FileIO.ReadBufferAsync` /
`WriteBytesAsync` and disables the watcher.

**Writes are in place**: `new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)` —
never write-then-rename. The file keeps its identity, ACLs, hard links and cloud-sync placeholder state
(book.md §10.5 says the Mac's write is a plain overwrite), and the watcher sees exactly one `Changed`
that the stamp guard filters as our own.

### 6.3 `Md.App.Logic.Documents.TextFileSession` — one state machine for documents and book articles

State: `Stage` (`Untitled | Editing(path) | Handoff(path) | Unreadable(path) | Empty`), `Text` (LF),
`Title` (base name without extension; "Untitled", "Untitled 2", … per process for untitled windows),
`Dressing { TextEncodingKind Encoding; bool HadUtf8Bom; NewLine NewLine }`, `IsDirty` (differs from the
last explicit save/open), `LastSavedText`, `DiskStamp (DateTime LastWriteUtc, long Length)?`, `Conflicted`,
`SaveErrorText?`, `ImportedFrom?` (bundle path, display only). Dependencies (seams): `IFileSystem`,
`IFileWatcher`, `IScheduler`, `IDocumentRegistry`, `IWordCounter`.

| Event | Behaviour |
| --- | --- |
| Open file | bytes → `Md.Core.Text.PlainTextCodec.Decode` (UTF-16 only behind a BOM; strict UTF-8; CP1251; Latin-1) — failure → alert "The document could not be opened." with the decode error; a leading U+FEFF from a UTF-8 file is stripped and `HadUtf8Bom = true` (re-emitted on save — decision pinned here); newline detected; stamp taken; `IsDirty = false`; `LastSavedText = Text`; MRU add; watcher on |
| Open `.textpack` / `.textbundle` folder | `TextBundle.TextFromPack(bytes)` / `TextBundle.TextFromBundleFolder(path)` (null → alert "The TextPack could not be read." — never fed to the text decoder); `Stage = Untitled`, `Title` = the bundle's stem, `ImportedFrom` = bundle path, `IsDirty = false`; Save → Save As suggesting `<stem>.md`; bundles are never written back (their `assets/` would be lost) |
| Keystroke | `Edit(text)`: `IsDirty = true`; if `Stage is Editing && !Conflicted` → autosave timer restart (1.0 s) |
| Autosave tick | `FlushNow(explicit: false)`: stamp check (`stamp(path) != DiskStamp`, a vanished file counts as stale → `Conflicted = true`, stop) → `PlainTextCodec.Encode(text, Encoding)` (returns the encoding actually used — CP1251 upgrades to UTF-8 and is **remembered**) → `TextFileDressing.Dress` (BOM, newline) → in-place write → new stamp. **Does not clear `IsDirty`** (the Mac stays "— Edited" after autosave-in-place). Failure → `SaveErrorText`, still dirty |
| Save (Ctrl+S) | untitled → Save As; else `FlushNow(explicit: true)` → on success `IsDirty = false`, `LastSavedText = Text` |
| Save As | picker → write the new path (same encoding/newline), `Stage = Editing(new)`, MRU add, watcher re-target, `ApplyMemory(newIdentity)`, `IsDirty = false`, `LastSavedText = Text`; the old file stays |
| Revert to Saved | `Text = LastSavedText` → write → editor replaced (§3.2) → `IsDirty = false` |
| External change (`IFileWatcher.Changed`) | ignore if `stamp == DiskStamp` (our own write / attribute-only touch); dirty → `Conflicted = true` (autosave suspended); clean → silent reload (text, dressing, stamp; selection preserved) |
| `Renamed` | `Stage = Editing(newPath)`; identity changed → §5.2 |
| `Deleted` | dirty → `Conflicted` (Keep My Version recreates); clean → **document window: keep the buffer, mark dirty, keep the path** (NSDocument keeps the window); **book article: detach** |
| Conflict UI | `InfoBar { Severity = InfoBarSeverity.Warning, Title = "The file changed on disk." }` whose `Content` is a `StackPanel` with two `Button`s **Reload from Disk** / **Keep My Version** (InfoBar has a single `ActionButton`, so both go in `Content`); a failed save: `InfoBar` "Couldn’t save — {error}" with **Retry** as `ActionButton` and **Save As…** in `Content`. Routine autosave shows nothing |
| Close | untitled && dirty → `ContentDialog` "Do you want to save the changes made to the document “{title}”?" — **Save** / **Don't Save** / **Cancel**; saved → `FlushNow(explicit: false)`; a failed flush → rescue copy `"{stem} (rescued).{ext}"`, `(rescued 2)`, … (100 attempts) + alert "Could not save “{name}”" / "Your text was kept as “{file}” in the same folder.", then close anyway |
| Exit | every window runs its close policy; Cancel on any aborts |

Flush is also forced on `Window.Activated` with `WindowActivationState.Deactivated`, before every
Share/Print/Export (so Share ▸ Source shares the saved bytes), on Close Book and on `AppDomain.ProcessExit`
(best effort). Read-only files open normally; the first autosave fails into the error `InfoBar` — never a
modal. Windows shutdown gives WinUI no reliable "will terminate" hook; the 1 s autosave bounds the loss.

The fourteen macOS `BookArticleSession` tests (book.md §10.10) port to xUnit against a temp folder
(`FileAttributes.ReadOnly` replaces `chmod 444`), plus the document-window rows above.

### 6.4 Rename / Move To / Duplicate

Rename…: `ContentDialog` "Rename" with a `TextBox` pre-filled with the base name and the message
"Only the name changes — the file extension is kept."; `FileNames.Validate(name)` (Md.App.Logic: rejects
`\ / : * ? " < > |`, trailing dot or space, reserved device names) → alert "Could not rename" with
"A name cannot contain \ / : * ? \" < > | or end with a dot or a space."; then flush,
`StorageFile.RenameAsync(newName, NameCollisionOption.FailIfExists)`, re-target session/watcher/MRU,
`ApplyMemory(newIdentity)`. Move To…: `FolderPicker` → `StorageFile.MoveAsync(folder, name, NameCollisionOption.FailIfExists)`,
same follow-up. Duplicate: new untitled window, text copied, `IsDirty = true`, title `"{name} copy"`.

### 6.5 TextBundle / TextPack import

`.textpack` (Open…, activation, drop): `TextBundle.TextFromPack` (Core `ZipReader`, only `text.*`
inflated). `.textbundle` (File ▸ Open TextBundle Folder…, folder drop): `TextBundle.TextFromBundleFolder(path)`
reads `text.md` → `text.markdown` → first `text.*` (sorted ordinal). Both open as **read-only documents
titled after the bundle** (§6.3 row) — the window title is the bundle's stem, closing an unedited import
prompts nothing, Save asks for a new `.md` location. The bundle's images are not shown (parity).

### 6.6 MRU and Jump List

`StorageApplicationPermissions.MostRecentlyUsedList.Add(file, file.Path, RecentStorageItemVisibility.AppAndSystem)`
on open and Save As — `AppAndSystem` also feeds Windows' Recent items and the taskbar Jump List for our
associated types; the list caps at 25 (`MaximumItemsAllowed`). File ▸ Open Recent lists `Entries` newest
first; a token whose `GetFileAsync(token)` fails → `Remove(token)` + alert "The file “{name}” could not be
found."; Clear Menu → `Clear()`. Untitled documents and bundle imports are not added.

### 6.7 Title and dirty indicator

`Window.Title` = `"{displayName}"` or `"{displayName} — Edited"` (U+2014; the Mac's Edited proxy).
Autosave never clears it; only an explicit Save does. Imported bundles show the stem; Examples show
"Untitled — Edited"; untitled windows are numbered "Untitled", "Untitled 2", … per process.

---

## 7. Exports and print

### 7.1 The export renderer — `Md.App.Web.ExportRenderer : IRenderSurface`

- A **fresh `WebView2` per export**, created inside the requesting window's `ExportCanvas` (a `Canvas` in
  the root `Grid`; `Canvas.SetLeft(view, -10000)`; `Width = 595`, `Height = 842` epx; `Visibility.Visible`
  — never `Collapsed`, so the controller reports `IsVisible = true` and Chromium does not throttle timers).
  `EnsureCoreWebView2Async(env)`, then §4.2 (`AssetHost.Attach`) and §4.4 settings (no scroll-sync or
  link-guard scripts), then
  `await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", "{\"width\":595,\"height\":842,\"deviceScaleFactor\":1,\"mobile\":false}")`
  so the layout viewport is exactly 595 CSS px and screenshots are DPI-independent (without it a 150 %
  monitor scales every EPUB PNG by 1.5). Chromium's page visibility follows `IsVisible`, not screen
  position, so an off-canvas child HWND is a fully live page.
- `LoadAsync(html)`: set `Html` → `Navigate(IndexUrl)` → await `NavigationCompleted` (`IsSuccess` else
  `ExportException("The rendered page could not be captured.")`) → `RenderCompletePoller` (§4.8).
- `EvalAsync<T>(script)`: `ExecuteScriptAsync` → JSON-decode (`JsonSerializer.Deserialize<T>`; the four
  characters `null` → `default`). Never write a raw result to disk.
- `CaptureRegionPngAsync(rect, scale)`: `CallDevToolsProtocolMethodAsync("Page.captureScreenshot", json)` with
  `{"format":"png","clip":{"x":X,"y":Y,"width":max(W,1),"height":max(H,1),"scale":2},"captureBeyondViewport":true}`
  → `{"data": base64}` → bytes. Fallback if the CDP call throws on some runtime: `Scripts.CanvasRasterise`
  (the VS Code canvas rasteriser — formulas then keep their source text).
- `PdfAsync(settings)`: `PrintToPdfAsync(tempPath, settings)` (returns `false` → "The PDF pages could not be produced.").
- Disposed after the export: remove from the `Canvas`, `webView.Close()`. Exports on one window are
  serialised (`SemaphoreSlim(1)`); a `ProgressRing` appears in the footer after 500 ms.
- **Fallback hosting** (written, off): `ExportHostWindow` — a `Window` whose `AppWindow` is shown with
  `AppWindow.Show(activateWindow: false)`, `IsShownInSwitchers = false`, `Move(new PointInt32(-10000, -10000))`,
  client 595 × 842. A `Window` that is merely constructed and never shown has no host-visible `XamlRoot`, and
  the WinUI control would set the controller invisible — hence `Show(false)`, never "create without
  `Activate()`" (fidelity defect fixed). Only used if the in-window `Canvas` misbehaves on a Windows build.

`Md.App.Logic.Export.ExportPipeline` holds every string step and drives `IRenderSurface`; it is tested
against a fake surface. `RenderKind` selects the HTML: `Screen` (preview), `Paper` (Print, PDF — Core HTML
with `export: true` **plus** `ScreenHtml.WithWindowsFonts`), `Export` (HTML / EPUB / SVG — pure Core HTML).

### 7.2 Print… (document and book) — `PrintOverlay`

1. `html = ScreenHtml.WithWindowsFonts(MarkdownHtml.Document(source, title, dark: false, export: true))`
   (paper: white, 11 pt, Georgia; no page-size rewrite — paper is the printer's business, as on the Mac).
2. Show `PrintOverlay` inside the calling window: a full-content `Grid` (dimmed paper) holding a **visible**
   `WebView2` (same environment and `AssetHost`, the DPI override, no injected scripts) and a small bar with
   **Print…** (re-show the dialog) and **Done**. `LoadAsync` + render-complete wait.
3. `core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser)` — Chromium's preview dialog appears over the
   overlay. It is drawn **inside the WebView2's bounds**; WebView2Feedback #3361 shows it is not displayed
   at all for a hidden control, so the off-canvas export renderer can never host it (two designs did this —
   fixed). Paper, margins and printer come from the dialog. Chromium's "Headers and footers" option
   defaults on and would print the title and `https://md.assets/index.html`; the dialog remembers the
   user's last choices in the user-data folder, so unticking it once sticks — documented in the README.
4. `ShowPrintUI` has no completion event ("doesn't open a new print dialog if it is already open"), hence
   **Done**. Render failure is swallowed (Mac). Fallback: `CoreWebView2PrintDialogKind.System` works even for
   a hidden control (same issue) but has no preview.

### 7.3 PDF (Export ▸ PDF…, Share ▸ Rendered PDF…, Book PDF)

```
html = PdfExport.StyledForExport(MarkdownHtml.Document(source, title, false, export: true), pageSize)   // Core: first "padding: 48px 56px;" → cssPadding
html = ScreenHtml.WithWindowsFonts(html)                                                                 // paper → Georgia
await renderer.LoadAsync(html)
settings = env.CreatePrintSettings();  Orientation = CoreWebView2PrintOrientation.Portrait
PageWidth = pageSize.Width / 72.0;  PageHeight = pageSize.Height / 72.0                                  // inches
ScaleFactor = 1.0;  ShouldPrintBackgrounds = true;  ShouldPrintHeaderAndFooter = false;  ShouldPrintSelectionOnly = false
MarginTop = MarginBottom = MarginLeft = MarginRight = 0.5
temp = Path.Combine(ApplicationData.Current.TemporaryFolder.Path, $"md-pdf-{Guid.NewGuid()}.pdf")
ok = await core.PrintToPdfAsync(temp, settings);  if (!ok) throw new PdfPaginationException("The PDF pages could not be produced.")
```

Export: `FileSavePicker` ("PDF" → `.pdf`, name `Sanitized(title).pdf`) **after** rendering (Mac order); the
picker's created file is filled with `File.Copy(temp, file.Path, overwrite: true)` then the temp deleted
(never `File.Move` without overwrite — the target already exists). Share: write
`TemporaryFolder\{Sanitized(title)}.pdf`, share (§7.8). Alerts "Could not export PDF" / "Could not generate PDF".
Margins: 0.5 in all round is the decision (Mac inherits Page Setup, Android 0.5, VS Code 0) — the body's
CSS padding wraps only the whole document, so pages 2…n need a printer margin. Pinned by the self-test
(§11.4): MediaBox 595.2 × 841.8 ±1 for A4, 432 × 648 for 6 × 9; three `\newpage` sections → exactly three
pages; first and last paragraph present; a 40-token code line wraps. Byte parity with the Mac's PDF is
impossible (§12).

### 7.4 HTML

`html = HtmlExport.PreparePage(source, title)` (Core: `Document(export: true)` + page-break rule swap, **all**
occurrences) → `LoadAsync` (pure, `RenderKind.Export`) → `EvalAsync<string>(Scripts.CaptureHtml)` (strip
`script, link[rel="stylesheet"]`, remove the completion flag, return `outerHTML`; empty → "The rendered
page could not be captured.") → `HtmlExport.Finish(inputHtml, "<!DOCTYPE html>\n" + captured, readAsset)`
(Core: KaTeX CSS + fonts inlined when the **input** references `rich/katex.min.css`, Mermaid notice when the
captured page contains `class="mermaid"`) → `FileSavePicker` ("HTML" → `.html`) → UTF-8 without BOM.
`readAsset(relative)` = `File.ReadAllBytes(Path.Combine(WebRoot, relative))` with containment. Alert
"Could not export HTML".

### 7.5 EPUB

Document: `title = EpubExport.DocumentTitle(MarkdownParser.FrontMatter(source), fileName)` **first** (the
suggested name must match), `FileSavePicker` ("EPUB" → `.epub`) **before** building, then
`EpubExport.BuildDocumentAsync(source, title, snapshotter)`. Book: picker first (`Sanitized(book.Title).epub`),
then `BookOutput.ExportEpub()` → flush gate → `BookFolder.ReadStructuredBook(...)` → `EpubExport.BuildAsync(book, snapshotter)`.

`snapshotter` = `Md.App.Export.RichSnapshotter : IRichSnapshotter` (renders `RenderKind.Export`):
1. `LoadAsync(documentHtml)`;
2. `h = EvalAsync<double>("document.documentElement.scrollHeight")`; set the renderer `Height = Math.Max(842, h)`;
   `await Task.Delay(300)` — the Mac's repaint wait is **kept** even though `captureBeyondViewport` would work
   without it: Mermaid's `max-width: 100%` layout must be final before rects are read;
3. `rects = EvalAsync<double[][]>(Scripts.RichElements)` (the 5-column Mac script; rows with `Length != 5`
   dropped, degenerate rects **kept**);
4. per rect `CaptureRegionPngAsync(rect, 2)`; Core pairs PNGs 1:1 with `ReplacingRichElements` and **throws**
   on a DOM-count vs markup-count mismatch (Android's guard, adopted) → "The rich-content images could not be
   captured."; tags `<img src="images/unit-{unit:000}-rich-{i}.png" alt="{formula|diagram}" width="{Math.Round(w, MidpointRounding.AwayFromZero)}"/>`.
Alert "Could not export EPUB".

### 7.6 Diagram as SVG and LaTeX

SVG: the row carries its `DiagramSvg.Diagram`; render `Document(dark: false, export: true)` (`RenderKind.Export`),
wait, `EvalAsync<string>(Scripts.DiagramSvg(ordinal))` (`pre.mermaid, div.plantuml, div.graphviz, div.plot`);
`null` → "This diagram couldn't be captured — it may have failed to render." (no empty file);
`DiagramSvg.StandaloneDocument(svg)`; picker ("SVG" → `.svg`, name `{Sanitized(title)}-{ordinal+1}.svg`);
alert "Could not export SVG". LaTeX: pure `LaTeXExport.Document(source)` / `LaTeXExport.Book(book)` →
picker ("LaTeX" → `.tex`) → UTF-8 without BOM; alert "Could not export LaTeX".

### 7.7 TextBundle (folder)

1. `FolderPicker` (title "Choose where to keep the TextBundle") → parent folder.
2. Target `{parent}\{Sanitized(title)}.textbundle`; exists → `ContentDialog` "A folder named “…” already
   exists." **Replace** / **Cancel** (the save panel's Replace).
3. `TextBundleExport.ExportRewriting(source, resolveAsset)` (Core) with
   `resolveAsset = AssetReader.Beside(documentPath)` (Md.App.Logic): unsaved document → always null; folder =
   `FileIdentity.Canonical(Path.GetDirectoryName(path))`; candidate = `FileIdentity.Canonical(Path.GetFullPath(Path.Combine(folder, rel)))`;
   require `candidate.StartsWith(folder + "\\", OrdinalIgnoreCase)` and `File.Exists`; also reject
   `^[A-Za-z]:[\\/]` and a leading `\` (documented Windows deviation).
4. Write `text.md` (UTF-8, no BOM), `info.json` (Core bytes), `assets\` (always created) + files.
Alert "Could not export TextBundle". `.textpack` export is not offered in 1.0 (the Mac product is a folder).

### 7.8 Share — `Md.App.Export.ShareBridge`

The desktop interop the docs prescribe: `[ComImport, Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IDataTransferManagerInterop { IntPtr GetForWindow([In] IntPtr appWindow, [In] ref Guid riid); void ShowShareUIForWindow(IntPtr appWindow); }`;
`DataTransferManager.As<IDataTransferManagerInterop>()`, `GetForWindow(hwnd, ref dtmIid)` with
`dtmIid = new Guid(0xa5caee9b, 0x8708, 0x49d1, 0x8d, 0x36, 0x67, 0xd2, 0x5a, 0x8d, 0xa0, 0x0c)`,
`WinRT.MarshalInterface<DataTransferManager>.FromAbi(ptr)`, `DataRequested += (s, e) => { e.Request.Data.Properties.Title = title; e.Request.Data.SetStorageItems(new[] { file }); }`,
then `ShowShareUIForWindow(hwnd)`. `GetForCurrentView()` throws in desktop apps — never used. Source: the
real `StorageFile` when saved, else `TemporaryFolder\{Sanitized(title)}.md` (UTF-8). Rendered PDF: render
**first**, then share the temp file (a `DataRequested` deferral would time out on a slow PlantUML page).
The Book pane publishes `fileURL = null`, so from the Book window Source… shares a copy of the article
text (Mac).

### 7.9 Alerts

All `ContentDialog { XamlRoot = window.Content.XamlRoot, CloseButtonText = "OK" }` (XamlRoot is required
in desktop apps), one at a time per window (`WinUiAlerts` serialises). Titles verbatim: "Could not generate
PDF", "Could not export PDF", "Could not export HTML", "Could not export LaTeX", "Could not export SVG",
"Could not export TextBundle", "Could not export EPUB"; message = the exception message.
`Sanitized(name)` (Core): split on `/\:?%*|"<>`, join `-`, `Trim()`, empty → "Document"; Md.App.Logic adds
`FileNames.ForWindows` (trailing dots/spaces, reserved names) before handing a name to a picker.

---

## 8. Books

### 8.1 Window and layout (`BookWindow`)

`SplitView { DisplayMode = SplitViewDisplayMode.Inline, IsPaneOpen = true, OpenPaneLength = 240, PanePlacement = SplitViewPanePlacement.Left }`
(Mac ideal 240; no user resize — WinUI ships no splitter and third-party toolkits are off limits): pane =
sidebar (§8.3); content = detail `CommandBar` (§8.4) + detail (§8.5). Empty state when no book: `FontIcon`
`` (36 epx, secondary), "No Book Open" (Georgia 22.7 epx), "A book is a folder: its subfolders are
chapters and its Markdown files are articles." (16 epx, secondary, centred, `MaxWidth 420`), button
"Open Book…". Full screen via F11 is the distraction-free room (no Zen here — the Mac publishes none).

Lifecycle: `Loaded` → `session.OpenBook()`, `Reload()`, `SelectionChanged(selection)` by hand; close →
`session.CloseBook()`; `md.bookBookmark` change → close/open session, `Reload()`; derived refresh as §5.5.

### 8.2 `BookLibraryHost` (Md.App) over `Md.Core.Book.BookFolder` (pure listing / rename / renumber / compile)

| Mac | Windows |
| --- | --- |
| `store(url)` bookmark | `StorageApplicationPermissions.FutureAccessList.AddOrReplace("md.book", folder)`; `LocalSettings["md.bookBookmark"] = folder.Path` (non-empty ⇒ book open; readable for diagnosis and PRIVACY) |
| `beginAccess()` | `FutureAccessList.ContainsItem("md.book")` → `GetFolderAsync("md.book")` → `folder.Path` (authoritative; a moved folder still resolves); failure → the stored path if `Directory.Exists`; else "book folder not accessible" placeholder, setting kept (the Mac's nil `beginAccess`) |
| `closeBook()` | `FutureAccessList.Remove("md.book")`, `LocalSettings.Remove("md.bookBookmark")`; the folder is untouched |
| `newBook()` save panel | `FolderPicker` (parent) + `ContentDialog` "New Book" with a `TextBox` default "My Book" and the message "Name your book and choose where to keep it. Chapters are folders inside it; articles are Markdown files.", button **Create** → `Directory.CreateDirectory`; exists → alert "Could not create book" |
| `unpackExampleBook()` | `FolderPicker` (message shown first: "Choose where to keep the example book. It is an ordinary book folder — chapters inside, Markdown articles in each — yours to edit.") → copy `<install>\Examples\Example Book` to `{picked}\Example Book`, deduped "Example Book 2", … (a picker cannot ask Replace; a half copy is removed on failure); alert "Could not unpack example book" |
| `chooseBook()` | `FolderPicker` (message shown first: "Choose a folder to open as a book. Its subfolders are chapters; its Markdown files are articles.") |

Listing skips names starting with `.`, `FileAttributes.Hidden`/`System`, and `.md-reorder-*`; inside a
chapter only files (the Mac's `Foo.md`-folder bug is not reproduced). Two-phase reorder via hidden
`.md-reorder-{Guid}` temp names with rollback; alert "Could not reorder". Case-only folder renames route
through the same staging (`Directory.Move` may refuse them). FutureAccessList holds 1000 items; one fixed
token keeps us at one.

### 8.3 Sidebar (built in code — no templates for xamlcheck to miss)

A `ListView` (`SelectionMode = Single`) whose items `BookSidebarBuilder` creates from `Book`: root article
rows, a "New Article…" row, then per chapter a non-selectable header row (full folder name, Georgia
17.3 epx) with `ContextFlyout` = the management menu, its article rows and its "New Article…" row.
Article row = `ListViewItem { Content = StackPanel(FontIcon , TextBlock article.Name 17.3 epx), Tag = path }`
with `ContextFlyout`: **Open in New Window**, divider, **Rename…**, **Move Up** (disabled at index 0),
**Move Down** (disabled at last), divider, **Delete…**. `SelectionChanged` → `navigator.SelectionChanged(path)`;
`DoubleTapped` → `OpenInWindow(path)`. Pane header: `AppBarButton` "New Chapter…" (``, tooltip
"Create a new chapter folder in the book"). No per-row gesture handlers that would race selection (the
Mac's lesson).

Prompts (`ContentDialog` + `TextBox`), verbatim: **New Chapter** — "A chapter is a folder. Start the name
with a number ("02 …") to place it in the reading order." (Create / Cancel); **New Article** — "A Markdown
file is created, started with a matching heading."; **Rename** — pre-filled `DisplayName`, "Only the name
changes — the ordering number and the file extension are kept."; **Delete “{name}”?** — chapter: "The
chapter folder and every article in it will be deleted." / article: "The article file will be deleted."
(**Delete** as `PrimaryButtonText` with `DefaultButton = ContentDialogButton.Close`, **Cancel**). All three
name prompts validate with `FileNames.Validate` and surface "Could not rename" / the Windows message (§6.4).

### 8.4 Detail toolbar (`CommandBar`, `DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed`)

1. View mode: three `AppBarToggleButton`s (`` Edit, `` Split, `` Preview), mutually
   exclusive in code, disabled when `session.EditingPath == null`, tooltip "Switch between editing, split and preview".
2. Previous / Next Article (`` / ``), tooltips "The previous article in reading order (Ctrl+Alt+Up)" /
   "The next article in reading order (Ctrl+Alt+Down)", enabled by the stepper flags.
3. Contents (``) — `AppBarButton.Flyout` is a `MenuFlyout` rebuilt in its `Opening` event (a
   `FlyoutBase` event that exists) with the §2.7 indent rule; tooltip "Jump to a heading".
4. Notes (``) — `NotePreview` rows; tooltip "Jump to a private author note".
5. Share (``), tooltip "Compile the whole book into a PDF, an EPUB, or print it": Share as PDF,
   Export as PDF…, **PDF Page Size ▸** (the seven `RadioMenuFlyoutItem`s bound to `md.pdfPageSize`), divider,
   Export as EPUB…, Export as LaTeX…, divider, Print…, divider, `ToggleMenuFlyoutItem`
   **"Open Articles in Separate Windows"** ↔ `md.bookOpensInSeparateWindows` (verbatim — product.md §1.11).

### 8.5 Detail pane and the session

The Book window hosts one `TextFileSession` (§6.3) in article mode — `Select(path?) → bool` (idempotent;
a failed flush returns `false` and the caller reverts the selection), `Detach(reportFailure)`,
`FlushNow`, `ResolveConflictKeepingMine` / `ResolveConflictReloading`, `HandOffForExternalOpen`,
`RecheckOwnership`, `WriteRescueCopy`, `TerminateFlush` — plus `Md.App.Logic.Books.BookNavigatorModel`
(pure): book.md §13.7 verbatim — `SelectionChanged`, `Reload(preferring)`, `PerformManaged`, `Move`,
`PerformRename`, `PerformDelete`, `DeletionNeighbor`, `OpenInWindow`, `RelativePath` for
`md.bookLastArticle` (**`/`-separated**, the Mac's spelling; compared after `Path.GetFullPath`,
`OrdinalIgnoreCase`), and `BookStepper` (`CanPrevious/CanNext/Previous/Next`, entering from the front/back;
`(false, false)` while `md.bookOpensInSeparateWindows`).

Stages render: **Editing** → `ArticlePanes` (the same editor+preview composite as a document window; a
fresh `TextBox` undo stack per article via `ClearUndoRedoHistory()`, preview `token = path`, footer);
**Handoff** → ``, "Open in Its Own Window", "“{title}” is open as a document window, and that window
owns the file while it stays open. Close it to write here again.", button **Show Window**; **Unreadable** →
``, "Could Not Read the Article", "“{title}” could not be read. It may have been moved or deleted
outside the book."; **Empty** → ``, "Select an Article", "Choose an article in the sidebar to write
here. Ctrl+Alt+Up and Ctrl+Alt+Down move through the book in reading order." (the shortcut is the only
string change).

Footer (Georgia 14.7 epx, `PaperBackgroundSecondaryBrush`): conflict → "The file changed on disk." (warning
glyph ``, orange) + **Reload from Disk** + **Keep My Version**; save error → "Couldn’t save — {err}"
+ **Retry**; right: words · characters. Mode = `md.bookViewMode` (app-wide, default split) overridden by a
transient nudge; a pick stores the raw value; books never touch `md.viewModeMemory`.

Watcher: `FileSystemWatcherAdapter : IFileWatcher` (Md.App.Logic) watches the article's **folder** with
`Filter = fileName`, `NotifyFilter = LastWrite | Size | FileName`, `Changed`/`Renamed`/`Deleted`, delivered
through `IUiThread.Post` (the App's `DispatcherQueue.TryEnqueue`). Our own writes are filtered by stamp
equality; editors that write twice are handled because the stamp — not the event count — decides.
`FileSystemWatcher` is unreliable on some shares; the stamp check before every write is the actual guard.

### 8.6 `WindowRegistry` (Md.App.Logic, `IDocumentRegistry`) and ownership

Dictionary canonical path (`OrdinalIgnoreCase`) → window id; `Owning(path)`, `FindByPath`, `Register`,
`Unregister`, `Windows` (for the Window menu). `WindowManager` (Md.App) maps ids to live windows and
implements `Activate(id)`. The Book session calls `RecheckOwnership()` on every window's `Activated`
event (the Mac's `didBecomeKeyNotification`); `ShowOwningWindow()` → `Activate`; `OpenInWindow` →
`HandOffForExternalOpen()` → `BookArticleOpens.Mark(identity)` (10 s, Core) → `WindowManager.OpenPath`.

### 8.7 `BookOutput` and the flush gate

`Md.App.Logic.Books.BookFlushGate`: a synchronous `event Action<BookFlushGate>? Request` with `bool Vetoed`;
`BookOutput` raises it before every output; the Book window's handler runs `FlushNow()` and sets `Vetoed`
on failure → the output aborts silently (the footer already says why). No Book window → no subscriber →
gate open. PDF / print compile via `BookFolder.CompileBookSource()` (alerts "Could not compile book" — "No
book is open, or the book folder is not accessible." / "The article \"{name}\" could not be read.");
EPUB / LaTeX via `BookFolder.ReadStructuredBook(failureTitle)`. **Compile decoder: `PlainTextCodec.Decode`
for compile/EPUB/LaTeX too** — the Mac's UTF-8-then-Latin-1 `readArticle` would compile a CP1251 article
that edits correctly into mojibake; recorded as a deliberate divergence for legacy-encoded books (UTF-8
books are byte-identical). All book renders are light (`dark: false`); they run in the Book window's
renderer if it exists, else the invoking document window's.

---

## 9. Settings — every `ApplicationData.Current.LocalSettings.Values` key

| Key | Type | Default | Written by | Read by | Notes |
| --- | --- | --- | --- | --- | --- |
| `md.viewModeMemory` | string (`v1` codec, Core) | absent ⇒ `[]` | `ViewModeController.SetMode` / migrate branch | `ViewModeMemory.Lookup` | ≤ 200 entries ≈ 4.6 KB, under the 8 KB per-value limit; identity = `sha256("file:" + canonical path)[0..8]` hex |
| `md.pdfPageSize` | string (`PageSize.Id`) | `"a4"` | the two PDF Page Size pickers | Share/Export PDF, Book PDF | unknown → A4 |
| `md.bookBookmark` | string (book folder path) | `""` = no book | `BookLibraryHost.Store/CloseBook` | Book menu enablement, Book window | the grant itself is `FutureAccessList["md.book"]` |
| `md.bookOpensInSeparateWindows` | bool | `false` | Book share menu toggle | `BookNavigatorModel`, `BookStepper` | |
| `md.bookLastArticle` | string (`/`-separated, relative to the root) | `""` | `BookNavigatorModel.SelectionChanged` | `Reload` | |
| `md.bookViewMode` | string (`edit`/`split`/`preview`) | `"split"` | Book pane `Select` | Book pane | app-wide, never per file |
| `md.win.windowSize.document` **(Win)** | string `"WxH"` epx | absent ⇒ `900x640` | `DocumentWindow` close | new document windows | last size |
| `md.win.windowSize.book` **(Win)** | string `"WxH"` | absent ⇒ `1000x700` | `BookWindow` close | Book window | |

Not in LocalSettings: `md.viewMode`, `md.zen`, `md.zenReading` and window placements → `LocalFolder\session.json`
(§1.6); recent documents → `MostRecentlyUsedList` (system-managed, 25); the book grant →
`FutureAccessList` token `md.book`; the WebView2 user-data folder → `LocalCacheFolder\WebView2`; per-document
encoding / newline / scroll positions → session memory only.

`ISettingsStore { string? GetString(string key); void SetString(string key, string value); bool GetBool(string key, bool fallback); void SetBool(string key, bool value); void Remove(string key); event Action<string> Changed; }`
— `LocalSettingsStore` (App) and `InMemorySettingsStore` (Logic, tests). **PRIVACY.md must enumerate
exactly**: the eight keys above, `session.json`, the `md.book` token, the MRU list, the WebView2 user-data
folder — plus the paragraphs product.md §4.3 demands on the WebView2 Runtime's own diagnostics (Microsoft's
privacy statement) and the Store's aggregate acquisition reports, with the sentence that md "adds nothing
to that stream and reads nothing from it".

---

## 10. Theme and typography

`App.xaml` `ResourceDictionary.ThemeDictionaries` with keys `Light`, `Default` (dark) and `HighContrast`
(system brushes):

| Resource | Light | Dark | Role |
| --- | --- | --- | --- |
| `PaperBackgroundBrush` / `PaperBackgroundColor` | `#F4EFE2` | `#241E18` | window, panes, WebView2 `DefaultBackgroundColor`, title bar |
| `PaperBackgroundSecondaryBrush` | `#EAE2CF` | `#2F2820` | footer, find bar, sidebar, Zen capsule tint |
| `PaperInkBrush` | `#2B2620` | `#E7DBC2` | text, caption buttons, icons |
| `AccentBrush` | `#9C6B2E` | `#C99A55` | selection highlight, selected Zen switch, toggles |
| `PaperInkSecondaryBrush` | ink @ 60 % | ink @ 60 % | footer, sidebar buttons, placeholder titles |
| `PaperInkTertiaryBrush` | ink @ 40 % | ink @ 40 % | `PlaceholderForeground` |
| `PaperBorderBrush` | `rgba(43,38,32,0.16)` | `rgba(231,219,194,0.16)` | dividers (the CSS `border` value) |

`Palette.For(ElementTheme) → (Paper, PaperSecondary, Ink, Accent)` (Md.App.Logic, pure record of the four
colours as ARGB ints) is the single source; `App.xaml` and the code that sets `DefaultBackgroundColor`,
the title-bar colours and the acrylic tint read from it. The app follows the system theme
(`Application.RequestedTheme` unset); `dark = rootGrid.ActualTheme == ElementTheme.Dark` feeds the preview
HTML; `ActualThemeChanged` re-tints and re-renders (§4.5). No Mica / system backdrop — the whole window
is paper on the Mac. The MSIX manifest already uses `#241E18` for tile and splash.

**Fonts (the facts file's settled decision).** The shared stylesheet bytes stay
`"American Typewriter", "Courier New", serif`. On screen and on paper the app appends
`<style id="md-win-fonts">body{font-family:Georgia,"Courier New",serif;}</style>` (§4.3); exports are pure.
XAML text that the Mac sets in American Typewriter uses **Georgia**, sizes converted ×4/3:

| Surface | Mac pt | WinUI epx |
| --- | --- | --- |
| editor, placeholder | 15 | **20** |
| footer | 11 | **14.7** |
| book sidebar rows, chapter headers, find bar | 13 | **17.3** |
| "New Article…" | 12 | **16** |
| placeholder titles ("Select an Article", "No Book Open") | 17 | **22.7** |
| placeholder messages | 12 | **16** |

Menus, dialogs, `CommandBar` labels keep Segoe UI Variable (the Mac's menus are the system font too). No
font is bundled (a licence decision nettrash has not made); the README states the Georgia stand-in as
md.vscode's does. Icons: Segoe Fluent Icons (`FontIcon.Glyph`) mapped from SF Symbols — `square.and.pencil`
``, `rectangle.split.2x1` ``, `eye` ``, `arrow.down.right.and.arrow.up.left` ``,
`chevron.up/down` ``/``, `list.bullet` ``, `note.text` ``, `square.and.arrow.up`
``, `doc.text` ``, `folder.badge.plus` ``, `plus` ``, `books.vertical` ``,
`macwindow` ``, `exclamationmark.triangle` ``, `printer` ``.

---

## 11. File layout

### 11.1 `src/Md.App.Logic` (new; plain `net10.0` class library; references `Md.Core` only; no WinUI/WinRT types)

`Md.App.Logic.csproj`: `<TargetFramework>net10.0</TargetFramework>`, `<Nullable>enable</Nullable>`,
`<InvariantGlobalization>false</InvariantGlobalization>` (Core's setting), `<InternalsVisibleTo Include="Md.App.Logic.Tests" />`,
`AllowUnsafeBlocks` for the two P/Invoke files. Builds and tests on macOS, Linux and Windows.

| File | Purpose | Tests |
| --- | --- | --- |
| `Commands/CommandId.cs`, `Commands/CommandTable.cs`, `Commands/Chord.cs`, `Commands/CommandEnablement.cs`, `Commands/ShellSnapshot.cs`, `Commands/CommandDispatcher.cs` | §2.9 — the table, chords, enablement, dispatcher with the 150 ms debounce (`IClock`) | every id once; no duplicate chord; Mac-verbatim titles; seven menu titles in order; every enablement row of §2; debounce |
| `Activation/ActivationRouter.cs`, `Activation/ActivationDescription.cs`, `Activation/ActivationAction.cs` | §1.2 | the table rows |
| `Windows/WindowRegistry.cs`, `Windows/WindowTitle.cs`, `Windows/WindowPlacement.cs`, `Windows/SessionStore.cs` | §8.6, §6.7, §1.3 sizes/clamping, §1.6 JSON | title rows; `"WxH"` round trip; clamp; JSON round trip and missing-file skipping |
| `Documents/TextFileSession.cs`, `Documents/TextFileDressing.cs`, `Documents/LineEndings.cs`, `Documents/EditorText.cs`, `Documents/LineOffsets.cs`, `Documents/FileStamp.cs`, `Documents/RescueCopy.cs`, `Documents/FileNames.cs`, `Documents/AssetReader.cs`, `Documents/TextSearch.cs`, `Documents/DocumentLoader.cs` | §6.3, §3.2, §3.3, §6.4, §7.7, §3.5, §6.5 | the 14 session tests + document rows; CR/CRLF/LF round trips; `OffsetOfLine` vectors; rescue naming; Windows name validation; containment; find wrap |
| `Documents/FileIdentity.cs` **(P/Invoke, Windows-only at run time)** | §5.2 canonical path | two spellings → one identity; junction equality (`[Fact]` skipped off Windows) |
| `Documents/FileSystemWatcherAdapter.cs`, `Documents/SystemIoFileSystem.cs` | `IFileWatcher`, `IFileSystem` over `System.IO` | stamp-filtered echo with a temp folder |
| `View/DocumentWindowState.cs`, `View/ViewModeController.cs`, `View/ZenController.cs`, `View/SplitLayout.cs`, `View/DerivedTextScheduler.cs`, `View/DerivedText.cs`, `View/ScrollSync.cs`, `View/ScrollSyncGuard.cs` | §5 | the seven Swift view-mode cases + migrate / re-decide / sticky book exemption / nudge cleared by a pick / Zen never stores; presenter-observer ordering; 640 rule; 250 ms scheduler with a fake clock |
| `Text/NotePreview.cs`, `Text/IWordCounter.cs`, `Text/SimpleWordCounter.cs`, `Text/IcuWordCounter.cs` **(P/Invoke)** | §5.5, §5.6 | emoji/combining marks; the five vectors for both counters; CJK vector for ICU (Windows leg). §2.3's `01-` stays `01-` vector lives in `Md.Core.Tests/ExampleLibraryTests`, with the bundled nine pinned by `BundledExamplesTests`. |
| `Preview/PreviewCoordinator.cs`, `Preview/IPreviewSurface.cs`, `Preview/PreviewNavigation.cs`, `Preview/EditorJump.cs`, `Preview/LinkPolicy.cs`, `Preview/ScreenHtml.cs`, `Preview/AssetMime.cs`, `Preview/Scripts.cs`, `Preview/JsonScript.cs` | §4 | first load / coalescing / token change / restore only when > 0 / stale-while-collapsed / parked navigation / dedupe by id; the link matrix incl. `https://md.assets/other → Cancel`; exact `md-win-fonts` bytes + idempotence; MIME table; the scroll script equals the Mac's except the one substitution; JSON decoding of `"1"`, `null`, numbers |
| `Export/ExportPipeline.cs`, `Export/IRenderSurface.cs`, `Export/RenderCompletePoller.cs`, `Export/PrintGeometry.cs`, `Export/ExportFileNames.cs`, `Export/RenderKind.cs` | §7 | every flow against a fake surface (picker-before-render order for EPUB, render-before-picker for PDF); 250 ms × 480 and timeout = success; inches = pt/72, margins 0.5; export HTML never contains `md-win-fonts` |
| `Books/BookNavigatorModel.cs`, `Books/BookStepper.cs`, `Books/BookFlushGate.cs`, `Books/BookOutput.cs`, `Books/BookSidebarModel.cs` | §8.5–§8.7 | book.md §13.7 algorithms against temp folders; stepper entering from front/back; gate veto |
| `Settings/ISettingsStore.cs`, `Settings/InMemorySettingsStore.cs`, `Settings/SettingsKeys.cs`, `Settings/Palette.cs` | §9, §10 | key constants pinned; palette values |
| `Seams/IFileSystem.cs`, `Seams/IFileWatcher.cs`, `Seams/IScheduler.cs`, `Seams/IClock.cs`, `Seams/IAlerts.cs`, `Seams/IPickers.cs`, `Seams/IShare.cs`, `Seams/IUiThread.cs`, `Seams/IDocumentRegistry.cs` | interfaces Md.App implements (§13.2) | fakes live in the test project |
| `Strings.cs` | every user-facing string (dialog titles, messages, labels) as constants | pinned verbatim |

`tests/Md.App.Logic.Tests` (xUnit, `net10.0`): one test class per file above; runs in the existing
`windows.yml` `core` matrix (`windows-latest` + `ubuntu-latest`); Windows-only facts are `[Fact]`s guarded
by `OperatingSystem.IsWindows()` (skipped elsewhere, exercised on the Windows leg).

### 11.2 `src/Md.App` — the thin WinUI layer (type-checked on the Mac through `tools/xamlcheck`)

| File | Purpose |
| --- | --- |
| `Program.cs`, `Redirection.cs` | §1.1 |
| `App.xaml`, `App.xaml.cs` | theme dictionaries (§10), `TextControl*` resource overrides (§3.1), `OnLaunched` → `ActivationRouter`, `AppInstance.Activated`, `UnhandledException` logging |
| `Windows/DocumentWindow.xaml(.cs)` | root `Grid` (rows: menu · content · find bar · info bar · footer), `ZenGrid`, `PrintOverlay` host, `ExportCanvas`; wires controllers ↔ controls; `AppWindow.Closing`, `Closed`, `Activated`, `Changed`, `SizeChanged`, `ActualThemeChanged` |
| `Windows/BookWindow.xaml(.cs)` | menu row, `SplitView`, sidebar `ListView`, detail `CommandBar`, `ArticlePanes`, stage placeholders, footer, `InfoBar`, `ExportCanvas` |
| `Windows/WindowManager.cs` | live windows ↔ `WindowRegistry` ids; create / activate / cascade; `ShowBookWindow`; `SetForegroundWindow` after a redirect |
| `Windows/TitleBarTint.cs` | `AppWindowTitleBar` colours + `PreferredTheme` from `Palette` |
| `Controls/ArticlePanes.xaml(.cs)` | editor + preview + divider composite (`SplitLayout` applied here) used by both windows |
| `Controls/EditorPane.cs` | `TextBox` configuration, Tab key, ScrollViewer lookup, caret jumps, `\r` adapter |
| `Controls/PreviewHost.cs` | `WebView2` init, `WebView2Surface : IPreviewSurface`, message/navigation handlers, context-menu pruning, `ProcessFailed` recovery |
| `Controls/ZenControls.cs`, `Controls/FindBar.xaml(.cs)`, `Controls/FooterBar.xaml(.cs)`, `Controls/PrintOverlay.xaml(.cs)`, `Controls/AboutDialog.cs` | §5.4, §3.5, §5.5, §7.2, §2.8 |
| `Menus/MenuBarBuilder.cs`, `Menus/AcceleratorInstaller.cs` | §2.9 |
| `Web/WebViewEnvironment.cs`, `Web/AssetHost.cs`, `Web/ExportRenderer.cs`, `Web/ExportHostWindow.cs` (fallback) | §4.1, §4.2, §7.1 |
| `Export/DocumentExports.cs`, `Export/RichSnapshotter.cs`, `Export/ShareBridge.cs`, `Export/RichAssets.cs` | §7 (the App halves: pickers, snapshots, share, `rich/` reads) |
| `Book/BookLibraryHost.cs`, `Book/BookSidebarBuilder.cs` | §8.2, §8.3 |
| `Services/LocalSettingsStore.cs`, `Services/DispatcherScheduler.cs`, `Services/UiThread.cs`, `Services/Pickers.cs`, `Services/RecentFiles.cs`, `Services/WinUiAlerts.cs`, `Services/ExampleLibrary.cs`, `Services/SelfTest.cs` | `ISettingsStore`, `IScheduler`/`IClock` over `DispatcherQueueTimer`, `IUiThread`, `IPickers`, MRU, `IAlerts`, examples, the `--selftest` runner (§11.4) |
| `Interop/NativeMethods.cs` | `user32!SetForegroundWindow`, `user32!GetDpiForWindow`, `IDataTransferManagerInterop` (COM), optional `ole32!CoWaitForMultipleObjects` |
| `Assets/`, `rich/` (→ `web\rich\`), `Examples/`, `Package.appxmanifest`, `app.manifest`, `Md.App.csproj` | as today plus the changes in §11.3 |

XAML totals five small files with static structure and `x:Name`s only — no `x:Bind`, no `{Binding}`, no
templates, no `ThemeResource` lookups the lint cannot prove (brushes are read from `Application.Current.Resources`
in code with a typed fallback). Everything dynamic is set from code.

### 11.3 Changes to existing files

- `Md.App.csproj`: `DISABLE_XAML_GENERATED_MAIN` + `<StartupObject>Md.App.Program</StartupObject>`;
  `<ProjectReference Include="..\Md.App.Logic\Md.App.Logic.csproj" />`; replace `<Content Include="rich\**\*" />`
  with `<Content Include="rich\**\*" Link="web\rich\%(RecursiveDir)%(Filename)%(Extension)" />`; give **every**
  `<Content>` group (`Assets\`, `rich\`, `Examples\`, `LICENSE`) `CopyToOutputDirectory="PreserveNewest"`,
  without which none of them reaches the output directory or the MSIX payload (§4.2);
  `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` for the interop file. Capabilities unchanged (`runFullTrust`).
- `Package.appxmanifest`: unchanged (associations already declared; `.textbundle` stays unassociated;
  `Identity` from Partner Center).
- `md.slnx`: add `src/Md.App.Logic`, `tests/Md.App.Logic.Tests`.
- `.github/workflows/windows.yml`: the `core` matrix job also runs `dotnet test tests/Md.App.Logic.Tests`;
  a new `xamlcheck` step on `ubuntu-latest` runs `tools/xamlcheck/run.sh`; the `app` job gains a
  post-build step that runs the unpackaged build with `--selftest` (§11.4) and uploads its report.

### 11.4 The WebView2 self-test (`md.exe --selftest <outDir>`)

A `tests/Md.App.Tests` project is not possible (app types need Windows and XamlCompiler). The
WebView2-backed assertions export.md §10 / rich.md §10 list therefore live in the app as a hidden mode:
`Services/SelfTest.cs` runs when `Launch` arguments start with `--selftest`: it opens one hidden-by-Zen-size
window, drives `ExportPipeline` against the real `ExportRenderer` for the fixture documents (engines render
offline; mhchem `\ce{}` with 0 `.katex-error`; hljs spans; plot 1 svg / 2 polylines / 1 script; self-contained
HTML round-trips from `file://`; EPUB snapshot count == markup count), produces PDFs and checks them with the
no-library regex (`/MediaBox\s*\[\s*0\s+0\s+([\d.]+)\s+([\d.]+)\s*\]` per page, `/Type\s*/Page[^s]` count —
Chromium writes page dictionaries uncompressed), writes `report.json`, and exits 0/1. CI runs it on
`windows-latest` after the build; it never ships enabled (Release builds compile it behind
`#if SELFTEST` set by the CI build).

---

## 12. Risks and what cannot be byte-identical

Ranked by how late the failure would be noticed.

1. **Runtime-only WinUI behaviour** (cannot be seen from the Mac): accelerators while the WebView2 has
   focus (forwarding is documented to work and to double-fire — the debounce handles both; `KeyRelay` is the
   written contingency if they do not fire at all); `TextBox` `\r` conversion (the session is correct either
   way); `AppWindow.Changed` timing around our own `SetPresenter` (`toggling` flag); the `ContentElement`
   template part name (fallback search); off-canvas `WebView2` staying live (fallback: `ExportHostWindow`).
   Each has a day-1 checklist item (§13.4).
2. **WebView2 contract details**: MIME inference of folder-mapped `.woff2`/`.js` (contingency `AssetMime`);
   `no-store` honoured on `Reload()`; `PrintToPdfAsync` returning `false` on a locked path (temp file, then
   copy); CDP `Page.captureScreenshot` availability (canvas fallback); `ShowPrintUI` with no close event
   (Done button); WebView2 runtime first-launch latency (~0.5–1 s) painted paper, not white.
3. **Not byte-identical, by platform fact**: PDF and print (Chromium lays out at 96 CSS px/in where WebKit
   paginated at 72; Georgia instead of American Typewriter; 0.5 in margins vs inherited Page Setup); the
   on-screen preview typeface (Georgia via the appended style); word counts for scripts ICU and
   CFStringTokenizer segment differently (rare; both are ICU-derived); the `localizedStandardCompare`
   fallback order (Core's `NaturalCompare`, parity with Android, not Finder); Windows name sanitising
   extended with reserved names; the compile decoder unified on the codec. **What is byte-identical**:
   every HTML/CSS/SVG/EPUB/LaTeX/TextBundle string Core produces (golden fixtures), the settings codec, the
   identity hash algorithm, `md-init.js` and `rich/`, the Examples — and the HTML/EPUB/SVG exports, because
   they never see the Windows font style.
4. **Missing macOS affordances**: Versions (no analogue); a document browser at launch (New instead);
   spell checking (deliberately off); `.textbundle` as an associated type (folder); `NSSharingServicePicker`'s
   rich targets (Windows Share offers whatever apps are installed); a "will terminate" hook (loss bounded by
   the 1 s autosave); autosave of *untitled* drafts (`~/Library/Autosave Information` has no equivalent —
   a dirty untitled window prompts on close and is not restored).
5. **Product traits inherited on purpose (do not "fix")**: local relative images never render in the preview
   or the HTML export; `javascript:` links are blocked host-side only; Mermaid's first-block light-theme race
   in dark mode (family-wide, documented in md.vscode); the `plantuml.js` 4096 px gate (app copy shipped —
   the md.vscode lift is a one-line candidate, not taken); raw HTML escaped, no reference links, `.dot`
   unclaimed.
6. **Caret colour**: the WinUI `TextBox` has no caret brush; the caret is ink, not accent.
7. **Ctrl+Alt+↑/↓** may collide with legacy Intel display-rotation hotkeys; Alt+↑/↓ is the one-row fallback.
8. **Sidebar width** fixed at 240 epx (no splitter without a toolkit); the Mac allows 200–320.
9. **Book folder access** is permanent once granted (`FutureAccessList`); there is no scope start/stop, so
   "Close Book in another window during a pending save" cannot revoke access — strictly safer.
10. **Toolchain**: Windows App SDK 2.4.0 with `Microsoft.Windows.SDK.BuildTools.WinApp` for `dotnet run`
    identity is new tooling; the CI MSIX job is the first place a packaging mistake shows; `PublishReadyToRun`
    + no trimming keeps WinRT/WebView2 interop reflection-safe.
11. **Store**: `runFullTrust` only; the privacy policy URL is mandatory; the listing must never say
    "no third-party dependencies", "zero permissions" or "no network", and must not use `<` `>`.

---

## 13. Implementation plan

### 13.1 Where testable logic lives — the decision

Everything that can be tested without Windows goes into **`src/Md.App.Logic`** (plain `net10.0`, references
`Md.Core`, no WinUI/WinRT types), tested by **`tests/Md.App.Logic.Tests`** (xUnit) in the existing
cross-OS CI matrix. Md.App holds only adapters over the seams in §13.2 and the XAML; it is type-checked on
every OS by `tools/xamlcheck/run.sh` and compiled/packaged on `windows-latest`. Md.Core stays the other
team's byte-parity library and is not modified by this design (the surface it is expected to expose is
listed in §13.5). WebView2-backed behaviour is checked by the in-app `--selftest` (§11.4) on the Windows leg.

### 13.2 The seams — C# signatures every package codes against

```csharp
namespace Md.App.Logic.Seams;

public interface IClock { DateTimeOffset Now { get; } }

public interface IScheduler                                   // UI-thread timers; App: DispatcherQueueTimer
{
    IDisposable After(TimeSpan delay, Action action);         // one-shot; disposing cancels
    void Post(Action action);                                 // next dispatcher turn
}

public interface IUiThread { void Post(Action action); bool IsCurrent { get; } }   // App: DispatcherQueue.TryEnqueue

public readonly record struct FileStamp(DateTime LastWriteUtc, long Length);

public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes);   // FileMode.Create over the existing file
    FileStamp? Stamp(string path);
    void Move(string from, string to);
    void Delete(string path);
    IEnumerable<string> EnumerateEntries(string directory);
}

public interface IFileWatcher : IDisposable
{
    void Watch(string filePath);                              // folder + Filter = file name
    void Stop();
    event Action<string> Changed;                             // args: path; raised on the UI thread
    event Action<string, string> Renamed;                     // old, new
    event Action<string> Deleted;
}

public interface IDocumentRegistry
{
    Guid? Owning(string canonicalPath);
    void Register(Guid windowId, string canonicalPath);
    void Unregister(Guid windowId);
    IReadOnlyList<(Guid Id, string Title)> Windows { get; }
    event Action Changed;
}

public interface IAlerts
{
    Task WarnAsync(string title, string message);
    Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel);
    Task<bool> ConfirmDeleteAsync(string title, string message);
    Task<CloseChoice> AskSaveChangesAsync(string title);      // Save | DontSave | Cancel
    Task<bool> ConfirmReplaceAsync(string message);
}

public interface IPickers
{
    Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions);
    Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension);
    Task<string?> PickFolderAsync(string? title);
}

public interface IShare { Task ShareFileAsync(string path, string title); }

public interface IWordCounter { int Count(string text); }

public interface IFileIdentity { string Canonical(string path); }

public interface IPreviewSurface
{
    string Html { get; set; }
    bool IsShown { get; }
    void Navigate(string url);
    void Reload();
    Task<string> EvalAsync(string script);                    // raw JSON result
    event Action<bool> NavigationCompleted;                   // IsSuccess
}

public interface IRenderSurface : IAsyncDisposable
{
    Task LoadAsync(string html, CancellationToken ct);        // navigate + render-complete wait
    Task<string> EvalAsync(string script);                    // raw JSON result
    Task<byte[]> CaptureRegionPngAsync(RectD cssRect, double scale);
    Task SetHeightAsync(double cssPx);
    Task<byte[]> PdfAsync(PrintGeometry geometry);
}

public interface IRenderSurfaceFactory { Task<IRenderSurface> CreateAsync(RenderKind kind); }   // App: ExportRenderer per export
```

```csharp
namespace Md.App.Logic.Commands;
public enum CommandId { New, Open, OpenRecentEntry, ClearRecent, OpenTextBundleFolder, Example, ExampleBook, Close, Save, SaveAs,
    Duplicate, Rename, MoveTo, RevertToSaved, Print, ShareSource, ShareRenderedPdf, ExportPdf, ExportHtml, ExportEpub, ExportLaTeX,
    ExportTextBundle, ExportDiagramSvg, PdfPageSize, Exit, Undo, Redo, Cut, Copy, Paste, Delete, SelectAll, Find, FindNext, FindPrevious,
    UseSelectionForFind, ViewEdit, ViewSplit, ViewPreview, ZenMode, ShowSidebar, FullScreen, NewBook, OpenBook, ShowBook, CloseBook,
    ShareBookPdf, PrintBook, ExportBookPdf, ExportBookEpub, ExportBookLaTeX, PreviousArticle, NextArticle, Contents, Notes,
    Minimize, Zoom, ActivateWindow, Help, PrivacyPolicy, About, Escape }
public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }
public readonly record struct Chord(int VirtualKey, KeyModifiers Modifiers) { public string DisplayText { get; } }
public sealed record CommandSpec(CommandId Id, string Title, Chord? Chord, MenuPath Path, CommandKind Kind, bool WindowsOnly);
public static class CommandTable { public static IReadOnlyList<CommandSpec> All { get; } }
public sealed record ShellSnapshot(bool HasDocument, bool IsBookWindow, bool IsEditingArticle, bool IsSaved, bool IsDirty, bool HasBook,
    Mode DisplayedMode, bool ZenActive, bool ZenReading, IReadOnlyList<OutlineEntry> Outline, IReadOnlyList<NoteEntry> Notes,
    IReadOnlyList<DiagramSvg.Diagram> Diagrams, bool CanPrevious, bool CanNext, bool EditorVisible, bool CanUndo, bool CanRedo,
    IReadOnlyList<RecentEntry> RecentEntries, IReadOnlyList<(Guid Id, string Title, bool IsThis)> WindowTitles,
    bool HasSelection, bool HasFindQuery, string PdfPageSizeId);
public static class CommandEnablement { public static bool IsEnabled(CommandId id, ShellSnapshot s); public static bool IsChecked(CommandId id, ShellSnapshot s); }
public sealed class CommandDispatcher(IClock clock, Func<ShellSnapshot> snapshot)
{ public void Register(CommandId id, Action<object?> handler); public bool TryInvoke(CommandId id, object? argument = null); }
```

```csharp
namespace Md.App.Logic.Documents;
public enum NewLine { Lf, CrLf }
public sealed record TextFileDressing(TextEncodingKind Encoding, bool HadUtf8Bom, NewLine NewLine)
{ public static (string Text, TextFileDressing Dressing) Undress(byte[] bytes); public byte[] Dress(string text, out TextEncodingKind used); }
public abstract record Stage { public sealed record Untitled : Stage; public sealed record Editing(string Path) : Stage;
    public sealed record Handoff(string Path) : Stage; public sealed record Unreadable(string Path) : Stage; public sealed record Empty : Stage; }
public sealed class TextFileSession(IFileSystem fs, IFileWatcher watcher, IScheduler scheduler, IDocumentRegistry registry, IFileIdentity identity)
{
    public Stage Stage { get; }  public string Text { get; }  public string Title { get; }  public bool IsDirty { get; }
    public bool Conflicted { get; }  public string? SaveErrorText { get; }  public string? ImportedFrom { get; }
    public TextFileDressing Dressing { get; }
    public event Action? Changed;  public event Action<string>? TextReplacedExternally;  public event Action<string?>? IdentityChanged;
    public void OpenUntitled(string text, string? title = null, bool dirty = false);
    public bool Open(string path);                                   // false + alert text on decode failure
    public bool ImportBundle(string bundlePath, string text, TextEncodingKind encoding);
    public bool Select(string? path);                                // book mode
    public void Edit(string text);
    public bool FlushNow(bool explicitSave);
    public bool SaveAs(string path);
    public void RevertToSaved();
    public void Detach(bool reportFailure);
    public void ResolveConflictKeepingMine();  public void ResolveConflictReloading();
    public bool HandOffForExternalOpen();  public void RecheckOwnership();  public string? WriteRescueCopy();  public void TerminateFlush();
}
```

```csharp
namespace Md.App.Logic.Preview;
public enum RenderKind { Screen, Paper, Export }
public static class ScreenHtml { public const string FontStyle = "<style id=\"md-win-fonts\">body{font-family:Georgia,\"Courier New\",serif;}</style>"; public static string WithWindowsFonts(string coreHtml); }
public enum LinkDecision { Allow, Cancel, OpenExternally }
public static class LinkPolicy { public static LinkDecision Decide(Uri uri, bool isUserInitiated, Uri indexUrl); }
public sealed class PreviewCoordinator(IPreviewSurface surface, IScheduler scheduler)
{ public void Update(string text, string title, bool dark, string? token); public void Show(); public void Hide();
  public void Navigate(PreviewNavigation nav, Action<Guid> onHandled); public double SavedScrollY { get; } }
```

```csharp
namespace Md.App.Logic.Export;
public sealed record PrintGeometry(double PageWidthIn, double PageHeightIn, double MarginIn = 0.5) { public static PrintGeometry For(PageSize size); }
public sealed class RenderCompletePoller(IScheduler scheduler) { public Task WaitAsync(IRenderSurface s, CancellationToken ct); /* 250 ms × 480 */ }
public sealed class ExportPipeline(IRenderSurfaceFactory renderers, IPickers pickers, IShare share, IAlerts alerts, IFileSystem fs, Func<string, byte[]?> readRichAsset)
{
    public Task PrintAsync(string source, string title, Func<string, Task> showPrintOverlay);   // overlay is App-side; pipeline supplies the paper HTML
    public Task ExportPdfAsync(string source, string title, PageSize size);   public Task SharePdfAsync(string source, string title, PageSize size);
    public Task ExportHtmlAsync(string source, string title);                  public Task ExportEpubAsync(string source, string fileName);
    public Task ExportBookEpubAsync(EpubBook book);                            public Task ExportLaTeXAsync(string source, string title);
    public Task ExportBookLaTeXAsync(EpubBook book);                           public Task ExportDiagramSvgAsync(string source, string title, DiagramSvg.Diagram d);
    public Task ExportTextBundleAsync(string source, string? documentPath, string title);
    public Task ShareSourceAsync(string? path, string text, string title);
}
```

### 13.3 Work packages — file ownership and the order they can start in

Each package owns the listed files exclusively; a package needing something from another codes against
the §13.2 interfaces (which **WP0 checks in first, frozen**). Fakes for every seam live in
`tests/Md.App.Logic.Tests/Fakes/` and are owned by WP0 too.

| WP | Scope | Owns (Logic) | Owns (App) | Depends on | Mac-verifiable deliverable |
| --- | --- | --- | --- | --- | --- |
| **WP0 Foundation** | csproj/slnx/CI, seams, fakes, palette, strings, Program/App skeleton, theme resources | `Md.App.Logic.csproj`, `Seams/*`, `Settings/*`, `Strings.cs`; `tests/Md.App.Logic.Tests` project + `Fakes/*` | `Program.cs`, `Redirection.cs`, `App.xaml(.cs)`, `Services/LocalSettingsStore.cs`, `Services/DispatcherScheduler.cs`, `Services/UiThread.cs`, `Interop/NativeMethods.cs`, csproj/manifest edits, `windows.yml` | — | both projects build; xamlcheck green; CI matrix runs Logic tests |
| **WP1 Commands & menus** | §2 | `Commands/*` | `Menus/MenuBarBuilder.cs`, `Menus/AcceleratorInstaller.cs`, `Controls/AboutDialog.cs` | WP0 | table invariants; enablement rows; debounce |
| **WP2 Documents** | §6, §3.2 | `Documents/*` (incl. `FileIdentity`, `FileSystemWatcherAdapter`, `SystemIoFileSystem`) | `Services/Pickers.cs`, `Services/RecentFiles.cs`, `Services/WinUiAlerts.cs`, `Services/ExampleLibrary.cs` | WP0 | 14 session tests + document rows; newline/BOM round trips; rescue; identity (Windows leg) |
| **WP3 Windows & activation** | §1 | `Activation/*`, `Windows/*` (registry, title, placement, session store) | `Windows/DocumentWindow.xaml(.cs)`, `Windows/WindowManager.cs`, `Windows/TitleBarTint.cs` | WP0; consumes WP1/WP2/WP4/WP5 through interfaces | router table; registry; session.json round trip |
| **WP4 Editor, modes, Zen** | §3, §5 | `View/*`, `Text/NotePreview.cs`, `Text/*WordCounter.cs` | `Controls/EditorPane.cs`, `Controls/ArticlePanes.xaml(.cs)`, `Controls/ZenControls.cs`, `Controls/FindBar.xaml(.cs)`, `Controls/FooterBar.xaml(.cs)` | WP0 | view-mode cases; Zen observer; 640 rule; word counters |
| **WP5 Preview host** | §4 | `Preview/*` | `Web/WebViewEnvironment.cs`, `Web/AssetHost.cs`, `Controls/PreviewHost.cs` | WP0 | coordinator branches; link matrix; `md-win-fonts` bytes; script substitution; MIME table |
| **WP6 Exports, print, share** | §7 | `Export/*` | `Web/ExportRenderer.cs`, `Web/ExportHostWindow.cs`, `Export/*`, `Controls/PrintOverlay.xaml(.cs)`, `Services/SelfTest.cs` | WP0, WP5 (`AssetHost`) | pipelines over the fake surface; poller; geometry; export HTML never carries the Windows style |
| **WP7 Books** | §8 | `Books/*` | `Windows/BookWindow.xaml(.cs)`, `Book/BookLibraryHost.cs`, `Book/BookSidebarBuilder.cs` | WP0, WP2 (`TextFileSession`), WP4 (`ArticlePanes`), WP6 (`BookOutput` → `ExportPipeline`) | book.md algorithms against temp folders; stepper; gate |
| **WP8 Store & docs** | product.md §2–§4 | — | `README.md`, `CHANGELOG.md`, `PRIVACY.md`, `msstore/` (per-field files + limits table), screenshots, IARC answers, the nettrash.me `assets/msstore/md/{privacy,support}.html` pages (other repo) | WP3/WP6 for screenshots | copy reviewed against the forbidden-claims list |

Parallelism: WP1, WP2, WP4, WP5 start together after WP0 freezes the seams (day 1–2); WP3 and WP6 start
once the interfaces exist and integrate as the others land; WP7 last. Integration points are exactly the
interfaces above plus three concrete classes other packages instantiate: `TextFileSession` (WP2),
`PreviewCoordinator` (WP5), `ExportPipeline` (WP6).

### 13.4 Stages and day-1 Windows checks

| Stage | Ships | Day-1 Windows checks (a tester with the unpackaged build) |
| --- | --- | --- |
| 0 Tooling (WP0) | both projects, CI, xamlcheck, `Program.cs` | launches packaged and unpackaged; a second `.md` double-click opens in the running instance and comes to the foreground |
| 1 Editor (WP1+WP2+WP3 minimum) | window, `MenuBar`, accelerators, open/save/Save As/Rename/Move/Duplicate/Revert, autosave + conflict `InfoBar` + rescue, MRU, title, session restore | `\r` behaviour; Tab inserts a tab; Ctrl+Shift+Enter does not insert a newline; autosave keeps LF/CRLF; conflict bar on external edit; Rename keeps identity in `md.viewModeMemory` re-decision |
| 2 Preview & modes (WP4+WP5) | virtual host, debounce/reload/scroll restore, links, scroll sync, Split/Edit/Preview, per-file memory, Contents/Notes, footer | fonts and `plantuml.js` load (MIME); `#slug` hops; a `javascript:` link does nothing; http link opens the browser; `[x](other.md)` does nothing; Ctrl+1 with the preview focused fires **once**; dark mode reload; Georgia on screen |
| 3 Exports (WP6) | HTML, LaTeX, SVG, PDF export/share, Print overlay, EPUB, TextBundle folder, page-size radios, alerts, `--selftest` | render-complete poll ends; PDF MediaBox/page count; CDP capture works at 150 % DPI; print preview appears in the overlay and Done closes it; Share sheet opens; exported HTML has no `md-win-fonts` |
| 4 Zen, theme, polish | Zen grid + capsule + fade, F11, theme switch, placement, About/Help | leaving full screen via F11/Esc/Win+Down drops Zen; no white flash on theme switch; title bar tinted |
| 5 Books (WP7) | Book window, sidebar, session hosting, management, stepper, outputs, FAL grant, Example Book | watcher events on the UI thread; case-only rename; handoff when an article is opened in a window; reorder swap |
| 6 Store (WP8) | copy, screenshots, IARC, unsigned MSIX from CI | Partner Center validation; `.md`/`.textpack` associations registered after install; privacy URL reachable from Help |

### 13.5 The Md.Core surface this design calls (names as the reports give them)

`Md.Core.Markdown.MarkdownHtml.Document(string source, string title, bool dark, bool export = false)`,
`MarkdownParser.Outline/Notes/FrontMatter(string)`, `Md.Core.Export.PageSize` (`All`, `Named(id)`, `Id`,
`Label`, `Width`, `Height`, `CssPadding`), `PdfExport.StyledForExport(html, pageSize)`, `HtmlExport.PreparePage/Finish`,
`EpubExport.DocumentTitle/BuildDocumentAsync/BuildAsync`, `DiagramSvg.Diagrams(source)` / `Diagram.MenuTitle` /
`StandaloneDocument(svg)`, `LaTeXExport.Document/Book`, `TextBundle.TextFromPack/TextFromBundleFolder`,
`TextBundleExport.ExportRewriting`, `ExportFileNames.Sanitized`, `Md.Core.Text.PlainTextCodec.Decode/Encode`,
`TextEncodingKind`, `Md.Core.Book.BookFolder` (`LoadBook`, `CreateChapter`, `CreateArticle`, `RenameItem`,
`DeleteItem`, `ApplyRenames`, `CompileBookSource`, `ReadStructuredBook`), `BookNaming`, `BookOrder.NaturalCompare`,
`RenumberPlan`, `ReadingOrder`, `Destination`, `ViewModeRule`, `ViewModeMemory` (`Identity`, `Lookup`,
`Remember`, `Sha256Prefix`), `BookArticleOpens.Mark/ClaimOpen`. If the Core team's final spelling differs,
the one place to change is the Logic call site; nothing in Md.App references Core directly except
`DiagramSvg.Diagram` in `ShellSnapshot`.

---

## 14. What the macOS app does that this design changes

Every deliberate deviation, with its reason. Everything not listed is ported as the Mac does it.

| # | macOS | Windows | Reason |
| --- | --- | --- | --- |
| 1 | Global menu bar; document windows have no chrome | A `MenuBar` row inside every window | Windows has no global menu bar; the row is the thinnest strip that carries every command |
| 2 | The **md** app menu (About, Quit) | Help ▸ About md; File ▸ Exit | No app menu on Windows |
| 3 | New / Open / Save / Save As / Duplicate / Rename / Move To / Revert / Close from NSDocument | Implemented in the app (§2.2, §6.4) | NSDocument does not exist; product.md §1.1 requires them |
| 4 | Versions | none | No Windows analogue; the docs must not promise it |
| 5 | Document browser / recents at launch | plain launch restores the last session, else one untitled window | No document browser on Windows |
| 6 | App keeps running with no windows | exits with the last window | A menu-bar-less process would be a ghost |
| 7 | `@SceneStorage` restores mode/Zen per window; window frames restored by AppKit | `session.json` (saved documents only) | No scene restoration in WinUI |
| 8 | Untitled drafts autosaved to `Autosave Information` and reopened | not autosaved; prompt on close | No equivalent store; a fake one would silently lose text |
| 9 | Edit menu Find / Spelling from `NSTextView` | own Find bar (Ctrl+F, F3, Shift+F3, Ctrl+E); no spelling, no replace | `TextBox` has neither; spell check is off on the Mac too |
| 10 | Accent-coloured caret | ink caret | `TextBox` has no caret brush |
| 11 | American Typewriter 15 pt in the editor and (via the shared CSS) the preview/paper | Georgia 20 epx in the editor; Georgia via an app-appended `<style>` on screen and paper; exports pure | Windows ships no American Typewriter; the facts file's settled decision; exports must stay byte-identical |
| 12 | PDF margins inherited from Page Setup; WebKit at 72 CSS px/in | 0.5 in on all sides; Chromium at 96 | The Mac inherits, Android sets 0.5, VS Code 0 — a choice had to be made; PDF bytes can never match |
| 13 | Print panel presets A4 | Chromium's dialog defaults (headers/footers on until unticked once) | `ShowPrintUI` accepts no presets; the product copy already allows "whatever the printer holds" |
| 14 | `.textbundle` is a document type (double-click, Open panel) | File ▸ Open TextBundle Folder… or folder drop | A folder cannot be associated or picked by `FileOpenPicker` |
| 15 | `.textbundle` export via the save panel | `FolderPicker` + Replace dialog | `FileSavePicker` cannot create a folder |
| 16 | `NSFilePresenter`/`NSFileCoordinator` | `FileSystemWatcher` + the (mtime,size) stamp check | No coordination API; the stamp is the actual guard |
| 17 | `ProcessInfo.disableSuddenTermination` | flush on close / deactivate, 1 s autosave | No sudden-termination counter |
| 18 | ⌃⌘↑ / ⌃⌘↓, ⇧⌘↩, ⌘1-3, ⇧⌘B, ⌘P | Ctrl+Alt+↑/↓, Ctrl+Shift+Enter, Ctrl+1-3, Ctrl+Shift+B, Ctrl+P; F11 full screen | Windows chords; F11 is the Windows full-screen convention |
| 19 | Book compile decodes UTF-8 then Latin-1; the editor uses the codec | Codec everywhere | A CP1251 article that edits correctly must not compile as mojibake (recorded divergence) |
| 20 | Rename rejects `/` and `:`; only Rename validates | All three name prompts reject `\ / : * ? " < > |`, trailing dot/space, reserved names | Windows' invalid set |
| 21 | Finder's `localizedStandardCompare` for unnumbered book names | Core's `NaturalCompare` | Parity with Android; no .NET equivalent of Finder order |
| 22 | Sidebar 200–320 pt resizable | fixed 240 epx | No splitter without a third-party toolkit |
| 23 | `.md-reorder-*` staging names hidden by the dot | hidden by the dot **and** `FileAttributes.Hidden` | Windows hides by attribute |
| 24 | Zen capsule in `.regularMaterial` | in-app `AcrylicBrush` tinted paper | Nearest material |
| 25 | `WKWebView` context menu (Copy, Reload…) | Copy / Select all only; Ctrl+wheel zoom disabled | Reload/Back/Print/Save make no sense for a live preview |
| 26 | Live preview never blocks `javascript:` itself (WebKit's policy did) | injected click guard | Chromium runs it in-page without a navigation event |
| 27 | Menus rebuilt on every menu build (Diagram as SVG) | rebuilt on snapshot change (Diagrams computed with the 250 ms derived tick) | `MenuFlyoutSubItem`/`MenuBarItem` expose no Opening event |
| 28 | `md.bookBookmark` holds a security-scoped bookmark | holds the folder path; the grant is the `FutureAccessList` token | No bookmarks on Windows; a readable path helps diagnosis and PRIVACY |
| 29 | Open Recent (10, NSDocumentController) | `MostRecentlyUsedList` (25, also feeds Windows Recent and the Jump List) | System-managed |
| 30 | Windows may be opened from the Window menu (system-supplied) | own Window menu (Minimize, Zoom, window list) | Literal port of a system feature |

---

## Appendix A — API verification log (learn.microsoft.com, 2026-09-06)

| Claim in a candidate design | Verified fact | Consequence here |
| --- | --- | --- |
| `TextBox.AcceptsTab` (fidelity) | Not on the WinUI 3 `TextBox` (only `AcceptsReturn`) | Tab handled in `KeyDown` (§3.1) |
| `MenuFlyoutSubItem.Opening` / `MenuBarItem` flyout (fidelity, risk-first) | `MenuFlyoutSubItem` declares `Icon`, `Items`, `Text`; `MenuBarItem` declares `Items`, `Title`; no events of their own | dynamic submenus rebuilt on snapshot change (§2.9) |
| WinUI `WebView2` exposes `CoreWebView2Controller` (native) | Members: `CoreWebView2`, `Source`, `CanGoBack/Forward`, `DefaultBackgroundColor`, `Close()`, `EnsureCoreWebView2Async()` / `(env)` / `(env, options)`, `ExecuteScriptAsync`, `GoBack/Forward`, `NavigateToString`, `Reload`; events `CoreProcessFailed`, `CoreWebView2Initialized`, `NavigationStarting/Completed`, `WebMessageReceived` | no controller bridge; debounce for the documented double-fire (microsoft-ui-xaml #6231, open) |
| Export renderer in a never-activated `Window` (fidelity) | `AppWindow.Show()` / `Show(bool activateWindow)` exist; `Hide()`, `Destroy()`, `IsShownInSwitchers`, `Move`, `ResizeClient`, `SetPresenter` confirmed | in-window `Canvas` primary; `Show(false)` window as fallback (§7.1) |
| `ShowPrintUI(Browser)` on an off-canvas renderer (risk-first, native) | WebView2Feedback #3361: the Browser preview is not displayed for a hidden control; `System` works; `ShowPrintUI` has no completion event | visible `PrintOverlay` + Done (§7.2) |
| `AppWindow.Closing` fires for `Window.Close()` (risk-first) | "Occurs when a window is being closed through a system affordance"; does not occur for `Destroy` | §1.4 three-route close policy |
| Two-argument `AddWebResourceRequestedFilter` (all three) | Deprecated in favour of `(String, CoreWebView2WebResourceContext, CoreWebView2WebResourceRequestSourceKinds)` | three-argument overload (§4.2) |
| `Application.Start(_ => new App())` without a synchronization context (risk-first, fidelity) | `DispatcherQueueSynchronizationContext(DispatcherQueue)` + `SynchronizationContext.SetSynchronizationContext` is what the generated `Main` installs | §1.1 |
| `File.Move(temp, pickedPath)` (risk-first) | WinRT `FileSavePicker.PickSaveFileAsync` returns a created file "but the file has no content" | `File.Copy(…, overwrite: true)` / in-place write; `DeleteAsync` on failure (§6.1, §7.3). Note: the App SDK `Microsoft.Windows.Storage.Pickers.FileSavePicker` stopped creating the file in 2.0.1 — a different API, not used here |
| `InfoBar` with two buttons (native) | single `ActionButton`; `Content` for more | two buttons in `Content` (§6.3) |
| `TextBlock.FontNumeralAlignment` (native) | `Typography.NumeralAlignment` attached property (`SetNumeralAlignment(DependencyObject, FontNumeralAlignment)`) | §5.5 |
| `SelectorBar` directly in `CommandBar.PrimaryCommands` (native) | not an `ICommandBarElement` | not used (MenuBar design) |
| `AcrylicInAppFillColorDefaultBrush` via `ThemeResource` (fidelity) | xamlcheck cannot prove resource lookups; `AcrylicBrush { TintColor, TintOpacity, TintLuminosityOpacity, FallbackColor, AlwaysUseFallback }` exists | code-built brush (§5.4) |
| Custom title bar hosting the `MenuBar` (fidelity) | `AppWindowTitleBar` colour properties apply to the standard title bar (alpha ignored when `ExtendsContentIntoTitleBar == false`); `PreferredTheme` (`TitleBarTheme`, 1.7+) | standard title bar, tinted (§1.3) |
| `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS` env var vs options (risk-first) | `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments` documented; `EnsureCoreWebView2Async(CoreWebView2Environment)` exists on the control | options object (§4.1) |
| ICU via `icu.dll` (fidelity) | `icu.dll` since Windows 10 1903, C API only, unversioned exports incl. `ubrk_open`, `ubrk_setText`, `ubrk_first`, `ubrk_next`, `ubrk_getRuleStatus`, `ubrk_close` | `IcuWordCounter` (§5.5) |
| `MostRecentlyUsedList.Add(file, name, RecentStorageItemVisibility.AppAndSystem)` (native) | overload exists; 25 items | §6.6 |
| `FutureAccessList.AddOrReplace("md.book", folder)` | `StorageItemAccessList.AddOrReplace(String, IStorageItem)`, `GetFolderAsync(String)`, `ContainsItem`, `Remove`; 1000 items | §8.2 |
| WinRT WebView2 signatures | `CoreWebView2Environment.CreateWithOptionsAsync(string, string, CoreWebView2EnvironmentOptions)`; `CreateWebResourceResponse(IRandomAccessStream, int, string, string)`; `CreatePrintSettings()`; `CoreWebView2EnvironmentOptions()` parameterless; `IAsyncOperation<string> ExecuteScriptAsync`; `IAsyncOperation<bool> PrintToPdfAsync(string, CoreWebView2PrintSettings)`; `ShowPrintUI(CoreWebView2PrintDialogKind)`; `CallDevToolsProtocolMethodAsync(string, string)`; `SetVirtualHostNameToFolderMapping(string, string, CoreWebView2HostResourceAccessKind)`; `CoreWebView2HostResourceAccessKind { Deny, Allow, DenyCors }`; `CoreWebView2PrintDialogKind { Browser, System }`; `CoreWebView2PrintSettings` has `MarginTop/Bottom/Left/Right` (inches), `PageWidth/Height` (inches), `ScaleFactor`, `Orientation`, `ShouldPrintBackgrounds`, `ShouldPrintHeaderAndFooter`, `ShouldPrintSelectionOnly`, `HeaderTitle`, `FooterUri`, `PrinterName`, `PageRanges`, `Copies`; `CoreWebView2Settings` has every property named in §4.4; `CoreWebView2Profile.PreferredColorScheme`; `CoreWebView2ContextMenuRequestedEventArgs.MenuItems` | §4, §7 as written |
| `AppInstance` | `FindOrRegisterForKey(String)`, `GetCurrent()`, `GetActivatedEventArgs()`, `RedirectActivationToAsync(AppActivationArguments)`, `IsCurrent`, `Key`, `Activated`; the C# sample redirects on a worker thread and waits on a semaphore | §1.1 |
| `Window` | `AppWindow`, `ExtendsContentIntoTitleBar`, `SetTitleBar`, `Activate`, `Close`, `Title`, `Content`, `Closed(WindowEventArgs.Handled)`, `Activated`, `SizeChanged`, `VisibilityChanged` | §1.3–1.4 |
| `OverlappedPresenter` | `PreferredMinimumWidth/Height`, `PreferredMaximumWidth/Height`, `Minimize()`, `Maximize()`, `Restore()`, `State`, `IsResizable` | §1.3, §2.8 |
| Windows App SDK 2.x notes | 2.0.1: semantic versioning, `SystemBackdropElement`, App SDK picker additions; 2.3.1: `DISABLE_XAML_GENERATED_MAIN` now renames the generated `Main`; 2.4.0: pickers restore focus; nothing removed that this design uses | §1.1 note |
| `Microsoft.Windows.Storage.Pickers` (1.8+) | exists, takes a `WindowId`, returns paths, works elevated | not used (hard constraint: `Windows.Storage.Pickers` + `InitializeWithWindow`); a candidate for a later swap |

## Appendix B — scripts the host injects or evaluates (source of truth: rich.md / export.md)

`Scripts.ScrollSync` (Mac script with the one `postMessage` substitution), `Scripts.LinkGuard` (§4.6),
`Scripts.CaptureHtml` (the verbatim capture IIFE), `Scripts.RichElements` (the 5-column
`querySelectorAll('.md-mathi, .md-mathd, .mermaid, .plantuml, .graphviz')` map), `Scripts.DiagramSvg(index)`
(the IIFE over `pre.mermaid, div.plantuml, div.graphviz, div.plot`), `Scripts.RenderComplete`
(`document.documentElement.getAttribute('data-md-render-complete')`), `Scripts.ScrollY` (`window.scrollY`),
`Scripts.ScrollHeight` (`document.documentElement.scrollHeight`), `Scripts.CanvasRasterise` (fallback only),
`Scripts.KeyRelay` (contingency, off). All number interpolation uses `CultureInfo.InvariantCulture`; every
result is JSON-decoded; nothing in `md-init.js` or `rich/` is edited.
