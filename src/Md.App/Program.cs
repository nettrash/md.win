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
        // Everything below can fail before a single window exists — a missing Windows App SDK
        // runtime for an unpackaged run, a redirect to an instance that is no longer answering, a
        // XAML resource that will not load. WinUI's own handlers are installed in the App
        // constructor, which is inside Application.Start, so before that a throw kills the process
        // with no window, no dialog and nothing written down: exactly "md finishes after launch".
        // The log is the only thing that can tell the next person what happened.
        try
        {
            return Run(args);
        }
        catch (Exception e)
        {
            App.Diagnostics.Write($"startup failed before any window: {e}");
            return 1;
        }
    }

    static int Run(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);      // CP1251 for Md.Core.Text.PlainTextCodec

        var current = AppInstance.GetCurrent();
        var activation = current.GetActivatedEventArgs();                    // the real activation; OnLaunched's argument is not
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        // A self-test run (§11.4) is not a second copy of md asking an existing one to open a file:
        // it has to drive its own WebView2 and exit with its own code, so it never redirects. The
        // property is false in every build that does not carry the self-test.
        if (!main.IsCurrent && !Services.SelfTest.Requested)
        {
            // Worth a line: if the instance we hand this to is not actually showing a window (a
            // crashed or window-less md that still holds the key), every launch after it looks
            // like "md does nothing", and this is the only trace of why.
            App.Diagnostics.Write($"redirecting activation kind={activation.Kind} to the instance holding \"{InstanceKey}\" and exiting");
            Redirection.RedirectAndWait(main, activation);
            return 0;
        }

        // The parameter is named (not `_`): with a lone `_` parameter, `_ = new App(...)` would assign
        // the App to the ApplicationInitializationCallbackParams instead of discarding it (CS0029).
        Application.Start(callbackParams =>
        {
            // What the XAML-generated Main installs: without it every await after a picker or a
            // WebView2 call would resume on a thread-pool thread.
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App(activation);                       // the Application registers itself with XAML
        });
        return 0;
    }
}
