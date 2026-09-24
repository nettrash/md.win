// The in-app self-test's view of the Book window (shell-design.md §11.4); #if SELFTEST only, as
// DocumentWindow.SelfTest.cs. The dispatcher and the editor pane are already public (Commands,
// EditorPane); the menu bar is the one part the self-test needs that is not.
#if SELFTEST
using Microsoft.UI.Xaml.Controls;

namespace Md.App;

internal sealed partial class BookWindow
{
    /// <summary>This window's menu bar, as MenuBarBuilder built it.</summary>
    internal MenuBar? SelfTestMenuBar => MenuSlot.Content as MenuBar;
}
#endif
