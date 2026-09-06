// The Application (shell-final.md §1.2): the first activation comes in through the constructor
// (OnLaunched's LaunchActivatedEventArgs is not the real activation), later ones through
// AppInstance.Activated on a background thread. Both go to RouteActivation on the UI thread.
using Md.App.Logic.Activation;
using Md.App.Logic.Preview;
using Md.App.Logic.Windows;
using Md.App.Services;
using Md.Core.Markdown;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.Storage;
// Aliases, not a `using Windows.ApplicationModel.Activation`: that namespace also declares a
// LaunchActivatedEventArgs, and OnLaunched's parameter is Microsoft.UI.Xaml's.
using IFileActivatedEventArgs = Windows.ApplicationModel.Activation.IFileActivatedEventArgs;
using ILaunchActivatedEventArgs = Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs;
using LogicActivationKind = Md.App.Logic.Activation.ActivationKind;

namespace Md.App;

public partial class App : Application
{
    readonly AppActivationArguments _first;
    DispatcherQueue? _dispatcher;
    AppServices? _services;
    WindowManager? _manager;

    public App(AppActivationArguments first)
    {
        _first = first;
        InitializeComponent();
        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
    }

    /// <summary>
    /// Never the constructor that runs: <c>Program.Main</c> (the <c>StartupObject</c>) constructs the
    /// App with the real activation. It exists because the XAML compiler still emits its own entry
    /// point — DISABLE_XAML_GENERATED_MAIN renames it (Windows App SDK 2.3.1+) rather than deleting
    /// it — and that generated code calls <c>new App()</c>; without this overload the Windows build
    /// fails inside App.g.i.cs, where tools/xamlcheck cannot look. If it ever did run it would read
    /// the same activation Program.Main reads.
    /// </summary>
    public App() : this(AppInstance.GetCurrent().GetActivatedEventArgs()) { }

    /// <summary>The UI thread's queue, for the services that need one before any window exists.</summary>
    public DispatcherQueue Dispatcher => _dispatcher ?? throw new InvalidOperationException("OnLaunched has not run yet.");

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // The one place Md.Core's HTML writer is handed to the pure preview coordinator (§4.3, WP5's
        // note): until it is set, the first Update would throw naming this property rather than
        // previewing something invented.
        DocumentHtml.Default = new CoreDocumentHtml();

        _services = new AppServices(Settings(), LocalFolder());
        _manager = new WindowManager(_services);

        AppInstance.GetCurrent().Activated += OnRedirected;
        RouteActivation(_first, first: true);
    }

    // Raised on a background thread; the router runs on the UI thread.
    void OnRedirected(object? sender, AppActivationArguments e) => _dispatcher?.TryEnqueue(() => RouteActivation(e, first: false));

    /// <summary>
    /// §1.2: describe the activation without WinRT, let <see cref="ActivationRouter"/> decide, and
    /// let the window manager do it. Nothing here judges — every row of the routing table is a test
    /// in Md.App.Logic.Tests, and this method exists only because <c>AppActivationArguments</c>
    /// cannot cross into a library that must build on a Mac.
    /// </summary>
    void RouteActivation(AppActivationArguments activation, bool first)
    {
        if (_manager is not { } manager || _services is not { } services) return;
        try
        {
            // Only a first, plain launch may restore, so only it pays for reading session.json.
            var description = Describe(activation, first, first && HasRestorableSession(services));
            manager.Perform(ActivationRouter.Route(description), redirected: !first);
        }
        catch (Exception e)
        {
            // An activation that throws would leave a window-less process pumping for ever; log it
            // and still give the user something to type in.
            Diagnostics.Write($"activation kind={activation.Kind} first={first} failed: {e}");
            manager.OpenUntitled().Activate();
        }
    }

    /// <summary>
    /// The OS-free description the router reads. Only <c>File</c> carries items; Launch carries its
    /// argument string, and Protocol, StartupTask and anything the platform adds later route as a
    /// launch (§1.2's last row).
    /// </summary>
    static ActivationDescription Describe(AppActivationArguments activation, bool first, bool hasRestorableSession) =>
        activation.Kind switch
        {
            ExtendedActivationKind.File when activation.Data is IFileActivatedEventArgs files =>
                ActivationDescription.ForFiles(files.Files.Select(Item), first, hasRestorableSession),
            ExtendedActivationKind.Launch when activation.Data is ILaunchActivatedEventArgs launch =>
                ActivationDescription.ForLaunch(launch.Arguments, first, hasRestorableSession),
            ExtendedActivationKind.Launch => ActivationDescription.ForLaunch(null, first, hasRestorableSession),
            ExtendedActivationKind.Protocol => ActivationDescription.ForOther(LogicActivationKind.Protocol, first, hasRestorableSession),
            ExtendedActivationKind.StartupTask => ActivationDescription.ForOther(LogicActivationKind.StartupTask, first, hasRestorableSession),
            _ => ActivationDescription.ForOther(LogicActivationKind.Other, first, hasRestorableSession),
        };

    // A .textbundle is a FOLDER whose name has an extension, and only the shell can tell us which
    // an item is — the router cannot infer it from the name (§6.5).
    static ActivationItem Item(IStorageItem item) => new(item.Path ?? "", item is StorageFolder);

    // Asking the disk here is what keeps ActivationRouter pure.
    static bool HasRestorableSession(AppServices services)
    {
        var fileSystem = Md.App.Logic.Documents.SystemIoFileSystem.Instance;
        return SessionStore.Restorable(SessionStore.Load(fileSystem, services.LocalFolder), fileSystem).Windows.Count > 0;
    }

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

        static string Folder() => LocalFolder();
    }

    /// <summary>
    /// <c>ApplicationData.Current.LocalSettings</c> (§9) — or, when the process has no package
    /// identity (an unpackaged debug run without the WinApp CLI's registration), a dictionary that
    /// lasts the run. Settings are then not remembered, which is visible and logged; refusing to
    /// launch at all would be worse, and every other identity-dependent surface here already
    /// degrades the same way (the MRU, the About version).
    /// </summary>
    static Md.App.Logic.Settings.ISettingsStore Settings()
    {
        try
        {
            return new LocalSettingsStore();
        }
        catch (Exception e)
        {
            Diagnostics.Write($"no package identity: settings are not persisted this run ({e.Message})");
            return new Md.App.Logic.Settings.InMemorySettingsStore();
        }
    }

    /// <summary>
    /// <c>LocalFolder</c> — session.json's home and the log's (§1.6, §9). Without package identity
    /// <c>ApplicationData.Current</c> throws, so an unpackaged run without the WinApp identity falls
    /// back to <c>%LOCALAPPDATA%\md</c>, which is created if it is not there.
    /// </summary>
    static string LocalFolder()
    {
        string folder;
        try { folder = ApplicationData.Current.LocalFolder.Path; }
        catch (Exception) { folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "md"); }
        try { Directory.CreateDirectory(folder); }
        catch (Exception) { /* the caller's own write will report it */ }
        return folder;
    }

    /// <summary>
    /// The one adapter between Md.Core's HTML writer and the pure preview coordinator. The
    /// coordinator appends the Windows font style itself (§4.3), so this returns PURE Core HTML —
    /// which is also why the export renderer can share the same call and stay byte-identical.
    /// </summary>
    sealed class CoreDocumentHtml : IDocumentHtml
    {
        public string Document(string source, string title, bool dark) => MarkdownHtml.Document(source, title, dark);
    }
}
