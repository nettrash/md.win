// The title bar (shell-design.md §1.3, §10). The window keeps the STANDARD system title bar —
// ExtendsContentIntoTitleBar stays false and nothing calls SetTitleBar — and is only tinted paper
// and ink through AppWindowTitleBar's colour properties. That is the whole point: a custom title
// bar needs drag-region arithmetic that only a Windows run can verify, and the Mac's chrome is its
// menu bar, which here is the first content row.
using Md.App.Logic.Settings;
using Microsoft.UI.Windowing;
using Windows.UI;

namespace Md.App;

internal static class TitleBarTint
{
    /// <summary>
    /// Paint <paramref name="window"/>'s caption in the palette. A no-op where the OS does not allow
    /// customisation (<c>AppWindowTitleBar.IsCustomizationSupported()</c>) — the window then keeps
    /// the system's own colours, which is correct, not broken.
    ///
    /// With <c>ExtendsContentIntoTitleBar == false</c> the documented behaviour is that the alpha
    /// channel is ignored, so every colour here is opaque; <see cref="Palette"/>'s ARGB values are
    /// too, and the two derived inks are only used where alpha does not apply.
    /// </summary>
    public static void Apply(AppWindow window, bool dark)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;

        var palette = Palette.For(dark);
        var paper = ToColor(palette.Paper);
        var ink = ToColor(palette.Ink);
        var secondary = ToColor(palette.PaperSecondary);
        var muted = Blend(palette.Ink, palette.Paper, 0.6);

        var bar = window.TitleBar;
        bar.BackgroundColor = paper;
        bar.ForegroundColor = ink;
        bar.InactiveBackgroundColor = paper;
        bar.InactiveForegroundColor = muted;

        bar.ButtonBackgroundColor = paper;
        bar.ButtonForegroundColor = ink;
        bar.ButtonHoverBackgroundColor = secondary;
        bar.ButtonHoverForegroundColor = ink;
        bar.ButtonPressedBackgroundColor = secondary;
        bar.ButtonPressedForegroundColor = ink;
        bar.ButtonInactiveBackgroundColor = paper;
        bar.ButtonInactiveForegroundColor = muted;

        // Keeps the caption glyphs legible in dark mode: the system draws them for the app's mode,
        // not the desktop's, and md follows the system theme without setting RequestedTheme.
        bar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
    }

    /// <summary>
    /// The inactive caption's ink: a real blend towards paper, not ink at 60 % alpha. The docs are
    /// explicit that the alpha channel is ignored here, so a translucent brush would come out fully
    /// opaque ink and the inactive window would look active.
    /// </summary>
    static Color Blend(uint foreground, uint background, double amount)
    {
        var (_, fr, fg, fb) = Palette.Channels(foreground);
        var (_, br, bg, bb) = Palette.Channels(background);
        return Color.FromArgb(255, Mix(fr, br, amount), Mix(fg, bg, amount), Mix(fb, bb, amount));
    }

    static byte Mix(byte foreground, byte background, double amount) =>
        (byte)Math.Round((foreground * amount) + (background * (1 - amount)), MidpointRounding.AwayFromZero);

    static Color ToColor(uint argb)
    {
        var (a, r, g, b) = Palette.Channels(argb);
        return Color.FromArgb(a, r, g, b);
    }
}
