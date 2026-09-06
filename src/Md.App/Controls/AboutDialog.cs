// Help ▸ About md (shell-final.md §2.8): the icon, "md", the package version, the copyright, and the
// bundled engines. Built in code like every other surface here — no XAML, so nothing for the lint to
// miss. The engine list is the honest half of the box: md ships KaTeX, Mermaid, Graphviz, PlantUML
// and highlight.js inside the package and says so. The phrase "no third-party dependencies" must
// never appear, here or in the Store copy.
using System.Globalization;
using Md.App.Logic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Md.App.Controls;

internal static class AboutDialog
{
    /// <summary>The 256-px app icon, the largest square in Assets\ (§11.2).</summary>
    const string IconUri = "ms-appx:///Assets/Square44x44Logo.targetsize-256.png";

    /// <summary>Shows the dialog over <paramref name="xamlRoot"/>'s window and returns when it is dismissed.</summary>
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        await Create(xamlRoot).ShowAsync();
    }

    /// <summary>The dialog, unshown — separated so a caller can hold it while another one is up.</summary>
    public static ContentDialog Create(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var panel = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri(IconUri)),
            Width = 64,
            Height = 64,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(Line(Strings.AppName, size: 28, center: true));
        panel.Children.Add(Line(VersionText(), center: true));
        panel.Children.Add(Line(Strings.Help.Copyright, center: true));

        var engines = new StackPanel { Spacing = 2, Margin = new Thickness(0, 12, 0, 0) };
        engines.Children.Add(Line(Strings.Help.EnginesHeading));
        foreach (var engine in Strings.Help.Engines) engines.Children.Add(Line(engine));
        panel.Children.Add(engines);

        return new ContentDialog
        {
            XamlRoot = xamlRoot,
            Content = panel,
            CloseButtonText = Strings.Buttons.OK,
            DefaultButton = ContentDialogButton.Close,
        };
    }

    /// <summary>
    /// "Version 1.0.0" from the MSIX identity. An unpackaged run (a developer build, or
    /// <c>--selftest</c>) has no <c>Package.Current</c> and throws, so the assembly's own version
    /// stands in — the same fallback shape <c>App.Diagnostics</c> uses for <c>ApplicationData</c>.
    /// The Store's fourth component is always 0 and is left out.
    /// </summary>
    public static string VersionText()
    {
        try
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return Format(version.Major, version.Minor, version.Build);
        }
        catch (Exception)
        {
            var version = typeof(AboutDialog).Assembly.GetName().Version;
            return version is null ? "Version 1.0.0" : Format(version.Major, version.Minor, version.Build);
        }
    }

    static string Format(int major, int minor, int build) =>
        string.Create(CultureInfo.InvariantCulture, $"Version {major}.{minor}.{build}");

    static TextBlock Line(string text, double size = 0, bool center = false)
    {
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (size > 0) block.FontSize = size;
        if (center) block.HorizontalAlignment = HorizontalAlignment.Center;
        return block;
    }
}
