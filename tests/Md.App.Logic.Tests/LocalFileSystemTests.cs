using System.Collections.Concurrent;
using System.Text;
using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>
/// The two seam adapters that touch a real disk (§6.2, §6.3): the in-place write whose stamp the
/// session compares, and the watcher whose events it filters. Against a temp folder, on every OS —
/// what is being pinned is our own code, not the platform's.
/// </summary>
public class LocalFileSystemTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "md-win-fs-" + Guid.NewGuid().ToString("N"));

    public LocalFileSystemTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
        GC.SuppressFinalize(this);
    }

    string At(string name) => Path.Combine(root, name);

    [Fact]
    public void AWriteInPlaceKeepsTheFileAndMovesItsStamp()
    {
        var fs = SystemIoFileSystem.Instance;
        var path = At("a.md");
        fs.WriteAllBytesInPlace(path, "one"u8);
        var first = fs.Stamp(path);
        Assert.NotNull(first);
        Assert.Equal(3, first!.Value.Length);

        // A longer, then a shorter body: the length half of the stamp is what catches a rewrite a
        // coarse time stamp would hide.
        fs.WriteAllBytesInPlace(path, "one two three"u8);
        Assert.Equal(13, fs.Stamp(path)!.Value.Length);
        fs.WriteAllBytesInPlace(path, "hi"u8);
        Assert.Equal("hi", Encoding.UTF8.GetString(fs.ReadAllBytes(path)));
        Assert.Equal(2, fs.Stamp(path)!.Value.Length);             // truncated, not overwritten in place
    }

    [Fact]
    public void StampIsNullForAMissingFileWhichIsWhatMakesAVanishedFileStale()
    {
        Assert.Null(SystemIoFileSystem.Instance.Stamp(At("never.md")));
        Assert.False(SystemIoFileSystem.Instance.FileExists(At("never.md")));
    }

    [Fact]
    public void MoveDeleteAndEnumerateCoverFoldersAsWellAsFiles()
    {
        var fs = SystemIoFileSystem.Instance;
        Directory.CreateDirectory(At("bundle"));
        fs.WriteAllBytesInPlace(At(Path.Combine("bundle", "text.md")), "x"u8);

        fs.Move(At("bundle"), At("moved"));
        Assert.True(fs.DirectoryExists(At("moved")));
        Assert.False(fs.DirectoryExists(At("bundle")));
        Assert.Contains(At(Path.Combine("moved", "text.md")), fs.EnumerateEntries(At("moved")));

        fs.Delete(At("moved"));
        Assert.False(fs.DirectoryExists(At("moved")));
        fs.Delete(At("never.md"));                                 // deleting nothing is not an error
    }

    [Fact]
    public void TheWatcherReportsAnExternalWriteThroughTheUiThread()
    {
        var ui = new RecordingUiThread();
        using var watcher = new FileSystemWatcherAdapter(ui);
        var events = new ConcurrentQueue<string>();
        watcher.Changed += p => events.Enqueue(p);

        var path = At("watched.md");
        File.WriteAllText(path, "before");
        watcher.Watch(path);
        Assert.Equal(path, watcher.WatchedPath);

        File.WriteAllText(path, "after, and longer");

        Assert.True(WaitFor(() => !events.IsEmpty, ui), "no file-change notification arrived in 15 s — the runner has none");
        Assert.True(ui.Posts > 0);                                 // never raised on the watcher's thread
        Assert.Contains(events, p => string.Equals(Path.GetFileName(p), "watched.md", StringComparison.Ordinal));
    }

    [Fact]
    public void AStoppedOrDisposedWatcherIsSilent()
    {
        var ui = new RecordingUiThread();
        var watcher = new FileSystemWatcherAdapter(ui);
        var events = new ConcurrentQueue<string>();
        watcher.Changed += p => events.Enqueue(p);

        var path = At("quiet.md");
        File.WriteAllText(path, "before");
        watcher.Watch(path);
        watcher.Stop();
        Assert.Null(watcher.WatchedPath);

        File.WriteAllText(path, "after");
        Thread.Sleep(250);
        ui.Pump();                                                 // nothing to run, and nothing enqueued
        Assert.True(events.IsEmpty);

        watcher.Dispose();
        watcher.Dispose();                                         // idempotent
        Assert.Throws<ObjectDisposedException>(() => watcher.Watch(path));
    }

    [Fact]
    public void AWatchOnAPathWithNoFolderIsANoOpRatherThanACrash()
    {
        var watcher = new FileSystemWatcherAdapter(new RecordingUiThread());
        watcher.Watch("bare-name.md");                             // no directory part
        watcher.Watch(Path.Combine(root, "missing-folder", "a.md"));
        watcher.Dispose();
    }

    [Fact]
    public void OurOwnWriteEchoesBackThroughTheWatcherAndTheStampFiltersIt()
    {
        // The end-to-end version of the guard: a real in-place write, a real watcher event, and a
        // session that must recognise the echo as its own rather than raise a conflict over its own
        // bytes. Everything here is the shipping adapter except the registry and the scheduler.
        var ui = new RecordingUiThread();
        using var watcher = new FileSystemWatcherAdapter(ui);
        var scheduler = new FakeScheduler();
        using var session = new TextFileSession(SystemIoFileSystem.Instance, watcher, scheduler,
            new FakeDocumentRegistry(), FileIdentity.Instance, SessionRole.Document, Guid.NewGuid());

        var path = At("echo.md");
        File.WriteAllText(path, "# Start\n");
        Assert.True(session.Open(path));

        session.Edit("# Start\nand more, which is longer\n");
        scheduler.Advance(TextFileSession.AutosaveDelay);
        Assert.Equal("# Start\nand more, which is longer\n", File.ReadAllText(path));

        // Wait for the watcher to deliver the echo of that write, then run the handler HERE —
        // on the thread that just finished the autosave, which is where the app runs it and the
        // only place it cannot interleave with the DiskStamp the autosave just set. A fixed sleep
        // in its place is a wall-clock bound that a loaded machine walks straight through.
        Assert.True(WaitFor(() => ui.Posts > 0), "the watcher never reported our own write");
        Assert.True(ui.Pump() > 0, "the echo was posted but never ran");
        Assert.False(session.Conflicted);
        Assert.False(session.HasUnsavedChanges);

        // A real external write, by contrast, is seen.
        session.Edit("# Start\nmine\n");
        File.WriteAllText(path, "somebody else entirely, at another length\n");
        Assert.True(WaitFor(() => session.Conflicted, ui), "the external write was never reported");
        Assert.Equal("# Start\nmine\n", session.Text);
    }

    [Fact]
    public void ACreatedFileIsReportedAsAChangeSoADeleteThenCreateSaveIsNotMissed()
    {
        // Plenty of editors save by writing a temp file and renaming it over the target, and a
        // sync client recreates a file it had removed. Both reach the folder watcher as Created,
        // not Changed — subscribing to Changed alone means a session sits on stale text for ever,
        // because nothing else ever asks the disk again. (The path need not exist when the watch
        // starts: the watcher is on the folder with the name as its filter.)
        var ui = new RecordingUiThread();
        using var watcher = new FileSystemWatcherAdapter(ui);
        var events = new ConcurrentQueue<string>();
        watcher.Changed += p => events.Enqueue(p);

        var path = At("appears-later.md");
        watcher.Watch(path);
        File.WriteAllText(path, "created out of nothing");

        Assert.True(WaitFor(() => events.Any(p => string.Equals(Path.GetFileName(p), "appears-later.md", StringComparison.Ordinal)), ui),
            "the file's creation was never reported as a change");
    }

    [Fact]
    public void AStrangersFileRenamedOntoOurNameIsAChangeAndNeverReTargetsTheSession()
    {
        // The watcher's filter matches either side of a rename, so "somebody moved X onto our
        // name" arrives on the same event as "our file was renamed away". Following the first one
        // would silently point the session — and its next save — at a file the writer never opened.
        var ui = new RecordingUiThread();
        using var watcher = new FileSystemWatcherAdapter(ui);
        var changes = new ConcurrentQueue<string>();
        var renames = new ConcurrentQueue<(string Old, string New)>();
        watcher.Changed += p => changes.Enqueue(p);
        watcher.Renamed += (o, n) => renames.Enqueue((o, n));

        var ours = At("ours.md");
        var stranger = At("stranger.md");
        File.WriteAllText(ours, "ours");
        File.WriteAllText(stranger, "not ours");
        watcher.Watch(ours);

        File.Delete(ours);
        File.Move(stranger, ours);                                 // lands on the watched name

        Assert.True(WaitFor(() => !changes.IsEmpty, ui), "the replacement was never reported at all");
        Assert.Empty(renames);                                     // never "our file moved to X"
        Assert.All(changes, p => Assert.Equal("ours.md", Path.GetFileName(p)));
    }

    /// <summary>
    /// Wait for something a watcher event brings about, pumping the UI thread as we go — the
    /// events arrive on a thread pool thread and the handlers run where <paramref name="ui"/> is
    /// pumped, so a condition that depends on one can only become true if somebody pumps.
    /// </summary>
    static bool WaitFor(Func<bool> condition, RecordingUiThread? ui = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            ui?.Pump();
            if (condition()) return true;
            Thread.Sleep(25);
        }
        ui?.Pump();
        return condition();
    }

    /// <summary>
    /// The UI thread as the seam promises it: <c>Post</c> QUEUES, and the work runs later on the
    /// thread that pumps — which here is the test's own thread, the one driving the session.
    ///
    /// It used to call <c>action()</c> inline, on whatever thread the watcher raised the event on,
    /// and that is not what <c>DispatcherQueue.TryEnqueue</c> does. The difference is not academic:
    /// the echo filter compares the file's stamp against <c>DiskStamp</c>, and the autosave sets
    /// <c>DiskStamp</c> right after it writes. On the UI thread those cannot interleave. Inline,
    /// they can — the watcher's thread read the pre-write stamp mid-autosave and reported the
    /// session's own bytes as somebody else's. It failed three runs out of three on a loaded
    /// machine and passed every time on a quiet one, which is the signature of a wall-clock race
    /// rather than a flake.
    /// </summary>
    sealed class RecordingUiThread : IUiThread
    {
        readonly ConcurrentQueue<Action> pending = new();
        int posts;
        public int Posts => Volatile.Read(ref posts);
        public bool IsCurrent => true;
        public void Post(Action action)
        {
            Interlocked.Increment(ref posts);
            pending.Enqueue(action);
        }

        /// <summary>Run what has been posted, here, now. Returns how many actions ran.</summary>
        public int Pump()
        {
            var ran = 0;
            while (pending.TryDequeue(out var action))
            {
                action();
                ran++;
            }
            return ran;
        }
    }
}
