using Microsoft.UI.Dispatching;

namespace Md.App.Services;

/// <summary>
/// Runs work on a <see cref="DispatcherQueue"/>'s thread and hands the caller back a task for it.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of a deliberate asymmetry between the two halves of the app.
/// <c>Md.App.Logic.Export.ExportPipeline</c> is pure logic that must run anywhere a test can put it,
/// so it awaits with <c>ConfigureAwait(false)</c> from end to end — it neither has nor wants a
/// synchronisation context. Every seam it drives, though, is implemented in <c>Md.App</c> by a
/// WinUI control, a WinRT picker or a <c>ContentDialog</c>, and all three have thread affinity.
/// <c>ConfigureAwait(false)</c> resumes on a thread-pool thread the moment an await actually
/// suspends, so the very first genuinely asynchronous step in an export — creating the renderer's
/// <c>CoreWebView2</c> — moves the rest of the flow off the UI thread and every call after it fails
/// with <c>RPC_E_WRONG_THREAD</c>: "The application called an interface that was marshalled for a
/// different thread."
/// </para>
/// <para>
/// The seams are frozen (§13.2) and the pipeline is right to be thread-agnostic, so the marshalling
/// belongs here, at the adapter boundary: an adapter answers from any thread and does its work on
/// the one it belongs to. Found by <c>md.exe --selftest</c> on 2026-09-07 — HTML, EPUB and both PDF
/// exports failed with exactly that message while the checks that drive <c>ExportRenderer</c>
/// directly from the UI thread all passed.
/// </para>
/// <para>
/// A call already on the right thread runs inline and allocates nothing, which is the usual case:
/// the UI is where every export starts.
/// </para>
/// </remarks>
internal static class UiDispatch
{
    public static Task<T> OnAsync<T>(DispatcherQueue queue, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(work);

        if (queue.HasThreadAccess) return work();

        // RunContinuationsAsynchronously: without it the awaiting thread-pool continuation would run
        // inline on the UI thread, which is exactly the thread this call was trying to get off.
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

        // async void by construction — DispatcherQueueHandler returns void — so nothing may escape
        // this lambda. Both the synchronous part of work() and everything it awaits are inside the
        // try, and the result of both is a completed TaskCompletionSource either way.
        if (!queue.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await work());
                }
                catch (Exception e)
                {
                    completion.TrySetException(e);
                }
            }))
        {
            // The window went while the export was in flight. The pipeline already treats this as a
            // failed export rather than a crash, and the token it passes usually cancels first.
            completion.TrySetException(new InvalidOperationException(
                "The dispatcher queue is shutting down; the work was not run."));
        }

        return completion.Task;
    }

    public static async Task OnAsync(DispatcherQueue queue, Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        await OnAsync(queue, async () =>
        {
            await work();
            return true;
        });
    }

    /// <summary>For a member that only touches the control and returns nothing to await.</summary>
    public static Task OnAsync(DispatcherQueue queue, Action work)
    {
        ArgumentNullException.ThrowIfNull(work);
        return OnAsync(queue, () =>
        {
            work();
            return Task.FromResult(true);
        });
    }
}
