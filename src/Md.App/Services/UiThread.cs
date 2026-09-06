using Md.App.Logic.Seams;
using Microsoft.UI.Dispatching;

namespace Md.App.Services;

/// <summary><see cref="IUiThread"/> over a <see cref="DispatcherQueue"/>: the file watcher and thread-pool results come to the UI thread through here.</summary>
internal sealed class UiThread(DispatcherQueue queue) : IUiThread
{
    public bool IsCurrent => queue.HasThreadAccess;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!queue.TryEnqueue(() => action()))
            throw new InvalidOperationException("The dispatcher queue is shutting down; the action was not posted.");
    }
}
