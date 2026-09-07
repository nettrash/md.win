// The editor (shell-design.md §3): one TextBox on paper, the Tab key WinUI has no property for,
// the template ScrollViewer the scroll sync needs, the deferred caret jump, and the \r adapter that
// keeps the model in LF. Everything decidable without Windows is in Md.App.Logic.View / .Documents;
// this file is the adapter.
using Md.App.Logic;
using Md.App.Logic.Documents;
using Md.App.Logic.Preview;
using Md.App.Logic.Settings;
using Md.App.Logic.View;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Md.App.Controls;

/// <summary>
/// The Georgia-on-paper editor. Owns the <see cref="TextBox"/> and nothing else: the text it hands
/// out and takes in is always LF (§3.2), the caret jump is performed once per id and one dispatcher
/// turn late (§3.3), and the scroll half reports and applies fractions through
/// <see cref="ScrollSync"/> (§3.4).
/// </summary>
public sealed class EditorPane : UserControl
{
    /// <summary>15 pt × 4/3. The Mac's American Typewriter is not on Windows; Georgia is the family's stand-in (§10).</summary>
    public const double FontSizeEpx = 20;

    readonly TextBox _box = new();
    ScrollViewer? _scroller;
    ScrollSync? _sync;
    ScrollSyncGuard? _guard;
    // app-api.md §WP4: the "performed once per id" rule is Md.App.Logic's, not a second copy of it
    // here — a bare `Guid? _lastJumpId` said the same thing in a file no test off Windows can reach.
    readonly EditorJumpTracker _jumps = new();
    bool _replacing;

    public EditorPane()
    {
        _box.AcceptsReturn = true;
        _box.TextWrapping = TextWrapping.Wrap;
        // The only two auto-correct sources WinUI has; Markdown punctuation must stay literal.
        _box.IsSpellCheckEnabled = false;
        _box.IsTextPredictionEnabled = false;
        _box.FontFamily = new FontFamily(PaneTypography.Family);
        _box.FontSize = FontSizeEpx;
        // textContainerInset 16 × 16 with lineFragmentPadding 0.
        _box.Padding = new Thickness(16);
        _box.BorderThickness = new Thickness(0);
        _box.PlaceholderText = Strings.EditorPlaceholder;
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;
        _box.VerticalAlignment = VerticalAlignment.Stretch;
        // Vertical only: a wrapped editor must never scroll sideways.
        ScrollViewer.SetHorizontalScrollBarVisibility(_box, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);

        _box.TextChanged += OnTextChanged;
        _box.KeyDown += OnKeyDown;
        Content = _box;

        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>Every keystroke, already normalised to LF. The owner compares with the session's text before writing (§3.2).</summary>
    public event Action<string>? TextEdited;

    /// <summary>
    /// The <see cref="TextBox"/> itself, for the Edit menu (Undo / Redo / Cut / Copy / Paste /
    /// Select All) and the book pane's <c>ClearUndoRedoHistory()</c> on every article switch. Nothing
    /// outside those commands should reach in here.
    /// </summary>
    public TextBox Control => _box;

    /// <summary>The document text, LF whatever the control reports.</summary>
    public string Text => EditorText.FromTextBox(_box.Text);

    public bool HasSelection => _box.SelectionLength > 0;

    /// <summary>
    /// An external replace — Revert, Reload from Disk, an example, an article switch. Nothing happens
    /// when the text already matches (our own echo), and the selection survives, clamped.
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(Text, text, StringComparison.Ordinal)) return;

        var start = _box.SelectionStart;
        var length = _box.SelectionLength;
        _replacing = true;
        try
        {
            _box.Text = EditorText.ToTextBox(text);
        }
        finally
        {
            _replacing = false;
        }

        (start, length) = EditorText.ClampSelection(start, length, _box.Text.Length);
        _box.Select(start, length);
    }

    /// <summary>
    /// Put the caret at the start of a 0-based parser line and focus the editor. Once per id, and one
    /// dispatcher turn late: a Notes jump may have just switched Preview → Edit, and the pane is not
    /// laid out yet.
    /// </summary>
    public void ApplyJump(EditorJump jump, Action<Guid> onHandled)
    {
        ArgumentNullException.ThrowIfNull(onHandled);
        if (!_jumps.Claim(jump)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            // SelectionStart indexes the string the control reports (with \r), and lines are 1:1
            // between that and the LF model, so the offset is computed over box.Text as it stands.
            var offset = LineOffsets.OffsetOfLine(jump.Line, _box.Text);
            _box.Focus(FocusState.Programmatic);
            _box.Select(offset, 0);
            onHandled(jump.Id);
        });
    }

    /// <summary>Hand the pane its half of the scroll link; re-handed on every rebuild so a recreated pane stays registered.</summary>
    public void Attach(ScrollSync sync, ScrollSyncGuard guard)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(guard);
        _sync = sync;
        _guard = guard;
        sync.ScrollEditor = ApplyFraction;
    }

    /// <summary>The Find bar's hit: select it and give the editor focus so the selection is visible.</summary>
    public void SelectRange(int start, int length)
    {
        var reported = _box.Text.Length;
        start = Math.Clamp(start, 0, reported);
        length = Math.Clamp(length, 0, reported - start);
        _box.Focus(FocusState.Programmatic);
        _box.Select(start, length);
    }

    public void FocusEditor() => _box.Focus(FocusState.Programmatic);

    void ApplyTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var palette = Palette.For(dark);
        // Clear backgrounds: the pane Grid paints paper, and the default template's fill, border and
        // focus underline are already off through the TextControl* resources in App.xaml.
        _box.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _box.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _box.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        _box.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
        _box.SelectionHighlightColor = new SolidColorBrush(PaneBrushes.ToColor(palette.Accent));
    }

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // Our own assignment is not an edit; the Mac's updateNSView guard, from the other side.
        if (_replacing) return;
        TextEdited?.Invoke(Text);
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // There is no AcceptsTab in WinUI (that is WPF): without this, Tab moves focus out.
        if (e.Key != VirtualKey.Tab || ShiftIsDown()) return;
        _box.SelectedText = "\t";
        _box.SelectionStart += 1;
        _box.SelectionLength = 0;
        e.Handled = true;
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroller ??= FindScroller(_box);
        if (_scroller is null) return;
        _scroller.ViewChanged -= OnViewChanged;
        _scroller.ViewChanged += OnViewChanged;
    }

    void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroller is not { } sv || _sync is null) return;
        if (_guard is { IsEcho: true }) return;
        if (sv.ScrollableHeight <= 0) return;
        _sync.EditorDidScroll(sv.VerticalOffset / sv.ScrollableHeight);
    }

    void ApplyFraction(double fraction)
    {
        if (_scroller is not { } sv || _guard is null) return;
        using (_guard.Applying())
        {
            sv.ChangeView(null, ScrollSync.Clamp(fraction) * sv.ScrollableHeight, null, disableAnimation: true);
        }
    }

    /// <summary>
    /// The template part the WinUI TextBox scrolls with. Named lookup first, then the first
    /// ScrollViewer in the subtree — the fallback is what keeps the sync alive if the part is ever
    /// renamed (§12, risk 1).
    /// </summary>
    static ScrollViewer? FindScroller(DependencyObject root) =>
        Descendant(root, sv => sv.Name == "ContentElement") ?? Descendant(root, _ => true);

    static ScrollViewer? Descendant(DependencyObject root, Func<ScrollViewer, bool> match)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv && match(sv)) return sv;
            if (Descendant(child, match) is { } found) return found;
        }
        return null;
    }

    static bool ShiftIsDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}

/// <summary>
/// The paper theme, read from <c>App.xaml</c>'s theme dictionaries at run time with
/// <see cref="Palette"/> as the typed fallback (§10, §11.2): a code-behind lookup, because
/// <c>{ThemeResource}</c> in XAML is the one thing <c>tools/xamlcheck</c> cannot prove.
/// </summary>
/// <remarks>Lives beside the editor because the editor is the first control that needs it; move it to its own file the moment a second package does.</remarks>
internal static class PaneBrushes
{
    /// <summary>
    /// The dictionary first, then <see cref="Palette"/>. The fallback is not a rarely-taken path to
    /// be sorry about: <c>PaletteTests</c> reads App.xaml and fails when its hex values drift from
    /// Palette's, so the two branches paint the same colour by construction. Every caller re-runs
    /// this from <c>ActualThemeChanged</c>, so a switch re-tints on either branch — which is what the
    /// day-1 "no white flash on theme switch" check (§13.4, stage 4) confirms on a real Windows.
    /// </summary>
    public static Brush Get(string key, uint fallbackArgb)
    {
        if (Application.Current?.Resources is { } resources && resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(ToColor(fallbackArgb));
    }

    public static Color ToColor(uint argb)
    {
        var (a, r, g, b) = Palette.Channels(argb);
        return new Color { A = a, R = r, G = g, B = b };
    }
}

/// <summary>
/// The Georgia sizes §10 fixes, in effective pixels (the Mac's points × 4/3). Menus, dialogs and
/// CommandBar labels keep Segoe UI Variable and are not here.
/// </summary>
internal static class PaneTypography
{
    /// <summary>No font is bundled; Georgia is the stand-in for American Typewriter the README states plainly.</summary>
    public const string Family = "Georgia";

    /// <summary>11 pt — the footer.</summary>
    public const double FooterEpx = 14.7;

    /// <summary>13 pt — the find bar and the book sidebar rows.</summary>
    public const double SmallEpx = 17.3;
}
