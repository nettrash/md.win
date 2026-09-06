namespace Md.App.Logic.Seams;

/// <summary>
/// The file operations the document session and the book model perform, over paths (the pickers
/// hand out <c>StorageFile</c>s, but everything after consent is <c>System.IO</c> on
/// <c>StorageFile.Path</c> — §6.2). App: <c>SystemIoFileSystem</c>; tests: <c>FakeFileSystem</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    byte[] ReadAllBytes(string path);

    /// <summary>
    /// <c>FileMode.Create</c> over the existing file — never write-then-rename, so the file keeps its
    /// identity, ACLs, hard links and cloud placeholder state, and the watcher sees one Changed.
    /// </summary>
    void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes);

    /// <summary>Null when the file does not exist (a vanished file counts as a stale stamp).</summary>
    FileStamp? Stamp(string path);

    void Move(string from, string to);
    void Delete(string path);
    IEnumerable<string> EnumerateEntries(string directory);
}
