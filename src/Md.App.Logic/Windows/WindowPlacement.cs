using System.Globalization;

namespace Md.App.Logic.Windows;

/// <summary>A window's client size in effective pixels — what <c>md.win.windowSize.*</c> holds, spelled <c>"WxH"</c> (§9).</summary>
public readonly record struct WindowSize(int Width, int Height)
{
    /// <summary>The stored spelling: <c>"900x640"</c>, lower-case <c>x</c>, InvariantCulture digits.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}");
}

/// <summary>A window's frame in effective pixels; <see cref="X"/> and <see cref="Y"/> are the client area's top-left in the virtual desktop.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public WindowSize Size => new(Width, Height);
}

/// <summary>
/// Where a new window goes and how big it is (shell-design.md §1.3, §9). Pure integer arithmetic in
/// effective pixels: the App converts to physical pixels with the window's DPI before calling
/// <c>AppWindow.ResizeClient</c>, and hands the work area in as numbers so nothing here needs a
/// <c>DisplayArea</c>.
/// </summary>
public static class WindowPlacement
{
    /// <summary>The document window's default client size (§1.3), and <c>md.win.windowSize.document</c>'s default.</summary>
    public static readonly WindowSize DocumentDefault = new(900, 640);

    /// <summary>The Book window's default client size (§1.3), and <c>md.win.windowSize.book</c>'s default.</summary>
    public static readonly WindowSize BookDefault = new(1000, 700);

    /// <summary>The windowed, non-Zen minimum (<c>OverlappedPresenter.PreferredMinimumWidth/Height</c>, §1.3).</summary>
    public static readonly WindowSize Minimum = new(480, 320);

    /// <summary>Each new window is offset this far right and down from the last active one (§1.3).</summary>
    public const int CascadeStep = 24;

    /// <summary>
    /// Read a stored <c>"WxH"</c>. Anything that is not two positive integers around one <c>x</c>
    /// reads as <paramref name="fallback"/> — a corrupted setting must not make the app windowless.
    /// The result is never smaller than <see cref="Minimum"/>.
    /// </summary>
    public static WindowSize ParseSize(string? stored, WindowSize fallback)
    {
        if (stored is null) return fallback;
        var cut = stored.IndexOf('x', StringComparison.Ordinal);
        if (cut <= 0 || cut == stored.Length - 1) return fallback;
        if (!int.TryParse(stored.AsSpan(0, cut), NumberStyles.None, CultureInfo.InvariantCulture, out var width)) return fallback;
        if (!int.TryParse(stored.AsSpan(cut + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var height)) return fallback;
        if (width <= 0 || height <= 0) return fallback;
        return AtLeastMinimum(new WindowSize(width, height));
    }

    /// <summary>The spelling <see cref="ParseSize"/> reads back.</summary>
    public static string FormatSize(WindowSize size) => size.ToString();

    /// <summary>Neither dimension below <see cref="Minimum"/>.</summary>
    public static WindowSize AtLeastMinimum(WindowSize size) =>
        new(Math.Max(size.Width, Minimum.Width), Math.Max(size.Height, Minimum.Height));

    /// <summary>
    /// Where the next window goes: <see cref="CascadeStep"/> right and down from the last active
    /// window, at <paramref name="size"/>. When that would push the window past the work area's
    /// right or bottom edge the cascade restarts at the base position — the classic wrap, and what
    /// keeps the tenth window on screen. The first window of a run (<paramref name="previous"/> is
    /// null) is centred.
    /// </summary>
    public static WindowRect Cascade(WindowRect? previous, WindowSize size, WindowRect workArea)
    {
        size = Fit(AtLeastMinimum(size), workArea);
        var baseRect = Centred(size, workArea);
        if (previous is not { } last) return Clamp(baseRect, workArea);

        var next = new WindowRect(last.X + CascadeStep, last.Y + CascadeStep, size.Width, size.Height);
        if (next.Right > workArea.Right || next.Bottom > workArea.Bottom) next = baseRect;
        return Clamp(next, workArea);
    }

    /// <summary>Centred in the work area — where the first window of a run lands.</summary>
    public static WindowRect Centred(WindowSize size, WindowRect workArea)
    {
        size = Fit(AtLeastMinimum(size), workArea);
        return new WindowRect(
            workArea.X + ((workArea.Width - size.Width) / 2),
            workArea.Y + ((workArea.Height - size.Height) / 2),
            size.Width,
            size.Height);
    }

    /// <summary>
    /// Bring a frame inside the work area: shrink it to fit first, then slide it in. This is what a
    /// restored <c>session.json</c> placement goes through, so a window saved on a monitor that is
    /// no longer attached — or one that was 4K and is now 1080p — still comes back reachable.
    /// </summary>
    public static WindowRect Clamp(WindowRect rect, WindowRect workArea)
    {
        var size = Fit(new WindowSize(rect.Width, rect.Height), workArea);
        var x = Math.Clamp(rect.X, workArea.X, Math.Max(workArea.X, workArea.Right - size.Width));
        var y = Math.Clamp(rect.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - size.Height));
        return new WindowRect(x, y, size.Width, size.Height);
    }

    /// <summary>No bigger than the work area — a maximised-elsewhere frame must not exceed this screen.</summary>
    static WindowSize Fit(WindowSize size, WindowRect workArea) =>
        new(Math.Min(size.Width, Math.Max(1, workArea.Width)), Math.Min(size.Height, Math.Max(1, workArea.Height)));
}
