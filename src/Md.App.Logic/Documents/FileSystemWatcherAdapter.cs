using Md.App.Logic.Seams;

namespace Md.App.Logic.Documents;

/// <summary>
/// <see cref="IFileWatcher"/> over <see cref="FileSystemWatcher"/> (§6.3, §8.5): one watcher on the
/// file's <em>folder</em> with <c>Filter</c> set to the file's name, because a watcher aimed at a
/// file stops seeing it the moment the file is replaced — and replacing is exactly what an editor
/// that writes truncate-then-write does.
///
/// The events are hints, never facts. Our own in-place write raises Changed too, so does an
/// attribute-only touch, and a save from another editor can raise Deleted+Created instead of
/// Changed; the session's (mtime, size) check when the event arrives is the actual guard, which is
/// why Created is reported as a Changed and no event is counted or debounced here. Callbacks arrive
/// on a watcher thread and are marshalled to the UI thread through <see cref="IUiThread"/>, so the
/// session — single-threaded by contract — never sees one from anywhere else.
/// </summary>
public sealed class FileSystemWatcherAdapter : IFileWatcher
{
    readonly IUiThread ui;
    FileSystemWatcher? watcher;
    string? watchedPath;
    bool disposed;

    public FileSystemWatcherAdapter(IUiThread uiThread)
    {
        ArgumentNullException.ThrowIfNull(uiThread);
        ui = uiThread;
    }

    public event Action<string> Changed = delegate { };
    public event Action<string, string> Renamed = delegate { };
    public event Action<string> Deleted = delegate { };

    /// <summary>The file currently watched, or null. For diagnostics and tests; the session keeps its own path.</summary>
    public string? WatchedPath => watchedPath;

    public void Watch(string filePath)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        Stop();
        var directory = FileNames.DirectoryOf(filePath);
        var name = FileNames.NameOf(filePath);
        if (directory.Length == 0 || name.Length == 0) return;
        watchedPath = filePath;
        try
        {
            watcher = new FileSystemWatcher(directory, name)
            {
                // Size as well as LastWrite: a rewrite inside a coarse time stamp's resolution is a
                // change, and it is the pair the session compares.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // No watcher (a provider path with no folder, a removed drive, a full inotify table):
            // the app is not broken, it just stops noticing external edits. The stamp check before
            // every write still refuses to clobber.
            Dispose(watcher);
            watcher = null;
        }
    }

    public void Stop()
    {
        var old = watcher;
        watcher = null;
        watchedPath = null;
        Dispose(old);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        Stop();
    }

    void OnChanged(object sender, FileSystemEventArgs e) => Post(sender, () => Changed(e.FullPath));

    void OnDeleted(object sender, FileSystemEventArgs e) => Post(sender, () => Deleted(e.FullPath));

    void OnRenamed(object sender, RenamedEventArgs e) => Post(sender, () =>
    {
        // The watcher's Filter matches either name, so this fires both for "our file was renamed
        // away" and for "something was renamed onto our name". Only the first re-targets; the second
        // is a replacement of the bytes under us.
        if (string.Equals(FileNames.NameOf(e.OldFullPath), FileNames.NameOf(watchedPath ?? ""), StringComparison.OrdinalIgnoreCase))
            Renamed(e.OldFullPath, e.FullPath);
        else
            Changed(e.FullPath);
    });

    // The internal buffer overflowed: some events were dropped, so report a change and let the
    // stamp decide. Losing the reload is what would look like corruption.
    void OnError(object sender, ErrorEventArgs e) => Post(sender, () =>
    {
        if (watchedPath is { } path) Changed(path);
    });

    void Post(object sender, Action raise)
    {
        // An event from the watcher we have already replaced or stopped is stale by definition.
        if (disposed || !ReferenceEquals(sender, watcher)) return;
        ui.Post(() =>
        {
            if (!disposed && watcher is not null) raise();
        });
    }

    static void Dispose(FileSystemWatcher? w)
    {
        if (w is null) return;
        try
        {
            w.EnableRaisingEvents = false;
        }
        catch (Exception e) when (e is ObjectDisposedException or InvalidOperationException)
        {
            // already gone
        }
        w.Dispose();
    }
}
