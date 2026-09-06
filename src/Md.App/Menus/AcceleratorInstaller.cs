// shell-final.md §2.9: one KeyboardAccelerator per chord on the window's root element, so a chord
// fires while the TextBox or the WebView2 has focus — the Windows answer to the Mac's global menu
// bar, whose key-equivalent dispatch wins over the focused text view.
//
// Two things are deliberately absent. There is no KeyDown re-dispatch (with the accelerator already
// forwarded by the WebView2 that would fire three times), and there is no CoreWebView2Controller
// bridge — the WinUI WebView2 does not expose its controller. The double fire the forwarding causes
// (microsoft-ui-xaml #6231) is absorbed by CommandDispatcher's 150 ms guard instead.
using Md.App.Logic.Commands;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Md.App.Menus;

internal static class AcceleratorInstaller
{
    /// <summary>
    /// Registers every chord of <see cref="CommandTable.RootAccelerators"/> on <paramref name="root"/>
    /// (the window's root <c>Grid</c>). Call once per window, after its content is built.
    ///
    /// A disabled command reports <c>Handled = false</c>, so its chord falls through to whatever has
    /// focus rather than being swallowed — which is what lets Ctrl+Z keep working in the find bar's
    /// own text box while the Edit menu's Undo is greyed out.
    /// </summary>
    public static void Install(UIElement root, CommandDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(dispatcher);

        foreach (var spec in CommandTable.RootAccelerators)
        {
            var id = spec.Id;
            var chord = spec.Chord!.Value;
            var accelerator = new KeyboardAccelerator
            {
                Key = (VirtualKey)chord.VirtualKey,
                Modifiers = Modifiers(chord.Modifiers),
            };
            accelerator.Invoked += (_, args) => args.Handled = dispatcher.TryInvoke(id);
            root.KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>Alt is spelled <c>Menu</c> in WinRT; the library's own flags never mention Windows-key chords because md has none.</summary>
    static VirtualKeyModifiers Modifiers(KeyModifiers modifiers)
    {
        var result = VirtualKeyModifiers.None;
        if (modifiers.HasFlag(KeyModifiers.Ctrl)) result |= VirtualKeyModifiers.Control;
        if (modifiers.HasFlag(KeyModifiers.Alt)) result |= VirtualKeyModifiers.Menu;
        if (modifiers.HasFlag(KeyModifiers.Shift)) result |= VirtualKeyModifiers.Shift;
        return result;
    }
}
