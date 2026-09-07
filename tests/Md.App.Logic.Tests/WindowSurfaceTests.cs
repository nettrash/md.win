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
        var dead = Missing(DocumentSnapshot, wired);
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
    /// The three §2.4/§2.2 rows the Book window may not light up, asserted from the other end: not
    /// "no file registers them" (which is what <see cref="TheBookWindowWiresEveryRowItsOwnMenusCanEnable"/>
    /// reads) but "the enablement itself says no", on a snapshot where every condition §2's tables
    /// name is true. A handler quietly appearing for one of them would leave that test green and
    /// this one is what would then have to be deleted on purpose.
    /// </summary>
    [Fact]
    public void TheBookWindowNeverEnablesTheThreeRowsItHasNoSurfaceFor()
    {
        var everything = BookSnapshot with { HasFindQuery = true, RecentEntries = [] };
        Assert.False(CommandEnablement.IsEnabled(CommandId.SaveAs, everything));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Find, everything));
        Assert.False(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, everything));
        // The same three in a document window, so this is about the Book window and not the snapshot.
        Assert.True(CommandEnablement.IsEnabled(CommandId.SaveAs, DocumentSnapshot));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Find, DocumentSnapshot));
        Assert.True(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, DocumentSnapshot));
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
