using System.Text;

namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IFileSystem"/> with Windows path semantics: case-insensitive, either
/// separator, stamps that behave like NTFS — every write gets a strictly later
/// <c>LastWriteUtc</c> (the clock's time, nudged by a tick when the clock has not moved), a
/// rename keeps the stamp, and <see cref="Stamp"/> is null for a missing file. The session's
/// (mtime, size) guard can therefore be exercised exactly: <see cref="WriteExternally"/> is
/// another program's edit, <see cref="Touch"/> an attribute-only touch, <see cref="SetReadOnly"/>
/// the read-only file whose autosave must fail into the InfoBar, <see cref="NextWriteFailure"/>
/// an I/O error for the rescue-copy path. Every write through the seam is logged in <see cref="Writes"/>.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    sealed class Entry
    {
        public byte[] Bytes = [];
        public DateTime LastWriteUtc;
        public bool ReadOnly;
    }

    readonly Dictionary<string, Entry> _files = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase);
    DateTime _lastStamp = DateTime.MinValue;

    public FakeFileSystem() : this(new FakeClock()) { }
    public FakeFileSystem(FakeClock clock) => Clock = clock;

    public FakeClock Clock { get; }

    /// <summary>Paths written through <see cref="WriteAllBytesInPlace"/>, in order (as given, not normalised).</summary>
    public List<string> Writes { get; } = [];

    /// <summary>Thrown once by the next <see cref="WriteAllBytesInPlace"/>, then cleared.</summary>
    public Exception? NextWriteFailure { get; set; }

    // ---- set-up and inspection ----

    public void AddDirectory(string path)
    {
        var key = Key(path);
        for (var d = key; d.Length > 0; d = Parent(d)) if (!_dirs.Add(d)) break;
    }

    public void AddFile(string path, byte[] bytes, DateTime? lastWriteUtc = null)
    {
        var key = Key(path);
        AddDirectory(Parent(key));
        _files[key] = new Entry { Bytes = bytes.ToArray(), LastWriteUtc = lastWriteUtc ?? NextStampTime() };
    }

    public void AddFile(string path, string utf8Text) => AddFile(path, Encoding.UTF8.GetBytes(utf8Text));

    /// <summary>Another program writes the file: new bytes, new mtime, no <see cref="Writes"/> entry.</summary>
    public void WriteExternally(string path, byte[] bytes)
    {
        var e = Existing(path);
        e.Bytes = bytes.ToArray();
        e.LastWriteUtc = NextStampTime();
    }

    public void WriteExternally(string path, string utf8Text) => WriteExternally(path, Encoding.UTF8.GetBytes(utf8Text));

    /// <summary>mtime only — the "attribute-only touch" the stamp guard must still treat as a change.</summary>
    public void Touch(string path) => Existing(path).LastWriteUtc = NextStampTime();

    public void SetReadOnly(string path, bool readOnly = true) => Existing(path).ReadOnly = readOnly;

    public byte[]? Bytes(string path) => _files.TryGetValue(Key(path), out var e) ? e.Bytes.ToArray() : null;
    public string? Text(string path) => Bytes(path) is { } b ? Encoding.UTF8.GetString(b) : null;

    // ---- IFileSystem ----

    public bool FileExists(string path) => _files.ContainsKey(Key(path));

    public bool DirectoryExists(string path) => _dirs.Contains(Key(path));

    public byte[] ReadAllBytes(string path) => Existing(path).Bytes.ToArray();

    public void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes)
    {
        Writes.Add(path);
        if (NextWriteFailure is { } failure) { NextWriteFailure = null; throw failure; }
        var key = Key(path);
        if (!_dirs.Contains(Parent(key))) throw new DirectoryNotFoundException($"Could not find a part of the path '{path}'.");
        if (_files.TryGetValue(key, out var e))
        {
            if (e.ReadOnly) throw new UnauthorizedAccessException($"Access to the path '{path}' is denied.");
        }
        else _files[key] = e = new Entry();
        e.Bytes = bytes.ToArray();
        e.LastWriteUtc = NextStampTime();
    }

    public FileStamp? Stamp(string path) => _files.TryGetValue(Key(path), out var e) ? new FileStamp(e.LastWriteUtc, e.Bytes.LongLength) : null;

    public void Move(string from, string to)
    {
        var src = Key(from);
        var dst = Key(to);
        if (_files.TryGetValue(src, out var file))
        {
            if (_files.ContainsKey(dst)) throw new IOException($"The file '{to}' already exists.");
            if (!_dirs.Contains(Parent(dst))) throw new DirectoryNotFoundException($"Could not find a part of the path '{to}'.");
            _files.Remove(src);
            _files[dst] = file;                       // a rename keeps the stamp, as NTFS does
            return;
        }
        if (_dirs.Contains(src))
        {
            if (_dirs.Contains(dst) || _files.ContainsKey(dst)) throw new IOException($"'{to}' already exists.");
            foreach (var d in _dirs.Where(d => IsUnder(d, src)).ToList()) { _dirs.Remove(d); _dirs.Add(dst + d[src.Length..]); }
            foreach (var (k, v) in _files.Where(kv => IsUnder(kv.Key, src)).ToList()) { _files.Remove(k); _files[dst + k[src.Length..]] = v; }
            AddDirectory(Parent(dst));
            return;
        }
        throw new FileNotFoundException($"Could not find file '{from}'.", from);
    }

    public void Delete(string path)
    {
        var key = Key(path);
        if (_files.Remove(key)) return;
        if (_dirs.Contains(key))
        {
            _dirs.RemoveWhere(d => IsUnder(d, key));
            foreach (var k in _files.Keys.Where(k => IsUnder(k, key)).ToList()) _files.Remove(k);
            return;
        }
        // File.Delete of a missing file is a no-op; mirror that.
    }

    public IEnumerable<string> EnumerateEntries(string directory)
    {
        var key = Key(directory);
        if (!_dirs.Contains(key)) throw new DirectoryNotFoundException($"Could not find a part of the path '{directory}'.");
        return _dirs.Where(d => Parent(d) == key && d != key)
            .Concat(_files.Keys.Where(f => Parent(f) == key))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---- internals ----

    Entry Existing(string path) => _files.TryGetValue(Key(path), out var e) ? e : throw new FileNotFoundException($"Could not find file '{path}'.", path);

    DateTime NextStampTime()
    {
        var t = Clock.Now.UtcDateTime;
        if (t <= _lastStamp) t = _lastStamp.AddTicks(1);
        _lastStamp = t;
        return t;
    }

    /// <summary>Forward slashes, no trailing slash; compared OrdinalIgnoreCase. "C:/Docs/a.md", "/tmp/a.md" and "//server/share/a.md" all work.</summary>
    internal static string Key(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var p = path.Replace('\\', '/');
        while (p.Length > 1 && p.EndsWith('/') && !(p.Length == 3 && p[1] == ':')) p = p[..^1];
        return p;
    }

    static string Parent(string key)
    {
        var cut = key.LastIndexOf('/');
        if (cut < 0) return "";
        if (cut == 0) return "/";
        if (cut == 2 && key[1] == ':') return key[..3];   // "C:/a.md" → "C:/"
        return key[..cut];
    }

    static bool IsUnder(string key, string dir) =>
        key.Equals(dir, StringComparison.OrdinalIgnoreCase) || key.StartsWith(dir.EndsWith('/') ? dir : dir + "/", StringComparison.OrdinalIgnoreCase);
}
