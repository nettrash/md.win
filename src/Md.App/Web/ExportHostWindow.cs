// The written-and-off fallback of shell-design.md §7.1. The export renderer normally lives in the
// requesting window's own ExportCanvas; if an off-canvas WebView2 turns out to misbehave on a real
// Windows build (the day-1 check in §13.4, stage 3), this hosts it in a window of its own instead.
//
// The window is SHOWN, with AppWindow.Show(activateWindow: false) — never merely constructed. A
// Window that is never shown has no host-visible XamlRoot, the WinUI control sets its controller
// invisible, and Chromium then throttles the very timers the diagram engines run on: the same
// failure the off-canvas Visibility.Visible rule exists to avoid.
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Md.App.Web;

/// <summary>
/// A borderless, unswitchable window parked off every monitor, holding one <see cref="Canvas"/> that
/// export renderers can be created in. Not used by default; <c>DocumentExports</c> names it as the
/// one-line swap.
/// </summary>
internal sealed class ExportHostWindow : IDisposable
{
    /// <summary>Off every plausible desktop, in physical pixels.</summary>
    public const int ParkedX = -10000;

    public const int ParkedY = -10000;

    readonly Window _window = new();

    public ExportHostWindow()
    {
        _window.Content = Host;
        _window.Title = Logic.Strings.AppName;

        var appWindow = _window.AppWindow;
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        appWindow.ResizeClient(new SizeInt32((int)ExportRenderer.PageWidthCssPx, (int)ExportRenderer.PageHeightCssPx));
        appWindow.Move(new PointInt32(ParkedX, ParkedY));
        appWindow.Show(activateWindow: false);
    }

    /// <summary>Where an <see cref="ExportRenderer"/> is added, exactly as a document window's own canvas is.</summary>
    public Canvas Host { get; } = new();

    public void Dispose() => _window.Close();
}
