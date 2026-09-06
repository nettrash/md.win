using System.Reflection;
using Md.App.Logic.Commands;
using Md.Core.Document;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>
/// The traps §2 leaves behind: the hand-written snapshot equality the whole refresh path hangs on,
/// the two "document" flags the Book window and a document window read differently, the WebView2
/// double fire of microsoft-ui-xaml #6231 at its *reported* gap, and the Zen nudge that must never
/// become the window's remembered mode.
///
/// Every case here was found by mutating the shipped code: each one failed to fail before it was
/// written.
/// </summary>
public class CommandAdversarialTests
{
    static ShellSnapshot None => ShellSnapshot.Empty;
    static ShellSnapshot Document => None with { HasDocument = true };

    // ── The snapshot's equality is load-bearing ───────────────────────────────────────────────

    /// <summary>
    /// Every value a snapshot carries, and a value for it that differs from <see cref="ShellSnapshot.Empty"/>'s.
    /// A field added to the record without a row here fails <see cref="EveryFieldTheSnapshotCarriesIsPartOfItsEquality"/>
    /// at the first assert — which is the point: the record's <c>Equals</c> is written out by hand.
    /// </summary>
    static readonly Dictionary<string, object?> Different = new(StringComparer.Ordinal)
    {
        ["HasDocument"] = true,
        ["IsBookWindow"] = true,
        ["IsEditingArticle"] = true,
        ["IsSaved"] = true,
        ["IsDirty"] = true,
        ["HasBook"] = true,
        ["DisplayedMode"] = ViewMode.Preview,                 // Empty's is ViewModes.WindowDefault == Split
        ["ZenActive"] = true,
        ["ZenReading"] = true,
        ["Outline"] = new List<OutlineEntry> { new(1, "Title", "title", 0) },
        ["Notes"] = new List<NoteEntry> { new("A note", 3) },
        ["Diagrams"] = new List<DiagramRef> { new(0, "Mermaid: flow") },
        ["CanPrevious"] = true,
        ["CanNext"] = true,
        ["EditorVisible"] = true,
        ["CanUndo"] = true,
        ["CanRedo"] = true,
        ["RecentEntries"] = new List<RecentEntry> { new("tok", "notes.md", @"C:\Users\n\Documents") },
        ["WindowTitles"] = new List<(Guid Id, string Title, bool IsThis)> { (Guid.Empty, "Book", true) },
        ["HasSelection"] = true,
        ["HasFindQuery"] = true,
        ["PdfPageSizeId"] = PageSize.UsLetter.Id,             // Empty's is PageSize.DefaultId == "a4"
        ["SidebarOpen"] = true,
        ["IsFullScreen"] = true,
        ["FindBarOpen"] = true,
    };

    /// <summary>
    /// <c>MenuBarBuilder.Refresh</c> returns at its first line when the new snapshot equals the last
    /// one, so a value the hand-written <c>Equals</c> forgets is a value the menu bar never redraws
    /// for: View's row would stay "Enter Full Screen" in full screen, Show Sidebar's tick would
    /// freeze, Esc's row would keep the enablement it had when the find bar opened. Records give
    /// this for free; this record does not, because it compares its five lists by value.
    /// </summary>
    [Fact]
    public void EveryFieldTheSnapshotCarriesIsPartOfItsEquality()
    {
        var constructor = typeof(ShellSnapshot).GetConstructors(BindingFlags.Public | BindingFlags.Instance).Single();
        var parameters = constructor.GetParameters();

        Assert.Equal(
            parameters.Select(p => p.Name!).OrderBy(n => n, StringComparer.Ordinal),
            Different.Keys.OrderBy(n => n, StringComparer.Ordinal));

        var baseArguments = parameters
            .Select(p => typeof(ShellSnapshot).GetProperty(p.Name!)!.GetValue(ShellSnapshot.Empty))
            .ToArray();
        var baseline = (ShellSnapshot)constructor.Invoke(baseArguments);
        Assert.Equal(ShellSnapshot.Empty, baseline);

        for (var i = 0; i < parameters.Length; i++)
        {
            var name = parameters[i].Name!;
            var arguments = (object?[])baseArguments.Clone();
            arguments[i] = Different[name];
            var mutated = (ShellSnapshot)constructor.Invoke(arguments);

            Assert.False(baseline.Equals(mutated), $"ShellSnapshot.Equals ignores {name}: the menu bar would never refresh for it");
            Assert.False(mutated.Equals(baseline), $"ShellSnapshot.Equals ignores {name} in the other direction");
        }
    }

    // ── The two "document" flags (§2.1's doc legend) ──────────────────────────────────────────

    /// <summary>
    /// §2.1: <c>doc</c> is "document windows always; the Book window only while
    /// <c>session.Stage == Editing</c>". Which flag answers depends on the window, so neither a
    /// Book window that also sets <c>HasDocument</c> (it does host an article) nor a document
    /// window carrying a stale <c>IsEditingArticle</c> may reach the File menu.
    /// </summary>
    [Fact]
    public void TheBookWindowsDocumentIsItsArticleStageAndNotItsHasDocumentFlag()
    {
        var bookWindowHostingSomething = None with { IsBookWindow = true, HasDocument = true };
        Assert.False(CommandEnablement.HasActiveDocument(bookWindowHostingSomething));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Save, bookWindowHostingSomething));
        Assert.False(CommandEnablement.IsEnabled(CommandId.ExportPdf, bookWindowHostingSomething));

        var bookWindowEditing = None with { IsBookWindow = true, IsEditingArticle = true };
        Assert.True(CommandEnablement.HasActiveDocument(bookWindowEditing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Save, bookWindowEditing));

        // A document window never reads IsEditingArticle — that field is the Book window's alone.
        var documentWindowWithAStaleFlag = None with { IsEditingArticle = true };
        Assert.False(CommandEnablement.HasActiveDocument(documentWindowWithAStaleFlag));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Save, documentWindowWithAStaleFlag));
        Assert.True(CommandEnablement.HasActiveDocument(Document));
    }

    /// <summary>
    /// §2.1: <c>zen</c> is "document windows only", and §2.5 keeps the row present but disabled in
    /// the Book window. The Book window publishing a document is exactly the case that separates
    /// "not a document window" from "no document".
    /// </summary>
    [Fact]
    public void ZenIsRefusedByTheBookWindowEvenWhileItIsEditingAnArticle()
    {
        var bookWindowEditing = None with { IsBookWindow = true, HasDocument = true, IsEditingArticle = true };
        Assert.False(CommandEnablement.IsEnabled(CommandId.ZenMode, bookWindowEditing));
        Assert.NotNull(CommandTable.For(CommandId.ZenMode).Path);      // present in the bar all the same
        Assert.True(CommandEnablement.IsEnabled(CommandId.ZenMode, Document));
    }

    // ── The WebView2 double fire (§2.9, §13.4 stage 2) ────────────────────────────────────────

    /// <summary>
    /// The day-1 Windows check of §13.4 stage 2, as a unit test: Ctrl+1 with the preview focused
    /// must fire <b>once</b>. microsoft-ui-xaml #6231 reports the second <c>Invoked</c> arriving
    /// roughly 100 ms after the first — inside the 150 ms window on purpose — and the third press
    /// here is a real one, at a human's repeat rate.
    /// </summary>
    [Fact]
    public void Ctrl1FiresOnceWhenTheWebView2ForwardsItAtTheGapTheBugReports()
    {
        var scheduler = new FakeScheduler();
        var snapshot = Document;
        var dispatcher = new CommandDispatcher(scheduler.Clock, () => snapshot);
        var runs = 0;
        dispatcher.Register(CommandId.ViewEdit, () => runs++);

        Assert.True(dispatcher.TryInvoke(CommandId.ViewEdit));
        scheduler.Clock.Advance(TimeSpan.FromMilliseconds(100));
        // Swallowed, but still reported handled so nothing downstream acts on the duplicate either.
        Assert.True(dispatcher.TryInvoke(CommandId.ViewEdit));
        Assert.Equal(1, runs);

        scheduler.Clock.Advance(TimeSpan.FromMilliseconds(400));
        Assert.True(dispatcher.TryInvoke(CommandId.ViewEdit));
        Assert.Equal(2, runs);
    }

    /// <summary>
    /// The guard is keyed on the <see cref="CommandId"/> alone, never on the argument, which is what
    /// §2.9 asks for — the forwarded duplicate carries the same argument (accelerators carry none),
    /// and no dynamic row can be clicked twice inside 150 ms because the menu has to be reopened
    /// between clicks. Pinned so that widening the key later is a decision and not a slip.
    /// </summary>
    [Fact]
    public void TheGuardIsKeyedOnTheCommandAndNotOnTheRowTheArgumentNames()
    {
        var scheduler = new FakeScheduler();
        var snapshot = Document with { RecentEntries = [new RecentEntry("a", "a.md", "f"), new RecentEntry("b", "b.md", "f")] };
        var dispatcher = new CommandDispatcher(scheduler.Clock, () => snapshot);
        var opened = new List<object?>();
        dispatcher.Register(CommandId.OpenRecentEntry, opened.Add);

        Assert.True(dispatcher.TryInvoke(CommandId.OpenRecentEntry, "a"));
        Assert.True(dispatcher.TryInvoke(CommandId.OpenRecentEntry, "b"));
        Assert.Equal(["a"], opened);

        scheduler.Clock.Advance(CommandDispatcher.DoubleFireWindow);
        Assert.True(dispatcher.TryInvoke(CommandId.OpenRecentEntry, "b"));
        Assert.Equal(["a", "b"], opened);
    }

    // ── Zen's mode nudge is not the window's mode (§2.5) ──────────────────────────────────────

    /// <summary>
    /// In Zen, Ctrl+3 means "read" and Ctrl+1/Ctrl+2 mean "write"; the window's own
    /// <c>DisplayedMode</c> is untouched, so leaving Zen returns to Split rather than to whatever
    /// the last Zen keystroke looked like. Split is never ticked while Zen is on (Mac §2.2).
    /// </summary>
    [Fact]
    public void AZenNudgeIsNeverRememberedAsTheWindowsMode()
    {
        var split = Document with { DisplayedMode = ViewMode.Split };
        var zenReading = split with { ZenActive = true, ZenReading = true };

        Assert.Equal(ViewMode.Preview, zenReading.PublishedMode);
        Assert.Equal(ViewMode.Split, zenReading.DisplayedMode);
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewSplit, zenReading));
        Assert.True(CommandEnablement.IsChecked(CommandId.ViewPreview, zenReading));

        var zenWriting = zenReading with { ZenReading = false };
        Assert.Equal(ViewMode.Edit, zenWriting.PublishedMode);
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewSplit, zenWriting));

        // Leaving Zen: the window is back in the mode it never left.
        Assert.Equal(ViewMode.Split, (zenWriting with { ZenActive = false }).PublishedMode);
        Assert.True(CommandEnablement.IsChecked(CommandId.ViewSplit, zenWriting with { ZenActive = false }));
    }

    // ── Esc, and the chords that must fall through ────────────────────────────────────────────

    /// <summary>
    /// §2.9: Esc is a root accelerator, so it must report <c>Handled = false</c> whenever the editor
    /// should keep it. An open find bar claims Esc even with an empty query — closing an empty find
    /// bar is exactly what Esc is for there — which <c>HasFindQuery</c> would not have said.
    /// </summary>
    [Fact]
    public void EscFallsThroughToTheEditorUntilZenOrAnEmptyFindBarClaimsIt()
    {
        var scheduler = new FakeScheduler();
        var snapshot = Document with { EditorVisible = true };
        var dispatcher = new CommandDispatcher(scheduler.Clock, () => snapshot);
        var runs = 0;
        dispatcher.Register(CommandId.Escape, () => runs++);

        Assert.False(dispatcher.TryInvoke(CommandId.Escape));
        Assert.Equal(0, runs);

        snapshot = snapshot with { FindBarOpen = true, HasFindQuery = false };
        Assert.True(dispatcher.TryInvoke(CommandId.Escape));
        Assert.Equal(1, runs);

        scheduler.Clock.Advance(TimeSpan.FromSeconds(1));
        snapshot = Document with { EditorVisible = true, ZenActive = true };
        Assert.True(dispatcher.TryInvoke(CommandId.Escape));
        Assert.Equal(2, runs);
    }

    /// <summary>
    /// The installer wires all twenty root accelerators on the window root. Against a window with
    /// nothing open, every one of them that §2 disables must answer <c>false</c> so the key reaches
    /// the focused control, and every one §2 leaves enabled must run — one assert per chord, so a
    /// table row that silently swaps sides is visible.
    /// </summary>
    [Fact]
    public void EveryRootAcceleratorAnswersHandledExactlyWhenSection2EnablesIt()
    {
        var scheduler = new FakeScheduler();
        var snapshot = ShellSnapshot.Empty;
        var dispatcher = new CommandDispatcher(scheduler.Clock, () => snapshot);
        var ran = new List<CommandId>();
        foreach (var spec in CommandTable.RootAccelerators)
        {
            var id = spec.Id;
            dispatcher.Register(id, () => ran.Add(id));
        }

        foreach (var spec in CommandTable.RootAccelerators)
        {
            var expected = CommandEnablement.IsEnabled(spec.Id, snapshot);
            Assert.Equal(expected, dispatcher.TryInvoke(spec.Id));
        }

        // With nothing open these are the chords that must NOT be swallowed by the window.
        Assert.DoesNotContain(CommandId.Save, ran);
        Assert.DoesNotContain(CommandId.Print, ran);
        Assert.DoesNotContain(CommandId.Find, ran);
        Assert.DoesNotContain(CommandId.Escape, ran);
        Assert.DoesNotContain(CommandId.ZenMode, ran);
        Assert.Contains(CommandId.New, ran);
        Assert.Contains(CommandId.Close, ran);
        Assert.Contains(CommandId.FullScreen, ran);
        Assert.Contains(CommandId.ShowBook, ran);
        Assert.Contains(CommandId.Help, ran);
    }
}
