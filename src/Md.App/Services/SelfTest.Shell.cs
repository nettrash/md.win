// The shell half of the in-app self-test (shell-design.md §11.4). SelfTest.cs proves the WebView2
// engines and the exports; this file proves the 1.5 editor and window behaviour in the REAL WinUI
// app — real DocumentWindows made by the real WindowManager, their real menu bars, find bars,
// dispatchers and TextBoxes — because on iOS ~500 unit tests passed over an app that stacked a second
// document view, drew every toolbar menu twice and searched a document that was no longer on screen.
//
// Two tiers. The first drives the windows through their own dispatchers and controls (what a menu
// click does) and needs nothing but a desktop. The second types with SendInput into the foreground
// window — Enter, letters, Backspace, Ctrl+Z, Ctrl+H, F3 — because only a real keystroke passes
// PreviewKeyDown, the accelerators, KeyDown, BeforeTextChanging, TextChanging and the RichEdit undo
// grouping in the order a writer meets them. `--no-input` skips the second tier (for a session
// without an interactive desktop); the report then says so.
//
// Nothing here touches the writer's own md: settings are an in-memory store, session.json and every
// file live under the report directory, and the run exits without closing a dirty window.
#if SELFTEST
using System.Globalization;
using Md.App.Controls;
using Md.App.Interop;
using Md.App.Logic.Activation;
using Md.App.Logic.Commands;
using Md.App.Logic.Documents;
using Md.App.Logic.Settings;
using Md.App.Logic.Windows;
using Md.Core.Document;
using Md.Core.Export;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Md.App.Services;

internal static partial class SelfTest
{
    /// <summary>Skip the SendInput tier (no interactive desktop). Everything else still runs.</summary>
    public const string NoInputFlag = "--no-input";

    /// <summary>Skip the WebView2 engine and export checks: the shell half alone, for a quick loop on a VM.</summary>
    public const string ShellOnlyFlag = "--shell-only";

    static bool HasFlag(string flag) => Array.IndexOf(Environment.GetCommandLineArgs(), flag) >= 0;

    static async Task ShellAsync(string directory, List<Check> checks)
    {
        var run = new ShellRun(directory, checks);
        await run.RunAsync();
    }

    /// <summary>One shell run: its own services, its own window manager, its own files.</summary>
    sealed class ShellRun
    {
        readonly List<Check> _checks;
        readonly string _files;
        readonly InMemorySettingsStore _settings = new();
        readonly AppServices _services;
        readonly WindowManager _manager;

        public ShellRun(string directory, List<Check> checks)
        {
            _checks = checks;
            var local = Path.Combine(directory, "shell-local");
            _files = Path.Combine(directory, "shell-files");
            // A clean slate every run: a file left by the last one would be "already open" nowhere,
            // but its text could make a check pass for the wrong reason.
            if (Directory.Exists(_files)) Directory.Delete(_files, recursive: true);
            Directory.CreateDirectory(local);
            Directory.CreateDirectory(_files);
            _services = new AppServices(_settings, local);
            _manager = new WindowManager(_services);
        }

        public async Task RunAsync()
        {
            await Section("examples", ExamplesMenusAndFindTargetAsync);
            await Section("preview", FindInPreviewAsync);
            await Section("typing", TypingSwitchesAsync);
            await Section("replace", ReplaceAsync);
            await Section("autosave", ReplaceAutosaveAndClobberGuardAsync);
            await Section("associations", FileAssociationsAsync);
            await Section("book", BookWindowAsync);
            await Section("ticks", ToggleTicksAsync);
            if (HasFlag(NoInputFlag))
            {
                Add("input.skipped", true, NoInputFlag + ": the keystroke tier was not run — capitals, the override, Ctrl+Z, Ctrl+H and F3 are unproved by this report");
                return;
            }
            await Section("input", KeystrokesAsync);
        }

        // ── plumbing ──

        void Add(string name, bool passed, string detail) => _checks.Add(new Check("shell." + name, passed, detail));

        async Task Section(string name, Func<Task> body)
        {
            try
            {
                await body();
            }
            catch (Exception e)
            {
                Add(name + ".completed", false, e.ToString());
            }
        }

        /// <summary>A dispatcher turn or more: the continuation comes back on the UI thread (Program.Main's synchronization context).</summary>
        static Task Turn(int milliseconds = 60) => Task.Delay(milliseconds);

        static string Show(string text) => text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

        static string Box(string lf) => EditorText.ToTextBox(lf);

        DocumentWindow Newest => _manager.Windows[^1];

        async Task<DocumentWindow> UntitledAsync(string text, string title)
        {
            var window = _manager.OpenUntitled(text, title, dirty: false);
            await Turn(250);
            // Edit mode: the editor is on screen whatever width the window came up at.
            window.SelfTestCommands.TryInvoke(CommandId.ViewEdit);
            await Turn();
            return window;
        }

        // ── menus: one of each, whatever has happened ──

        static MenuBarItem? Top(MenuBar bar, string title) => bar.Items.FirstOrDefault(i => i.Title == title);

        static IList<MenuFlyoutItemBase>? Submenu(MenuBar bar, string menu, string title) =>
            Top(bar, menu)?.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(s => s.Text == title)?.Items;

        static IEnumerable<(string Path, IList<MenuFlyoutItemBase> Items)> Flyouts(MenuBar bar)
        {
            foreach (var top in bar.Items)
            {
                foreach (var flyout in Walk(top.Title, top.Items)) yield return flyout;
            }

            static IEnumerable<(string, IList<MenuFlyoutItemBase>)> Walk(string path, IList<MenuFlyoutItemBase> items)
            {
                yield return (path, items);
                foreach (var sub in items.OfType<MenuFlyoutSubItem>())
                {
                    foreach (var inner in Walk(path + " > " + sub.Text, sub.Items)) yield return inner;
                }
            }
        }

        static IReadOnlyList<string> Rows(IList<MenuFlyoutItemBase> items) =>
            [.. items.Select(i => i switch { MenuFlyoutSubItem s => s.Text, MenuFlyoutItem m => m.Text, _ => null }).OfType<string>()];

        /// <summary>
        /// The menu bar of one window, checked against the table and against this window's snapshot:
        /// seven menus, each once and in order; no flyout holding the same row twice (the dynamic
        /// runs whose rows may legitimately repeat — two headings with one text — are counted
        /// against their source instead); Examples, Edit ▸ Typing, the page sizes and the Window
        /// list at exactly their sizes, with one tick in the Window list.
        /// </summary>
        void CheckMenuBar(string label, MenuBar? bar, ShellSnapshot snapshot)
        {
            if (bar is null)
            {
                Add("menus." + label + ".present", false, "the window has no MenuBar");
                return;
            }

            var titles = bar.Items.Select(i => i.Title).ToList();
            Add("menus." + label + ".sevenMenusOnce", titles.SequenceEqual(CommandTable.MenuTitles), string.Join(", ", titles));

            var repeated = new List<string>();
            foreach (var (path, items) in Flyouts(bar))
            {
                if (path is "Go > Contents" or "Go > Notes" or "Window" or "File > Open Recent") continue;
                var rows = Rows(items);
                repeated.AddRange(rows.GroupBy(r => r, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => path + " > " + g.Key + " ×" + g.Count()));
            }
            Add("menus." + label + ".noRowTwice", repeated.Count == 0, repeated.Count == 0 ? "every flyout's rows are distinct" : string.Join("; ", repeated));

            var examples = Submenu(bar, "File", "Examples");
            var exampleRows = examples is null ? [] : Rows(examples);
            var expectedExamples = _services.Examples.Examples.Select(e => e.Name).Append(CommandTable.For(CommandId.ExampleBook).Title).ToList();
            Add("menus." + label + ".examplesOnce", exampleRows.SequenceEqual(expectedExamples), string.Join(" | ", exampleRows));

            var typing = Submenu(bar, "Edit", "Typing");
            var typingRows = typing?.OfType<ToggleMenuFlyoutItem>().ToList() ?? [];
            Add("menus." + label + ".typingTwoToggles",
                typingRows.Select(t => t.Text).SequenceEqual([CommandTable.For(CommandId.ContinueLists).Title, CommandTable.For(CommandId.CapitalizeSentences).Title])
                && typing!.Count == 2,
                string.Join(" | ", typingRows.Select(t => t.Text + (t.IsChecked ? " ✓" : ""))));

            var sizes = bar.Items.FirstOrDefault(i => i.Title == "File")?.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(s => s.Text == "Export")
                ?.Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(s => s.Text == "PDF Page Size")?.Items.OfType<RadioMenuFlyoutItem>().ToList() ?? [];
            Add("menus." + label + ".pageSizesOnce", sizes.Count == PageSize.All.Count && sizes.Count(r => r.IsChecked) == 1,
                sizes.Count.ToString(CultureInfo.InvariantCulture) + " radios, " + sizes.Count(r => r.IsChecked).ToString(CultureInfo.InvariantCulture) + " ticked");

            var windowRows = Top(bar, "Window")?.Items.OfType<ToggleMenuFlyoutItem>().ToList() ?? [];
            var registered = _services.Registry.Windows.Count();
            Add("menus." + label + ".windowListOnceEach",
                windowRows.Count == registered && windowRows.Count(r => r.IsChecked) == 1,
                $"{windowRows.Count} rows for {registered} windows, {windowRows.Count(r => r.IsChecked)} ticked: " + string.Join(" | ", windowRows.Select(r => r.Text)));

            var contents = Submenu(bar, "Go", "Contents");
            Add("menus." + label + ".contentsMatchTheOutline", (contents?.Count ?? -1) == snapshot.Outline.Count,
                $"{contents?.Count ?? -1} rows for {snapshot.Outline.Count} headings");
        }

        void CheckEveryWindowsMenus(string stage)
        {
            for (var i = 0; i < _manager.Windows.Count; i++)
            {
                var window = _manager.Windows[i];
                CheckMenuBar(stage + ".window" + (i + 1).ToString(CultureInfo.InvariantCulture), window.SelfTestMenuBar, window.SelfTestSnapshot);
            }
        }

        // ── three Examples: menus stay single, and Find acts on the newest document ──

        async Task ExamplesMenusAndFindTargetAsync()
        {
            var examples = _services.Examples.Examples;
            Add("examples.present", examples.Count >= 3, examples.Count.ToString(CultureInfo.InvariantCulture) + " examples in " + ExampleLibrary.FolderPath);
            if (examples.Count < 3) return;

            for (var n = 0; n < 3; n++)
            {
                var before = _manager.Windows.Count;
                // Exactly what File ▸ Examples ▸ row dispatches, from the window in front.
                if (n == 0) _manager.OpenExample(examples[0].FileName);
                else Newest.SelfTestCommands.TryInvoke(CommandId.Example, examples[n].FileName);
                await Turn(400);
                Add($"examples.open{n + 1}.oneNewWindow", _manager.Windows.Count == before + 1, $"{before} → {_manager.Windows.Count} windows");
                CheckEveryWindowsMenus($"afterExample{n + 1}");
            }

            // Find in the newest window searches the newest document — and only it.
            var windows = _manager.Windows.TakeLast(3).ToList();
            var newest = windows[^1];
            var box = newest.SelfTestEditor.Control;
            var query = FirstWord(box.Text);
            var others = windows.Take(2).Select(w => (w, w.SelfTestEditor.Control.SelectionStart, w.SelfTestEditor.Control.SelectionLength)).ToList();

            newest.SelfTestCommands.TryInvoke(CommandId.ViewEdit);
            await Turn();
            newest.SelfTestCommands.TryInvoke(CommandId.Find);
            newest.SelfTestFind.SetQuery(query);
            box.Select(0, 0);
            await Turn(150);   // QueryChanged → Publish, so HasFindQuery is in the snapshot

            Add("find.opensOnlyTheNewestBar",
                newest.SelfTestFind.Visibility == Visibility.Visible && windows.Take(2).All(w => w.SelfTestFind.Visibility == Visibility.Collapsed),
                string.Join(", ", windows.Select(w => w.SelfTestFind.Visibility.ToString())));

            var expected = TextSearch.Next(box.Text, query, 0);
            var ran = newest.SelfTestCommands.TryInvoke(CommandId.FindNext);
            await Turn();
            Add("find.searchesTheNewestDocument",
                ran && expected is { } hit && box.SelectionStart == hit.Index && box.SelectionLength == hit.Length,
                $"query \"{query}\", ran={ran}, selection {box.SelectionStart}+{box.SelectionLength}, wanted {expected?.Index}+{expected?.Length}");
            Add("find.leavesTheOtherWindowsAlone",
                others.All(o => o.w.SelfTestEditor.Control.SelectionStart == o.SelectionStart && o.w.SelfTestEditor.Control.SelectionLength == o.SelectionLength),
                "selections of the two older windows unchanged");
            Add("find.eachWindowHasItsOwnBar",
                windows.Select(w => w.SelfTestFind).Distinct().Count() == 3 && windows.Select(w => w.SelfTestEditor).Distinct().Count() == 3,
                "three windows, three find bars, three editors");

            // A theme change re-tints; it must not rebuild a single menu row.
            newest.SelfTestRoot.RequestedTheme = ElementTheme.Dark;
            await Turn(200);
            CheckMenuBar("afterThemeFlip", newest.SelfTestMenuBar, newest.SelfTestSnapshot);
            newest.SelfTestRoot.RequestedTheme = ElementTheme.Default;
            await Turn(100);
        }

        static string FirstWord(string text)
        {
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var letter = i < text.Length && char.IsLetter(text[i]);
                if (letter && start < 0) start = i;
                else if (!letter && start >= 0)
                {
                    if (i - start >= 4) return text[start..i];
                    start = -1;
                }
            }
            return "the";
        }

        // ── Preview: every find row is dead or acts, never lit and silent ──

        async Task FindInPreviewAsync()
        {
            var window = await UntitledAsync("cat and cat\n\ncat again", "Preview Find");
            var box = window.SelfTestEditor.Control;
            window.SelfTestFind.SetQuery("cat");
            box.Select(0, 3);                                  // a selection the hidden editor keeps
            await Turn(150);
            window.SelfTestCommands.TryInvoke(CommandId.ViewPreview);
            await Turn(200);

            var editorShown = window.SelfTestSnapshot.EditorVisible;
            Add("preview.editorOffScreen", !editorShown, "EditorVisible=" + editorShown);
            Add("preview.findBarClosed", window.SelfTestFind.Visibility == Visibility.Collapsed, window.SelfTestFind.Visibility.ToString());

            foreach (var id in new[] { CommandId.Find, CommandId.FindNext, CommandId.FindPrevious, CommandId.Replace, CommandId.UseSelectionForFind })
            {
                var start = box.SelectionStart;
                var length = box.SelectionLength;
                var text = box.Text;
                var enabled = window.SelfTestCommands.CanInvoke(id);
                var row = MenuRow(window.SelfTestMenuBar, CommandTable.For(id).Title);
                var ran = window.SelfTestCommands.TryInvoke(id);
                window.SelfTestCommands.ResetDebounce();
                await Turn();
                // Enabled is allowed only if the command did something the writer can SEE: the design
                // greys the row, and a command that brought the editor back and found there would do
                // too — but a hit selected inside a collapsed editor is the lit-and-silent bug itself,
                // so it only counts with the editor on screen afterwards.
                var visible = window.SelfTestSnapshot.EditorVisible;
                var acted = visible && (box.SelectionStart != start || box.SelectionLength != length || box.Text != text || window.SelfTestFind.Visibility == Visibility.Visible);
                Add("preview." + id + ".disabledOrActs",
                    (!enabled && !ran && row is { IsEnabled: false }) || (enabled && ran && acted),
                    $"enabled={enabled}, menuRowEnabled={row?.IsEnabled}, ran={ran}, acted={acted}");
                window.SelfTestFind.Visibility = Visibility.Collapsed;
            }

            window.SelfTestCommands.TryInvoke(CommandId.ViewEdit);
            await Turn(150);
            Add("preview.findComesBackWithTheEditor", window.SelfTestCommands.CanInvoke(CommandId.FindNext) && window.SelfTestCommands.CanInvoke(CommandId.Find),
                "Find and Find Next live again in Edit");
        }

        static MenuFlyoutItem? MenuRow(MenuBar? bar, string title)
        {
            if (bar is null) return null;
            foreach (var (_, items) in Flyouts(bar))
            {
                if (items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Text == title) is { } row) return row;
            }
            return null;
        }

        // ── the Edit ▸ Typing switches, through the menu's own command, in every window ──

        async Task TypingSwitchesAsync()
        {
            var window = await UntitledAsync("", "Typing");
            var pane = window.SelfTestEditor;

            async Task<(bool Continued, string Text)> EnterOnABulletAsync()
            {
                pane.SetText("- a");
                pane.ResetHistory();
                pane.Control.Select(3, 0);
                await Turn();
                var continued = pane.TryContinueOnEnter();
                await Turn();
                return (continued, pane.Control.Text);
            }

            var on = await EnterOnABulletAsync();
            Add("typing.enterContinuesABullet", on.Continued && on.Text == "- a\r- " && pane.Control.SelectionStart == 6,
                $"continued={on.Continued}, text \"{Show(on.Text)}\", caret {pane.Control.SelectionStart}");
            pane.Undo();
            await Turn();
            Add("typing.enterIsOneUndoUnit", pane.Control.Text == "- a", "after one Undo: \"" + Show(pane.Control.Text) + "\"");

            foreach (var (id, key) in new[] { (CommandId.ContinueLists, SettingsKeys.ContinueLists), (CommandId.CapitalizeSentences, SettingsKeys.CapitalizeSentences) })
            {
                foreach (var wanted in new[] { false, true })
                {
                    window.SelfTestCommands.ResetDebounce();
                    var ran = window.SelfTestCommands.TryInvoke(id);
                    await Turn(150);
                    var stored = _settings.GetBool(key, !wanted);
                    var ticks = _manager.Windows.Select(w => Submenu(w.SelfTestMenuBar!, "Edit", "Typing")?.OfType<ToggleMenuFlyoutItem>()
                        .FirstOrDefault(t => t.Text == CommandTable.For(id).Title)?.IsChecked).ToList();
                    Add($"typing.{id}.{(wanted ? "on" : "off")}.everyWindowTicks",
                        ran && stored == wanted && ticks.All(t => t == wanted),
                        $"ran={ran}, stored={stored}, ticks: {string.Join(",", ticks)}");

                    if (id == CommandId.ContinueLists)
                    {
                        var result = await EnterOnABulletAsync();
                        Add($"typing.ContinueLists.{(wanted ? "on" : "off")}.enterFollows",
                            result.Continued == wanted && result.Text == (wanted ? "- a\r- " : "- a"),
                            $"continued={result.Continued}, text \"{Show(result.Text)}\"");
                    }
                }
            }
        }

        // ── Replace and Replace All on a real editor: text, dirty flag, ONE undo ──

        async Task ReplaceAsync()
        {
            const string original = "cat and Cat\ncat 😀cat";
            var window = await UntitledAsync(original, "Replace");
            var pane = window.SelfTestEditor;
            var box = pane.Control;
            Add("replace.startsClean", !window.Session.IsDirty, "IsDirty=" + window.Session.IsDirty);

            var opened = window.SelfTestCommands.TryInvoke(CommandId.Replace);
            window.SelfTestFind.SetQuery("cat");
            window.SelfTestFind.SelfTestSetReplacement("dog");
            await Turn(150);
            Add("replace.ctrlHOpensTheBar", opened && window.SelfTestFind.Visibility == Visibility.Visible, "ran=" + opened);

            pane.SelectRange(0, 3);
            window.SelfTestFind.Replace();
            await Turn(150);
            var once = Box("dog and Cat\ncat 😀cat");
            Add("replace.one.text", box.Text == once, "\"" + Show(box.Text) + "\"");
            Add("replace.one.selectsTheNextHit", box.SelectionStart == 8 && box.SelectionLength == 3, $"{box.SelectionStart}+{box.SelectionLength}");
            Add("replace.one.reachesTheSession", window.Session.Text == "dog and Cat\ncat 😀cat" && window.Session.IsDirty,
                $"session \"{Show(window.Session.Text)}\", dirty={window.Session.IsDirty}");
            Add("replace.one.titleSaysEdited", window.Title == WindowTitle.For("Replace", isDirty: true), window.Title);
            pane.Undo();
            await Turn(150);
            Add("replace.one.oneUndoPutsItBack", box.Text == Box(original) && window.Session.Text == original, "\"" + Show(box.Text) + "\"");

            window.SelfTestFind.ReplaceAll();
            await Turn(150);
            var all = "dog and dog\ndog 😀dog";
            Add("replace.all.text", box.Text == Box(all) && window.Session.Text == all, "\"" + Show(box.Text) + "\"");
            Add("replace.all.focusBackInTheEditor", ReferenceEquals(FocusManager.GetFocusedElement(window.SelfTestRoot.XamlRoot), box),
                FocusManager.GetFocusedElement(window.SelfTestRoot.XamlRoot)?.GetType().Name ?? "nothing focused");
            pane.Undo();
            await Turn(150);
            Add("replace.all.oneUndoPutsEveryHitBack", box.Text == Box(original) && window.Session.Text == original, "\"" + Show(box.Text) + "\"");
            pane.Redo();
            await Turn(150);
            Add("replace.all.redoIsLiteral", box.Text == Box(all), "\"" + Show(box.Text) + "\"");
        }

        // ── the same edit on a file: autosave writes it, and the clobber guard holds ──

        async Task ReplaceAutosaveAndClobberGuardAsync()
        {
            var path = Path.Combine(_files, "replace.md");
            await File.WriteAllTextAsync(path, "cat\ncat\n");
            var window = _manager.OpenPath(path);
            await Turn(400);
            if (window is null || window.Session.EditingPath is null)
            {
                Add("autosave.opened", false, path);
                return;
            }
            window.SelfTestCommands.TryInvoke(CommandId.ViewEdit);
            window.SelfTestCommands.TryInvoke(CommandId.Replace);
            window.SelfTestFind.SetQuery("cat");
            window.SelfTestFind.SelfTestSetReplacement("dog");
            await Turn(150);
            window.SelfTestFind.ReplaceAll();
            await Turn(2200);   // the 1 s autosave, with room
            var disk = await File.ReadAllTextAsync(path);
            Add("autosave.writesTheReplacement", disk == "dog\ndog\n", "\"" + Show(disk) + "\"");

            // Unsaved edit + the file changed underneath = a conflict, and the disk keeps what it was given.
            window.SelfTestFind.SetQuery("dog");
            window.SelfTestFind.SelfTestSetReplacement("cow");
            await Turn(150);
            window.SelfTestFind.ReplaceAll();
            await File.WriteAllTextAsync(path, "written elsewhere\n");
            await Turn(2500);
            var after = await File.ReadAllTextAsync(path);
            Add("autosave.clobberGuardHolds", after == "written elsewhere\n" && window.Session.Conflicted,
                $"disk \"{Show(after)}\", conflicted={window.Session.Conflicted}");
        }

        // ── every associated extension, from an Explorer-shaped activation to an open editor ──

        async Task FileAssociationsAsync()
        {
            var folder = Path.Combine(_files, "associations");
            Directory.CreateDirectory(folder);

            foreach (var extension in DocumentLoader.OpenExtensions)
            {
                if (extension == TextBundle.PackExtension) continue;
                var path = Path.Combine(folder, "sample" + extension);
                var text = "# " + extension + "\n\nbody\n";
                await File.WriteAllTextAsync(path, text);

                // The loader alone, then the whole road a double-click takes.
                var load = DocumentLoader.Load(path, SystemIoFileSystem.Instance);
                Add("associations" + extension + ".loads", load.Document is { Kind: DocumentKind.PlainText } d && d.Text == text, load.Failure.ToString());

                var before = _manager.Windows.Count;
                Activate(path, folder: false);
                await Turn(300);
                var window = _manager.Windows.FirstOrDefault(w => string.Equals(w.Session.EditingPath, path, StringComparison.OrdinalIgnoreCase));
                Add("associations" + extension + ".opensAWindow",
                    window is not null && window.SelfTestEditor.Control.Text == Box(text) && _manager.Windows.Count == before + 1,
                    window is null ? "no window holds " + path : $"\"{Show(window.SelfTestEditor.Control.Text)}\", {before} → {_manager.Windows.Count} windows");

                // A second activation of the same file brings that window forward instead.
                Activate(path, folder: false);
                await Turn(150);
                Add("associations" + extension + ".secondActivationReusesIt", _manager.Windows.Count == before + 1, $"{_manager.Windows.Count} windows");

                if (window is not null && await window.RequestCloseAsync()) window.CloseApproved();
                await Turn(150);
            }

            // Upper case is the same extension to Windows, and must be to md.
            var upper = Path.Combine(folder, "UPPER.MD");
            await File.WriteAllTextAsync(upper, "upper\n");
            Activate(upper, folder: false);
            await Turn(300);
            Add("associations.upperCaseExtension", _manager.Windows.Any(w => string.Equals(w.Session.EditingPath, upper, StringComparison.OrdinalIgnoreCase)), upper);

            // The two bundle kinds import as untitled documents holding the bundle's text.
            var pack = Path.Combine(folder, "sample" + TextBundle.PackExtension);
            await File.WriteAllBytesAsync(pack, TextBundle.BundleWrapper("# Pack\n\nbody\n", []).ToTextPack("sample" + TextBundle.BundleExtension));
            var count = _manager.Windows.Count;
            Activate(pack, folder: false);
            await Turn(300);
            Add("associations.textpack.imports", _manager.Windows.Count == count + 1 && Newest.SelfTestEditor.Control.Text == Box("# Pack\n\nbody\n"),
                $"{count} → {_manager.Windows.Count} windows, \"{Show(Newest.SelfTestEditor.Control.Text)}\"");

            var bundle = Path.Combine(folder, "sample" + TextBundle.BundleExtension);
            TextBundle.BundleWrapper("# Bundle\n\nbody\n", []).Write(bundle);
            count = _manager.Windows.Count;
            Activate(bundle, folder: true);
            await Turn(300);
            Add("associations.textbundleFolder.imports", _manager.Windows.Count == count + 1 && Newest.SelfTestEditor.Control.Text == Box("# Bundle\n\nbody\n"),
                $"{count} → {_manager.Windows.Count} windows, \"{Show(Newest.SelfTestEditor.Control.Text)}\"");

            // A drop goes down the same road (§6.1).
            var dropped = Path.Combine(folder, "dropped.markdown");
            await File.WriteAllTextAsync(dropped, "dropped\n");
            _manager.PerformDrop([.. ActivationRouter.Route(ActivationDescription.ForFiles([ActivationItem.File(dropped)], isFirst: false))]);
            await Turn(300);
            Add("associations.dropOpensToo", _manager.Windows.Any(w => string.Equals(w.Session.EditingPath, dropped, StringComparison.OrdinalIgnoreCase)), dropped);
        }

        /// <summary>What App.RouteActivation does with an Explorer double-click, minus the WinRT argument object.</summary>
        void Activate(string path, bool folder)
        {
            var item = folder ? ActivationItem.Folder(path) : ActivationItem.File(path);
            _manager.Perform(ActivationRouter.Route(ActivationDescription.ForFiles([item], isFirst: false)), redirected: false);
        }

        // ── the Book window with no book: no find family, no Edit rows, one of each menu ──

        async Task BookWindowAsync()
        {
            var book = _manager.ShowBookWindow();
            // Listed settles on the window's Loaded; bounded, so a window that never loads is a
            // failed check rather than a self-test that never exits.
            var listed = await Task.WhenAny(book.Listed, Task.Delay(TimeSpan.FromSeconds(10))) == book.Listed;
            Add("book.listsItsBook", listed, listed ? "Listed settled" : "Book window never finished listing (Loaded not raised?)");
            await Turn(300);
            CheckMenuBar("book", book.SelfTestMenuBar, book.Snapshot());
            var live = new[] { CommandId.Find, CommandId.FindNext, CommandId.FindPrevious, CommandId.Replace, CommandId.UseSelectionForFind,
                               CommandId.Undo, CommandId.Redo, CommandId.Cut, CommandId.Copy, CommandId.Paste, CommandId.Delete, CommandId.SelectAll }
                .Where(book.Commands.CanInvoke).ToList();
            Add("book.noArticle.editAndFindRowsDead", live.Count == 0, live.Count == 0 ? "all dead" : "live: " + string.Join(", ", live));
            Add("book.typingSwitchesLive", book.Commands.CanInvoke(CommandId.ContinueLists) && book.Commands.CanInvoke(CommandId.CapitalizeSentences), "settings are live in every window");
            CheckEveryWindowsMenus("withBook");
        }

        // ── a toggle row's tick follows the snapshot, even when the click changes nothing ──

        async Task ToggleTicksAsync()
        {
            var window = await UntitledAsync("tick", "Ticks");
            var bar = window.SelfTestMenuBar!;
            var edit = Top(bar, "View")?.Items.OfType<ToggleMenuFlyoutItem>().FirstOrDefault(t => t.Text == CommandTable.For(CommandId.ViewEdit).Title);
            if (edit is null)
            {
                Add("ticks.viewEditRow", false, "View ▸ Edit not found");
                return;
            }
            // The automation peer's Toggle is the control's own invoke: the tick flips, Click is raised.
            window.SelfTestCommands.ResetDebounce();
            new ToggleMenuFlyoutItemAutomationPeer(edit).Toggle();
            await Turn(200);
            Add("ticks.viewEditStaysTickedWhenReclicked", edit.IsChecked && window.SelfTestSnapshot.PublishedMode == ViewMode.Edit,
                $"IsChecked={edit.IsChecked}, mode={window.SelfTestSnapshot.PublishedMode}");

            var own = Top(bar, "Window")?.Items.OfType<ToggleMenuFlyoutItem>().FirstOrDefault(t => t.IsChecked);
            if (own is not null)
            {
                window.SelfTestCommands.ResetDebounce();
                new ToggleMenuFlyoutItemAutomationPeer(own).Toggle();
                await Turn(200);
                Add("ticks.ownWindowRowStaysTicked", own.IsChecked, "IsChecked=" + own.IsChecked);
            }
        }

        // ── the keystroke tier ──

        async Task<bool> ForegroundAsync(DocumentWindow window, UIElement focus)
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                window.Activate();
                NativeMethods.SetForegroundWindow(window.Handle);
                focus.Focus(FocusState.Programmatic);
                await Turn(150);
                if (SelfTestInput.GetForegroundWindow() == window.Handle && ReferenceEquals(FocusManager.GetFocusedElement(window.SelfTestRoot.XamlRoot), focus)) return true;
            }
            return false;
        }

        /// <summary>The foreground, or a failed check under <paramref name="name"/> saying why the keystroke check could not run.</summary>
        async Task<bool> Front(string name, DocumentWindow window, UIElement focus)
        {
            if (await ForegroundAsync(window, focus)) return true;
            Add(name, false, "could not put md in front with the focus on " + focus.GetType().Name + " — nothing was typed");
            return false;
        }

        async Task PressAsync(TypingKey key)
        {
            switch (key.Kind)
            {
                case TypingKeyKind.Text:
                    // One scalar per keystroke, each its own input message, as a keyboard sends them.
                    foreach (var rune in key.Text.EnumerateRunes())
                    {
                        SelfTestInput.Text(rune.ToString());
                        await Turn(40);
                    }
                    break;
                case TypingKeyKind.Enter:
                    SelfTestInput.Key(SelfTestInput.VK_RETURN);
                    break;
                case TypingKeyKind.Backspace:
                    SelfTestInput.Key(SelfTestInput.VK_BACK);
                    break;
                case TypingKeyKind.Undo:
                    SelfTestInput.Key('Z', control: true);
                    break;
            }
            await Turn(80);
        }

        async Task KeystrokesAsync()
        {
            var window = await UntitledAsync("", "Keys");
            var pane = window.SelfTestEditor;
            var box = pane.Control;

            if (!await ForegroundAsync(window, box))
            {
                Add("input.foreground", false,
                    $"md could not take the foreground (foreground hwnd {SelfTestInput.GetForegroundWindow()}, ours {window.Handle}): keep the desktop unlocked and hands off the keyboard, or pass {NoInputFlag}");
                return;
            }
            Add("input.foreground", true, "keystrokes go to the editor");

            // Every typing scenario, with the scenario's own switches and a fresh history.
            foreach (var scenario in SelfTestTyping.Scenarios)
            {
                _settings.SetBool(SettingsKeys.ContinueLists, scenario.ContinueLists);
                _settings.SetBool(SettingsKeys.CapitalizeSentences, scenario.CapitalizeSentences);
                await Turn(120);   // the pane re-reads both on its own queue
                pane.SetText(scenario.Initial.Replace("\r", "\n", StringComparison.Ordinal));
                pane.ResetHistory();
                box.Select(scenario.Caret, 0);
                if (!await ForegroundAsync(window, box))
                {
                    Add("input." + scenario.Name, false, "lost the foreground before the scenario");
                    continue;
                }
                foreach (var key in scenario.Keys) await PressAsync(key);
                await Turn(200);
                Add("input." + scenario.Name, box.Text == scenario.Expected, $"typed \"{Show(box.Text)}\", wanted \"{Show(scenario.Expected)}\"");
            }
            _settings.SetBool(SettingsKeys.ContinueLists, true);
            _settings.SetBool(SettingsKeys.CapitalizeSentences, true);
            await Turn(120);

            // Ctrl+H: the Replace row wins, and RichEdit's Ctrl+H-is-Backspace never reaches either box.
            pane.SetText("abc");
            pane.ResetHistory();
            box.Select(3, 0);
            window.SelfTestFind.SelfTestSetReplacement("keep");
            window.SelfTestFind.Visibility = Visibility.Collapsed;
            if (await Front("input.ctrlH.opensReplace", window, box))
            {
                SelfTestInput.Key('H', control: true);
                await Turn(250);
                var focused = FocusManager.GetFocusedElement(window.SelfTestRoot.XamlRoot);
                Add("input.ctrlH.opensReplace", window.SelfTestFind.Visibility == Visibility.Visible && ReferenceEquals(focused, window.SelfTestFind.SelfTestReplaceBox),
                    $"bar {window.SelfTestFind.Visibility}, focus on {focused?.GetType().Name ?? "nothing"}");
                Add("input.ctrlH.deletesNothing", box.Text == "abc" && window.SelfTestFind.SelfTestReplaceBox.Text == "keep",
                    $"editor \"{Show(box.Text)}\", replacement \"{window.SelfTestFind.SelfTestReplaceBox.Text}\"");
            }

            // Enter in the replacement box is Replace; Ctrl+Z after Replace All reaches the document.
            pane.SetText("cat cat");
            pane.ResetHistory();
            window.SelfTestFind.SetQuery("cat");
            window.SelfTestFind.SelfTestSetReplacement("dog");
            await Turn(150);
            pane.SelectRange(0, 3);
            window.SelfTestFind.Visibility = Visibility.Visible;
            await Turn();
            if (await Front("input.enterInTheReplacementBoxReplacesOne", window, window.SelfTestFind.SelfTestReplaceBox))
            {
                SelfTestInput.Key(SelfTestInput.VK_RETURN);
                await Turn(250);
                Add("input.enterInTheReplacementBoxReplacesOne", box.Text == "dog cat", "\"" + Show(box.Text) + "\"");
            }
            pane.SetText("cat cat");
            pane.ResetHistory();
            window.SelfTestFind.ReplaceAll();
            await Turn(200);
            if (await Front("input.ctrlZAfterReplaceAllPutsEveryHitBack", window, box))
            {
                SelfTestInput.Key('Z', control: true);
                await Turn(250);
                Add("input.ctrlZAfterReplaceAllPutsEveryHitBack", box.Text == "cat cat", "\"" + Show(box.Text) + "\"");
            }

            // F3 from the editor, and F3 with the focus in the preview's WebView2 — once, not twice
            // (microsoft-ui-xaml #6231's double fire is the dispatcher guard's to absorb).
            pane.SetText("cat x cat y cat");
            pane.ResetHistory();
            window.SelfTestFind.SetQuery("cat");
            box.Select(0, 0);
            await Turn(150);
            if (await Front("input.f3.findsTheNextHit", window, box))
            {
                SelfTestInput.Key(SelfTestInput.VK_F3);
                await Turn(250);
                Add("input.f3.findsTheNextHit", box.SelectionStart == 0 && box.SelectionLength == 3, $"{box.SelectionStart}+{box.SelectionLength}");
            }

            window.SelfTestCommands.TryInvoke(CommandId.ViewSplit);
            await Turn(2000);   // the preview's WebView2 comes up
            box.Select(0, 0);
            var web = Descendant<WebView2>(window.SelfTestRoot);
            if (web is null || !SplitShowsPreview(window))
            {
                Add("input.f3.fromThePreviewFiresOnce", false, web is null ? "no preview WebView2 in the window" : "the preview is not on screen at this width");
                return;
            }
            if (await ForegroundAsync(window, web))
            {
                SelfTestInput.Key(SelfTestInput.VK_F3);
                await Turn(400);
                Add("input.f3.fromThePreviewFiresOnce", box.SelectionStart == 0 && box.SelectionLength == 3,
                    $"{box.SelectionStart}+{box.SelectionLength} (6+3 would be a double fire, 0+0 no fire)");
            }
            else
            {
                Add("input.f3.fromThePreviewFiresOnce", false, "the preview WebView2 would not take focus");
            }
        }

        static bool SplitShowsPreview(DocumentWindow window) => window.SelfTestSnapshot.DisplayedMode == ViewMode.Split;

        static T? Descendant<T>(DependencyObject root) where T : DependencyObject
        {
            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T found) return found;
                if (Descendant<T>(child) is { } inner) return inner;
            }
            return null;
        }
    }
}
#endif
