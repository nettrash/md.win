using Md.App.Logic.Commands;
using Md.Core.Document;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>
/// Every *Enabled* cell of shell-final.md §2, one case each, plus the tick rules of §2.5 and the
/// submenu rule of §2.2/§2.6. The snapshots are built from <see cref="ShellSnapshot.Empty"/> with
/// exactly the one flag each row is about, so a case that passes for the wrong reason is visible.
/// </summary>
public class CommandEnablementTests
{
    static ShellSnapshot None => ShellSnapshot.Empty;
    static ShellSnapshot Document => None with { HasDocument = true };
    static ShellSnapshot SavedDocument => Document with { IsSaved = true };
    static ShellSnapshot BookWindowIdle => None with { IsBookWindow = true };
    static ShellSnapshot BookWindowEditing => BookWindowIdle with { IsEditingArticle = true };

    static IReadOnlyList<OutlineEntry> OneHeading => [new OutlineEntry(1, "Title", "title", 0)];
    static IReadOnlyList<NoteEntry> OneNote => [new NoteEntry("A note", 3)];
    static IReadOnlyList<DiagramRef> OneDiagram => [new DiagramRef(0, "Mermaid: flow")];
    static IReadOnlyList<RecentEntry> OneRecent => [new RecentEntry("tok", "notes.md", @"C:\Users\n\Documents")];

    // ── "always" ──────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.New)]
    [InlineData(CommandId.Open)]
    [InlineData(CommandId.OpenTextBundleFolder)]
    [InlineData(CommandId.Example)]
    [InlineData(CommandId.ExampleBook)]
    [InlineData(CommandId.Close)]
    [InlineData(CommandId.PdfPageSize)]
    [InlineData(CommandId.Exit)]
    [InlineData(CommandId.FullScreen)]
    [InlineData(CommandId.NewBook)]
    [InlineData(CommandId.OpenBook)]
    [InlineData(CommandId.ShowBook)]
    [InlineData(CommandId.Minimize)]
    [InlineData(CommandId.Zoom)]
    [InlineData(CommandId.Help)]
    [InlineData(CommandId.PrivacyPolicy)]
    [InlineData(CommandId.About)]
    public void AlwaysEnabledEvenWithNothingOpen(CommandId id) => Assert.True(CommandEnablement.IsEnabled(id, None));

    // ── "doc" ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.Save)]
    [InlineData(CommandId.Duplicate)]
    [InlineData(CommandId.Print)]
    [InlineData(CommandId.ShareSource)]
    [InlineData(CommandId.ShareRenderedPdf)]
    [InlineData(CommandId.ExportPdf)]
    [InlineData(CommandId.ExportHtml)]
    [InlineData(CommandId.ExportEpub)]
    [InlineData(CommandId.ExportLaTeX)]
    [InlineData(CommandId.ExportTextBundle)]
    [InlineData(CommandId.ViewEdit)]
    [InlineData(CommandId.ViewSplit)]
    [InlineData(CommandId.ViewPreview)]
    public void NeedsAnActiveDocument(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, None));
        Assert.True(CommandEnablement.IsEnabled(id, Document));
        // The Book window publishes a document only while its detail session is editing an article.
        Assert.False(CommandEnablement.IsEnabled(id, BookWindowIdle));
        Assert.True(CommandEnablement.IsEnabled(id, BookWindowEditing));
    }

    /// <summary>
    /// §2.2's <c>doc</c> everywhere except the Book window, where an article is its file inside the
    /// book folder and there is nowhere else for it to be saved <em>as</em> (§8.2, §8.5). The row
    /// stays in the bar — every window carries the same seven menus (§2.1) — it is simply never live
    /// there, which is what keeps it from being a row that looks alive and does nothing.
    /// </summary>
    [Fact]
    public void SaveAsIsADocumentWindowRowAndIsRefusedByTheBookWindow()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.SaveAs, None));
        Assert.True(CommandEnablement.IsEnabled(CommandId.SaveAs, Document));
        Assert.False(CommandEnablement.IsEnabled(CommandId.SaveAs, BookWindowIdle));
        Assert.False(CommandEnablement.IsEnabled(CommandId.SaveAs, BookWindowEditing));
        // Not "because there is no document": the Book window IS publishing one here, and Save,
        // Print and every Export row are live on the same snapshot.
        Assert.True(CommandEnablement.HasActiveDocument(BookWindowEditing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Save, BookWindowEditing));
    }

    // ── "saved" and "dirty" ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.Rename)]
    [InlineData(CommandId.MoveTo)]
    public void NeedsAPathOnDisk(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, None));
        Assert.False(CommandEnablement.IsEnabled(id, Document));
        Assert.True(CommandEnablement.IsEnabled(id, SavedDocument));
    }

    [Fact]
    public void RevertToSavedNeedsBothAPathAndUnsavedChanges()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.RevertToSaved, SavedDocument));
        Assert.False(CommandEnablement.IsEnabled(CommandId.RevertToSaved, Document with { IsDirty = true }));
        Assert.True(CommandEnablement.IsEnabled(CommandId.RevertToSaved, SavedDocument with { IsDirty = true }));
    }

    // ── the dynamic rows' own gates ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.OpenRecentEntry)]
    [InlineData(CommandId.ClearRecent)]
    public void OpenRecentNeedsEntries(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, None));
        Assert.True(CommandEnablement.IsEnabled(id, None with { RecentEntries = OneRecent }));
    }

    [Fact]
    public void DiagramAsSvgNeedsADiagram()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.ExportDiagramSvg, Document));
        Assert.True(CommandEnablement.IsEnabled(CommandId.ExportDiagramSvg, Document with { Diagrams = OneDiagram }));
    }

    [Fact]
    public void ContentsNeedsAnOutlineAndNotesNeedsANote()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.Contents, Document));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Contents, Document with { Outline = OneHeading }));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Notes, Document));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Notes, Document with { Notes = OneNote }));
    }

    [Fact]
    public void TheWindowListNeedsAWindow()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.ActivateWindow, None));
        Assert.True(CommandEnablement.IsEnabled(CommandId.ActivateWindow, None with { WindowTitles = [(Guid.NewGuid(), "Untitled", true)] }));
    }

    // ── Edit (§2.4) ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.Cut)]
    [InlineData(CommandId.Copy)]
    [InlineData(CommandId.Paste)]
    [InlineData(CommandId.Delete)]
    [InlineData(CommandId.SelectAll)]
    [InlineData(CommandId.Find)]
    public void NeedsTheEditorPaneOnScreen(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, Document));
        Assert.True(CommandEnablement.IsEnabled(id, Document with { EditorVisible = true }));
    }

    [Fact]
    public void UndoAndRedoNeedTheEditorAndAStack()
    {
        var editing = Document with { EditorVisible = true };
        Assert.False(CommandEnablement.IsEnabled(CommandId.Undo, editing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Undo, editing with { CanUndo = true }));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Undo, Document with { CanUndo = true }));

        Assert.False(CommandEnablement.IsEnabled(CommandId.Redo, editing));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Redo, editing with { CanRedo = true }));
        Assert.False(CommandEnablement.IsEnabled(CommandId.Redo, Document with { CanRedo = true }));
    }

    [Theory]
    [InlineData(CommandId.FindNext)]
    [InlineData(CommandId.FindPrevious)]
    public void FindNextNeedsAQueryNotAVisibleEditor(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, Document with { EditorVisible = true }));
        Assert.True(CommandEnablement.IsEnabled(id, Document with { HasFindQuery = true }));
    }

    [Fact]
    public void UseSelectionForFindNeedsASelection()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, Document with { EditorVisible = true }));
        Assert.True(CommandEnablement.IsEnabled(CommandId.UseSelectionForFind, Document with { HasSelection = true }));
    }

    /// <summary>
    /// §8.1 gives the Book window a menu row, a <c>SplitView</c>, a footer and an <c>InfoBar</c> —
    /// and no find bar. So the whole of §2.4's find family is dead there, however live the article
    /// itself is: the editor pane is on screen, the selection is real, and the rows still must not
    /// light up, because there is no bar for a query to go into.
    /// </summary>
    [Theory]
    [InlineData(CommandId.Find)]
    [InlineData(CommandId.FindNext)]
    [InlineData(CommandId.FindPrevious)]
    [InlineData(CommandId.UseSelectionForFind)]
    public void TheFindFamilyIsNeverLiveInTheBookWindow(CommandId id)
    {
        // Everything §2.4 asks of each of the four, at once — in a document window it is enabled.
        Assert.True(CommandEnablement.IsEnabled(id, Document with { EditorVisible = true, HasSelection = true, HasFindQuery = true }));

        var bookWindow = BookWindowEditing with { EditorVisible = true, HasSelection = true, HasFindQuery = true };
        Assert.False(CommandEnablement.IsEnabled(id, bookWindow));

        // The Edit rows that DO have a handler there are unaffected — this is about the find bar,
        // not about the Book window's editor.
        Assert.True(CommandEnablement.IsEnabled(CommandId.Copy, bookWindow));
        Assert.True(CommandEnablement.IsEnabled(CommandId.SelectAll, bookWindow));
    }

    // ── View (§2.5) ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ZenIsADocumentWindowCommandPresentButDisabledInTheBookWindow()
    {
        Assert.True(CommandEnablement.IsEnabled(CommandId.ZenMode, Document));
        Assert.False(CommandEnablement.IsEnabled(CommandId.ZenMode, BookWindowEditing));
        Assert.False(CommandEnablement.IsEnabled(CommandId.ZenMode, None));
        Assert.NotNull(CommandTable.For(CommandId.ZenMode).Path);   // present in every window's View menu
    }

    [Fact]
    public void ShowSidebarBelongsToTheBookWindow()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.ShowSidebar, Document));
        Assert.True(CommandEnablement.IsEnabled(CommandId.ShowSidebar, BookWindowIdle));
    }

    // ── Book (§2.6) ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(CommandId.CloseBook)]
    [InlineData(CommandId.ShareBookPdf)]
    [InlineData(CommandId.PrintBook)]
    [InlineData(CommandId.ExportBookPdf)]
    [InlineData(CommandId.ExportBookEpub)]
    [InlineData(CommandId.ExportBookLaTeX)]
    public void NeedsAnOpenBook(CommandId id)
    {
        Assert.False(CommandEnablement.IsEnabled(id, Document));
        Assert.True(CommandEnablement.IsEnabled(id, None with { HasBook = true }));
    }

    // ── Go (§2.7) ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheStepperIsTheBookWindows()
    {
        var book = BookWindowEditing with { CanPrevious = true, CanNext = true };
        Assert.True(CommandEnablement.IsEnabled(CommandId.PreviousArticle, book));
        Assert.True(CommandEnablement.IsEnabled(CommandId.NextArticle, book));
        Assert.False(CommandEnablement.IsEnabled(CommandId.PreviousArticle, BookWindowEditing));
        Assert.False(CommandEnablement.IsEnabled(CommandId.NextArticle, BookWindowEditing));
        // A document window publishes no stepper, even one hosting a book article.
        Assert.False(CommandEnablement.IsEnabled(CommandId.PreviousArticle, Document with { CanPrevious = true }));
        Assert.False(CommandEnablement.IsEnabled(CommandId.NextArticle, Document with { CanNext = true }));
    }

    // ── Escape (§2.9) ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EscapeBelongsToTheEditorUnlessZenOrTheFindBarWantsIt()
    {
        Assert.False(CommandEnablement.IsEnabled(CommandId.Escape, Document with { EditorVisible = true }));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Escape, Document with { ZenActive = true }));
        Assert.True(CommandEnablement.IsEnabled(CommandId.Escape, Document with { FindBarOpen = true }));
    }

    // ── Submenus (§2.2, §2.6) ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ShareGoesWithTheDocumentAndExportIsNeverDisabled()
    {
        var share = new MenuPath("File", "Share");
        var export = new MenuPath("File", "Export");
        Assert.False(CommandEnablement.IsSubmenuEnabled(share, None));
        Assert.True(CommandEnablement.IsSubmenuEnabled(share, Document));
        // PDF Page Size inside it is a setting, so Export is live with no document at all.
        Assert.True(CommandEnablement.IsSubmenuEnabled(export, None));
        Assert.True(CommandEnablement.IsSubmenuEnabled(export, Document));
    }

    [Fact]
    public void TheOtherSubmenusFollowTheirContents()
    {
        Assert.False(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Export", "Diagram as SVG"), Document));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Export", "Diagram as SVG"), Document with { Diagrams = OneDiagram }));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Export", "PDF Page Size"), None));
        Assert.False(CommandEnablement.IsSubmenuEnabled(new MenuPath("Book", "Export Book"), Document));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("Book", "Export Book"), None with { HasBook = true }));
        Assert.False(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Open Recent"), None));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Open Recent"), None with { RecentEntries = OneRecent }));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("File", "Examples"), None));
        Assert.False(CommandEnablement.IsSubmenuEnabled(new MenuPath("Go", "Contents"), Document));
        Assert.True(CommandEnablement.IsSubmenuEnabled(new MenuPath("Go", "Contents"), Document with { Outline = OneHeading }));
    }

    // ── Ticks (§2.5) ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ViewMode.Edit, true, false, false)]
    [InlineData(ViewMode.Split, false, true, false)]
    [InlineData(ViewMode.Preview, false, false, true)]
    public void TheViewTickFollowsTheDisplayedMode(ViewMode mode, bool edit, bool split, bool preview)
    {
        var s = Document with { DisplayedMode = mode };
        Assert.Equal(edit, CommandEnablement.IsChecked(CommandId.ViewEdit, s));
        Assert.Equal(split, CommandEnablement.IsChecked(CommandId.ViewSplit, s));
        Assert.Equal(preview, CommandEnablement.IsChecked(CommandId.ViewPreview, s));
    }

    [Fact]
    public void InZenTheTickIsWriteOrReadAndSplitIsNeverTicked()
    {
        // Whatever the window's own mode was, Zen publishes edit or preview (§2.5).
        var writing = Document with { DisplayedMode = ViewMode.Split, ZenActive = true, ZenReading = false };
        Assert.True(CommandEnablement.IsChecked(CommandId.ViewEdit, writing));
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewSplit, writing));
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewPreview, writing));

        var reading = writing with { ZenReading = true };
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewEdit, reading));
        Assert.False(CommandEnablement.IsChecked(CommandId.ViewSplit, reading));
        Assert.True(CommandEnablement.IsChecked(CommandId.ViewPreview, reading));
    }

    [Fact]
    public void ZenAndSidebarTickThemselves()
    {
        Assert.False(CommandEnablement.IsChecked(CommandId.ZenMode, Document));
        Assert.True(CommandEnablement.IsChecked(CommandId.ZenMode, Document with { ZenActive = true }));
        Assert.False(CommandEnablement.IsChecked(CommandId.ShowSidebar, BookWindowIdle));
        Assert.True(CommandEnablement.IsChecked(CommandId.ShowSidebar, BookWindowIdle with { SidebarOpen = true }));
    }

    [Fact]
    public void NothingElseIsEverTicked()
    {
        CommandId[] toggles = [CommandId.ViewEdit, CommandId.ViewSplit, CommandId.ViewPreview, CommandId.ZenMode, CommandId.ShowSidebar];
        var full = Document with { ZenActive = true, ZenReading = true, SidebarOpen = true, IsBookWindow = true, IsFullScreen = true };
        foreach (var id in Enum.GetValues<CommandId>().Where(i => !toggles.Contains(i)))
            Assert.False(CommandEnablement.IsChecked(id, full));
    }

    [Fact]
    public void TheRadioTicksTheStoredPageSizeAndTheWindowRowTicksThisWindow()
    {
        var s = None with { PdfPageSizeId = PageSize.A5.Id };
        Assert.True(CommandEnablement.IsRowChecked(CommandId.PdfPageSize, "a5", s));
        Assert.False(CommandEnablement.IsRowChecked(CommandId.PdfPageSize, "a4", s));
        Assert.False(CommandEnablement.IsRowChecked(CommandId.PdfPageSize, "A5", s));   // ordinal, like PageSize.Named

        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        var windows = None with { WindowTitles = [(mine, "notes", true), (other, "Book", false)] };
        Assert.True(CommandEnablement.IsRowChecked(CommandId.ActivateWindow, mine, windows));
        Assert.False(CommandEnablement.IsRowChecked(CommandId.ActivateWindow, other, windows));
    }

    // ── The snapshot itself ───────────────────────────────────────────────────────────────────

    [Fact]
    public void SnapshotsCompareTheirListsByValueSoAnUnchangedOutlineDoesNotRebuildTheMenu()
    {
        // §2.9 / §14 row 27: the Go and Diagram submenus are rebuilt on snapshot change, and the
        // derived-text tick hands over freshly parsed but identical lists 250 ms after every edit.
        var a = Document with { Outline = [new OutlineEntry(1, "Title", "title", 0)], Diagrams = [new DiagramRef(0, "Plot")] };
        var b = Document with { Outline = [new OutlineEntry(1, "Title", "title", 0)], Diagrams = [new DiagramRef(0, "Plot")] };
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Outline = [new OutlineEntry(2, "Title", "title", 0)] });
        Assert.NotEqual(a, b with { Diagrams = [] });
    }

    [Fact]
    public void TheEmptySnapshotIsTheDefaultPageSizeAndTheDefaultMode()
    {
        Assert.Equal(PageSize.DefaultId, ShellSnapshot.Empty.PdfPageSizeId);
        Assert.Equal(ViewModes.WindowDefault, ShellSnapshot.Empty.DisplayedMode);
        Assert.Empty(ShellSnapshot.Empty.Outline);
        Assert.Empty(ShellSnapshot.Empty.WindowTitles);
    }

    [Fact]
    public void EveryCommandHasAnEnablementRule()
    {
        foreach (var id in Enum.GetValues<CommandId>()) CommandEnablement.IsEnabled(id, None);
    }
}
