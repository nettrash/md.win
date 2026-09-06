// The Application (shell-final.md §1.2): the first activation comes in through the constructor
// (OnLaunched's LaunchActivatedEventArgs is not the real activation), later ones through
// AppInstance.Activated on a background thread. Both go to RouteActivation on the UI thread.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Md.App;

public partial class App : Application
{
    readonly AppActivationArguments _first;
    DispatcherQueue? _dispatcher;

    public App(AppActivationArguments first)
    {
        _first = first;
        InitializeComponent();
        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <summary>The UI thread's queue, for the services that need one before any window exists.</summary>
    public DispatcherQueue Dispatcher => _dispatcher ?? throw new InvalidOperationException("OnLaunched has not run yet.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        AppInstance.GetCurrent().Activated += OnRedirected;
        RouteActivation(_first, first: true);
    }

    // Raised on a background thread; the router runs on the UI thread.
    void OnRedirected(object? sender, AppActivationArguments e) => _dispatcher?.TryEnqueue(() => RouteActivation(e, first: false));

    // ───────────────────────────── WP3 PLACEHOLDER ─────────────────────────────
    // WP3 (Windows & activation) replaces this body with
    //   ActivationRouter.Route(ActivationDescription.From(activation, first)) → WindowManager
    // plus SetForegroundWindow on the target window after a redirect (§1.1.1, §1.2). Until then the
    // skeleton opens nothing; and because a WinUI process with no window would pump forever, the
    // first activation ends the process instead of leaving a ghost md.exe behind.
    void RouteActivation(AppActivationArguments activation, bool first)
    {
        Diagnostics.Write($"activation kind={activation.Kind} first={first}: no window manager yet (WP3)");
        if (first) Exit();
    }
    // ───────────────────────────────────────────────────────────────────────────

    void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e) =>
        Diagnostics.Write($"unhandled (XAML): {e.Message}\n{e.Exception}");

    static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e) =>
        Diagnostics.Write($"unhandled (AppDomain, terminating={e.IsTerminating}): {e.ExceptionObject}");

    /// <summary>
    /// Append-only log in <c>LocalFolder\md.log</c> (§11.2 "UnhandledException logging to LocalFolder").
    /// Without package identity <c>ApplicationData.Current</c> throws, so an unpackaged run without the
    /// WinApp identity falls back to <c>%LOCALAPPDATA%\md</c>. Never throws: a failing logger must not
    /// turn a logged exception into a second one.
    /// </summary>
    static class Diagnostics
    {
        const string FileName = "md.log";

        public static void Write(string message)
        {
            try
            {
                var folder = Folder();
                Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder, FileName), $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
            catch (Exception)
            {
                // Nothing left to log to.
            }
        }

        static string Folder()
        {
            try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch (Exception) { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "md"); }
        }
    }
}
