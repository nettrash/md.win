// The find panel (shell-design.md §3.5). The search and the replace themselves are WP2's TextSearch
// over the TextBox's own string; this control only collects the query and the replacement and says
// which of the four things to do — so Ctrl+F, F3, Shift+F3, Ctrl+E and Ctrl+H all reach the same
// four events. Every change to the query is reported too, and the owner searches on it: find as you
// type (2026-09-27, which is when the full-width bar became this card).
using Md.App.Logic;
using Md.App.Logic.Settings;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Md.App.Controls;

/// <summary>
/// A card of two rows, as Notepad, Edge and VS Code draw it: the replace-row chevron, the query, the
/// count, Previous, Next and Done; then, when shown, the replacement with Replace and Replace All.
/// Typing in the query searches as you type; Enter in the query box is next, Shift+Enter previous;
/// Enter in the replacement box is Replace, Shift+Enter Replace All; Esc closes either and hands
/// focus back. Replace leaves focus in the replacement box, so a writer can press Enter through a
/// document without the next one reaching the editor. Replace All is a one-shot with no run to keep:
/// where focus lands after it is the owner's to decide, and this control does not overrule the answer.
/// </summary>
public sealed partial class FindBar : UserControl
{
    // Segoe Fluent Icons, which ships with Windows 11 (the default face of a FontIcon): no asset.
    const string ChevronRight = "", ChevronDown = "", Up = "", Down = "", Cancel = "";

    /// <summary>The card's width in effective pixels, before the owner narrows it to fit the editor.</summary>
    public const double PreferredWidth = 460;

    public FindBar()
    {
        InitializeComponent();
        Root.Padding = new Thickness(8, 6, 8, 6);
        Root.ColumnSpacing = 4;
        Root.RowSpacing = 6;
        Root.CornerRadius = new CornerRadius(8);
        Root.BorderThickness = new Thickness(1);

        QueryBox.FontFamily = new FontFamily(PaneTypography.Family);
        QueryBox.FontSize = PaneTypography.SmallEpx;
        QueryBox.PlaceholderText = Strings.Find.QueryPlaceholder;
        ReplaceBox.FontFamily = new FontFamily(PaneTypography.Family);
        ReplaceBox.FontSize = PaneTypography.SmallEpx;
        ReplaceBox.PlaceholderText = Strings.Find.ReplacementPlaceholder;
        ReplaceRow.ColumnSpacing = 4;
        CountText.Margin = new Thickness(6, 0, 4, 0);
        CountText.MinWidth = 64;
        CountText.TextAlignment = TextAlignment.Right;
        // The icon buttons carry the Mac's words as their tooltip and their accessible name.
        Icon(PreviousButton, Up, Strings.Find.Previous);
        Icon(NextButton, Down, Strings.Find.Next);
        Icon(DoneButton, Cancel, Strings.Find.Done);
        Icon(ToggleReplaceButton, ChevronRight, Strings.Find.ToggleReplace);
        ReplaceButton.Content = Strings.Find.Replace;
        ReplaceAllButton.Content = Strings.Find.ReplaceAll;

        NextButton.Click += (_, _) => Search(forward: true);
        PreviousButton.Click += (_, _) => Search(forward: false);
        ReplaceButton.Click += (_, _) => Replace();
        ReplaceAllButton.Click += (_, _) => ReplaceAll();
        DoneButton.Click += (_, _) => Dismissed?.Invoke();
        ToggleReplaceButton.Click += (_, _) => ToggleReplace();
        QueryBox.KeyDown += OnQueryKeyDown;
        ReplaceBox.KeyDown += OnReplacementKeyDown;
        QueryBox.TextChanged += (_, _) => QueryChanged?.Invoke(QueryBox.Text);
        QueryBox.GotFocus += (_, _) => QueryFocused?.Invoke();

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>Args: the query, and true for forwards. Never raised with an empty query.</summary>
    public event Action<string, bool>? SearchRequested;

    /// <summary>Args: the query and the replacement. Replace the hit the editor stands on, then move to the next. Never raised with an empty query.</summary>
    public event Action<string, string>? ReplaceRequested;

    /// <summary>Args: the query and the replacement. Every hit, in one undo step. Never raised with an empty query.</summary>
    public event Action<string, string>? ReplaceAllRequested;

    /// <summary>Done or Esc: the owner hides the bar and focuses the editor again.</summary>
    public event Action? Dismissed;

    /// <summary>Every change to the query: the owner searches as you type, and it feeds the Find-command enablement in <c>ShellSnapshot.HasFindQuery</c>.</summary>
    public event Action<string>? QueryChanged;

    /// <summary>The query box got focus: where the caret stands now is where typing searches from.</summary>
    public event Action? QueryFocused;

    /// <summary>Whether focus is anywhere in the panel — a box or one of its buttons: then a step selects the hit without taking focus away.</summary>
    public bool HasFocusWithin
    {
        get
        {
            if (XamlRoot is null) return false;
            for (var element = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
                if (ReferenceEquals(element, this)) return true;
            return false;
        }
    }

    /// <summary>Whether the replacement row is showing.</summary>
    public bool IsReplaceShown => ReplaceRow.Visibility == Visibility.Visible;

    /// <summary>Ctrl+H shows the replacement row, Ctrl+F hides it; the chevron toggles it.</summary>
    public void ShowReplace(bool show)
    {
        // A row going away with the caret in it would leave focus nowhere — read before collapsing,
        // since WinUI may move focus off a collapsed element at once.
        var hadFocus = !show && ReplaceRow.Visibility == Visibility.Visible
            && (ReplaceBox.FocusState != FocusState.Unfocused || ReplaceButton.FocusState != FocusState.Unfocused || ReplaceAllButton.FocusState != FocusState.Unfocused);
        ReplaceRow.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ((FontIcon)ToggleReplaceButton.Content).Glyph = show ? ChevronDown : ChevronRight;
        if (hadFocus) FocusQuery();
    }

    /// <summary>"3 of 12", "No results", or nothing — <c>Strings.Find.Tally</c>, worked out by the owner.</summary>
    public void SetTally(string tally) => CountText.Text = tally;

    public string Query => QueryBox.Text;

    /// <summary>What goes in place of a hit; the empty string is a deletion, which is a legitimate replace.</summary>
    public string Replacement => ReplaceBox.Text;

    /// <summary>Ctrl+F with a selection, and Ctrl+E (Use Selection for Find).</summary>
    public void SetQuery(string query)
    {
        ArgumentNullException.ThrowIfNull(query);
        QueryBox.Text = query;
        QueryBox.SelectionStart = query.Length;
        QueryBox.SelectionLength = 0;
    }

    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Programmatic);
        QueryBox.SelectAll();
    }

    /// <summary>Ctrl+H (Edit ▸ Replace…): the same bar, with the caret waiting in the replacement box.</summary>
    public void FocusReplacement()
    {
        ShowReplace(true);
        ReplaceBox.Focus(FocusState.Programmatic);
        ReplaceBox.SelectAll();
    }

    /// <summary>F3 / Shift+F3 while the editor has focus: the bar answers with the query it holds.</summary>
    public void Search(bool forward)
    {
        if (QueryBox.Text.Length == 0) return;
        SearchRequested?.Invoke(QueryBox.Text, forward);
    }

    /// <summary>The Replace button and Enter in the replacement box; focus stays here for the next press.</summary>
    public void Replace()
    {
        if (QueryBox.Text.Length == 0) return;
        ReplaceRequested?.Invoke(QueryBox.Text, ReplaceBox.Text);
        ReplaceBox.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// The Replace All button and Shift+Enter in the replacement box. Focus is <b>not</b> pulled
    /// back here afterwards: one press does the whole document, so there is no run of Enters to
    /// preserve, and a focused TextBox would own the Ctrl+Z that follows — Undo is not a root
    /// accelerator (§2.4), so it goes to the focused control, and the writer means the document.
    /// The owner focuses the editor by selecting what was rewritten (§3.5).
    /// </summary>
    public void ReplaceAll()
    {
        if (QueryBox.Text.Length == 0) return;
        ReplaceAllRequested?.Invoke(QueryBox.Text, ReplaceBox.Text);
    }

    void OnQueryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                Search(forward: !ShiftIsDown());
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Dismissed?.Invoke();
                e.Handled = true;
                break;
        }
    }

    void OnReplacementKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter:
                if (ShiftIsDown()) ReplaceAll();
                else Replace();
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Dismissed?.Invoke();
                e.Handled = true;
                break;
        }
    }

    void ToggleReplace()
    {
        ShowReplace(!IsReplaceShown);
        if (IsReplaceShown) FocusReplacement();
    }

    static void Icon(Button button, string glyph, string name)
    {
        button.Content = new FontIcon { Glyph = glyph, FontSize = 12 };
        button.Width = 32;
        button.Height = 32;
        button.Padding = new Thickness(0);
        button.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        ToolTipService.SetToolTip(button, name);
        AutomationProperties.SetName(button, name);
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        Root.BorderBrush = new SolidColorBrush(PaneBrushes.ToColor(palette.Border));
        CountText.Foreground = new SolidColorBrush(PaneBrushes.ToColor(palette.InkSecondary));
        foreach (var button in new[] { PreviousButton, NextButton, DoneButton, ToggleReplaceButton })
            button.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        QueryBox.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        QueryBox.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
        ReplaceBox.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        ReplaceBox.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
    }

    static bool ShiftIsDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
