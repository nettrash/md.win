using System.Runtime.InteropServices;
using Md.App.Logic.Seams;
using Md.Core.Document;

namespace Md.App.Logic.Documents;

/// <summary>
/// The truest spelling of a path, so two spellings of one file are one identity (§5.2): one entry in
/// <c>md.viewModeMemory</c>, one key in the document registry, one answer to "is this file already
/// open?".
///
/// On Windows the answer comes from the file system itself — open a handle and ask
/// <c>GetFinalPathNameByHandle</c> — because that is the only call that undoes all four ways one
/// file arrives spelled differently: case (<c>c:\docs\a.md</c>), a junction or symlink in the middle
/// of the path, a mapped drive, and an 8.3 short name (<c>C:\PROGRA~1\…</c>), which neither
/// <c>GetFullPath</c> nor <c>ResolveLinkTarget</c> expands. Everywhere else — and for a path that
/// names nothing yet, which has no final name — it falls back to Core's
/// <c>ViewModeMemory.CanonicalPath</c> without case folding: absolute, dot segments collapsed, links
/// resolved. Case is deliberately left alone here; every consumer compares
/// <c>OrdinalIgnoreCase</c> (the registry seam says so), and folding here would only hide which
/// spelling is real.
/// </summary>
public sealed partial class FileIdentity : IFileIdentity
{
    public static FileIdentity Instance { get; } = new();

    public string Canonical(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0) return path;
        if (OperatingSystem.IsWindows() && FinalPath(path) is { } final) return final;
        return Lexical(path);
    }

    /// <summary>The non-Windows / missing-file answer, exposed so a test can pin that both legs agree on a plain path.</summary>
    public static string Lexical(string path)
    {
        try
        {
            return ViewModeMemory.CanonicalPath(path, foldCase: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return path;                       // an unopenable path is still a key; better a stable one than a throw
        }
    }

    // ---- Win32 (never reached off Windows) ----

    const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;   // read | write | delete: never block another writer
    const uint OpenExisting = 3;
    const uint FileFlagBackupSemantics = 0x02000000;                  // required to open a DIRECTORY handle
    const uint VolumeNameDos = 0x0;
    static readonly nint InvalidHandle = -1;

    static unsafe string? FinalPath(string path)
    {
        // dwDesiredAccess = 0: the name is metadata, and asking for read access would fail on a file
        // another process holds exclusively — exactly the file we most need to identify.
        var handle = CreateFileW(path, 0, FileShareAll, 0, OpenExisting, FileFlagBackupSemantics, 0);
        if (handle == InvalidHandle) return null;
        try
        {
            var buffer = new char[1024];
            uint length;
            fixed (char* p = buffer) length = GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, VolumeNameDos);
            if (length == 0) return null;
            if (length > buffer.Length)                                // the return is the required size, NUL included
            {
                buffer = new char[length];
                fixed (char* p = buffer) length = GetFinalPathNameByHandleW(handle, p, (uint)buffer.Length, VolumeNameDos);
                if (length == 0 || length > buffer.Length) return null;
            }
            return StripPrefix(new string(buffer, 0, (int)length));
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// <c>GetFinalPathNameByHandle</c> always answers in the extended form: <c>\\?\C:\Docs\a.md</c>,
    /// or <c>\\?\UNC\server\share\a.md</c> for a network path. Both are stripped back to the spelling
    /// a user or a picker would produce, so the key matches one built from a plain path.
    /// </summary>
    internal static string StripPrefix(string finalPath)
    {
        const string unc = @"\\?\UNC\";
        const string dos = @"\\?\";
        if (finalPath.StartsWith(unc, StringComparison.Ordinal)) return @"\\" + finalPath[unc.Length..];
        if (finalPath.StartsWith(dos, StringComparison.Ordinal)) return finalPath[dos.Length..];
        return finalPath;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint CreateFileW(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    // char*, not ref char / Span<char>: a by-ref char parameter needs runtime marshalling disabled
    // assembly-wide (SYSLIB1051), and this library is shared with code that has no business turning
    // that off. The pointer form is blittable and needs nothing.
    [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
    private static unsafe partial uint GetFinalPathNameByHandleW(nint file, char* filePath, uint charCount, uint flags);

    /// <summary>Non-zero on success; the result is ignored — the handle is closed either way.</summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);
}
