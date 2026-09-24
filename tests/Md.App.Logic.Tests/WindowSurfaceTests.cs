using System.Text.RegularExpressions;
using Md.App.Logic.Commands;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>
/// The Md.App half of WP3 — <c>Windows/DocumentWindow.xaml(.cs)</c>, <c>WindowManager.cs</c>,
/// <c>TitleBarTint.cs</c> — cannot run off Windows, and <c>tools/xamlcheck</c> only proves it
/// compiles. The same trick <see cref="AppSurfaceTests"/> uses for WP4's controls applies here: the
/// numbers and rules shell-design.md §1 fixes verbatim are pinned against the source, so a value
/// that drifts fails in CI rather than on a tester's machine four stages later.
///
/// The load-bearing ones are the three wiring tests at the bottom: every command in the table
/// reaches a handler in a window, and nothing a window's own menus can ENABLE is left reaching
/// none — which is the dead menu row this design has no other way to catch off Windows.
/// </summary>
public sealed class WindowSurfaceTests
{
    static string Source(string file) => File.ReadAllText(RepoFiles.At("src", "Md.App", "Windows", file));

    /// <summary>
    /// The file with its commentary removed. These files document the very rules this suite pins —
    /// "the title bar is not extended", "Closed never vetoes" — so a correct file mentions every
    /// forbidden phrase in prose; only the CODE may be searched for one.
    /// </summary>
    static string CodeOnly(string file)
    {
        var text = Regex.Replace(Source(file), @"(?m)^\s*//.*$", string.Empty);
        text = Regex.Replace(text, @"///.*$", string.Empty, RegexOptions.Multiline);
        return Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
    }

    /// <summary>
    /// One method's body, by the signature that starts it, brace-matched. Several rules below are
    /// about what a PARTICULAR handler does or does not do, and a file-wide substring search answers
    /// a different question — that is how the §1.4 "Closed never vetoes" pin first misfired on an
    /// unrelated handler marking its own event args handled.
    /// </summary>
    static string Method(string file, string signature)
    {
        var source = Source(file);
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{file} no longer declares `{signature}`.");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"{signature} in {file} has no body.");
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new Xunit.Sdk.XunitException($"{signature} in {file} is not brace-balanced.");
    }

    static void Pins(string file, string design, params string[] fragments)
    {
        var source = Source(file);
        foreach (var fragment in fragments)
            Assert.True(source.Contains(fragment, StringComparison.Ordinal),
                $"{file} no longer contains `{fragment}` — shell-design.md {design} fixes it.");
    }

    // ── §11.2: the XAML carries structure and names only ──────────────────────────────────────

    [Fact]
    public void TheWindowXamlIsStructureAndNamesOnly()
    {
        var xaml = Regex.Replace(Source("DocumentWindow.xaml"), "<!--.*?-->", string.Empty, RegexOptions.Singleline);
        foreach (var banned in new[] { "x:Bind", "{Binding", "ThemeResource", "StaticResource", "Click=", "<Style", "ControlTemplate", "DataTemplate", "CommunityToolkit" })
            Assert.True(!xaml.Contains(banned, StringComparison.Ordinal), $"DocumentWindow.xaml contains `{banned}`, which §11.2 forbids.");
    }

    [Fact]
    public void TheWindowHasTheFiveRowsSection13NamesInOrder()
    {
        // MenuBar · content · find bar · InfoBar · footer (§1.3), plus the two layers WP6 fills.
        var xaml = Source("DocumentWindow.xaml");
        var order = new[] { "x:Name=\"MenuSlot\"", "x:Name=\"ContentHost\"", "x:Name=\"Find\"", "x:Name=\"Info\"", "x:Name=\"Footer\"", "x:Name=\"ZenCapsule\"", "x:Name=\"ExportCanvas\"", "x:Name=\"OverlayHost\"" };
        var at = -1;
        foreach (var name in order)
        {
            var next = xaml.IndexOf(name, StringComparison.Ordinal);
            Assert.True(next > at, $"DocumentWindow.xaml is missing {name}, or it moved out of §1.3's order.");
            at = next;
        }
    }

    // ── §1.3, §1.4: the window's own rules ────────────────────────────────────────────────────

    [Fact]
    public void TheClientAreaIsSizedInPhysicalPixelsAndTheMinimumIsThePresenters()
    {
        // ResizeClient takes the CLIENT area in physical pixels; the minimum is an OverlappedPresenter
        // property, not a XAML MinWidth.
        Pins("DocumentWindow.xaml.cs", "§1.3",
            "AppWindow.ResizeClient(new SizeInt32(",
            "presenter.PreferredMinimumWidth = Px(WindowPlacement.Minimum.Width, scale);",
            "presenter.PreferredMinimumHeight = Px(WindowPlacement.Minimum.Height, scale);",
            "GetDpiForWindow");
    }

    [Fact]
    public void AllThreeCloseRoutesArePresentAndClosedIsCleanupOnly()
    {
        // §1.4: Closing cancels and asks; the menu and Exit ask directly; Closed only cleans up and
        // never vetoes (nothing assigns WindowEventArgs.Handled).
        Pins("DocumentWindow.xaml.cs", "§1.4",
            "AppWindow.Closing += OnAppWindowClosing",
            "args.Cancel = true;",
            "public async Task<bool> RequestCloseAsync()",
            "public void CloseApproved()",
            "Closed += OnClosed");

        // Scoped to OnClosed, not to the file: §1.4 route 3 is about WindowEventArgs.Handled, and
        // other handlers (the §6.1 drop) legitimately mark their OWN args handled.
        Assert.DoesNotContain(".Handled = true", Method("DocumentWindow.xaml.cs", "void OnClosed("), StringComparison.Ordinal);
    }

    [Fact]
    public void CtrlWAndTheSystemCloseButtonRunTheSamePolicyExactlyOnce()
    {
        // §1.4 routes 1 and 2 differ only in who asks. Both must reach the ONE approval path, and
        // that path must be re-entrant-safe: a second Ctrl+W over the open "Save changes?" dialog
        // would otherwise stack a second dialog, and answering one would close a window the other
        // is still waiting on.
        var source = Source("DocumentWindow.xaml.cs");
        Assert.Contains("_dispatcher.Register(CommandId.Close, () => _ = ApproveAndCloseAsync());", source, StringComparison.Ordinal);
        Assert.Contains("_ = ApproveAndCloseAsync();", Method("DocumentWindow.xaml.cs", "void OnAppWindowClosing("), StringComparison.Ordinal);

        var approve = Method("DocumentWindow.xaml.cs", "async Task ApproveAndCloseAsync()");
        Assert.Contains("if (_closePending || _closeApproved) return;", approve, StringComparison.Ordinal);
        Assert.Contains("if (await RequestCloseAsync()) CloseApproved();", approve, StringComparison.Ordinal);

        // And the already-approved close must pass Closing straight through: cancelling it there
        // after CloseApproved() has run would leave a window that can never be shut.
        Assert.Contains("if (_closeApproved) return;", Method("DocumentWindow.xaml.cs", "void OnAppWindowClosing("), StringComparison.Ordinal);
    }

    [Fact]
    public void ACloseWaitsForAnExportOrPrintInFlight()
    {
        // §1.4: "an export or print in flight → awaited (bounded 10 s) before the window goes".
        // Without the await a close tears the renderer's WebView2 down mid-render; without the
        // bound a wedged renderer makes the window unclosable. Both halves are the rule.
        Assert.Contains("await DrainOutputAsync();", Method("DocumentWindow.xaml.cs", "public async Task<bool> RequestCloseAsync()"), StringComparison.Ordinal);
        Pins("DocumentWindow.xaml.cs", "§1.4",
            "OutputDrainTimeout = TimeSpan.FromSeconds(10)",
            "await Task.WhenAny(_output, Task.Delay(OutputDrainTimeout));");
    }

    [Fact]
    public void OnlyASavedDocumentGetsASessionRow()
    {
        // §1.6: "Only saved documents are listed (untitled drafts are not autosaved — §14)". The
        // window's own guard, not just the codec's: a row for an untitled window would restore an
        // empty window over text the writer never got back.
        Pins("DocumentWindow.xaml.cs", "§1.6",
            "public SessionWindow? SessionRow() =>\n        _session.EditingPath is { } path\n            ? new SessionWindow(",
            "            : null;");
    }

    [Fact]
    public void TheSessionIsWrittenOnEveryWindowCloseAndFromTheWindowsExitFoundOpen()
    {
        // §1.6: "Written on every window close and on Exit." Exit writes BEFORE it closes anything,
        // or quitting with three windows would record the last one standing and restore one.
        Assert.Contains("SaveSession();", Method("WindowManager.cs", "public void Forget(DocumentWindow window)"), StringComparison.Ordinal);

        var exit = Method("WindowManager.cs", "public async Task<bool> RequestExitAsync()");
        var wrote = exit.IndexOf("Write(open);", StringComparison.Ordinal);
        var closed = exit.IndexOf("CloseApproved();", StringComparison.Ordinal);
        Assert.True(wrote >= 0, "RequestExitAsync no longer records the open windows — §1.6.");
        Assert.True(closed > wrote, "RequestExitAsync closes windows before it records them; §1.6 wants the session as Exit found it.");
    }

    [Fact]
    public void AWindowRootAcceptsDroppedDocuments()
    {
        // §6.1's drag & drop, which is how a .textbundle FOLDER arrives at all (§6.5): a folder
        // cannot be a file association. Classified by the router, so a drop and a double-click can
        // never drift apart.
        Pins("DocumentWindow.xaml.cs", "§6.1",
            "Root.AllowDrop = true;",
            "Root.DragOver += OnDragOver;",
            "Root.Drop += OnDrop;",
            "StandardDataFormats.StorageItems",
            "DataPackageOperation.Copy",
            "await args.DataView.GetStorageItemsAsync()",
            "ActivationRouter.Classify(new ActivationItem(item.Path ?? \"\", item is StorageFolder))");
    }

    [Fact]
    public void ZenIsTheMacsFractionsAppliedToTheContentGrid()
    {
        // 1 : 4 : 1 by 4 : 92 : 4 — the Mac's fractions, DPI-free (§5.4).
        Pins("DocumentWindow.xaml.cs", "§5.4",
            "ContentHost.ColumnDefinitions.Add(Star(1));",
            "ContentHost.ColumnDefinitions.Add(Star(4));",
            "ContentHost.RowDefinitions.Add(StarRow(4));",
            "ContentHost.RowDefinitions.Add(StarRow(92));");
    }

    [Fact]
    public void TheRedirectAlsoAsksWin32ToComeForward()
    {
        // §1.1.1: a process that is not foreground cannot raise its own window by Activate() alone.
        Pins("WindowManager.cs", "§1.1.1", "if (redirected) Interop.NativeMethods.SetForegroundWindow(target.Handle);");
    }

    [Fact]
    public void TheTitleBarIsTintedRatherThanReplaced()
    {
        // §1.3: the STANDARD title bar, tinted — nothing extends content into it or calls SetTitleBar.
        Pins("TitleBarTint.cs", "§1.3",
            "AppWindowTitleBar.IsCustomizationSupported()",
            "bar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;");
        var code = CodeOnly("TitleBarTint.cs") + CodeOnly("DocumentWindow.xaml.cs");
        Assert.DoesNotContain("ExtendsContentIntoTitleBar", code, StringComparison.Ordinal);
        Assert.DoesNotContain("SetTitleBar", code, StringComparison.Ordinal);
    }

    const string DocumentWindowFile = "DocumentWindow.xaml.cs";
    const string BookWindowFile = "BookWindow.xaml.cs";
    const string ManagerFile = "WindowManager.cs";

    // ── §6.1, §6.6, §7.1: the affordances every window carries ────────────────────────────────

    /// <summary>
    /// §6.1 gives drag &amp; drop to <em>any</em> window root, and the README says so too. The Book
    /// window had none, so a dropped file landed nowhere at all — a silent no-op is exactly what a
    /// source pin catches off Windows. Both windows now run one classification and one manager call,
    /// so a drop cannot drift from a double-click.
    /// </summary>
    [Theory]
    [InlineData(DocumentWindowFile)]
    [InlineData(BookWindowFile)]
    public void BothWindowsAcceptADroppedDocument(string file)
    {
        Pins(file, "§6.1",
            "Root.AllowDrop = true;",
            "Root.DragOver += OnDragOver;",
            "Root.Drop += OnDrop;",
            "args.DataView.Contains(StandardDataFormats.StorageItems)",
            "DataPackageOperation.Copy",
            "await args.DataView.GetStorageItemsAsync()",
            "ActivationRouter.Classify(new ActivationItem(item.Path ?? \"\", item is StorageFolder))",
            // GetStorageItemsAsync is awaited: without the deferral the data view can be released
            // under the handler, and the drop silently yields nothing.
            "args.GetDeferral()",
            "deferral.Complete();");
    }

    /// <summary>
    /// §2.1: "every window carries the same seven menus". Open Recent's rows are the app's MRU, so
    /// the Book window publishes them and the manager wires both of its commands — without the rows
    /// the row is permanently greyed, and without the commands it is enabled and inert.
    /// </summary>
    [Fact]
    public void TheBookWindowPublishesAndWiresOpenRecent()
    {
        Pins(BookWindowFile, "§2.1, §6.6",
            "RecentEntries = _recent,",
            "public void RefreshRecent()",
            "_services.Recent.Entries");
        Pins(ManagerFile, "§2.1, §6.6",
            "commands.Register(CommandId.OpenRecentEntry,",
            "commands.Register(CommandId.ClearRecent,",
            "window.RefreshRecent();");
    }

    /// <summary>
    /// §7.1: "a ProgressRing appears in the footer after 500 ms". The delay and the UI-thread hop are
    /// <c>Md.App.Logic.Export.BusyRing</c>'s (and tested there); what cannot be tested off Windows is
    /// that each window actually subscribes one to its pipeline — which is what left
    /// <c>BusyChanged</c> with no consumer at all.
    /// </summary>
    [Fact]
    public void BothWindowsShowTheExportRingInTheirFooter()
    {
        Pins(DocumentWindowFile, "§7.1",
            "_busy = new BusyRing(_scheduler, _ui, Footer.ShowBusy);",
            "_exports.Pipeline.BusyChanged += _busy.BusyChanged;",
            "_busy.Cancel();");
        Pins(BookWindowFile, "§7.1",
            "_busy = new BusyRing(services.Scheduler, services.UiThread, _counts.ShowBusy);",
            "_busy.Cancel();");
        // The Book window's pipeline does not exist when its constructor runs — the manager builds it
        // — so the subscription lives in the hand-over rather than in the constructor.
        Pins(BookWindowFile, "§7.1", "exports.Pipeline.BusyChanged += _busy.BusyChanged;");
        Pins(ManagerFile, "§7.1", "window.AttachExports(exports);");
    }

    /// <summary>
    /// One spelling of the two things §9 and §1.3 give the Book window: its "WxH" client size and its
    /// title. Both were hand-built copies in <c>BookWindow.xaml.cs</c> — the codec beside
    /// <c>WindowPlacement</c>'s own, the title beside <c>WindowTitle.ForBook</c> — and the copies
    /// disagreed with the originals about an empty book name and about a size below the minimum.
    /// </summary>
    [Fact]
    public void TheBookWindowUsesTheSharedTitleAndSizeCodecs()
    {
        Pins(BookWindowFile, "§1.3, §9",
            "WindowTitle.ForBook(book?.Name,",
            "WindowPlacement.ParseSize(_services.Settings.GetString(SettingsKeys.BookWindowSize), WindowPlacement.BookDefault)",
            "WindowPlacement.FormatSize(new WindowSize(");
        // And the second copy is gone rather than merely unused.
        Assert.DoesNotContain("static (int Width, int Height) ParseSize(", Source(BookWindowFile), StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Canvas.Left</c> is an attached property a <em>Canvas parent</em> reads. Both export canvases
    /// are children of a <c>Grid</c>, so setting it on the canvas itself positioned nothing; it is the
    /// WebView2 INSIDE that ExportRenderer parks off-screen (§7.1). The Book window used to set both,
    /// which read as "the canvas is parked" and was not true.
    /// </summary>
    [Fact]
    public void NeitherWindowPositionsItsExportCanvasWithCanvasAttachedProperties()
    {
        foreach (var file in new[] { DocumentWindowFile, BookWindowFile })
        {
            var code = CodeOnly(file);
            Assert.DoesNotContain("Canvas.SetLeft(ExportCanvas", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Canvas.SetTop(ExportCanvas", code, StringComparison.Ordinal);
        }
    }

    // ── the wiring contract: no menu row reaches nothing ──────────────────────────────────────

    /// <summary>
    /// Every command registration in the shell, by the file it lives in. The Md.App half cannot be
    /// executed off Windows — a WinUI <c>Window</c> needs the XAML compiler and a Windows message
    /// loop — so "is this id wired?" is asked of the source, the way this suite already asks
    /// §1.3's numbers of it. The three files are the whole of the wiring: the two windows and the
    /// manager, which registers the Book window's File / Edit / Window / Help rows on it.
    /// </summary>
    static IReadOnlyList<string> RegisteredIn(params string[] files) =>
        [.. files.SelectMany(file => Regex.Matches(Source(file), @"\.Register\(CommandId\.(\w+)").Select(m => m.Groups[1].Value)).Distinct()];

    [Fact]
    public void EveryCommandIsWiredInAWindow()
    {
        // The inverse of the list this test used to pin: while WP6 and WP7 were being written, the
        // export, print and book ids were routed to one named no-op and this suite named them. They
        // are wired now — the no-op is gone — and what is pinned instead is that NOTHING reaches no
        // handler, because a menu row that silently does nothing is the failure this guards.
        var wired = RegisteredIn(DocumentWindowFile, BookWindowFile, ManagerFile).ToHashSet(StringComparer.Ordinal);
        var missing = CommandTable.All.Select(spec => spec.Id.ToString()).Where(id => !wired.Contains(id)).Order().ToList();
        Assert.Equal([], missing);

        Assert.DoesNotContain("NotYetWired", Source(DocumentWindowFile), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDocumentWindowWiresEveryRowItsOwnMenusCanEnable()
    {
        // Enablement is what a menu row's IsEnabled reads (§2.9), so "enabled here but wired
        // nowhere" is exactly the dead row. Everything a document window can light up must be in
        // DocumentWindow.xaml.cs — including all of §7's exports and §2.6's Book rows, which reach
        // the Book window through the manager.
        var wired = RegisteredIn(DocumentWindowFile).ToHashSet(StringComparer.Ordinal);
        // Both sides of Zen: the snapshot has it on (Esc and Zen's own rows light up), and the
        // three rows that open the find bar are dead there — so they are asked for with it off too.
        var dead = Missing(DocumentSnapshot, wired).Union(Missing(DocumentSnapshot with { ZenActive = false }, wired)).ToList();
        Assert.Equal([], dead);

        // And the three rows a document window cannot light up are the Book window's own: they are
        // deliberately absent here rather than wired to a no-op.
        Assert.All(
            new[] { CommandId.PreviousArticle, CommandId.NextArticle, CommandId.ShowSidebar },
            id => Assert.False(CommandEnablement.IsEnabled(id, DocumentSnapshot)));
    }

    [Fact]
    public void TheBookWindowWiresEveryRowItsOwnMenusCanEnable()
    {
        // The Book window registers its own Book, Go and View rows (§2.6, §2.7); the manager
        // registers File, Edit, Window, Help and §7's outputs on the same dispatcher, so both files
        // count as "the Book window's wiring".
        var wired = RegisteredIn(BookWindowFile, ManagerFile).ToHashSet(StringComparer.Ordinal);
        var dead = Missing(BookSnapshot, wired);

        // Empty, and it was not always: Find…, Save As… and Use Selection for Find were enabled here
        // by §2.2/§2.4's tables while no file wired them, and were named in this list so a fourth
        // could not appear unnoticed. They are settled now — CommandEnablement refuses all three in
        // the Book window (§8.1 gives it no find bar; an article that must stay inside its book
        // folder has nowhere to be saved AS) — so the list is gone rather than grown.
        Assert.Equal([], dead);

        // And the row this snapshot exists to catch: Open Recent belongs to every window (§2.1), so
        // a Book window with entries lights it up and the manager must wire it.
        Assert.True(CommandEnablement.IsEnabled(CommandId.OpenRecentEntry, BookSnapshot));
        Assert.True(CommandEnablement.IsEnabled(CommandId.ClearRecent, BookSnapshot));
    }

    /// <summary>
    /// §3.5: the find bar's two replace events reach the document window, and what they produce
    /// travels the normal document path — <c>EditorPane.ReplaceRange</c>, which is one
    /// <c>SelectedText</c> assignment — rather than touching the box or the session directly. The
    /// two halves of §3.5's engine are each called once, with Replace All applied as the single
    /// span TextSearch plans, which is what makes it one undo step.
    /// </summary>
    [Fact]
    public void TheFindBarsReplaceAndReplaceAllTravelTheNormalDocumentPath()
    {
        Pins(DocumentWindowFile, "§3.5",
            "Find.ReplaceRequested += OnReplaceRequested;",
            "Find.ReplaceAllRequested += OnReplaceAllRequested;",
            "_dispatcher.Register(CommandId.Replace, ShowReplaceBar);",
            "Find.FocusReplacement();",
            "TextSearch.Replace(Editor.Text, query, replacement, Editor.SelectionStart, Editor.SelectionLength)",
            "if (step.Apply is { } edit) Panes.Editor.ReplaceRange(edit.Start, edit.Length, edit.Text);",
            "TextSearch.Next(Editor.Text, query, step.SearchFrom)",
            "var plan = TextSearch.ReplaceAll(Editor.Text, query, replacement);",
            "if (plan.Count == 0) return;",                                                      // no hit, no edit and nothing to select
            "Panes.Editor.ReplaceRange(plan.Apply.Start, plan.Apply.Length, plan.Apply.Text);");

        // The window never assigns the control's Text (that would clear the undo history), and both
        // replaces go through the pane rather than the box: exactly two ReplaceRange calls.
        var source = Source(DocumentWindowFile);
        Assert.DoesNotContain("Editor.Text =", source, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(source, @"Panes\.Editor\.ReplaceRange\(").Count);
    }

    /// <summary>
    /// §3.5 and the README's own promise — "Replace All is a single undo step, so one Ctrl+Z puts
    /// every hit back". Ctrl+Z reaches the document only through the control that holds focus: Undo
    /// is deliberately not a root accelerator (§2.4), so a Replace All that left focus in the find
    /// bar's replacement box would undo the writer's typing in that box and leave the document
    /// rewritten. The window hands focus back by selecting the span it just rewrote, which is also
    /// the only way to see what changed.
    /// </summary>
    [Fact]
    public void ReplaceAllSelectsWhatItRewroteSoCtrlZReachesTheDocument()
    {
        Assert.False(CommandTable.For(CommandId.Undo).RootAccelerator);

        var body = Method(DocumentWindowFile, "void OnReplaceAllRequested(string query, string replacement)");
        Assert.Contains("Panes.Editor.SelectRange(plan.Apply.Start, plan.Apply.Text.Length);", body, StringComparison.Ordinal);
        var edit = body.IndexOf("Panes.Editor.ReplaceRange(", StringComparison.Ordinal);
        var select = body.IndexOf("Panes.Editor.SelectRange(", StringComparison.Ordinal);
        Assert.True(edit >= 0 && select > edit, "Replace All must select the rewritten span after it applies the plan.");
    }

    /// <summary>
    /// §3.5 read with §2.4's enablement: Find… and Replace… are greyed the moment the editor leaves
    /// the screen, and a bar already open has to obey the same rule. Left visible over Preview it
    /// rewrites a document nobody is looking at and then selects a hit inside a collapsed control,
    /// while the two menu rows that open it say the feature is unavailable.
    /// </summary>
    [Fact]
    public void TheFindBarLeavesTheScreenWithTheEditor()
    {
        var preview = DocumentSnapshot with { EditorVisible = false };
        Assert.False(CommandEnablement.IsEnabled(CommandId.Find, preview));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Replace, preview));

        var body = Method(DocumentWindowFile, "void OnLayoutChanged(PaneLayout layout)");
        Assert.Contains("SplitLayout.ShowsEditor(layout)", body, StringComparison.Ordinal);
        var hide = body.IndexOf("Find.Visibility = Visibility.Collapsed;", StringComparison.Ordinal);
        var publish = body.IndexOf("Publish();", StringComparison.Ordinal);
        Assert.True(hide >= 0, "OnLayoutChanged no longer hides the find bar when the editor goes off screen.");
        Assert.True(publish > hide, "OnLayoutChanged publishes before it hides the bar, so FindBarOpen is stale in the snapshot.");
    }

    /// <summary>
    /// The four §2.4/§2.2 rows the Book window may not light up, asserted from the other end: not
    /// "no file registers them" (which is what <see cref="TheBookWindowWiresEveryRowItsOwnMenusCanEnable"/>
    /// reads) but "the enablement itself says no", on a snapshot where every condition §2's tables
    /// name is true. A handler quietly appearing for one of them would leave that test green and
    /// this one is what would then have to be deleted on purpose.
    /// </summary>
    [Fact]
    public void TheBookWindowNeverEnablesTheFourRowsItHasNoSurfaceFor()
    {
        var everything = BookSnapshot with { HasFindQuery = true, RecentEntries = [] };
        Assert.False(CommandEnablement.IsEnabled(CommandId.SaveAs, everything));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Find, everything));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Replace, everything));
        Assert.False(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, everything));
        // The same four in a document window, so this is about the Book window and not the snapshot.
        var writing = DocumentSnapshot with { ZenActive = false };
        Assert.True(CommandEnablement.IsEnabled(CommandId.SaveAs, writing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Find, writing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Replace, writing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, writing));
    }

    /// <summary>
    /// §3.6: both windows hand the editor the settings seam beside <c>TextEdited</c> and take it back
    /// on close (the store outlives the window), publish the two typing bools into their snapshot so
    /// the Edit menu's ticks read the store, and re-publish when either key changes anywhere.
    /// </summary>
    [Fact]
    public void BothWindowsGiveTheEditorTheTypingSettingsAndTickThemFromTheStore()
    {
        Pins(DocumentWindowFile, "§3.6",
            "Panes.Editor.UseSettings(_services.Settings);",
            "Panes.Editor.UseSettings(null);",
            "var typing = TypingSettings.Read(_services.Settings);",
            "ContinueLists: typing.ContinueLists,",
            "CapitalizeSentences: typing.CapitalizeSentences);",
            "TypingSettings.IsKey(key)) _ui.Post(Publish);",
            "TypingSettings.Toggle(_services.Settings, key);");
        Pins(BookWindowFile, "§3.6",
            "_panes.Editor.UseSettings(_services.Settings);",
            "_panes.Editor.UseSettings(null);",
            "var typing = TypingSettings.Read(_services.Settings);",
            "ContinueLists = typing.ContinueLists,",
            "CapitalizeSentences = typing.CapitalizeSentences,",
            "else if (TypingSettings.IsKey(key)) _services.UiThread.Post(Publish);");
        Pins(ManagerFile, "§3.6",
            "commands.Register(CommandId.ContinueLists, () => TypingSettings.Toggle(_services.Settings, SettingsKeys.ContinueLists));",
            "commands.Register(CommandId.CapitalizeSentences, () => TypingSettings.Toggle(_services.Settings, SettingsKeys.CapitalizeSentences));");
    }

    /// <summary>
    /// §3.6: Undo, Redo and Paste reach the control through the pane — <c>EditorPane.Undo()</c> /
    /// <c>Redo()</c> / <c>Paste()</c>, which tell the typing hooks first — and never through
    /// <c>Control</c> directly. A row that reached the TextBox would have its Redo re-capitalized
    /// (and the redo stack wiped by the edit that did it) and its Paste judged as typing.
    /// </summary>
    [Fact]
    public void BothWindowsRouteUndoRedoAndPasteThroughThePane()
    {
        Pins(DocumentWindowFile, "§3.6",
            "_dispatcher.Register(CommandId.Undo, () => Panes.Editor.Undo());",
            "_dispatcher.Register(CommandId.Redo, () => Panes.Editor.Redo());",
            "_dispatcher.Register(CommandId.Paste, () => Panes.Editor.Paste());");
        Pins(ManagerFile, "§3.6",
            "commands.Register(CommandId.Undo, () => window.EditorPane.Undo());",
            "commands.Register(CommandId.Redo, () => window.EditorPane.Redo());",
            "commands.Register(CommandId.Paste, () => window.EditorPane.Paste());");
        Pins(BookWindowFile, "§3.6", "public EditorPane EditorPane => _panes.Editor;");
        foreach (var file in new[] { DocumentWindowFile, BookWindowFile, ManagerFile })
        {
            var code = CodeOnly(file);
            foreach (var direct in new[] { "PasteFromClipboard", "Control.Undo(", "Control.Redo(", "=> Editor.Undo()", "=> Editor.Redo()", "window.Editor.Undo()", "window.Editor.Redo()" })
                Assert.False(code.Contains(direct, StringComparison.Ordinal), $"{file} reaches the TextBox with `{direct}`; §3.6 routes it through the pane.");
        }
    }

    /// <summary>
    /// §3.6: a new <c>UndoGeneration</c> — a fresh document, Revert, Reload from Disk, an article
    /// switch — resets the editor through the pane (<c>EditorPane.ResetHistory()</c>, which tells the
    /// typing hooks before it clears the control's history) and never through <c>Control</c>. A
    /// bare <c>ClearUndoRedoHistory()</c> left the tracked capital and the armed override standing
    /// whenever the text coming back equalled the box's — two articles holding the same text — and
    /// <c>SetText</c> rightly saw the echo.
    /// </summary>
    [Fact]
    public void BothWindowsResetTheEditorThroughThePaneOnANewUndoGeneration()
    {
        Assert.Contains("Panes.Editor.ResetHistory();", Method(DocumentWindowFile, "void OnSessionChanged()"), StringComparison.Ordinal);
        Assert.Contains("_panes.Editor.ResetHistory();", Method(BookWindowFile, "void RenderStage()"), StringComparison.Ordinal);
        foreach (var file in new[] { DocumentWindowFile, BookWindowFile, ManagerFile })
            Assert.False(CodeOnly(file).Contains("Control.ClearUndoRedoHistory(", StringComparison.Ordinal), $"{file} clears the TextBox's history directly; §3.6 resets it through the pane.");
    }

    /// <summary>
    /// The silent no-op class (the macOS Preview-only Find, the iOS Find over a document no longer on
    /// screen), read from both ends at once: a row the enablement lights up must be a row whose
    /// handler acts. The document window's three handlers that open the find bar refuse in Zen
    /// (§5.4: Zen has no chrome), so the enablement must say no there as well; Find Next / Previous
    /// select a hit in the editor, so they need the editor on screen; Use Selection for Find opens
    /// the bar, which the layout rule forbids over no editor. Each rule is pinned where it is
    /// decided — the handler's guard here, the enablement in CommandEnablementTests — and this test
    /// fails if either half moves without the other.
    /// </summary>
    [Fact]
    public void EveryFindRowTheEnablementLightsUpHasAHandlerThatActs()
    {
        foreach (var signature in new[] { "void ShowFindBar()", "void ShowReplaceBar()", "void UseSelectionForFind()" })
            Assert.Contains("_state.ZenActive", Method(DocumentWindowFile, signature), StringComparison.Ordinal);
        var zen = DocumentSnapshot with { ZenActive = true, EditorVisible = true };
        foreach (var id in new[] { CommandId.Find, CommandId.Replace, CommandId.UseSelectionForFind })
            Assert.False(CommandEnablement.IsEnabled(id, zen), $"{id} is enabled in Zen, where its handler returns without doing anything.");

        // F3 lands in the editor: SelectRange on the pane. With the editor collapsed that selects
        // nothing anyone can see, so no editor on screen means no F3.
        Assert.Contains("Panes.Editor.SelectRange(hit.Index, hit.Length);", Method(DocumentWindowFile, "void OnFindRequested(string query, bool forward)"), StringComparison.Ordinal);
        var preview = DocumentSnapshot with { ZenActive = false, EditorVisible = false, HasFindQuery = true, HasSelection = true };
        foreach (var id in new[] { CommandId.Find, CommandId.FindNext, CommandId.FindPrevious, CommandId.Replace, CommandId.UseSelectionForFind })
            Assert.False(CommandEnablement.IsEnabled(id, preview), $"{id} is enabled with no editor on screen, where it can only act on a collapsed control.");

        // Use Selection for Find shows the bar: a bar the layout rule would hide the moment it opened.
        Assert.Contains("Find.Visibility = Visibility.Visible;", Method(DocumentWindowFile, "void UseSelectionForFind()"), StringComparison.Ordinal);
    }

    /// <summary>
    /// Cut, Copy and Delete are enabled on a selection (CommandEnablementTests), and the Book
    /// window's snapshot reads it from the pane — so the Book window must re-publish when the
    /// selection moves, as the document window does. Without it the three rows would keep whatever
    /// the last render saw: greyed out over a real selection, or live over none.
    /// </summary>
    [Fact]
    public void BothWindowsRepublishWhenTheEditorsSelectionMoves()
    {
        Pins(DocumentWindowFile, "§2.9", "Panes.Editor.Control.SelectionChanged += (_, _) => Publish();");
        Pins(BookWindowFile, "§2.9", "_panes.Editor.Control.SelectionChanged += (_, _) => Publish();");
    }

    /// <summary>
    /// A <c>ToggleMenuFlyoutItem</c> flips its own tick when it is clicked, before the command runs.
    /// <c>MenuBarBuilder.Refresh</c> returns at its first line when the snapshot has not changed —
    /// and a click that changes nothing (View ▸ Edit while already in Edit, this window's own Window
    /// row) leaves the snapshot exactly as it was, so the flipped tick stayed: no view mode ticked,
    /// or two windows ticked in the Window list, until something else moved. Every toggle row puts
    /// its tick back from the snapshot after its command has run.
    /// </summary>
    [Fact]
    public void AToggleRowsTickIsPutBackFromTheSnapshotAfterEveryClick()
    {
        var builder = File.ReadAllText(RepoFiles.At("src", "Md.App", "Menus", "MenuBarBuilder.cs"));
        Assert.Contains("CommandEnablement.IsChecked(id, dispatcher.Snapshot)", builder, StringComparison.Ordinal);
        Assert.Contains("CommandEnablement.IsRowChecked(id, windowId, dispatcher.Snapshot)", builder, StringComparison.Ordinal);
        // Posted, not inline: whether the control flips IsChecked before or after it raises Click
        // is WinUI's business, and a re-tick that ran first would be overwritten.
        Assert.True(Regex.Matches(builder, @"DispatcherQueue\.TryEnqueue\(").Count >= 2, "MenuBarBuilder no longer posts the re-tick of its toggle rows.");
    }

    /// <summary>The ids §2's tables light up for this window that no file wires — the dead rows.</summary>
    static IReadOnlyList<string> Missing(ShellSnapshot snapshot, IReadOnlySet<string> wired) =>
        [.. CommandTable.All.Select(spec => spec.Id).Distinct()
            .Where(id => CommandEnablement.IsEnabled(id, snapshot))
            .Select(id => id.ToString())
            .Where(id => !wired.Contains(id))
            .Order(StringComparer.Ordinal)];

    /// <summary>A document window with everything on: saved, dirty, a book open, rows in every dynamic submenu.</summary>
    static ShellSnapshot DocumentSnapshot => ShellSnapshot.Empty with
    {
        HasDocument = true,
        IsSaved = true,
        IsDirty = true,
        HasBook = true,
        Outline = [new OutlineEntry(1, "Title", "title", 0)],
        Notes = [new NoteEntry("A note", 3)],
        Diagrams = [new DiagramRef(0, "Mermaid: flow")],
        EditorVisible = true,
        CanUndo = true,
        CanRedo = true,
        RecentEntries = [new RecentEntry("tok", "notes.md", @"C:\Users\n\Documents")],
        WindowTitles = [(Guid.NewGuid(), "notes.md", true)],
        HasSelection = true,
        HasFindQuery = true,
        ZenActive = true,
    };

    /// <summary>The Book window with an article open, a book on disk and a stepper that can move both ways.</summary>
    static ShellSnapshot BookSnapshot => ShellSnapshot.Empty with
    {
        IsBookWindow = true,
        IsEditingArticle = true,
        IsDirty = true,
        HasBook = true,
        Outline = [new OutlineEntry(1, "Title", "title", 0)],
        Notes = [new NoteEntry("A note", 3)],
        Diagrams = [new DiagramRef(0, "Mermaid: flow")],
        CanPrevious = true,
        CanNext = true,
        EditorVisible = true,
        CanUndo = true,
        CanRedo = true,
        // §2.1: Open Recent's rows are every window's, the Book window included (§6.6).
        RecentEntries = [new RecentEntry("tok", "notes.md", @"C:\Users\n\Documents")],
        WindowTitles = [(Guid.NewGuid(), "Book", true)],
        HasSelection = true,
        SidebarOpen = true,
    };
}
