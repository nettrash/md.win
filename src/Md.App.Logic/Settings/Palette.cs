namespace Md.App.Logic.Settings;

/// <summary>Light or dark — the two palettes; the app maps <c>ElementTheme</c> onto this (HighContrast uses system brushes).</summary>
public enum PaletteTheme
{
    Light,
    Dark,
}

/// <summary>
/// The paper theme's colours (shell-final.md §10) as ARGB, the single source for <c>App.xaml</c>,
/// the WebView2 <c>DefaultBackgroundColor</c>, the title-bar tint and the Zen acrylic tint. The
/// values are the shared stylesheet's: paper <c>#F4EFE2</c> / <c>#241E18</c>, ink <c>#2B2620</c> /
/// <c>#E7DBC2</c>, accent <c>#9C6B2E</c> / <c>#C99A55</c>. The MSIX manifest's tile and splash
/// already use the dark paper. The three derived brushes are ink at 60 %, 40 % and 16 % (the
/// CSS border's <c>rgba(…, 0.16)</c>).
/// </summary>
public sealed record Palette(uint Paper, uint PaperSecondary, uint Ink, uint Accent)
{
    public const double InkSecondaryOpacity = 0.6;
    public const double InkTertiaryOpacity = 0.4;
    public const double BorderOpacity = 0.16;

    public static readonly Palette Light = new(0xFFF4EFE2, 0xFFEAE2CF, 0xFF2B2620, 0xFF9C6B2E);
    public static readonly Palette Dark = new(0xFF241E18, 0xFF2F2820, 0xFFE7DBC2, 0xFFC99A55);

    public static Palette For(PaletteTheme theme) => theme == PaletteTheme.Dark ? Dark : Light;
    public static Palette For(bool dark) => dark ? Dark : Light;

    /// <summary>Footer, sidebar buttons, placeholder titles — ink at 60 %.</summary>
    public uint InkSecondary => WithOpacity(Ink, InkSecondaryOpacity);

    /// <summary>The editor's <c>PlaceholderForeground</c> — ink at 40 %.</summary>
    public uint InkTertiary => WithOpacity(Ink, InkTertiaryOpacity);

    /// <summary>Dividers — ink at 16 %, the stylesheet's border.</summary>
    public uint Border => WithOpacity(Ink, BorderOpacity);

    /// <summary>The same RGB with alpha = round(255 × opacity).</summary>
    public static uint WithOpacity(uint argb, double opacity)
    {
        var alpha = (uint)Math.Round(255 * Math.Clamp(opacity, 0, 1), MidpointRounding.AwayFromZero);
        return (alpha << 24) | (argb & 0x00FFFFFF);
    }

    /// <summary>The four channels, for building a <c>Windows.UI.Color</c> without arithmetic at the call site.</summary>
    public static (byte A, byte R, byte G, byte B) Channels(uint argb) =>
        ((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

    /// <summary><c>#RRGGBB</c>, upper case — the spelling the stylesheet and <c>App.xaml</c> use; alpha is dropped.</summary>
    public static string Hex(uint argb) => $"#{argb & 0x00FFFFFF:X6}";
}
