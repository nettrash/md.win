namespace Md.App.Logic.Seams;

/// <summary>
/// The UI thread as a destination: file-watcher and thread-pool results are marshalled through it.
/// App: <c>DispatcherQueue.TryEnqueue</c> / <c>HasThreadAccess</c>; tests: <c>FakeUiThread</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IUiThread
{
    void Post(Action action);
    bool IsCurrent { get; }
}
