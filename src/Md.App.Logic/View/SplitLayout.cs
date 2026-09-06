using Md.Core.Document;

namespace Md.App.Logic.View;

/// <summary>How the two panes are laid out in the content row (shell-design.md §5.3).</summary>
public enum PaneLayout
{
    /// <summary>Edit: the preview control is collapsed.</summary>
    EditorOnly,

    /// <summary>Preview: the editor control is collapsed.</summary>
    PreviewOnly,

    /// <summary>Split with room: two equal columns and a 1-epx divider between them.</summary>
    SideBySide,

    /// <summary>Split without room: editor above preview, equal rows, the same divider.</summary>
    Stacked,
}

/// <summary>
/// The Mac's <c>content</c> switch (§5.3, macOS §6.7): the mode picks the layout, and Split picks
/// between side-by-side and stacked on the content width alone. Pure — the code-behind turns the
/// answer into <c>ColumnDefinitions</c> / <c>RowDefinitions</c> and <c>Grid.SetColumn/SetRow</c>.
/// </summary>
public static class SplitLayout
{
    /// <summary>
    /// 640 epx of content width, the Mac's 640 pt threshold kept as a number rather than converted:
    /// SwiftUI's points and XAML's effective pixels are both the DPI-independent unit the layout is
    /// written in, so the pane splits at the same apparent width on both platforms.
    /// </summary>
    public const double SideBySideMinimumWidth = 640;

    /// <param name="contentWidth">
    /// The content row's width in epx. NaN or a negative width (a pass before the first layout)
    /// answers <see cref="PaneLayout.Stacked"/> — every comparison with NaN is false, and stacking
    /// is the layout that survives any width.
    /// </param>
    public static PaneLayout Arrange(ViewMode mode, double contentWidth) => mode switch
    {
        ViewMode.Edit => PaneLayout.EditorOnly,
        ViewMode.Preview => PaneLayout.PreviewOnly,
        ViewMode.Split => contentWidth >= SideBySideMinimumWidth ? PaneLayout.SideBySide : PaneLayout.Stacked,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>The editor control is visible (Preview collapses it).</summary>
    public static bool ShowsEditor(PaneLayout layout) => layout != PaneLayout.PreviewOnly;

    /// <summary>The preview control is visible (Edit collapses it — and a collapsed preview only records "stale", §4.5).</summary>
    public static bool ShowsPreview(PaneLayout layout) => layout != PaneLayout.EditorOnly;

    /// <summary>The 1-epx divider is drawn only between two visible panes.</summary>
    public static bool ShowsDivider(PaneLayout layout) => layout is PaneLayout.SideBySide or PaneLayout.Stacked;
}
