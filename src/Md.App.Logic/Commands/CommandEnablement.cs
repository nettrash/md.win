using Md.Core.Document;

namespace Md.App.Logic.Commands;

/// <summary>
/// The *Enabled* column of shell-final.md §2 as code, plus the tick marks. Pure functions of one
/// <see cref="ShellSnapshot"/>: the menu builder calls them when the snapshot changes, and
/// <see cref="CommandDispatcher"/> calls <see cref="IsEnabled"/> before every invocation — so a
/// disabled chord falls through to the focused control instead of doing nothing loudly (§2.9).
///
/// The legend, from §2.1: <c>doc</c> = the window publishes an active document (document windows
/// always; the Book window only while its session is Editing); <c>viewMode</c> = the same set;
/// <c>zen</c> = document windows only; <c>saved</c> = the document has a path; <c>dirty</c> =
/// differs from the last explicit save; <c>book</c> = <c>md.bookBookmark</c> is non-empty;
/// <c>stepper</c> = Book window only.
/// </summary>
public static class CommandEnablement
{
    /// <summary>§2.1 <c>doc</c>. A document window always publishes one; the Book window only while the detail session is editing an article.</summary>
    public static bool HasActiveDocument(ShellSnapshot s) => s.IsBookWindow ? s.IsEditingArticle : s.HasDocument;

    /// <summary>§2.1 <c>saved</c> — an active document that has a path.</summary>
    static bool Saved(ShellSnapshot s) => HasActiveDocument(s) && s.IsSaved;

    /// <summary>
    /// Whether this window has a find bar at all. §8.1 lists the Book window's rows — the menu row,
    /// the <c>SplitView</c>, the footer and the <c>InfoBar</c> — and there is no find bar among
    /// them: Find is a Windows addition to the document window's §1.3 stack (§3.5), because the Mac
    /// gets Find from <c>NSTextView</c> and its book pane publishes no find of its own.
    ///
    /// So the whole find family is dead in the Book window, not merely unhandled there. Reading it
    /// off <c>HasFindQuery</c> / <c>FindBarOpen</c> alone would have made Find Next and Esc right by
    /// accident — a Book window can never set either — while leaving Find… and Use Selection for
    /// Find enabled on <c>EditorVisible</c> and <c>HasSelection</c>, which an article does set.
    /// </summary>
    static bool HasFindBar(ShellSnapshot s) => !s.IsBookWindow;

    /// <summary>Whether the row for <paramref name="id"/> is live in this window right now.</summary>
    public static bool IsEnabled(CommandId id, ShellSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return id switch
        {
            // File (§2.2)
            CommandId.New => true,
            CommandId.Open => true,
            CommandId.OpenRecentEntry => s.RecentEntries.Count > 0,
            CommandId.ClearRecent => s.RecentEntries.Count > 0,
            CommandId.OpenTextBundleFolder => true,
            CommandId.Example => true,
            CommandId.ExampleBook => true,
            CommandId.Close => true,
            CommandId.Save => HasActiveDocument(s),
            // Never in the Book window (§8.5): an article IS its file inside the book folder, and
            // the folder is the book — the reading order, the chapter it belongs to and the name the
            // sidebar shows are all read back out of that path (§8.2, §8.3). "Save this article
            // somewhere else" either silently drops it out of the book or writes a second copy the
            // book never lists, and the Mac's book pane offers no Save As for the same reason: its
            // activeDocument publishes a nil fileURL. Rename… and Move To… (inside the book) are the
            // Book window's answer, and the sidebar's own menu is where a writer reaches them.
            CommandId.SaveAs => HasActiveDocument(s) && !s.IsBookWindow,
            CommandId.Duplicate => HasActiveDocument(s),
            CommandId.Rename => Saved(s),
            CommandId.MoveTo => Saved(s),
            CommandId.RevertToSaved => Saved(s) && s.IsDirty,
            CommandId.Print => HasActiveDocument(s),
            CommandId.ShareSource => HasActiveDocument(s),
            CommandId.ShareRenderedPdf => HasActiveDocument(s),
            CommandId.ExportPdf => HasActiveDocument(s),
            CommandId.ExportHtml => HasActiveDocument(s),
            CommandId.ExportEpub => HasActiveDocument(s),
            CommandId.ExportLaTeX => HasActiveDocument(s),
            CommandId.ExportTextBundle => HasActiveDocument(s),
            // The Export submenu itself is never disabled (the page-size picker inside it is a setting),
            // but its diagram rows exist only while the document has diagrams.
            CommandId.ExportDiagramSvg => s.Diagrams.Count > 0,
            CommandId.PdfPageSize => true,
            CommandId.Exit => true,

            // Edit (§2.4) — the TextBox implements these; they live while an editor pane is on screen.
            CommandId.Undo => s.EditorVisible && s.CanUndo,
            CommandId.Redo => s.EditorVisible && s.CanRedo,
            CommandId.Cut => s.EditorVisible,
            CommandId.Copy => s.EditorVisible,
            CommandId.Paste => s.EditorVisible,
            CommandId.Delete => s.EditorVisible,
            CommandId.SelectAll => s.EditorVisible,
            // The find family, all four gated on the window HAVING a find bar (§8.1) as well as on
            // §2.4's own condition.
            CommandId.Find => HasFindBar(s) && s.EditorVisible,
            CommandId.FindNext => HasFindBar(s) && s.HasFindQuery,
            CommandId.FindPrevious => HasFindBar(s) && s.HasFindQuery,
            CommandId.UseSelectionForFind => HasFindBar(s) && s.HasSelection,

            // View (§2.5)
            CommandId.ViewEdit => HasActiveDocument(s),
            CommandId.ViewSplit => HasActiveDocument(s),
            CommandId.ViewPreview => HasActiveDocument(s),
            // Present but disabled in the Book window, which publishes no Zen command.
            CommandId.ZenMode => !s.IsBookWindow && s.HasDocument,
            CommandId.ShowSidebar => s.IsBookWindow,
            CommandId.FullScreen => true,

            // Book (§2.6)
            CommandId.NewBook => true,
            CommandId.OpenBook => true,
            CommandId.ShowBook => true,
            CommandId.CloseBook => s.HasBook,
            CommandId.ShareBookPdf => s.HasBook,
            CommandId.PrintBook => s.HasBook,
            CommandId.ExportBookPdf => s.HasBook,
            CommandId.ExportBookEpub => s.HasBook,
            CommandId.ExportBookLaTeX => s.HasBook,

            // Go (§2.7) — the stepper is the Book window's.
            CommandId.PreviousArticle => s.IsBookWindow && s.CanPrevious,
            CommandId.NextArticle => s.IsBookWindow && s.CanNext,
            CommandId.Contents => s.Outline.Count > 0,
            CommandId.Notes => s.Notes.Count > 0,

            // Window and Help (§2.8)
            CommandId.Minimize => true,
            CommandId.Zoom => true,
            CommandId.ActivateWindow => s.WindowTitles.Count > 0,
            CommandId.Help => true,
            CommandId.PrivacyPolicy => true,
            CommandId.About => true,

            // §2.9: Esc belongs to the editor unless Zen or the find bar has a use for it.
            CommandId.Escape => s.ZenActive || s.FindBarOpen,

            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "No enablement rule"),
        };
    }

    /// <summary>
    /// A submenu is live while anything inside it is (§2.2, §2.6): Share ▸ goes with the document,
    /// Export Book ▸ with the book, Diagram as SVG ▸ with the diagrams, and Export ▸ is never
    /// disabled because PDF Page Size ▸ inside it never is.
    /// </summary>
    public static bool IsSubmenuEnabled(MenuPath path, ShellSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(s);
        foreach (var spec in CommandTable.All)
        {
            if (spec.Path is not null && spec.Path.IsUnder(path) && IsEnabled(spec.Id, s)) return true;
            if (spec.Path == path && IsEnabled(spec.Id, s)) return true;
        }
        return false;
    }

    /// <summary>The tick on a <c>ToggleMenuFlyoutItem</c>. False for every command that is not one.</summary>
    public static bool IsChecked(CommandId id, ShellSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return id switch
        {
            // In Zen, Ctrl+1 and Ctrl+2 both mean "write" and Split is never ticked (§2.5).
            CommandId.ViewEdit => s.PublishedMode == ViewMode.Edit,
            CommandId.ViewSplit => s.PublishedMode == ViewMode.Split,
            CommandId.ViewPreview => s.PublishedMode == ViewMode.Preview,
            CommandId.ZenMode => s.ZenActive,
            CommandId.ShowSidebar => s.SidebarOpen,
            _ => false,
        };
    }

    /// <summary>
    /// The tick on one row of a dynamic command: a PDF Page Size radio whose <see cref="PageSize.Id"/>
    /// is the stored one, or the Window row for this window. <paramref name="argument"/> is what the
    /// row would dispatch with.
    /// </summary>
    public static bool IsRowChecked(CommandId id, object? argument, ShellSnapshot s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return id switch
        {
            CommandId.PdfPageSize => argument is string pageSizeId && string.Equals(pageSizeId, s.PdfPageSizeId, StringComparison.Ordinal),
            CommandId.ActivateWindow => argument is Guid windowId && s.WindowTitles.Any(w => w.Id == windowId && w.IsThis),
            _ => false,
        };
    }
}
