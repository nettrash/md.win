using Md.App.Logic.Seams;

namespace Md.App.Logic.Documents;

/// <summary>
/// <see cref="IFileSystem"/> over <c>System.IO</c> (§6.2). The pickers hand out consent as a
/// <c>StorageFile</c>; everything after that is a path, because the close path needs a synchronous
/// flush, the watcher needs a path, and the stamp needs <c>GetLastWriteTimeUtc</c> +
/// <c>FileInfo.Length</c> — none of which the async storage API gives.
/// </summary>
public sealed class SystemIoFileSystem : IFileSystem
{
    public static SystemIoFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <summary>
    /// <c>FileMode.Create</c> over the existing file, never write-then-rename: the file keeps its
    /// identity, its ACLs, its hard links and its cloud-sync placeholder state, and the watcher sees
    /// exactly one Changed that the stamp guard recognises as ours. <c>FileShare.Read</c> so a
    /// preview or an indexer reading along does not turn a save into an error.
    /// </summary>
    public void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes)
    {
        // No FlushToDisk: an autosave every second must not stall on a spinning disk or a synced
        // folder, and the 1 s window is what bounds the loss either way (§6.3).
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.Write(bytes);
    }

    /// <summary>A fresh <see cref="FileInfo"/> every time — never a cached attribute set, which is the whole point of the guard.</summary>
    public FileStamp? Stamp(string path)
    {
        var info = new FileInfo(path);
        return info.Exists ? new FileStamp(info.LastWriteTimeUtc, info.Length) : null;
    }

    public void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to, overwrite: false);
    }

    public void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    public IEnumerable<string> EnumerateEntries(string directory) => Directory.EnumerateFileSystemEntries(directory);
}
