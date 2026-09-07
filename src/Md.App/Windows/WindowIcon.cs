// The window's own icon (the one in the title bar and in Alt+Tab).
//
// <ApplicationIcon> embeds AppIcon.ico in md.exe — and it does: the icon is in the binary, and the
// shell shows it for the file, the Start entry and the taskbar. What it does NOT do is reach the
// title bar. A WinUI 3 Window creates its HWND from a class with no icon of its own and never
// consults the executable's, so Windows falls back to the generic application icon: the little
// blue-and-green window that md wore in every screenshot of this port until 2026-09-07.
//
// AppWindow.SetIcon is the documented fix and the only one — there is no XAML or manifest property
// that does it, and the packaged manifest's Square44x44Logo governs the shell, never the caption.
using Microsoft.UI.Windowing;

namespace Md.App;

internal static class WindowIcon
{
    /// <summary>
    /// Beside md.exe, because <c>Md.App.csproj</c> copies it there as Content — the same reasoning
    /// as the preview's <c>web\</c> and the Examples: <c>AppContext.BaseDirectory</c> is right both
    /// packaged and unpackaged, where <c>Package.Current</c> throws.
    /// </summary>
    static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

    /// <summary>
    /// Give <paramref name="window"/> md's icon. Called once per window, never on a theme change:
    /// the icon does not follow the palette.
    /// </summary>
    public static void Apply(AppWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        try
        {
            if (!File.Exists(IconPath))
            {
                // Losing the icon is cosmetic and must never be fatal, but it is exactly the kind of
                // packaging slip that is invisible in a green build — the same class as the engines
                // reaching neither the output directory nor the MSIX.
                App.Diagnostics.Write($"window icon: {IconPath} is not there");
                return;
            }

            window.SetIcon(IconPath);
        }
        catch (Exception e)
        {
            App.Diagnostics.Write($"window icon could not be set: {e.Message}");
        }
    }
}
