using Md.App.Logic.Seams;

namespace Md.App.Logic.Export;

/// <summary>
/// Every wait the export path takes — the poller's 250 ms, the snapshotter's 300 ms repaint, the
/// canvas contingency's poll in <c>ExportRenderer</c> — expressed through <see cref="IScheduler"/> so
/// a test can advance them instead of sleeping, and so every one of them lands on the UI thread.
/// </summary>
/// <remarks>
/// The completion source is deliberately <b>not</b> <c>RunContinuationsAsynchronously</c>: everything
/// here runs on the UI thread, and an inline continuation is what lets a test drive a whole export
/// synchronously by advancing its fake scheduler. Without a scheduler the wait falls back to
/// <see cref="Task.Delay(TimeSpan, CancellationToken)"/> — correct, but not drivable, so the App
/// always passes one.
/// </remarks>
public static class ExportDelay
{
    public static async Task For(IScheduler? scheduler, TimeSpan delay, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (scheduler is null)
        {
            await Task.Delay(delay, ct).ConfigureAwait(false);
            return;
        }

        var completion = new TaskCompletionSource();
        var timer = scheduler.After(delay, () => completion.TrySetResult());

        // Disposing the handle stops the timer; the registration is disposed on the way out so a
        // long-lived token does not accumulate one per wait.
        using var registration = ct.CanBeCanceled
            ? ct.Register(() => { timer.Dispose(); completion.TrySetCanceled(ct); })
            : default;

        await completion.Task.ConfigureAwait(false);
    }
}
