using Md.Core.Markdown;

namespace Md.App.Logic.Commands;

/// <summary>
/// Where a command sits in the bar: the top-level menu, then up to two nested submenus
/// ("File ▸ Export ▸ PDF Page Size"). Submenu *containers* are not table rows — they exist because
/// commands name them here, which keeps every title in one place and every id unique.
/// </summary>
public sealed record MenuPath(string Menu, string? Submenu = null, string? Nested = null)
{
    /// <summary>1, 2 or 3.</summary>
    public int Depth => Nested is not null ? 3 : Submenu is not null ? 2 : 1;

    /// <summary>The last segment — a submenu's own title, or the menu's.</summary>
    public string Leaf => Nested ?? Submenu ?? Menu;

    public IReadOnlyList<string> Segments =>
        Nested is not null ? [Menu, Submenu!, Nested] : Submenu is not null ? [Menu, Submenu] : [Menu];

    /// <summary>True when this path is strictly deeper than <paramref name="parent"/> and starts with it.</summary>
    public bool IsUnder(MenuPath parent)
    {
        if (Depth <= parent.Depth) return false;
        if (!string.Equals(Menu, parent.Menu, StringComparison.Ordinal)) return false;
        if (parent.Depth == 1) return true;
        return string.Equals(Submenu, parent.Submenu, StringComparison.Ordinal);
    }

    /// <summary>This path cut to <paramref name="depth"/> segments — how a child's path names the submenu it goes into.</summary>
    public MenuPath Truncate(int depth) => depth switch
    {
        1 => new MenuPath(Menu),
        2 => new MenuPath(Menu, Submenu),
        _ => this,
    };

    public override string ToString() => string.Join(" \u25B8 ", Segments);
}

/// <summary>
/// One row of the tables in shell-final.md §2. <paramref name="Title"/> is the Mac's own wording,
/// byte for byte (U+2026 ellipsis); for the dynamic kinds it is the *submenu's* title and the rows
/// inside get their titles from the snapshot.
/// </summary>
/// <param name="Id">The command; unique across the table.</param>
/// <param name="Title">The menu row's text, verbatim.</param>
/// <param name="Chord">The shortcut shown next to the row; null for the many rows that have none.</param>
/// <param name="Path">Where the row goes; null only for <see cref="CommandKind.Accelerator"/>, which has no row.</param>
/// <param name="Kind">What the builder makes of it.</param>
/// <param name="WindowsOnly">Marked "(Win)" in §2 — a command md.macOS does not have.</param>
/// <param name="Group">Rows sharing a group sit together; a change of group between siblings is a separator. Numbers are per parent menu, in ascending order.</param>
/// <param name="RootAccelerator">
/// False for the chords §2.4 deliberately leaves with the focused control (Ctrl+Z/Y/X/C/V/A and Del):
/// they are *shown* in the Edit menu but never registered on the window root, so the <c>TextBox</c>
/// and the <c>WebView2</c> keep their own editing keys.
/// </param>
public sealed record CommandSpec(
    CommandId Id,
    string Title,
    Chord? Chord,
    MenuPath? Path,
    CommandKind Kind,
    bool WindowsOnly,
    int Group = 0,
    bool RootAccelerator = true);

/// <summary>
/// The menu bar as a tree, derived from <see cref="CommandTable.All"/>: seven roots in bar order,
/// each holding its rows and submenus in table order with the separators already worked out. The
/// WinUI builder walks this and does nothing but create controls (§2.9).
/// </summary>
/// <param name="Title">The row's or submenu's text.</param>
/// <param name="Path">The path of the command, or of the submenu itself.</param>
/// <param name="Command">The command this row runs; null for a submenu container and for the seven roots.</param>
/// <param name="Children">A submenu's contents, in order; empty for a command row.</param>
/// <param name="SeparatorBefore">A <c>MenuFlyoutSeparator</c> goes in front of this row.</param>
public sealed record MenuNode(
    string Title,
    MenuPath Path,
    CommandSpec? Command,
    IReadOnlyList<MenuNode> Children,
    bool SeparatorBefore);

/// <summary>
/// Every command in shell-final.md §2, declared once. The menus are built from this table, the
/// accelerators are installed from it, and <see cref="CommandEnablement"/> answers for the same ids —
/// so a command cannot exist in one of the three and not the others.
/// </summary>
public static class CommandTable
{
    /// <summary>The seven menus, in bar order. The Mac's bar without its app menu (§2.1): About moved to Help, Quit to File ▸ Exit.</summary>
    public static readonly IReadOnlyList<string> MenuTitles = ["File", "Edit", "View", "Book", "Go", "Window", "Help"];

    /// <summary>The <c>GroupName</c> the seven PDF Page Size radios share (§2.2).</summary>
    public const string PdfPageSizeGroupName = "PdfPageSize";

    /// <summary>View's full-screen row while the presenter is FullScreen (§2.5); <see cref="CommandId.FullScreen"/>'s table title is the other half.</summary>
    public const string ExitFullScreenTitle = "Exit Full Screen";

    // Paths, named once so a typo cannot split a submenu in two.
    static readonly MenuPath File = new("File");
    static readonly MenuPath FileOpenRecent = new("File", "Open Recent");
    static readonly MenuPath FileExamples = new("File", "Examples");
    static readonly MenuPath FileShare = new("File", "Share");
    static readonly MenuPath FileExport = new("File", "Export");
    static readonly MenuPath FileExportDiagrams = new("File", "Export", "Diagram as SVG");
    static readonly MenuPath FileExportPageSize = new("File", "Export", "PDF Page Size");
    static readonly MenuPath Edit = new("Edit");
    static readonly MenuPath View = new("View");
    static readonly MenuPath Book = new("Book");
    static readonly MenuPath BookExport = new("Book", "Export Book");
    static readonly MenuPath Go = new("Go");
    static readonly MenuPath GoContents = new("Go", "Contents");
    static readonly MenuPath GoNotes = new("Go", "Notes");
    static readonly MenuPath Window = new("Window");
    static readonly MenuPath Help = new("Help");

    const KeyModifiers Ctrl = KeyModifiers.Ctrl;
    const KeyModifiers CtrlShift = KeyModifiers.Ctrl | KeyModifiers.Shift;
    const KeyModifiers CtrlAlt = KeyModifiers.Ctrl | KeyModifiers.Alt;

    /// <summary>
    /// The table. Row order is menu order; <c>Group</c> jumps mark the dividers §2 draws. Titles carry
    /// the Mac's U+2026 ellipsis, written as an escape so no editor can quietly swap it for three dots.
    /// </summary>
    public static readonly IReadOnlyList<CommandSpec> All =
    [
        // ── File (§2.2) ───────────────────────────────────────────────────────────────────────
        new(CommandId.New, "New", new Chord(VirtualKeys.N, Ctrl), File, CommandKind.Item, false, 0),
        new(CommandId.Open, "Open\u2026", new Chord(VirtualKeys.O, Ctrl), File, CommandKind.Item, false, 0),
        new(CommandId.OpenRecentEntry, "Open Recent", null, FileOpenRecent, CommandKind.DynamicItems, false, 0),
        new(CommandId.ClearRecent, "Clear Menu", null, FileOpenRecent, CommandKind.Item, false, 1),
        new(CommandId.OpenTextBundleFolder, "Open TextBundle Folder\u2026", null, File, CommandKind.Item, true, 0),
        new(CommandId.Example, "Examples", null, FileExamples, CommandKind.DynamicItems, false, 0),
        new(CommandId.ExampleBook, "Example Book\u2026", null, FileExamples, CommandKind.Item, false, 1),
        new(CommandId.Close, "Close", new Chord(VirtualKeys.W, Ctrl), File, CommandKind.Item, false, 1),
        new(CommandId.Save, "Save", new Chord(VirtualKeys.S, Ctrl), File, CommandKind.Item, false, 1),
        new(CommandId.SaveAs, "Save As\u2026", new Chord(VirtualKeys.S, CtrlShift), File, CommandKind.Item, false, 1),
        new(CommandId.Duplicate, "Duplicate", null, File, CommandKind.Item, false, 1),
        new(CommandId.Rename, "Rename\u2026", null, File, CommandKind.Item, false, 1),
        new(CommandId.MoveTo, "Move To\u2026", null, File, CommandKind.Item, false, 1),
        new(CommandId.RevertToSaved, "Revert to Saved", null, File, CommandKind.Item, false, 1),
        new(CommandId.Print, "Print\u2026", new Chord(VirtualKeys.P, Ctrl), File, CommandKind.Item, false, 2),
        new(CommandId.ShareSource, "Source\u2026", null, FileShare, CommandKind.Item, false, 0),
        new(CommandId.ShareRenderedPdf, "Rendered PDF\u2026", null, FileShare, CommandKind.Item, false, 0),
        new(CommandId.ExportPdf, "PDF\u2026", null, FileExport, CommandKind.Item, false, 0),
        new(CommandId.ExportHtml, "HTML\u2026", null, FileExport, CommandKind.Item, false, 0),
        new(CommandId.ExportEpub, "EPUB\u2026", null, FileExport, CommandKind.Item, false, 0),
        new(CommandId.ExportLaTeX, "LaTeX\u2026", null, FileExport, CommandKind.Item, false, 0),
        new(CommandId.ExportTextBundle, "TextBundle\u2026", null, FileExport, CommandKind.Item, false, 0),
        new(CommandId.ExportDiagramSvg, "Diagram as SVG", null, FileExportDiagrams, CommandKind.DynamicItems, false, 0),
        new(CommandId.PdfPageSize, "PDF Page Size", null, FileExportPageSize, CommandKind.DynamicRadios, false, 0),
        new(CommandId.Exit, "Exit", null, File, CommandKind.Item, true, 4),

        // ── Edit (§2.4) ───────────────────────────────────────────────────────────────────────
        // Ctrl+Z/Y/X/C/V/A and Del are shown but never registered on the root: the focused control owns them.
        new(CommandId.Undo, "Undo", new Chord(VirtualKeys.Z, Ctrl), Edit, CommandKind.Item, false, 0, RootAccelerator: false),
        new(CommandId.Redo, "Redo", new Chord(VirtualKeys.Y, Ctrl), Edit, CommandKind.Item, false, 0, RootAccelerator: false),
        new(CommandId.Cut, "Cut", new Chord(VirtualKeys.X, Ctrl), Edit, CommandKind.Item, false, 1, RootAccelerator: false),
        new(CommandId.Copy, "Copy", new Chord(VirtualKeys.C, Ctrl), Edit, CommandKind.Item, false, 1, RootAccelerator: false),
        new(CommandId.Paste, "Paste", new Chord(VirtualKeys.V, Ctrl), Edit, CommandKind.Item, false, 1, RootAccelerator: false),
        new(CommandId.Delete, "Delete", new Chord(VirtualKeys.Delete), Edit, CommandKind.Item, false, 1, RootAccelerator: false),
        new(CommandId.SelectAll, "Select All", new Chord(VirtualKeys.A, Ctrl), Edit, CommandKind.Item, false, 1, RootAccelerator: false),
        new(CommandId.Find, "Find\u2026", new Chord(VirtualKeys.F, Ctrl), Edit, CommandKind.Item, true, 2),
        new(CommandId.FindNext, "Find Next", new Chord(VirtualKeys.F3), Edit, CommandKind.Item, true, 2),
        new(CommandId.FindPrevious, "Find Previous", new Chord(VirtualKeys.F3, KeyModifiers.Shift), Edit, CommandKind.Item, true, 2),
        new(CommandId.UseSelectionForFind, "Use Selection for Find", new Chord(VirtualKeys.E, Ctrl), Edit, CommandKind.Item, true, 2),

        // ── View (§2.5) ───────────────────────────────────────────────────────────────────────
        new(CommandId.ViewEdit, "Edit", new Chord(VirtualKeys.Number1, Ctrl), View, CommandKind.Toggle, false, 0),
        new(CommandId.ViewSplit, "Split", new Chord(VirtualKeys.Number2, Ctrl), View, CommandKind.Toggle, false, 0),
        new(CommandId.ViewPreview, "Preview", new Chord(VirtualKeys.Number3, Ctrl), View, CommandKind.Toggle, false, 0),
        new(CommandId.ZenMode, "Zen Mode", new Chord(VirtualKeys.Enter, CtrlShift), View, CommandKind.Toggle, false, 0),
        new(CommandId.ShowSidebar, "Show Sidebar", null, View, CommandKind.Toggle, false, 1),
        new(CommandId.FullScreen, "Enter Full Screen", new Chord(VirtualKeys.F11), View, CommandKind.Item, false, 1),

        // ── Book (§2.6) ───────────────────────────────────────────────────────────────────────
        new(CommandId.NewBook, "New Book\u2026", null, Book, CommandKind.Item, false, 0),
        new(CommandId.OpenBook, "Open Book\u2026", null, Book, CommandKind.Item, false, 0),
        new(CommandId.ShowBook, "Show Book", new Chord(VirtualKeys.B, CtrlShift), Book, CommandKind.Item, false, 0),
        new(CommandId.CloseBook, "Close Book", null, Book, CommandKind.Item, false, 0),
        new(CommandId.ShareBookPdf, "Share Book as PDF", null, Book, CommandKind.Item, false, 1),
        new(CommandId.PrintBook, "Print Book\u2026", null, Book, CommandKind.Item, false, 1),
        new(CommandId.ExportBookPdf, "PDF\u2026", null, BookExport, CommandKind.Item, false, 0),
        new(CommandId.ExportBookEpub, "EPUB\u2026", null, BookExport, CommandKind.Item, false, 0),
        new(CommandId.ExportBookLaTeX, "LaTeX\u2026", null, BookExport, CommandKind.Item, false, 0),

        // ── Go (§2.7) ─────────────────────────────────────────────────────────────────────────
        new(CommandId.PreviousArticle, "Previous Article", new Chord(VirtualKeys.Up, CtrlAlt), Go, CommandKind.Item, false, 0),
        new(CommandId.NextArticle, "Next Article", new Chord(VirtualKeys.Down, CtrlAlt), Go, CommandKind.Item, false, 0),
        new(CommandId.Contents, "Contents", null, GoContents, CommandKind.DynamicItems, false, 0),
        new(CommandId.Notes, "Notes", null, GoNotes, CommandKind.DynamicItems, false, 0),

        // ── Window (Win, §2.8) ────────────────────────────────────────────────────────────────
        new(CommandId.Minimize, "Minimize", null, Window, CommandKind.Item, true, 0),
        new(CommandId.Zoom, "Zoom", null, Window, CommandKind.Item, true, 0),
        // Title never shown: the rows carry the windows' own titles.
        new(CommandId.ActivateWindow, "Window List", null, Window, CommandKind.DynamicToggles, true, 1),

        // ── Help (§2.8) ───────────────────────────────────────────────────────────────────────
        new(CommandId.Help, "md Help", new Chord(VirtualKeys.F1), Help, CommandKind.Item, false, 0),
        new(CommandId.PrivacyPolicy, "Privacy Policy", null, Help, CommandKind.Item, false, 0),
        new(CommandId.About, "About md", null, Help, CommandKind.Item, false, 1),

        // ── No menu row (§2.9) ────────────────────────────────────────────────────────────────
        new(CommandId.Escape, "Escape", new Chord(VirtualKeys.Escape), null, CommandKind.Accelerator, true),
    ];

    /// <summary>The rows <c>AcceleratorInstaller</c> registers on the window root — a chord, and not one §2.4 leaves to the focused control.</summary>
    public static readonly IReadOnlyList<CommandSpec> RootAccelerators =
        [.. All.Where(s => s.Chord is not null && s.RootAccelerator)];

    static readonly Dictionary<CommandId, CommandSpec> ById = All.ToDictionary(s => s.Id);

    /// <summary>The row for <paramref name="id"/>. Throws when the table and the enum disagree — a build-time bug, pinned by a test.</summary>
    public static CommandSpec For(CommandId id) =>
        ById.TryGetValue(id, out var spec) ? spec : throw new KeyNotFoundException($"No CommandSpec for {id}");

    static readonly Lazy<IReadOnlyList<MenuNode>> LazyMenus = new(BuildMenus);

    /// <summary>The seven menus in bar order, each a tree of rows, submenus and separators.</summary>
    public static IReadOnlyList<MenuNode> Menus => LazyMenus.Value;

    /// <summary>
    /// The row's text for this snapshot: the table title, except View's full-screen row, which the
    /// Mac's system item spells both ways (§1.3, §2.5).
    /// </summary>
    public static string DisplayTitle(CommandId id, ShellSnapshot snapshot) =>
        id == CommandId.FullScreen && snapshot.IsFullScreen ? ExitFullScreenTitle : For(id).Title;

    /// <summary>
    /// A Go ▸ Contents row: two spaces of indent per heading level below 1, then the heading text —
    /// the Mac's <c>String(repeating: "  ", count: max(0, entry.level - 1)) + entry.text</c> (shell.md §2.4).
    /// </summary>
    public static string ContentsRowTitle(OutlineEntry entry) =>
        new string(' ', 2 * Math.Max(0, entry.Level - 1)) + entry.Text;

    /// <summary>
    /// Where a submenu sits among its siblings: its group, in the parent's numbering. Its *position*
    /// is where its first command appears in <see cref="All"/>; only the divider needs saying here.
    /// </summary>
    static int SubmenuGroup(MenuPath path) => path switch
    {
        _ when path == FileOpenRecent => 0,
        _ when path == FileExamples => 0,
        _ when path == FileShare => 3,
        _ when path == FileExport => 3,
        _ when path == FileExportDiagrams => 1,
        _ when path == FileExportPageSize => 2,
        _ when path == BookExport => 1,
        _ when path == GoContents => 1,
        _ when path == GoNotes => 1,
        _ => 0,
    };

    static IReadOnlyList<MenuNode> BuildMenus() =>
        [.. MenuTitles.Select(title =>
        {
            var path = new MenuPath(title);
            return new MenuNode(title, path, null, Children(path), SeparatorBefore: false);
        })];

    static IReadOnlyList<MenuNode> Children(MenuPath parent)
    {
        // One pass over the table keeps every sibling in declaration order; a submenu takes the
        // place of its first command.
        var slots = new List<(MenuPath Path, CommandSpec? Spec, int Group)>();
        var opened = new HashSet<MenuPath>();
        foreach (var spec in All)
        {
            if (spec.Path is null) continue;
            if (spec.Path == parent)
            {
                slots.Add((parent, spec, spec.Group));
            }
            else if (spec.Path.IsUnder(parent))
            {
                var child = spec.Path.Truncate(parent.Depth + 1);
                if (opened.Add(child)) slots.Add((child, null, SubmenuGroup(child)));
            }
        }

        var nodes = new List<MenuNode>(slots.Count);
        var previousGroup = 0;
        foreach (var (path, spec, group) in slots)
        {
            var separator = nodes.Count > 0 && group != previousGroup;
            nodes.Add(spec is not null
                ? new MenuNode(spec.Title, path, spec, [], separator)
                : new MenuNode(path.Leaf, path, null, Children(path), separator));
            previousGroup = group;
        }
        return nodes;
    }
}
