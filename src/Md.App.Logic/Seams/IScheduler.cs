namespace Md.App.Logic.Seams;

/// <summary>
/// UI-thread timers. App: <c>DispatcherQueueTimer</c> (<c>DispatcherScheduler</c>); tests:
/// <c>FakeScheduler</c> with manual advance. Everything here runs on the UI thread, so the callers
/// (autosave, re-render debounce, derived text) never lock.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IScheduler
{
    /// <summary>One-shot timer; disposing the handle before it fires cancels it.</summary>
    IDisposable After(TimeSpan delay, Action action);

    /// <summary>Runs <paramref name="action"/> on the next dispatcher turn.</summary>
    void Post(Action action);
}
