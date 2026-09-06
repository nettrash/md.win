using System.Globalization;
using System.Xml.Linq;
using Md.App.Logic.Settings;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-final.md §10: the paper palette's numbers, the derived opacities, and the single-source
/// rule — App.xaml's theme dictionaries and the MSIX manifest carry the same values as <see cref="Palette"/>.
/// </summary>
public class PaletteTests
{
    [Fact]
    public void Light_and_dark_are_the_stylesheets_paper_ink_and_accent()
    {
        Assert.Equal(new Palette(0xFFF4EFE2, 0xFFEAE2CF, 0xFF2B2620, 0xFF9C6B2E), Palette.Light);
        Assert.Equal(new Palette(0xFF241E18, 0xFF2F2820, 0xFFE7DBC2, 0xFFC99A55), Palette.Dark);
    }

    [Fact]
    public void For_maps_the_theme_and_the_dark_flag()
    {
        Assert.Same(Palette.Light, Palette.For(PaletteTheme.Light));
        Assert.Same(Palette.Dark, Palette.For(PaletteTheme.Dark));
        Assert.Same(Palette.Light, Palette.For(dark: false));
        Assert.Same(Palette.Dark, Palette.For(dark: true));
    }

    [Theory]
    [InlineData(0.6, 0x99)]
    [InlineData(0.4, 0x66)]
    [InlineData(0.16, 0x29)]
    [InlineData(1.0, 0xFF)]
    [InlineData(0.0, 0x00)]
    [InlineData(1.5, 0xFF)]     // clamped
    [InlineData(-1.0, 0x00)]
    public void WithOpacity_rounds_the_alpha_and_keeps_the_rgb(double opacity, int alpha)
    {
        var argb = Palette.WithOpacity(0xFF2B2620, opacity);
        Assert.Equal((uint)alpha, argb >> 24);
        Assert.Equal(0x2B2620u, argb & 0x00FFFFFF);
    }

    [Fact]
    public void Derived_brushes_are_ink_at_60_40_and_16_percent()
    {
        Assert.Equal(0.6, Palette.InkSecondaryOpacity);
        Assert.Equal(0.4, Palette.InkTertiaryOpacity);
        Assert.Equal(0.16, Palette.BorderOpacity);
        Assert.Equal(0x992B2620u, Palette.Light.InkSecondary);
        Assert.Equal(0x662B2620u, Palette.Light.InkTertiary);
        Assert.Equal(0x292B2620u, Palette.Light.Border);
        Assert.Equal(0x99E7DBC2u, Palette.Dark.InkSecondary);
        Assert.Equal(0x66E7DBC2u, Palette.Dark.InkTertiary);
        Assert.Equal(0x29E7DBC2u, Palette.Dark.Border);
    }

    [Fact]
    public void Channels_split_argb_in_order()
    {
        Assert.Equal(((byte)0xFF, (byte)0xF4, (byte)0xEF, (byte)0xE2), Palette.Channels(0xFFF4EFE2));
        Assert.Equal(((byte)0x29, (byte)0x2B, (byte)0x26, (byte)0x20), Palette.Channels(0x292B2620));
    }

    [Fact]
    public void Hex_is_upper_case_rgb_without_alpha()
    {
        Assert.Equal("#F4EFE2", Palette.Hex(Palette.Light.Paper));
        Assert.Equal("#241E18", Palette.Hex(Palette.Dark.Paper));
        Assert.Equal("#2B2620", Palette.Hex(Palette.Light.InkSecondary));
        Assert.Equal("#000000", Palette.Hex(0xFF000000));
        Assert.Equal("#0A0B0C", Palette.Hex(0x000A0B0C));
    }

    // ---- App.xaml (src/Md.App) carries the same numbers; the XAML compiler cannot read Palette, so this test does ----

    static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static XElement ThemeDictionary(string key)
    {
        var doc = XDocument.Load(RepoFiles.At("src", "Md.App", "App.xaml"));
        return doc.Descendants(P + "ResourceDictionary").Single(e => (string?)e.Attribute(X + "Key") == key);
    }

    static IEnumerable<string?> Keys(XElement dictionary) => dictionary.Elements().Select(e => (string?)e.Attribute(X + "Key"));

    static XElement Resource(XElement dictionary, string key) =>
        dictionary.Elements().Single(e => (string?)e.Attribute(X + "Key") == key);

    static string Color(XElement dictionary, string key) =>
        (string?)Resource(dictionary, key).Attribute("Color") ?? throw new Xunit.Sdk.XunitException($"{key} has no Color attribute");

    static double Opacity(XElement dictionary, string key) =>
        double.Parse((string?)Resource(dictionary, key).Attribute("Opacity") ?? "1", CultureInfo.InvariantCulture);

    static readonly string[] PaperKeys =
    [
        "PaperBackgroundColor", "PaperBackgroundBrush", "PaperBackgroundSecondaryBrush", "PaperInkBrush",
        "AccentBrush", "PaperInkSecondaryBrush", "PaperInkTertiaryBrush", "PaperBorderBrush",
    ];

    static readonly string[] TextControlBrushKeys =
    [
        "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBackgroundDisabled",
        "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused", "TextControlBorderBrushDisabled",
    ];

    static readonly string[] TextControlThicknessKeys = ["TextControlBorderThemeThickness", "TextControlBorderThemeThicknessFocused"];

    [Theory]
    [InlineData("Light", false)]
    [InlineData("Default", true)]      // WinUI's "Default" theme dictionary is the dark one
    public void App_xaml_light_and_dark_dictionaries_carry_the_palette(string dictionaryKey, bool dark)
    {
        var p = Palette.For(dark);
        var d = ThemeDictionary(dictionaryKey);
        Assert.Equal(Palette.Hex(p.Paper), Resource(d, "PaperBackgroundColor").Value);
        Assert.Equal(Palette.Hex(p.Paper), Color(d, "PaperBackgroundBrush"));
        Assert.Equal(Palette.Hex(p.PaperSecondary), Color(d, "PaperBackgroundSecondaryBrush"));
        Assert.Equal(Palette.Hex(p.Ink), Color(d, "PaperInkBrush"));
        Assert.Equal(Palette.Hex(p.Accent), Color(d, "AccentBrush"));
        foreach (var (key, opacity) in new[]
        {
            ("PaperInkSecondaryBrush", Palette.InkSecondaryOpacity),
            ("PaperInkTertiaryBrush", Palette.InkTertiaryOpacity),
            ("PaperBorderBrush", Palette.BorderOpacity),
        })
        {
            Assert.Equal(Palette.Hex(p.Ink), Color(d, key));
            Assert.Equal(opacity, Opacity(d, key));
        }
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Default")]
    [InlineData("HighContrast")]
    public void Every_theme_dictionary_defines_every_paper_key_once(string dictionaryKey)
    {
        // A key missing from one dictionary would silently fall back to another theme's colour.
        var keys = Keys(ThemeDictionary(dictionaryKey)).ToList();
        foreach (var key in PaperKeys) Assert.Contains(key, keys);
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Theory]
    [InlineData("Light")]
    [InlineData("Default")]
    public void Light_and_dark_strip_the_TextBox_fill_border_and_focus_underline(string dictionaryKey)
    {
        // §3.1: the editor is bare text on paper — resource keys, not a template.
        var d = ThemeDictionary(dictionaryKey);
        foreach (var key in TextControlBrushKeys) Assert.Equal("Transparent", Color(d, key));
        foreach (var key in TextControlThicknessKeys) Assert.Equal("0", Resource(d, key).Value);
    }

    [Fact]
    public void High_contrast_keeps_the_system_TextBox_and_uses_system_colours()
    {
        var d = ThemeDictionary("HighContrast");
        Assert.DoesNotContain(Keys(d), k => k is not null && k.StartsWith("TextControl", StringComparison.Ordinal));
        foreach (var key in PaperKeys.Where(k => k.EndsWith("Brush", StringComparison.Ordinal)))
            Assert.StartsWith("{ThemeResource SystemColor", Color(d, key), StringComparison.Ordinal);
        // A markup extension is not valid as element text; the Color must come through StaticResource's element form.
        var paper = Resource(d, "PaperBackgroundColor");
        Assert.Equal(P + "StaticResource", paper.Name);
        Assert.StartsWith("SystemColor", (string?)paper.Attribute("ResourceKey"), StringComparison.Ordinal);
    }

    [Fact]
    public void App_xaml_uses_no_bindings()
    {
        var text = File.ReadAllText(RepoFiles.At("src", "Md.App", "App.xaml"));
        Assert.DoesNotContain("{x:Bind", text);
        Assert.DoesNotContain("{Binding", text);
    }

    [Fact]
    public void The_package_manifest_tile_and_splash_use_the_dark_paper()
    {
        var manifest = XDocument.Load(RepoFiles.At("src", "Md.App", "Package.appxmanifest"));
        var colours = manifest.Descendants().Attributes("BackgroundColor").Select(a => a.Value).ToList();
        Assert.NotEmpty(colours);
        Assert.All(colours, c => Assert.Equal(Palette.Hex(Palette.Dark.Paper), c, ignoreCase: true));
    }
}
