using Md.App.Logic.Seams;
using Microsoft.UI.Dispatching;

namespace Md.App.Services;

/// <summary>
/// <see cref="IScheduler"/> and <see cref="IClock"/> over a window's <see cref="DispatcherQueue"/>: one
/// non-repeating <see cref="DispatcherQueueTimer"/> per <see cref="After"/>, disposed = stopped,
/// and <see cref="Post"/> = TryEnqueue. Everything the logic schedules through this runs on the UI
/// thread, so the autosave, the re-render debounce and the derived-text tick never lock.
/// </summary>
internal sealed class DispatcherScheduler(DispatcherQueue queue) : IScheduler, IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public IDisposable After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timer = queue.CreateTimer();
        timer.Interval = delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        timer.IsRepeating = false;
        var handle = new Handle(timer);
        timer.Tick += (t, _) =>
        {
            t.Stop();
            if (!handle.IsCancelled) action();
        };
        timer.Start();
        return handle;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!queue.TryEnqueue(() => action()))
            throw new InvalidOperationException("The dispatcher queue is shutting down; the action was not posted.");
    }

    sealed class Handle(DispatcherQueueTimer timer) : IDisposable
    {
        public bool IsCancelled { get; private set; }

        public void Dispose()
        {
            IsCancelled = true;
            timer.Stop();
        }
    }
}
