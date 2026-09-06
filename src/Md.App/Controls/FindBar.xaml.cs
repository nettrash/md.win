// The find bar (shell-design.md §3.5). The search itself is WP2's TextSearch over the TextBox's own
// string; this control only collects the query and says which direction — so Ctrl+F, F3, Shift+F3 and
// Ctrl+E all reach the same two events.
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

/// <summary>Query, Next, Previous, Done — Enter is next, Shift+Enter previous, Esc closes and hands focus back.</summary>
public sealed partial class FindBar : UserControl
{
    public FindBar()
    {
        InitializeComponent();
        Root.Padding = new Thickness(12, 5, 12, 5);
        Root.ColumnSpacing = 6;

        QueryBox.FontFamily = new FontFamily(PaneTypography.Family);
        QueryBox.FontSize = PaneTypography.SmallEpx;
        NextButton.Content = Strings.Find.Next;
        PreviousButton.Content = Strings.Find.Previous;
        DoneButton.Content = Strings.Find.Done;

        NextButton.Click += (_, _) => Search(forward: true);
        PreviousButton.Click += (_, _) => Search(forward: false);
        DoneButton.Click += (_, _) => Dismissed?.Invoke();
        QueryBox.KeyDown += OnQueryKeyDown;
        QueryBox.TextChanged += (_, _) => QueryChanged?.Invoke(QueryBox.Text);

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>Args: the query, and true for forwards. Never raised with an empty query.</summary>
    public event Action<string, bool>? SearchRequested;

    /// <summary>Done or Esc: the owner hides the bar and focuses the editor again.</summary>
    public event Action? Dismissed;

    /// <summary>For the Find-command enablement in <c>ShellSnapshot.HasFindQuery</c>.</summary>
    public event Action<string>? QueryChanged;

    public string Query => QueryBox.Text;

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

    /// <summary>F3 / Shift+F3 while the editor has focus: the bar answers with the query it holds.</summary>
    public void Search(bool forward)
    {
        if (QueryBox.Text.Length == 0) return;
        SearchRequested?.Invoke(QueryBox.Text, forward);
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

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        QueryBox.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
    }

    static bool ShiftIsDown() =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
