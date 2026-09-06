<!-- The Md.App.Logic API and integration notes the app work packages published for each other.
     Written from the WP1/WP2/WP4/WP5 hand-off reports (2026-09-06), after their adversarial reviews.
     WP3 (windows and activation), WP6 (exports and print) and WP7 (books) code against this.
     Where this file and the code disagree, the code and its tests win. -->

# Md.App.Logic — the API the work packages publish

Suite after WP1/2/4/5 were merged: **Md.App.Logic.Tests 907 green, Md.Core.Tests 1163 green,
`tools/xamlcheck/run.sh` clean over 4 XAML files / 11 named elements.**


---

## WP1 — commands and menus

## 3. Public API added in `Md.App.Logic` (namespace `Md.App.Logic.Commands`)

```csharp
public enum CommandId { New, Open, OpenRecentEntry, ClearRecent, OpenTextBundleFolder, Example, ExampleBook,
    Close, Save, SaveAs, Duplicate, Rename, MoveTo, RevertToSaved, Print, ShareSource, ShareRenderedPdf,
    ExportPdf, ExportHtml, ExportEpub, ExportLaTeX, ExportTextBundle, ExportDiagramSvg, PdfPageSize, Exit,
    Undo, Redo, Cut, Copy, Paste, Delete, SelectAll, Find, FindNext, FindPrevious, UseSelectionForFind,
    ViewEdit, ViewSplit, ViewPreview, ZenMode, ShowSidebar, FullScreen,
    NewBook, OpenBook, ShowBook, CloseBook, ShareBookPdf, PrintBook, ExportBookPdf, ExportBookEpub,
    ExportBookLaTeX, PreviousArticle, NextArticle, Contents, Notes, Minimize, Zoom, ActivateWindow,
    Help, PrivacyPolicy, About, Escape }                       // exactly §13.2's list

public enum CommandKind { Item, Toggle, DynamicItems, DynamicToggles, DynamicRadios, Accelerator }
[Flags] public enum KeyModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public static class VirtualKeys { public const int Enter, Escape, Delete, Up, Down, Number1, Number2,
    Number3, A, B, C, E, F, N, O, P, S, V, W, X, Y, Z, F1, F3, F11; }   // Win32 VK == Windows.System.VirtualKey

public readonly record struct Chord(int VirtualKey, KeyModifiers Modifiers) {
    public Chord(int virtualKey);
    public string DisplayText { get; }                 // "Ctrl+Shift+Enter"; order Ctrl, Alt, Shift
    public static string KeyName(int virtualKey); }    // "Del", "Esc", "Enter", "Up", "F11", "1", "Z"

public sealed record MenuPath(string Menu, string? Submenu = null, string? Nested = null) {
    public int Depth { get; } public string Leaf { get; } public IReadOnlyList<string> Segments { get; }
    public bool IsUnder(MenuPath parent); public MenuPath Truncate(int depth); }

public sealed record CommandSpec(CommandId Id, string Title, Chord? Chord, MenuPath? Path,
    CommandKind Kind, bool WindowsOnly, int Group = 0, bool RootAccelerator = true);

public sealed record MenuNode(string Title, MenuPath Path, CommandSpec? Command,
    IReadOnlyList<MenuNode> Children, bool SeparatorBefore);

public static class CommandTable {
    public static readonly IReadOnlyList<string> MenuTitles;   // File, Edit, View, Book, Go, Window, Help
    public const string PdfPageSizeGroupName = "PdfPageSize";
    public const string ExitFullScreenTitle = "Exit Full Screen";
    public static readonly IReadOnlyList<CommandSpec> All;             // 62 rows
    public static readonly IReadOnlyList<CommandSpec> RootAccelerators; // 20 of the 27 chords
    public static CommandSpec For(CommandId id);
    public static IReadOnlyList<MenuNode> Menus { get; }               // the seven menus as a tree
    public static string DisplayTitle(CommandId id, ShellSnapshot s);  // Enter/Exit Full Screen
    public static string ContentsRowTitle(Md.Core.Markdown.OutlineEntry e); }

public sealed record RecentEntry(string Token, string Name, string Folder);
public sealed record DiagramRef(int Ordinal, string MenuTitle);

public sealed record ShellSnapshot(bool HasDocument, bool IsBookWindow, bool IsEditingArticle, bool IsSaved,
    bool IsDirty, bool HasBook, Md.Core.Document.ViewMode DisplayedMode, bool ZenActive, bool ZenReading,
    IReadOnlyList<OutlineEntry> Outline, IReadOnlyList<NoteEntry> Notes, IReadOnlyList<DiagramRef> Diagrams,
    bool CanPrevious, bool CanNext, bool EditorVisible, bool CanUndo, bool CanRedo,
    IReadOnlyList<RecentEntry> RecentEntries, IReadOnlyList<(Guid Id, string Title, bool IsThis)> WindowTitles,
    bool HasSelection, bool HasFindQuery, string PdfPageSizeId,
    bool SidebarOpen = false, bool IsFullScreen = false, bool FindBarOpen = false) {
    public static readonly ShellSnapshot Empty;
    public Md.Core.Document.ViewMode PublishedMode { get; } }   // Zen rule of §2.5 applied

public static class CommandEnablement {
    public static bool HasActiveDocument(ShellSnapshot s);            // the §2.1 `doc` legend
    public static bool IsEnabled(CommandId id, ShellSnapshot s);
    public static bool IsSubmenuEnabled(MenuPath path, ShellSnapshot s);
    public static bool IsChecked(CommandId id, ShellSnapshot s);
    public static bool IsRowChecked(CommandId id, object? argument, ShellSnapshot s); }

public sealed class CommandDispatcher(Seams.IClock clock, Func<ShellSnapshot> snapshot) {
    public static readonly TimeSpan DoubleFireWindow;      // 150 ms
    public ShellSnapshot Snapshot { get; }
    public void Register(CommandId id, Action<object?> handler);
    public void Register(CommandId id, Action handler);
    public bool CanInvoke(CommandId id);
    public bool TryInvoke(CommandId id, object? argument = null);   // == args.Handled
    public void Execute(CommandId id, object? argument = null);
    public void ResetDebounce(); }
```

App side (internal): `Md.App.Menus.MenuBarSources(IReadOnlyList<Md.Core.Document.Example> Examples, Func<string,string> NotePreview)`; `Md.App.Menus.MenuBarBuilder(CommandDispatcher, MenuBarSources)` with `MenuBar Build()` and `void Refresh(ShellSnapshot)`; `Md.App.Menus.AcceleratorInstaller.Install(UIElement root, CommandDispatcher)`; `Md.App.Controls.AboutDialog.ShowAsync(XamlRoot) / Create(XamlRoot) / VersionText()`.

## 4. Integration notes

**For WP3 (window wiring) — the dispatch arguments.** `TryInvoke`/`Execute` carry one argument per dynamic row, and the window's handler must expect exactly this type: `OpenRecentEntry` → `string` MRU token; `Example` → `string` file name; `Contents` → `OutlineEntry`; `Notes` → `NoteEntry`; `ExportDiagramSvg` → `int` ordinal (re-resolve the real diagram from the source); `PdfPageSize` → `string` `PageSize.Id`; `ActivateWindow` → `Guid`. Everything else gets `null`.

**Wiring order per window:** build the content, `var bar = builder.Build()` into the menu row, `AcceleratorInstaller.Install(rootGrid, dispatcher)`, register every handler, then call `builder.Refresh(snapshot)` on every snapshot publish. `Refresh` returns immediately when the snapshot is equal, so publishing on every keystroke is free.

**`MenuBarBuilder` needs two things the snapshot does not carry** — pass them in `MenuBarSources`: the bundled `Example` list (WP2's `Services/ExampleLibrary.cs`, a process constant) and `NotePreview.Of` (WP4's `Text/NotePreview.cs`). Both are function/record parameters, so nothing in my files depends on code that does not exist yet; when WP4 lands, WP3 passes `NotePreview.Of` as the delegate.

**Part B Core API I had to work around.** `Md.Core.Export.DiagramSvg.Diagram` does not exist in this checkout (Wave C). §13.2 puts it in `ShellSnapshot`; I carry `DiagramRef(int Ordinal, string MenuTitle)` instead — the only two fields the menu uses. Integration is one line at the snapshot producer: `Diagrams = DiagramSvg.Diagrams(text).Select(d => new DiagramRef(d.Ordinal, d.MenuTitle)).ToList()`. This also means `Md.App` no longer references Core for the command surface at all (§13.5's one stated exception disappears) — Md.App still references `Md.Core.Document.{Example, PageSize, ViewMode}` from the builder.

**Decisions I had to take (all deviations from a literal §13.2, none from §2's behaviour):**
1. `CommandSpec` gained two trailing optional parameters. `int Group` places the dividers §2 draws (a group change between siblings ⇒ a separator) without needing separator ids that would break "every id appears exactly once". `bool RootAccelerator` marks the seven chords §2.4 leaves to the focused control (Ctrl+Z/Y/X/C/V/A and Del): they are shown via `KeyboardAcceleratorTextOverride` but never registered on the root, so the `TextBox` and the `WebView2` keep their editing keys. 20 of the 27 chords are root accelerators.
2. `MenuPath` is `MenuPath?` — null only for `Escape`, whose `CommandKind.Accelerator` has no menu row.
3. **Submenu containers are not table rows.** "Share ▸", "Export ▸", "Export Book ▸", "Open Recent ▸", "Examples ▸", "Diagram as SVG ▸", "PDF Page Size ▸", "Contents ▸", "Notes ▸" come from the paths, so no ids had to be invented. Their enablement is `IsSubmenuEnabled` = "any command inside is enabled", which reproduces every §2 row exactly, including "Export ▸ is never disabled" (PDF Page Size inside it never is).
4. **`ShellSnapshot` gained three trailing optional flags** the §13.2 field list omits but §2/§1.3 require: `SidebarOpen` (View ▸ Show Sidebar's tick), `IsFullScreen` (the Enter/Exit Full Screen title), `FindBarOpen` (with `ZenActive`, what makes Esc a command — an open find bar with an empty query is not `HasFindQuery`). Every producer of a snapshot must set these.
5. `DisplayedMode` is `Md.Core.Document.ViewMode` (§13.2 wrote `Mode`). `ShellSnapshot.PublishedMode` applies the Zen rule (`zenReading ? Preview : Edit`) so the View ticks are right even if a producer passes the window's raw mode.
6. **`ShellSnapshot` has structural equality** over its five lists (records give reference equality). This is the Mac's focused-value semantics and is what makes "rebuild the Go/Diagram submenus on snapshot change" (§2.9, §14 row 27) not rebuild on every 250 ms derived-text tick.
7. `Chord` uses `int VirtualKey` (§13.2) rather than §2.9's `KeyCode`; `VirtualKeys` gives them names and the values are `Windows.System.VirtualKey`'s, so `AcceleratorInstaller`'s cast is just a cast. `DisplayText` orders modifiers Ctrl → Alt → Shift (unambiguous for every chord md defines).
8. The debounce is **leading-edge against the last invocation that actually ran**, keyed by `CommandId`: a suppressed duplicate does not extend the window, so a held chord repeats at 150 ms instead of locking out. A suppressed duplicate returns `true` (handled) so nothing else acts on it; a disabled or unwired command returns `false` so the key falls through. No `IScheduler` is needed — the guard is a clock comparison, and the tests drive it through `FakeScheduler.Clock`.
9. `Register` on the same id twice replaces the handler rather than throwing (a window that re-wires must not have to unwire first).
10. `Strings.Help.EnginesHeading = "Bundled open source engines:"` — §2.8 fixes the engine list but not a lead-in, and a bare list is unreadable. Wording follows facts/windows-facts.md; a test asserts the About text never contains "third-party" or "no dependencies".
11. About shows `Version {Major}.{Minor}.{Build}` (the Store's fourth component is always 0), from `Package.Current.Id.Version`, falling back to the assembly version when unpackaged — the same shape `App.Diagnostics` uses for `ApplicationData`.
12. `PreviousArticle`/`NextArticle` are gated on `IsBookWindow && Can…` (§2.1's "stepper = Book window only"); `ZenMode` on `!IsBookWindow && HasDocument`.

**Day-1 Windows check this package owns:** Ctrl+1 with the preview focused must fire *once* (§13.4 stage 2). That is the debounce; if it ever fires more than twice ~100 ms apart, widen `CommandDispatcher.DoubleFireWindow`, not the wiring. The contingency `KeyRelay` of §2.9 is not written (it is explicitly "written, off" only if root accelerators turn out not to fire at all).


---

## WP2 — documents

## 3. Public API added (namespace `Md.App.Logic.Documents`)

```csharp
public enum NewLine { Lf, CrLf }
public static class LineEndings {
    public const string Lf = "\n"; public const string CrLf = "\r\n";
    public static string Text(NewLine); public static NewLine Detect(string);
    public static string Normalize(string); public static string Apply(string lfText, NewLine); }

public static class EditorText {
    public static string FromTextBox(string); public static string ToTextBox(string);
    public static (int Start, int Length) ClampSelection(int start, int length, int textLength); }

public static class LineOffsets { public static int OffsetOfLine(int line, string text); }

public sealed record TextFileDressing(Md.Core.Document.TextEncoding Encoding, bool HadUtf8Bom, NewLine NewLine) {
    public static TextFileDressing Default { get; }
    public static (string Text, TextFileDressing Dressing)? Undress(byte[] bytes);
    public byte[] Dress(string text, out Md.Core.Document.TextEncoding used);
    public TextFileDressing WithEncoding(Md.Core.Document.TextEncoding); }

public static class FileNames {
    public const string InvalidCharacters = "\\/:*?\"<>|";
    public static readonly IReadOnlyList<string> ReservedDeviceNames;
    public static bool Validate(string? name);          public static bool IsReservedDeviceName(string);
    public static string ForWindows(string? name);      public static string WithExtensionOf(string originalFileName, string newStem);
    public static string NameOf(string);   public static string DirectoryOf(string);
    public static string StemOf(string);   public static string ExtensionOf(string);   // ".md" or ""
    public static string Combine(string directory, string name);
    public static bool IsRooted(string reference);
    public static string? ResolveRelative(string directory, string relative);
    public static string Standardize(string path);      public static bool SamePath(string a, string b); }

public static class RescueCopy {
    public const int MaxAttempts = 100;
    public static string NameFor(string fileName, int attempt);
    public static string? Write(IFileSystem, string path, byte[] data); }

public static class TextSearch {
    public readonly record struct Match(int Index, int Length);
    public static Match? Next(string text, string query, int from);
    public static Match? Previous(string text, string query, int from); }

public static class AssetReader {
    public static Func<string, byte[]?> Beside(string? documentPath, IFileSystem, IFileIdentity);
    public static string? Resolve(string folder, string canonicalFolder, string relative, IFileIdentity);
    public static bool IsStrictlyInside(string path, string folder); }

public enum DocumentKind { PlainText, TextPack, TextBundleFolder }
public enum LoadFailure { None, Unreadable, BundleUnreadable }
public sealed record LoadedDocument(DocumentKind Kind, string Path, string Text, TextFileDressing Dressing, string Title);
public sealed record DocumentLoad(LoadedDocument? Document, LoadFailure Failure, string ErrorText) { public string? AlertTitle { get; } }
public static class DocumentLoader {
    public static readonly IReadOnlyList<string> MarkdownExtensions, PlainTextExtensions, OpenExtensions;
    public static IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> SaveChoices { get; }
    public static string DefaultExtensionFor(string? path);
    public static DocumentLoad Load(string path, IFileSystem); }

public sealed partial class FileIdentity : IFileIdentity {
    public static FileIdentity Instance { get; }
    public string Canonical(string path);  public static string Lexical(string path); }

public sealed class SystemIoFileSystem : IFileSystem { public static SystemIoFileSystem Instance { get; } }
public sealed class FileSystemWatcherAdapter : IFileWatcher {
    public FileSystemWatcherAdapter(IUiThread); public string? WatchedPath { get; } }

public abstract record Stage {                       // closed
    public sealed record Untitled : Stage;  public sealed record Editing(string Path) : Stage;
    public sealed record Handoff(string Path) : Stage;  public sealed record Unreadable(string Path) : Stage;
    public sealed record Empty : Stage;
    public string? FilePath { get; } }
public enum SessionRole { Document, BookArticle }

public sealed class TextFileSession : IDisposable {
    public static readonly TimeSpan AutosaveDelay;                       // 1 s
    public TextFileSession(IFileSystem, IFileWatcher, IScheduler, IDocumentRegistry, IFileIdentity,
                           SessionRole role = SessionRole.Document, Guid? windowId = null);
    public SessionRole Role { get; }  public Guid? WindowId { get; }
    public Stage Stage { get; }  public string Text { get; }  public string Title { get; }
    public TextFileDressing Dressing { get; }
    public bool IsDirty { get; }            // the title's "— Edited"; an autosave never clears it
    public bool HasUnsavedChanges { get; }  // the buffer is ahead of the bytes on disk
    public bool Conflicted { get; }  public string? SaveErrorText { get; }  public string? ImportedFrom { get; }
    public string LastSavedText { get; }  public FileStamp? DiskStamp { get; }  public int UndoGeneration { get; }
    public string? CanonicalPath { get; }  public string? EditingPath { get; }  public bool HasFileIdentity { get; }
    public Guid? OwningWindow { get; }
    public event Action? Changed;
    public event Action<string>? TextReplacedExternally;
    public event Action<string?>? IdentityChanged;      // the new file path, null when there is none
    public event Action<string, string>? AlertRequested; // (title, message)
    public void OpenUntitled(string text, string? title = null, bool dirty = false);
    public bool Open(string path);
    public bool ImportBundle(string bundlePath, string text, Md.Core.Document.TextEncoding encoding);
    public bool Select(string? path);
    public void Edit(string text);
    public bool FlushNow(bool explicitSave);
    public bool SaveAs(string path);
    public void Retarget(string newPath);
    public void RevertToSaved();
    public void Detach(bool reportFailure);   public void CloseFlush();   public void TerminateFlush();
    public string? WriteRescueCopy();
    public void ResolveConflictKeepingMine();  public void ResolveConflictReloading();
    public void RecheckOwnership();  public bool HandOffForExternalOpen();
    public void Dispose(); }
```

App side (`Md.App.Services`, `internal`): `Pickers(Window, IAlerts? = null) : IPickers`; `WinUiAlerts(Window) : IAlerts`; `RecentFiles` with `readonly record struct Entry(string Token, string Path)`, `Entries`, `Add(StorageFile)`, `AddAsync(string)`, `ResolveAsync(token) → StorageFile?`, `Remove`, `Clear`; `ExampleLibrary` with static `FolderPath` / `BookFolderPath`, `IReadOnlyList<Md.Core.Document.Example> Examples`, `string? ReadText(Example)`.

## 4. Integration notes

- **`TextFileDressing` uses `Md.Core.Document.TextEncoding`, not §13.2's `TextEncodingKind`** — core-api.md wins; same for `TextBundle.TextFromBundle(string)` vs the design's `TextFromBundleFolder`. No Part B Core API was needed, so nothing is stubbed.
- **Two deviations from the §13.2 sketch, both deliberate**: `Undress` returns a nullable tuple (decode can fail); `Open` returns `bool` and the alert text arrives on the new `AlertRequested` event (the session has no `IAlerts` in the frozen ctor). Two optional ctor parameters were appended after the frozen five (`role`, `windowId`).
- **Two "dirty" flags.** `IsDirty` is the title's "— Edited" (cleared only by an explicit save); `HasUnsavedChanges` is "disk is behind". For `SessionRole.BookArticle` every successful save clears both, which is what the 14 macOS tests pin. **WP3 must bind the title to `IsDirty`, not `HasUnsavedChanges`.**
- **WP3 owns** (the session raises the trigger, it does not do these): untitled numbering (`Strings.UntitledNumbered(n)` → `OpenUntitled(title:)`); MRU adds on `IdentityChanged`; deleting the picker-created empty file when `SaveAs` returns false (§6.1); the `StorageFile.RenameAsync`/`MoveAsync` call, then `session.Retarget(newPath)`; the untitled-and-dirty close dialog, then `CloseFlush()`; calling `RecheckOwnership()` on window activation and `FlushNow(false)` on deactivate/before share/print/export.
- **`Detach`/`Dispose` call `registry.Unregister(windowId)`**, which removes the window from `IDocumentRegistry.Windows` (the Window menu). WP3 should re-add a window title if a document window ever outlives its file.
- **WP4**: `IdentityChanged` carries the raw path — call `ViewModeMemory.IdentityFor(path)` yourself; `UndoGeneration` is the signal to call `ClearUndoRedoHistory()`. `TextReplacedExternally` + `EditorText.ClampSelection` is the §3.2 external-replace path.
- **WP7**: the book pane passes `SessionRole.BookArticle` and `windowId: null`. `Title` already strips the ordering prefix for that role. `BookFlushGate` wiring is yours — the session half is `FlushNow(false)`'s bool.
- **WP1**: `RecentFiles.Entry` is my own tuple-ish type in Md.App; map it to your `Commands.RecentEntry`.
- **`FileIdentity.Canonical` does not case-fold** (it returns the file system's true spelling on Windows; `GetFullPath`+link-resolution elsewhere). Every consumer must compare `OrdinalIgnoreCase`, as `IDocumentRegistry` already specifies. It is *not* interchangeable with `ViewModeMemory.CanonicalPath`, which upper-cases on Windows.
- **A C# trap worth knowing repo-wide**: `s.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries)` compiles and silently binds to `Split(char, int count, options)` — `'\\'` converts to `int` 92 — so it splits on `/` only, up to 92 times. Every backslash path comes back as one segment. Use a `char[]`. It cost three failing tests here; `FileNames.Separators` carries the comment.
- Three bugs my tests caught in my own code before they shipped: the `Split` binding above, a UTF-8 BOM being *added* to a file whose encoding upgraded to UTF-8, and `WithEncoding` carrying the BOM flag across an upgrade.


---

## WP4 — editor, view modes, Zen

## 3. Public API added in Md.App.Logic

```csharp
namespace Md.App.Logic.View;

public readonly record struct IdentityMemo {
    public static readonly IdentityMemo NeverRan;
    public bool Ran { get; } public string? Value { get; } public bool RanWithNoIdentity { get; }
    public static IdentityMemo Of(string? identity); }

// PLACEHOLDERS for WP5's Preview/PreviewNavigation.cs and Preview/EditorJump.cs — see note 1.
public readonly record struct PreviewNavigation(Guid Id, string Slug);
public readonly record struct EditorJump(Guid Id, int Line);

public sealed class DocumentWindowState {
    public const bool IsWide = true;
    public event Action? Changed;
    public ViewMode StoredMode { get; set; }            // raw preference, default Split
    public bool ZenActive { get; set; }  public bool ZenReading { get; set; }
    public ViewMode? NavigationMode { get; set; }
    public IdentityMemo LastIdentity { get; set; }
    public bool IsBookArticle { get; }  public void MarkBookArticle();
    public PreviewNavigation? PreviewNavigation { get; set; }
    public EditorJump? EditorJump { get; set; }
    public DerivedText Derived { get; set; }
    public ViewMode EffectiveMode { get; }
    public void PreviewNavigationHandled(Guid id);  public void EditorJumpHandled(Guid id); }

public sealed class ViewModeController {
    public ViewModeController(DocumentWindowState state, IViewModeStore memory, Func<Guid>? newId = null);
    public void Select(ViewMode mode);                  // View menu / Ctrl+1-3; swallowed by Zen
    public void SetMode(ViewMode mode);                 // the single writer of a preference
    public void ApplyMemory(string? path, string text); // window appearance + every identity change
    public void JumpToHeading(OutlineEntry entry);  public void JumpToNote(NoteEntry note); }

public sealed class ZenController {
    public static readonly TimeSpan ChromeHideDelay;    // 2.5 s
    public ZenController(DocumentWindowState state, IScheduler? scheduler = null);
    public event Action<bool>? FullScreenRequested;     // App: AppWindow.SetPresenter
    public event Action<bool>? ControlsShownChanged;    // App: capsule Opacity / IsHitTestVisible
    public bool IsActive { get; } public bool IsReading { get; }
    public bool ControlsShown { get; } public bool IsToggling { get; }
    public void Toggle(); public void SetActive(bool active);
    public void PresenterChanged(bool isFullScreen);    // App: AppWindow.Changed + DidPresenterChange
    public void SetReading(bool reading); public void Reveal(); }

public enum PaneLayout { EditorOnly, PreviewOnly, SideBySide, Stacked }
public static class SplitLayout {
    public const double SideBySideMinimumWidth = 640;
    public static PaneLayout Arrange(ViewMode mode, double contentWidth);
    public static bool ShowsEditor(PaneLayout); public static bool ShowsPreview(PaneLayout);
    public static bool ShowsDivider(PaneLayout); }

public readonly record struct DiagramRef(int Ordinal, string Engine, string Source, string MenuTitle);
public sealed record DerivedText(int Words, int Characters,
    IReadOnlyList<OutlineEntry> Outline, IReadOnlyList<NoteEntry> Notes, IReadOnlyList<DiagramRef> Diagrams)
    { public static readonly DerivedText Empty; }

public sealed class DerivedTextScheduler {
    public static readonly TimeSpan Delay;              // 250 ms
    public delegate void RunOffThread(Action work);     // default Task.Run
    public DerivedTextScheduler(IScheduler scheduler, IUiThread ui, IWordCounter words,
        Func<string, IReadOnlyList<DiagramRef>>? diagrams = null, RunOffThread? offThread = null);
    public DerivedText Current { get; }  public event Action<DerivedText>? Changed;
    public void TextChanged(string text); public void Cancel(); public DerivedText Compute(string text); }

public sealed class ScrollSync {
    public Action<double>? ScrollEditor { get; set; }  public Action<double>? ScrollPreview { get; set; }
    public void EditorDidScroll(double fraction);      public void PreviewDidScroll(double fraction);
    public static double Clamp(double fraction); }

public sealed class ScrollSyncGuard {
    public static readonly TimeSpan DefaultWindow;      // 50 ms
    public ScrollSyncGuard(IClock clock, TimeSpan? window = null);   // Zero = the plain flag
    public bool IsApplying { get; } public bool IsEcho { get; } public IDisposable Applying(); }

public sealed class SettingsViewModeStore(ISettingsStore settings) : IViewModeStore;

namespace Md.App.Logic.Text;
public static class NotePreview { public const int MaxTextElements = 50; public static string Of(string text); }
public sealed class SimpleWordCounter : IWordCounter { public static readonly SimpleWordCounter Instance; }
[SupportedOSPlatform("windows")] public sealed partial class IcuWordCounter : IWordCounter
    { public static IcuWordCounter? TryCreate(); public unsafe int Count(string text); }
public static class WordCounters { public static IWordCounter Create(); }   // ICU, else Simple; never null
```

App-side (`Md.App.Controls`): `EditorPane` (`TextEdited`, `Text`, `Control`, `SetText`, `ApplyJump`, `Attach(ScrollSync, ScrollSyncGuard)`, `SelectRange`, `FocusEditor`, `HasSelection`, `const FontSizeEpx = 20`), `ArticlePanes` (`Editor`, `ScrollSync`, `Mode`, `Layout`, `LayoutChanged`, `SetPreview(UIElement?)`, `AttachScrollSync(ScrollSyncGuard)`), `ZenControls` (`Attach(ZenController, DocumentWindowState)`, `Reveal()`), `FindBar` (`SearchRequested(string, bool)`, `Dismissed`, `QueryChanged`, `Query`, `SetQuery`, `FocusQuery`, `Search(bool)`), `FooterBar` (`Update(DerivedText)`).

## 4. Integration notes

1. **WP5 types.** `PreviewNavigation` and `EditorJump` are declared in `Md.App.Logic.View` (bottom of `DocumentWindowState.cs`) because WP5's `Preview/*` does not exist yet. Different namespace, so no clash. Integration = delete those two records and add `using Md.App.Logic.Preview;` to that one file.
2. **Core Wave C.** `DiagramRef` mirrors the dictated `Md.Core.Export.DiagramSvg.Diagram` field-for-field, and the scheduler reaches diagrams only through its `diagrams` delegate (default: none). Integration = retype `DerivedText.Diagrams` and pass `DiagramSvg.Diagrams` at the one construction site. Note this also touches WP1's `ShellSnapshot.Diagrams`, which already codes against `DiagramSvg.Diagram`.
3. **Design deviation — the identity function (decided).** I use `ViewModeMemory.IdentityFor(path)` (Core), *not* the design's `Sha256Prefix("file:" + FileIdentity.Canonical(path))`. Two reasons, both load-bearing: `BookArticleOpens.Mark/ClaimOpen` take a **path** and hash it with `IdentityFor` themselves (core-api.md Part A), so a second canonicalisation would silently break the sticky book exemption for any path spelled two ways — exactly the case canonicalisation exists for; and Core's own comment argues that case folding survives a case-only rename where `GetFinalPathNameByHandle`'s true-case spelling does not. **WP2/WP3: `IFileIdentity` / `Documents/FileIdentity.cs` stays for window ownership (the registry); never feed its output into `md.viewModeMemory`.**
4. **Design deviation — `SimpleWordCounter` (decided, as the task invited).** It delegates to `Md.Core.Book.WritingStats.Words` instead of the design's "runs of `\p{L}\p{N}` joined across apostrophes" sketch: Core's counter is the shipped, golden-tested port of the very `enumerateSubstrings(.byWords)` / `BreakIterator` calls macOS and Android make, and the regex sketch would disagree with both for anything past ASCII. `IcuWordCounter` is kept exactly as the design demands, because only the OS segmenter runs ICU's CJK/Thai dictionaries — `WordCounterTests` pins that gap as a relation (`WritingStats` gives 4 for 北京大学; ICU must give 1–3).
5. **WP3 contract — view mode.** `ApplyMemory(path, text)` must be called on window appearance *and every change of the document's file* (Save of an untitled doc, Save As, Rename, Move To). It caches the identity that `SetMode` then writes under; skip a call and a mode would be remembered against the previous file. `text` is the document text (an Example has content and no file → Split).
6. **WP3 contract — Zen.** `ZenController.FullScreenRequested` → `AppWindow.SetPresenter(FullScreen|Overlapped)`; `AppWindow.Changed` with `DidPresenterChange` → `PresenterChanged(Presenter.Kind == FullScreen)`. `PresenterChanged(false)` clears Zen **without** asking for a presenter change (the window is already Overlapped). The ZenGrid (`1*,4*,1*` × `4*,92*,4*`) is WP3's file; `ZenControls` is only the capsule — call `ZenControls.Attach(zen, state)` and forward the grid's `PointerMoved` to `ZenControls.Reveal()`.
7. **WP3/WP5 contract — panes.** WP3 sets `ArticlePanes.Mode = state.EffectiveMode` and calls `AttachScrollSync(guard)`; WP5 calls `SetPreview(host)` and registers `articlePanes.ScrollSync.ScrollPreview`. `ArticlePanes` re-arranges itself on `SizeChanged`, so nobody needs to feed it a width.
8. **WP2 contract — find.** `FindBar` has no dependency on WP2: it raises `SearchRequested(query, forward)` and the window calls `TextSearch.Next/Previous`, then `EditorPane.SelectRange(index, length)`. WP1's Ctrl+F / F3 / Shift+F3 / Ctrl+E drive `FocusQuery()`, `Search(bool)` and `SetQuery(string)`.
9. **WP2 replaces two private helpers** in `EditorPane.cs` (clearly fenced): `LfText` → `Documents.EditorText.FromTextBox`, `OffsetOfLine` → `Documents.LineOffsets.OffsetOfLine`.
10. **Two files/types beyond §11.1's literal list, both inside directories WP4 owns exclusively:** `View/SettingsViewModeStore.cs` (§5.2 requires an `IViewModeStore` over `ISettingsStore`; it is the only reader/writer of `md.viewModeMemory`), and `internal PaneBrushes` / `PaneTypography` declared at the bottom of `Controls/EditorPane.cs`. Move the latter two to their own file the moment WP3/WP5/WP7 need them — the names are the integration point, and every control resolves brushes through `PaneBrushes.Get(key, paletteFallback)` re-run from `ActualThemeChanged`.
11. **No csproj/manifest edit needed.** WinUI's default globs pick up `Controls/*.xaml` as `Page` items and the `.cs` files; xamlcheck's shadow project confirms all five code-behinds compile against the real WinUI 3 / WindowsAppSDK 2.4 surface.
12. **Test isolation.** `[CollectionDefinition("BookArticleOpens", DisableParallelization = true)]` now exists in `ViewModeControllerTests.cs` because `BookArticleOpens` is a process-wide static with an injectable clock. Any other package testing the book claim must join `[Collection("BookArticleOpens")]`.
13. **Small deliberate behaviour difference from the Mac:** leaving Zen leaves the capsule in the "shown" state (the Mac leaves `zenControlsShown = false`), so re-entering Zen does not start behind a 300 ms fade-in. Invisible while Zen is off; pinned by a test.
14. Md.Core.Tests already pins symlink/junction identity equality and the Windows case-fold (`ViewModeTests`), so WP4 does not duplicate the junction integration test; the WP4 half is the controller-level "two spellings of one path are one identity", guarded to the Windows leg.


---

## WP5 — preview host

## 3. Public API added (namespace `Md.App.Logic.Preview`)

```csharp
public static class AssetOrigin {
    public const string Host = "md.assets";
    public const string IndexUrl = "https://md.assets/index.html";
    public static readonly Uri IndexUri;
    public const string WebFolderName = "web";
    public const string RichFolderName = "rich"; }

public static class ScreenHtml {
    public const string FontStyle = "<style id=\"md-win-fonts\">body{font-family:Georgia,\"Courier New\",serif;}</style>";
    public const string IdMarker  = "id=\"md-win-fonts\"";
    public static string WithWindowsFonts(string coreHtml); }      // inserts "\n" + FontStyle before the first </head>

public enum LinkDecision { Allow, Cancel, OpenExternally }
public static class LinkPolicy { public static LinkDecision Decide(Uri uri, bool isUserInitiated, Uri indexUrl); }

public static class Scripts {
    public static readonly string ScrollSync, LinkGuard;           // LF-normalised, injected at document-created
    public const string MacPostMessageCall, WebView2PostMessageCall;
    public const string RenderComplete, RenderCompleteResult /* "\"1\"" */, ScrollY, ScrollHeight;
    public static string ScrollTo(double y);                        // window.__mdScrollTo?.(y)
    public static string SyncScrollTo(double fraction);             // "R", InvariantCulture
    public static string JumpToSlug(string slug); }

public static class JsonScript {
    public static double? Number(string? json);
    public static string? String(string? json);
    public static (double Fraction, bool Echo)? ScrollMessage(string? json); }   // WebMessageAsJson

public static class AssetMime { public const string Fallback = "application/octet-stream";
                                public static string For(string? pathOrExtension); }

public sealed record PreviewNavigation(Guid Id, string Slug);
public sealed record EditorJump(Guid Id, int Line);
public sealed class EditorJumpTracker { public bool Claim(EditorJump? jump); }   // "performed once per id"

public interface IDocumentHtml { string Document(string source, string title, bool dark); }
public static class DocumentHtml { public static IDocumentHtml Default { get; set; } }

public sealed class PreviewCoordinator {
    public static readonly TimeSpan DebounceDelay;                  // 350 ms
    public PreviewCoordinator(IPreviewSurface surface, IScheduler scheduler);              // §13.2, unchanged
    public PreviewCoordinator(IPreviewSurface surface, IScheduler scheduler, IDocumentHtml documentHtml);
    public double SavedScrollY { get; }
    public bool IsStale { get; }
    public void Update(string text, string title, bool dark, string? token);
    public void Show();
    public void Hide();
    public void Navigate(PreviewNavigation navigation, Action<Guid>? onHandled = null); }
```

App side (internal): `Md.App.Web.WebViewEnvironment.GetAsync()` / `.BrowserArguments`; `Md.App.Web.AssetHost.WebRoot` and `Attach(CoreWebView2 core, Func<string> html, bool serveAssetsFromDisk = false)`; `Md.App.Controls.PreviewHost : UserControl` with `IPreviewSurface Surface`, `event Action<double>? PreviewDidScroll`, `void ApplyScrollFraction(double)`, `void ApplyTheme(bool dark)`, `Task EnsureInitialisedAsync()`.

## 4. Integration notes

- **Core Part B stub.** `Md.Core.Markdown.MarkdownHtml` does not exist in my copy. The single call sits behind `IDocumentHtml`; the coordinator applies `ScreenHtml.WithWindowsFonts` itself, so the Core adapter returns pure Core HTML. **Someone must set `DocumentHtml.Default` once at start-up** (WP3, in `App.OnLaunched`) — until then the first `Update` throws `InvalidOperationException` naming the property rather than previewing something invented. When Core lands the whole integration is `DocumentHtml.Default = new CoreDocumentHtml();` with the body `MarkdownHtml.Document(source, title, dark)`.
- **Design §4.5 pseudo-code has a bug I had to fix.** `OnNavigationCompleted` calling `Navigate(parked)` would always be swallowed by `if nav.Id == lastNavigationId return`, because the id is recorded when the request is *parked*. Implemented as a private `Perform(nav, onHandled)`; `Navigate` still dedupes, parks or performs. Parked requests carry their `onHandled`. A failed load keeps the request parked for the next successful one (pinned by a test).
- **`isUserInitiated` decided.** §4.7's table has no column for it. I use it only to narrow: a cross-host http(s) navigation the user did not start (script assigning `location`, `meta refresh`) is **Cancel**, not `OpenExternally`. Every table row is otherwise as written, including `https://md.assets/other → Cancel` and `http://md.assets/index.html → Cancel` (host checked before scheme). Recorded in the XML doc and pinned by two tests.
- **`AssetMime` is the Mac's nine rows exactly** (`js, mjs, css, html, json, svg, woff2, woff, ttf`, else `application/octet-stream`), per design §4.2's enumerated table. The brief also named **png and wasm**: they are in neither the Mac table nor `rich/` (which holds only `.css .js .ttf .woff .woff2`), so they fall to `application/octet-stream` — pinned by a test *as that behaviour*. Adding them is one line if the family table gains them too.
- **`Scripts.cs` is a shared, append-only file** like `Strings.cs`. WP6 must append its export scripts there (Appendix B: `CaptureHtml`, `RichElements`, `DiagramSvg(index)`, `CanvasRasterise`, `KeyRelay`) — they need `export.md`'s verbatim text, which is WP6's source. `RenderComplete` / `RenderCompleteResult` / `ScrollHeight` are already there for WP6's `RenderCompletePoller`.
- **`Preview/AssetOrigin.cs` is a file the design's §11.1 list does not name** — 20 lines holding the host, the index URL and the two folder names so `AssetHost`, `PreviewHost`, `PreviewCoordinator` and `LinkPolicy` spell them once.
- **WP4 (`ArticlePanes`)**: construct `PreviewHost` in code-behind (it is code-only, no XAML, no `x:Bind`), toggle its `Visibility` for Edit mode rather than removing it, call `coordinator.Hide()` / `Show()` alongside, wire `PreviewDidScroll` → `ScrollSync.PreviewDidScroll` and `ScrollSync.ScrollPreview` → `ApplyScrollFraction`, and use `EditorJumpTracker` in `EditorPane`. `IPreviewSurface.IsShown` is `PreviewHost.Visibility && WebView2.Visibility`, so set Visibility **before** calling `Update`.
- **WP3**: call `ApplyTheme(dark)` from `ActualThemeChanged` **before** `coordinator.Update(...)` with the new flag (background first, then the new page).
- **WP6**: `AssetHost.Attach` works unchanged for the export renderer's `CoreWebView2`; use `ScreenHtml.WithWindowsFonts` for `RenderKind.Paper` only and never for `Export`.
- `AssetHost`'s disk-serving contingency is **written and off** (`serveAssetsFromDisk: false`), with path containment and a 404, exactly as §4.2 asks.

