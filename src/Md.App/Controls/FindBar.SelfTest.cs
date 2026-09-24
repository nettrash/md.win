// The in-app self-test's view of the find bar (shell-design.md §11.4); #if SELFTEST only. The query
// already has SetQuery; the replacement field has no setter because nothing but the writer's own
// typing fills it — the self-test fills it the way typing would, through the box.
#if SELFTEST
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Controls;

public sealed partial class FindBar
{
    /// <summary>The query field.</summary>
    internal TextBox SelfTestQueryBox => QueryBox;

    /// <summary>The replacement field.</summary>
    internal TextBox SelfTestReplaceBox => ReplaceBox;

    /// <summary>What the writer would have typed into the replacement field.</summary>
    internal void SelfTestSetReplacement(string replacement)
    {
        ReplaceBox.SelectAll();
        ReplaceBox.SelectedText = replacement;
    }
}
#endif
