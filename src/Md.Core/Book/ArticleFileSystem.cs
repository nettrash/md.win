namespace Md.Core.Book;

/// <summary>
/// (modification time, size) of a file — the session's change detector. Size is in
/// the stamp because a FAT time stamp is 2 s coarse and a rewrite inside that window
/// would otherwise pass as unchanged; NTFS and APFS make the date alone enough, but
/// the pair costs nothing.
/// </summary>
public readonly record struct FileStamp(DateTime ModifiedUtc, long Size);

/// <summary>
/// The file operations <see cref="BookArticleSession"/> performs, behind a seam so the
/// state machine is testable against a fake and the app can route through whatever
/// storage API granted the book folder. Read and write throw on failure (the session
/// turns the message into <c>SaveErrorText</c>); <see cref="Stamp"/> returns null for
/// a file that is gone.
/// </summary>
public interface IArticleFileSystem
{
    bool FileExists(string path);
    byte[] ReadAllBytes(string path);
    void WriteAllBytes(string path, byte[] data);
    FileStamp? Stamp(string path);
}

/// <summary><see cref="IArticleFileSystem"/> over System.IO. Writes are plain overwrites, like Swift's <c>data.write(to:)</c>.</summary>
public sealed class LocalArticleFileSystem : IArticleFileSystem
{
    public static LocalArticleFileSystem Instance { get; } = new();

    public bool FileExists(string path) => File.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public FileStamp? Stamp(string path)
    {
        // A fresh FileInfo every time: never a cached attribute set, which is what
        // Swift's attributesOfItem (as opposed to URL.resourceValues) buys it.
        var info = new FileInfo(path);
        if (!info.Exists) return null;
        return new FileStamp(info.LastWriteTimeUtc, info.Length);
    }
}
