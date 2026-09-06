// The Zen capsule (shell-design.md §5.4): write / read / exit, floating at the top of the column and
// fading 2.5 s after the pointer stops. Built in code — no template and no ThemeResource for
// tools/xamlcheck to miss. The timing and the "is Zen on" state are ZenController's; this is chrome.
using Md.App.Logic;
using Md.App.Logic.Settings;
using Md.App.Logic.View;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Md.App.Controls;

/// <summary>
/// The floating capsule. <see cref="Attach"/> wires it to the window's <see cref="ZenController"/>
/// and <see cref="DocumentWindowState"/>; after that it needs nothing from its owner but pointer
/// movement over the Zen column, which the window forwards to <see cref="Reveal"/>.
/// </summary>
public sealed class ZenControls : UserControl
{
    // Segoe Fluent Icons for the Mac's SF Symbols (§10): square.and.pencil, eye,
    // arrow.down.right.and.arrow.up.left.
    const string WriteGlyph = "\uE70F";
    const string ReadGlyph = "\uE7B3";
    const string ExitGlyph = "\uE73F";

    readonly Border _capsule = new();
    readonly StackPanel _row = new();
    readonly Button _write = new();
    readonly Button _read = new();
    readonly Button _exit = new();
    readonly FontIcon _writeIcon = new() { Glyph = WriteGlyph };
    readonly FontIcon _readIcon = new() { Glyph = ReadGlyph };
    readonly FontIcon _exitIcon = new() { Glyph = ExitGlyph };
    readonly Rectangle _separator = new() { Width = 1, Height = 14 };

    ZenController? _zen;
    DocumentWindowState? _state;

    public ZenControls()
    {
        _row.Orientation = Orientation.Horizontal;
        _row.Spacing = 2;

        Configure(_write, _writeIcon, Strings.Zen.Write, new Thickness(8, 2, 8, 2));
        Configure(_read, _readIcon, Strings.Zen.Read, new Thickness(8, 2, 8, 2));
        Configure(_exit, _exitIcon, Strings.Zen.Exit, new Thickness(6, 2, 6, 2));

        _separator.VerticalAlignment = VerticalAlignment.Center;
        _row.Children.Add(_write);
        _row.Children.Add(_read);
        _row.Children.Add(_separator);
        _row.Children.Add(_exit);

        _capsule.Child = _row;
        _capsule.CornerRadius = new CornerRadius(20);
        _capsule.Padding = new Thickness(6);
        // The Mac's .regularMaterial behind a Capsule.
        _capsule.HorizontalAlignment = HorizontalAlignment.Center;
        _capsule.VerticalAlignment = VerticalAlignment.Top;
        _capsule.Margin = new Thickness(0, 10, 0, 0);
        // The ease the Mac gets from .easeInOut(duration: 0.3); an implicit animation, so setting
        // Opacity from the controller is all the fade needs.
        _capsule.OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(300) };

        Content = _capsule;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Top;
        IsTabStop = false;

        PointerMoved += (_, _) => Reveal();
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>
    /// Wire the capsule to the window. The state is needed because Zen's write/read switch can also
    /// be moved by Ctrl+1/2/3 through <see cref="ViewModeController.Select"/>, and the tint has to
    /// follow either way.
    /// </summary>
    public void Attach(ZenController zen, DocumentWindowState state)
    {
        ArgumentNullException.ThrowIfNull(zen);
        ArgumentNullException.ThrowIfNull(state);
        _zen = zen;
        _state = state;

        _write.Click += (_, _) => zen.SetReading(false);
        _read.Click += (_, _) => zen.SetReading(true);
        _exit.Click += (_, _) => zen.SetActive(false);

        zen.ControlsShownChanged += Show;
        state.Changed += UpdateSelection;

        Show(zen.ControlsShown);
        UpdateSelection();
    }

    /// <summary>Pointer movement anywhere over the Zen column; the window forwards it here.</summary>
    public void Reveal() => _zen?.Reveal();

    void Show(bool shown)
    {
        _capsule.Opacity = shown ? 1 : 0;
        // A faded capsule must not swallow a click meant for the text under it.
        _capsule.IsHitTestVisible = shown;
    }

    void UpdateSelection()
    {
        if (_state is not { } state) return;
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        var accent = PaneBrushes.Get("AccentBrush", palette.Accent);
        var secondary = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);
        _writeIcon.Foreground = state.ZenReading ? secondary : accent;
        _readIcon.Foreground = state.ZenReading ? accent : secondary;
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        var tint = PaneBrushes.ToColor(palette.PaperSecondary);
        _capsule.Background = new AcrylicBrush { TintColor = tint, TintOpacity = 0.8, FallbackColor = tint };
        _separator.Fill = PaneBrushes.Get("PaperBorderBrush", palette.Border);
        _exitIcon.Foreground = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);
        UpdateSelection();
    }

    static void Configure(Button button, FontIcon icon, string tooltip, Thickness padding)
    {
        button.Content = icon;
        button.Padding = padding;
        button.BorderThickness = new Thickness(0);
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        ToolTipService.SetToolTip(button, tooltip);
    }
}
