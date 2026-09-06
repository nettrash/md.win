// The content row (shell-design.md §5.3): SplitLayout decides, this applies. Equal halves, a 1-epx
// divider, no user-draggable splitter — the Mac has none.
using Md.App.Logic.Settings;
using Md.App.Logic.View;
using Md.Core.Document;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Controls;

/// <summary>
/// Editor + divider + preview, arranged by <see cref="SplitLayout"/>. Used by the document window
/// and by the Book window's detail pane, which is why the preview is a slot the owner fills rather
/// than a control this one builds.
/// </summary>
public sealed partial class ArticlePanes : UserControl
{
    ViewMode _mode = ViewModes.WindowDefault;
    PaneLayout? _applied;

    public ArticlePanes()
    {
        InitializeComponent();
        Editor = new EditorPane();
        EditorSlot.Content = Editor;

        // The width the rule is asked about is the content row's, so re-arrange on every resize —
        // that is what makes a window dragged narrower stack its panes.
        SizeChanged += (_, _) => Apply();
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Apply();
    }

    public EditorPane Editor { get; }

    /// <summary>The pane link; the preview host registers its half, the editor its own (§3.4).</summary>
    public ScrollSync ScrollSync { get; } = new();

    /// <summary>The arrangement currently applied — for the owner's own layout decisions (the footer stays put; Zen replaces this whole control).</summary>
    public PaneLayout Layout { get; private set; } = PaneLayout.Stacked;

    public event Action<PaneLayout>? LayoutChanged;

    /// <summary>The displayed mode (never the raw preference): the owner passes <c>DocumentWindowState.EffectiveMode</c>.</summary>
    public ViewMode Mode
    {
        get => _mode;
        set
        {
            if (_mode == value) return;
            _mode = value;
            Apply();
        }
    }

    /// <summary>WP5's preview host goes here; null empties the slot (a window that has not built one yet).</summary>
    public void SetPreview(UIElement? preview) => PreviewSlot.Content = preview;

    /// <summary>Hands the editor its half of the scroll link. The preview host registers <c>ScrollSync.ScrollPreview</c> itself.</summary>
    public void AttachScrollSync(ScrollSyncGuard guard) => Editor.Attach(ScrollSync, guard);

    void Apply()
    {
        var layout = SplitLayout.Arrange(_mode, Panes.ActualWidth);
        if (_applied == layout) return;
        _applied = layout;
        Layout = layout;

        Panes.ColumnDefinitions.Clear();
        Panes.RowDefinitions.Clear();
        Grid.SetColumn(EditorSlot, 0);
        Grid.SetColumn(Divider, 0);
        Grid.SetColumn(PreviewSlot, 0);
        Grid.SetRow(EditorSlot, 0);
        Grid.SetRow(Divider, 0);
        Grid.SetRow(PreviewSlot, 0);

        // Edit collapses the preview control and Preview the editor — collapsed, not hidden, so the
        // WebView2 reports IsShown false and the coordinator only records "stale" (§4.5).
        EditorSlot.Visibility = SplitLayout.ShowsEditor(layout) ? Visibility.Visible : Visibility.Collapsed;
        PreviewSlot.Visibility = SplitLayout.ShowsPreview(layout) ? Visibility.Visible : Visibility.Collapsed;
        Divider.Visibility = SplitLayout.ShowsDivider(layout) ? Visibility.Visible : Visibility.Collapsed;

        switch (layout)
        {
            case PaneLayout.SideBySide:
                Panes.ColumnDefinitions.Add(Star());
                Panes.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Panes.ColumnDefinitions.Add(Star());
                Grid.SetColumn(Divider, 1);
                Grid.SetColumn(PreviewSlot, 2);
                Divider.Width = 1;
                Divider.Height = double.NaN;
                break;

            case PaneLayout.Stacked:
                Panes.RowDefinitions.Add(StarRow());
                Panes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Panes.RowDefinitions.Add(StarRow());
                Grid.SetRow(Divider, 1);
                Grid.SetRow(PreviewSlot, 2);
                Divider.Width = double.NaN;
                Divider.Height = 1;
                break;
        }

        LayoutChanged?.Invoke(layout);
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Panes.Background = PaneBrushes.Get("PaperBackgroundBrush", palette.Paper);
        Divider.Background = PaneBrushes.Get("PaperBorderBrush", palette.Border);
    }

    static ColumnDefinition Star() => new() { Width = new GridLength(1, GridUnitType.Star) };

    static RowDefinition StarRow() => new() { Height = new GridLength(1, GridUnitType.Star) };
}
