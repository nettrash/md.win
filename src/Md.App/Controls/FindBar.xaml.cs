// The find bar (shell-design.md §3.5). The search and the replace themselves are WP2's TextSearch
// over the TextBox's own string; this control only collects the query and the replacement and says
// which of the four things to do — so Ctrl+F, F3, Shift+F3, Ctrl+E and Ctrl+H all reach the same
// four events.
using Md.App.Logic;
using Md.App.Logic.Settings;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Md.App.Controls;

/// <summary>
/// Query, replacement, Next, Previous, Replace, Replace All, Done — Enter in the query box is next,
/// Shift+Enter previous; Enter in the replacement box is Replace, Shift+Enter Replace All; Esc
/// closes either and hands focus back. Replace leaves focus in the replacement box, so a writer can
/// press Enter through a document without the next one reaching the editor. Replace All is a
/// one-shot with no run to keep: where focus lands after it is the owner's to decide, and this
/// control does not overrule the answer.
/// </summary>
public sealed partial class FindBar : UserControl
{
    public FindBar()
    {
        InitializeComponent();
        Root.Padding = new Thickness(12, 5, 12, 5);
        Root.ColumnSpacing = 6;

        QueryBox.FontFamily = new FontFamily(PaneTypography.Family);
        QueryBox.FontSize = PaneTypography.SmallEpx;
        QueryBox.PlaceholderText = Strings.Find.QueryPlaceholder;
        ReplaceBox.FontFamily = new FontFamily(PaneTypography.Family);
        ReplaceBox.FontSize = PaneTypography.SmallEpx;
        ReplaceBox.PlaceholderText = Strings.Find.ReplacementPlaceholder;
        NextButton.Content = Strings.Find.Next;
        PreviousButton.Content = Strings.Find.Previous;
        ReplaceButton.Content = Strings.Find.Replace;
        ReplaceAllButton.Content = Strings.Find.ReplaceAll;
        DoneButton.Content = Strings.Find.Done;

        NextButton.Click += (_, _) => Search(forward: true);
        PreviousButton.Click += (_, _) => Search(forward: false);
        ReplaceButton.Click += (_, _) => Replace();
        ReplaceAllButton.Click += (_, _) => ReplaceAll();
        DoneButton.Click += (_, _) => Dismissed?.Invoke();
        QueryBox.KeyDown += OnQueryKeyDown;
        ReplaceBox.KeyDown += OnReplacementKeyDown;
        QueryBox.TextChanged += (_, _) => QueryChanged?.Invoke(QueryBox.Text);

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

    /// <summary>For the Find-command enablement in <c>ShellSnapshot.HasFindQuery</c>.</summary>
    public event Action<string>? QueryChanged;

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

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        QueryBox.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        QueryBox.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
        ReplaceBox.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        ReplaceBox.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
    }

    static bool ShiftIsDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
