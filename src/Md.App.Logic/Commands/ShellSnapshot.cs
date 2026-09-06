using Md.Core.Document;
using Md.Core.Markdown;

namespace Md.App.Logic.Commands;

/// <summary>
/// One row of File ▸ Open Recent (§2.2): the MRU token that reopens the file, the file name the row
/// shows, and the folder the row's tooltip shows. The App's <c>RecentFiles</c> service (WP2) fills
/// these from <c>MostRecentlyUsedList.Entries</c>, newest first.
/// </summary>
public sealed record RecentEntry(string Token, string Name, string Folder);

/// <summary>
/// One row of File ▸ Export ▸ Diagram as SVG (§2.2). Only two of Core's
/// <c>Md.Core.Export.DiagramSvg.Diagram</c> fields reach the menu — the 0-based ordinal that
/// identifies the diagram and the title the row shows — so the command surface carries this pair
/// instead of the Core record, and neither Md.App.Logic.Commands nor the menu builder has to wait
/// for the Wave C export module. The producer of the snapshot maps
/// <c>DiagramSvg.Diagrams(text)</c> → <c>new DiagramRef(d.Ordinal, d.MenuTitle)</c>, and dispatch
/// carries the ordinal back so the export pipeline can re-resolve the real diagram from the source.
/// </summary>
public sealed record DiagramRef(int Ordinal, string MenuTitle);

/// <summary>
/// What a window publishes about itself so the menus can be enabled, ticked and filled — the
/// Windows shape of the Mac's five focused-scene values (§2.1, §2.9, shell.md §3). One snapshot per
/// window; the menu bar and the accelerators of that window read this one and nothing else.
///
/// Equality is structural over the five lists as well as the scalars, because the Mac's focused
/// values compare their outline and notes by value and that comparison is what decides when the Go
/// and Diagram submenus rebuild (§2.9, §14 row 27): a recomputed but identical outline must not
/// rebuild an open menu.
/// </summary>
/// <param name="HasDocument">The window publishes an active document. Document windows always; the Book window never (it publishes <paramref name="IsEditingArticle"/> instead).</param>
/// <param name="IsBookWindow">This is the one app-wide Book window.</param>
/// <param name="IsEditingArticle">Book window only: the detail session is in its Editing stage, so the article is a document for the File menu's purposes.</param>
/// <param name="IsSaved">The document has a path (Rename / Move To / Revert).</param>
/// <param name="IsDirty">The document differs from its last explicit save or open.</param>
/// <param name="HasBook">A book is open — <c>md.bookBookmark</c> is non-empty.</param>
/// <param name="DisplayedMode">The mode the window shows *outside* Zen; the View ticks apply the Zen rule of §2.5 on top of it.</param>
/// <param name="ZenActive">Zen is on (document windows only).</param>
/// <param name="ZenReading">Zen is showing the preview rather than the editor.</param>
/// <param name="Outline">Go ▸ Contents' rows, in document order.</param>
/// <param name="Notes">Go ▸ Notes' rows, in document order.</param>
/// <param name="Diagrams">File ▸ Export ▸ Diagram as SVG's rows; arrives with the 250 ms derived-text tick (§5.5).</param>
/// <param name="CanPrevious">Book window only: the article stepper can step back.</param>
/// <param name="CanNext">Book window only: the article stepper can step forward.</param>
/// <param name="EditorVisible">An editor pane is on screen (Edit, Split, or Zen writing) — the Edit menu's gate.</param>
/// <param name="CanUndo">The editor's own undo stack is non-empty.</param>
/// <param name="CanRedo">The editor's own redo stack is non-empty.</param>
/// <param name="RecentEntries">File ▸ Open Recent's rows, newest first.</param>
/// <param name="WindowTitles">The Window menu's rows: every open window, and which one is this one.</param>
/// <param name="HasSelection">The editor has a non-empty selection (Use Selection for Find).</param>
/// <param name="HasFindQuery">The find bar holds a query (Find Next / Find Previous).</param>
/// <param name="PdfPageSizeId">The <c>md.pdfPageSize</c> value; ticks one PDF Page Size radio.</param>
/// <param name="SidebarOpen">Book window only: the sidebar is showing (View ▸ Show Sidebar's tick).</param>
/// <param name="IsFullScreen">The window's presenter is FullScreen — View's row reads "Exit Full Screen" then (§1.3).</param>
/// <param name="FindBarOpen">The find bar is showing; with <paramref name="ZenActive"/> it is what makes Esc a command (§2.9).</param>
public sealed record ShellSnapshot(
    bool HasDocument,
    bool IsBookWindow,
    bool IsEditingArticle,
    bool IsSaved,
    bool IsDirty,
    bool HasBook,
    ViewMode DisplayedMode,
    bool ZenActive,
    bool ZenReading,
    IReadOnlyList<OutlineEntry> Outline,
    IReadOnlyList<NoteEntry> Notes,
    IReadOnlyList<DiagramRef> Diagrams,
    bool CanPrevious,
    bool CanNext,
    bool EditorVisible,
    bool CanUndo,
    bool CanRedo,
    IReadOnlyList<RecentEntry> RecentEntries,
    IReadOnlyList<(Guid Id, string Title, bool IsThis)> WindowTitles,
    bool HasSelection,
    bool HasFindQuery,
    string PdfPageSizeId,
    bool SidebarOpen = false,
    bool IsFullScreen = false,
    bool FindBarOpen = false)
{
    /// <summary>
    /// Nothing published: no document, no book, no rows. The base every window's snapshot is built
    /// from with <c>with</c>, and what a window reports before its content exists — every command
    /// that needs a document is disabled against it, and New / Open / Show Book still work.
    /// </summary>
    public static readonly ShellSnapshot Empty = new(
        HasDocument: false,
        IsBookWindow: false,
        IsEditingArticle: false,
        IsSaved: false,
        IsDirty: false,
        HasBook: false,
        DisplayedMode: ViewModes.WindowDefault,
        ZenActive: false,
        ZenReading: false,
        Outline: [],
        Notes: [],
        Diagrams: [],
        CanPrevious: false,
        CanNext: false,
        EditorVisible: false,
        CanUndo: false,
        CanRedo: false,
        RecentEntries: [],
        WindowTitles: [],
        HasSelection: false,
        HasFindQuery: false,
        PdfPageSizeId: PageSize.DefaultId);

    /// <summary>
    /// The mode the three View ticks read: outside Zen the displayed mode; in Zen the Mac's
    /// published mode, <c>zenReading ? .preview : .edit</c> — which is why Ctrl+1 and Ctrl+2 both
    /// mean "write", Ctrl+3 means "read", and Split is never ticked in Zen (§2.5).
    /// </summary>
    public ViewMode PublishedMode => ZenActive ? (ZenReading ? ViewMode.Preview : ViewMode.Edit) : DisplayedMode;

    public bool Equals(ShellSnapshot? other) =>
        other is not null
        && HasDocument == other.HasDocument
        && IsBookWindow == other.IsBookWindow
        && IsEditingArticle == other.IsEditingArticle
        && IsSaved == other.IsSaved
        && IsDirty == other.IsDirty
        && HasBook == other.HasBook
        && DisplayedMode == other.DisplayedMode
        && ZenActive == other.ZenActive
        && ZenReading == other.ZenReading
        && CanPrevious == other.CanPrevious
        && CanNext == other.CanNext
        && EditorVisible == other.EditorVisible
        && CanUndo == other.CanUndo
        && CanRedo == other.CanRedo
        && HasSelection == other.HasSelection
        && HasFindQuery == other.HasFindQuery
        && SidebarOpen == other.SidebarOpen
        && IsFullScreen == other.IsFullScreen
        && FindBarOpen == other.FindBarOpen
        && string.Equals(PdfPageSizeId, other.PdfPageSizeId, StringComparison.Ordinal)
        && Same(Outline, other.Outline)
        && Same(Notes, other.Notes)
        && Same(Diagrams, other.Diagrams)
        && Same(RecentEntries, other.RecentEntries)
        && Same(WindowTitles, other.WindowTitles);

    public override int GetHashCode()
    {
        // The counts, not the contents: cheap, stable, and equal snapshots still collide correctly.
        var hash = new HashCode();
        hash.Add(HasDocument); hash.Add(IsBookWindow); hash.Add(IsEditingArticle); hash.Add(IsSaved);
        hash.Add(IsDirty); hash.Add(HasBook); hash.Add(DisplayedMode); hash.Add(ZenActive);
        hash.Add(ZenReading); hash.Add(CanPrevious); hash.Add(CanNext); hash.Add(EditorVisible);
        hash.Add(CanUndo); hash.Add(CanRedo); hash.Add(HasSelection); hash.Add(HasFindQuery);
        hash.Add(SidebarOpen); hash.Add(IsFullScreen); hash.Add(FindBarOpen);
        hash.Add(PdfPageSizeId, StringComparer.Ordinal);
        hash.Add(Outline.Count); hash.Add(Notes.Count); hash.Add(Diagrams.Count);
        hash.Add(RecentEntries.Count); hash.Add(WindowTitles.Count);
        return hash.ToHashCode();
    }

    static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(a[i], b[i])) return false;
        }
        return true;
    }
}
