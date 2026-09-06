using System.Text.RegularExpressions;
using Md.App.Logic.Commands;

namespace Md.App.Logic.Tests;

/// <summary>
/// The Md.App half of WP3 — <c>Windows/DocumentWindow.xaml(.cs)</c>, <c>WindowManager.cs</c>,
/// <c>TitleBarTint.cs</c> — cannot run off Windows, and <c>tools/xamlcheck</c> only proves it
/// compiles. The same trick <see cref="AppSurfaceTests"/> uses for WP4's controls applies here: the
/// numbers and rules shell-design.md §1 fixes verbatim are pinned against the source, so a value
/// that drifts fails in CI rather than on a tester's machine four stages later.
///
/// The load-bearing one is <see cref="EveryCommandIsEitherWiredOrOnTheNamedNotYetWiredList"/>: it
/// is what tells WP6 and WP7 exactly which ids are still theirs, and it fails the moment that list
/// and the command table disagree.
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

    // ── the wiring contract WP6 and WP7 read ──────────────────────────────────────────────────

    [Fact]
    public void EveryCommandIsEitherWiredOrOnTheNamedNotYetWiredList()
    {
        var source = Source("DocumentWindow.xaml.cs");
        var registered = Ids(Regex.Matches(source, @"_dispatcher\.Register\(CommandId\.(\w+)"));
        var deferred = Ids(Regex.Matches(NotYetWiredBlock(source), @"CommandId\.(\w+)"));

        // Nothing may be on both lists, and nothing may be on neither: an id that reaches no handler
        // is a menu row that silently does nothing.
        Assert.Empty(registered.Intersect(deferred));
        var handled = registered.Union(deferred).ToHashSet();
        var missing = CommandTable.All.Select(spec => spec.Id.ToString()).Where(id => !handled.Contains(id)).Order().ToList();
        Assert.Equal([], missing);
    }

    [Fact]
    public void TheNotYetWiredListIsExactlyTheExportPrintAndBookCommands()
    {
        // The report WP6 and WP7 work from. Every id here must disappear from the list — not from
        // the command table — as those packages land.
        string[] expected =
        [
            "ExampleBook",
            "ExportBookEpub", "ExportBookLaTeX", "ExportBookPdf",
            "ExportDiagramSvg", "ExportEpub", "ExportHtml", "ExportLaTeX", "ExportPdf", "ExportTextBundle",
            "NewBook", "NextArticle", "OpenBook", "PreviousArticle", "Print", "PrintBook",
            "ShareBookPdf", "ShareRenderedPdf", "ShareSource", "ShowBook", "ShowSidebar", "CloseBook",
        ];
        Assert.Equal(expected.Order(), Ids(Regex.Matches(NotYetWiredBlock(Source("DocumentWindow.xaml.cs")), @"CommandId\.(\w+)")).Order());
    }

    static string NotYetWiredBlock(string source)
    {
        var start = source.IndexOf("static readonly CommandId[] NotYetWired", StringComparison.Ordinal);
        Assert.True(start >= 0, "DocumentWindow no longer declares the NotYetWired list — WP6/WP7's contract lives there.");
        var end = source.IndexOf("];", start, StringComparison.Ordinal);
        return source[start..end];
    }

    static IEnumerable<string> Ids(MatchCollection matches) => matches.Select(m => m.Groups[1].Value).Distinct();
}
