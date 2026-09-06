namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IFileWatcher"/> a test drives by hand. Events raised while stopped or disposed are
/// dropped, as the real adapter's would be; a raise is synchronous — in a test the caller is the UI thread.
/// </summary>
public sealed class FakeFileWatcher : IFileWatcher
{
    public string? WatchedPath { get; private set; }
    public bool IsWatching { get; private set; }
    public bool IsDisposed { get; private set; }

    /// <summary>Every path ever passed to <see cref="Watch"/>, in order (re-targets included).</summary>
    public List<string> WatchHistory { get; } = [];

    public event Action<string> Changed = delegate { };
    public event Action<string, string> Renamed = delegate { };
    public event Action<string> Deleted = delegate { };

    public void Watch(string filePath)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        WatchedPath = filePath;
        IsWatching = true;
        WatchHistory.Add(filePath);
    }

    public void Stop() => IsWatching = false;

    public void Dispose()
    {
        IsWatching = false;
        IsDisposed = true;
    }

    /// <summary>The watched file changed; delivered only while watching.</summary>
    public bool RaiseChanged(string? path = null) => Deliver(() => Changed(path ?? WatchedPath!));
    public bool RaiseRenamed(string newPath, string? oldPath = null) => Deliver(() => Renamed(oldPath ?? WatchedPath!, newPath));
    public bool RaiseDeleted(string? path = null) => Deliver(() => Deleted(path ?? WatchedPath!));

    bool Deliver(Action raise)
    {
        if (!IsWatching || IsDisposed) return false;
        raise();
        return true;
    }
}
