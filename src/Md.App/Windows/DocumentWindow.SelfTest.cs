// The in-app self-test's view of a document window (shell-design.md §11.4). Compiled only into the
// -p:SelfTest=true build (#if SELFTEST); a Store build has none of it. Read-only handles on the
// window's OWN parts — the dispatcher its menu rows and chords fire through, its editor, its find bar,
// its menu bar — so the self-test drives exactly what a click or a key would, in this window and no
// other. Nothing here is a second path to a behaviour: every action still goes through the dispatcher
// or the control.
#if SELFTEST
using Md.App.Controls;
using Md.App.Logic.Commands;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App;

internal sealed partial class DocumentWindow
{
    /// <summary>The dispatcher this window's menu rows and accelerators fire through.</summary>
    internal CommandDispatcher SelfTestCommands => _dispatcher;

    /// <summary>The snapshot the menu bar was last refreshed from.</summary>
    internal ShellSnapshot SelfTestSnapshot => _snapshot;

    /// <summary>This window's editor pane.</summary>
    internal EditorPane SelfTestEditor => Panes.Editor;

    /// <summary>This window's find bar.</summary>
    internal FindBar SelfTestFind => Find;

    /// <summary>This window's menu bar, as MenuBarBuilder built it.</summary>
    internal MenuBar? SelfTestMenuBar => MenuSlot.Content as MenuBar;

    /// <summary>The root element, whose theme the self-test flips and whose XamlRoot answers "who has focus".</summary>
    internal FrameworkElement SelfTestRoot => Root;
}
#endif
