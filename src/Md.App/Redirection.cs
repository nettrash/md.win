// Hands a second process's activation to the running instance (shell-final.md §1.1.1). Pre-window,
// pre-pump: no XAML exists yet in either process.
using Md.App.Interop;
using Microsoft.Windows.AppLifecycle;

namespace Md.App;

internal static class Redirection
{
    // Contingency, off: the Windows Developer Blog "single-instanced, part 3" wait through
    // ole32!CoWaitForMultipleObjects, for a machine where the semaphore wait deadlocks. A static
    // readonly (not const) so the unused branch compiles without an unreachable-code warning.
    static readonly bool UseCoWait = false;

    /// <summary>
    /// RedirectActivationToAsync must not be awaited on the STA thread; the App SDK instancing
    /// sample redirects on a worker thread and blocks on a semaphore — that is this. The wait is
    /// bounded and the release is in a finally, so a main instance that never answers cannot leave a
    /// second md.exe hanging behind a double-click.
    /// </summary>
    public static void RedirectAndWait(AppInstance target, AppActivationArguments args)
    {
        if (UseCoWait) { RedirectAndCoWait(target, args); return; }

        var done = new SemaphoreSlim(0, 1);
        Task.Run(() =>
        {
            try { target.RedirectActivationToAsync(args).AsTask().Wait(); }
            finally { done.Release(); }
        });
        done.Wait(TimeSpan.FromSeconds(10));
    }

    static void RedirectAndCoWait(AppInstance target, AppActivationArguments args)
    {
        using var done = new ManualResetEvent(false);
        Task.Run(() =>
        {
            try { target.RedirectActivationToAsync(args).AsTask().Wait(); }
            finally { done.Set(); }
        });
        var handles = new[] { done.SafeWaitHandle.DangerousGetHandle() };
        _ = NativeMethods.CoWaitForMultipleObjects(NativeMethods.COWAIT_DEFAULT, 10_000, 1, handles, out _);
    }
}
