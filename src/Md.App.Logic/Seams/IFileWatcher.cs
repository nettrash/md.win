namespace Md.App.Logic.Seams;

/// <summary>
/// One file watched for external edits. App: <c>FileSystemWatcherAdapter</c> — a
/// <c>FileSystemWatcher</c> on the folder with <c>Filter</c> = the file name, events delivered
/// through <see cref="IUiThread"/>; tests: <c>FakeFileWatcher</c>. Events are hints only: the
/// stamp check before every write is the actual guard (§8.5).
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IFileWatcher : IDisposable
{
    /// <summary>Watch this file (folder + Filter = file name); replaces any previous target.</summary>
    void Watch(string filePath);

    void Stop();

    /// <summary>Args: the path. Raised on the UI thread.</summary>
    event Action<string> Changed;

    /// <summary>Args: old path, new path.</summary>
    event Action<string, string> Renamed;

    event Action<string> Deleted;
}
