// The footer (shell-design.md §5.5). Hidden in Zen; the numbers arrive from DerivedTextScheduler,
// never computed here.
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

        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
        Update(DerivedText.Empty);
    }

    public void Update(DerivedText derived)
    {
        ArgumentNullException.ThrowIfNull(derived);
        Counts.Text = Strings.Footer(derived.Words, derived.Characters);
    }

    void ApplyTheme()
    {
        var palette = Palette.For(ActualTheme == ElementTheme.Dark);
        Root.Background = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        Counts.Foreground = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);
    }
}
