// The footer (shell-design.md §5.5, §7.1). Hidden in Zen; the numbers arrive from
// DerivedTextScheduler and the ring's timing from Md.App.Logic.Export.BusyRing — neither is decided
// here.
using Md.App.Logic;
using Md.App.Logic.Settings;
using Md.App.Logic.View;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace Md.App.Controls;

/// <summary>One line of secondary ink on secondary paper: "N words · M characters" (U+00B7, no pluralisation).</summary>
public sealed partial class FooterBar : UserControl
{
    public FooterBar()
    {
        InitializeComponent();
        Root.Padding = new Thickness(12, 5, 12, 5);
        Counts.FontFamily = new FontFamily(PaneTypography.Family);
        Counts.FontSize = PaneTypography.FooterEpx;
        // Tabular figures so the numbers do not jitter as they change; it is an attached property —
        // TextBlock has no FontNumeralAlignment of its own.
        Typography.SetNumeralAlignment(Counts, FontNumeralAlignment.Tabular);

        Line.Spacing = 8;
        Busy.Width = BusyRingEpx;
        Busy.Height = BusyRingEpx;
        // A bare spinner says nothing to a screen reader; the counts beside it are not its label.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Busy, Strings.Exports.Working);

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Update(DerivedText.Empty);
    }

    /// <summary>The ring sits on one line with 14.7 epx text, so it is sized to it rather than to the WinUI default (32).</summary>
    public const double BusyRingEpx = 16;

    public void Update(DerivedText derived)
    {
        ArgumentNullException.ThrowIfNull(derived);
        Counts.Text = Strings.Footer(derived.Words, derived.Characters);
    }

    /// <summary>
    /// §7.1's export ring. Hand this to <c>Md.App.Logic.Export.BusyRing</c> — it owns the 500 ms
    /// delay and the UI-thread hop, and this only applies the answer. <c>IsActive</c> is set as well
    /// as <c>Visibility</c>: a collapsed but active <c>ProgressRing</c> keeps its animation running.
    /// </summary>
    public void ShowBusy(bool shown)
    {
        Busy.IsActive = shown;
        Busy.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        Counts.Foreground = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);
    }
}
