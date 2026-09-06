// The entry point (shell-final.md §1.1). Md.App.csproj defines DISABLE_XAML_GENERATED_MAIN and
// names this class as StartupObject: since Windows App SDK 2.3.1 the switch renames the generated
// Main to XamlGeneratedProgram.XamlGeneratedMain() rather than deleting it, so StartupObject is
// what makes ours the one that runs.
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Md.App;

internal static class Program
{
    /// <summary>The single-instance key: a second process redirects its activation here and exits (§1.1.1).</summary>
    internal const string InstanceKey = "me.nettrash.md";

    [STAThread]
    static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);      // CP1251 for Md.Core.Text.PlainTextCodec

        var current = AppInstance.GetCurrent();
        var activation = current.GetActivatedEventArgs();                    // the real activation; OnLaunched's argument is not
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            Redirection.RedirectAndWait(main, activation);
            return 0;
        }

        Application.Start(_ =>
        {
            // What the XAML-generated Main installs: without it every await after a picker or a
            // WebView2 call would resume on a thread-pool thread.
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(activation);
        });
        return 0;
    }
}
