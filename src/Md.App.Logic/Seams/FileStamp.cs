namespace Md.App.Logic.Seams;

/// <summary>
/// The (mtime, size) pair that decides whether a file on disk is still the one we last wrote or
/// read: equal stamps mean "ours", anything else is an external change (§6.3). Both fields are
/// what <c>File.GetLastWriteTimeUtc</c> and <c>FileInfo.Length</c> return; no hashing, so the
/// check before every autosave costs one stat.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public readonly record struct FileStamp(DateTime LastWriteUtc, long Length);
