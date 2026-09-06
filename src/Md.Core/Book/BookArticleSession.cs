using System.Globalization;
using System.Text;

namespace Md.Core.Book;

/// <summary>What the book window's detail pane should show.</summary>
public enum BookStageKind
{
    /// <summary>Nothing selected (or the book is empty).</summary>
    Empty,
    /// <summary>The article is open here, editable.</summary>
    Editing,
    /// <summary>The article is open in its own document window — that window owns the file; the pane shows a handoff notice.</summary>
    Handoff,
    /// <summary>The article could not be read.</summary>
    Unreadable,
}

/// <summary>Swift's <c>BookArticleSession.Stage</c>: the kind plus the standardized path it refers to (null only for Empty).</summary>
public sealed record BookStage(BookStageKind Kind, string? Path)
{
    public static BookStage Empty { get; } = new(BookStageKind.Empty, null);
    public static BookStage Editing(string path) => new(BookStageKind.Editing, path);
    public static BookStage Handoff(string path) => new(BookStageKind.Handoff, path);
    public static BookStage Unreadable(string path) => new(BookStageKind.Unreadable, path);
}

/// <summary>
/// Ask the in-place editor — wherever it lives — to save before the book is read from
/// disk. A book output action posts one; the session flushes in its handler and flips
/// <see cref="Vetoed"/> when the save failed, so the compile stops rather than shipping
/// a stale page. The event is static and synchronous for the same reason Swift used a
/// synchronous notification: the caller reads the files the moment <see cref="Post"/>
/// returns. No book window this launch → no subscriber → nothing unsaved → the gate
/// stays open, which is exactly right.
/// </summary>
public sealed class BookFlushGate
{
    public bool Vetoed { get; set; }

    /// <summary>Raised on the posting thread; subscribers flush synchronously.</summary>
    public static event Action<BookFlushGate>? Requested;

    /// <summary>Post a request and return the gate so the caller can read the verdict.</summary>
    public static BookFlushGate Post()
    {
        var gate = new BookFlushGate();
        Requested?.Invoke(gate);
        return gate;
    }

    /// <summary>Swift's <c>BookOutput.flushEditor()</c>: true when every editor saved (or none exists).</summary>
    public static bool FlushEditor() => !Post().Vetoed;
}

/// <summary>
/// The app's registry of open document windows, keyed by file path — Swift's
/// <c>NSDocumentController.shared.documents</c>. The session steps aside while a
/// document owns the article it is editing, so there is never a second writer.
/// </summary>
public interface IDocumentOwnership
{
    /// <summary>Whether a document window currently owns <paramref name="path"/> (compare with <see cref="BookPaths.Same"/>).</summary>
    bool Owns(string path);

    /// <summary>Bring the owning window to the front.</summary>
    void ShowOwner(string path);
}

/// <summary>The modal "no silent failures" door (<c>BookLibrary.presentError</c>): the app installs one with the book window's root.</summary>
public interface IBookAlerts
{
    void PresentError(string message, string informative);
}

/// <summary>
/// The debounced autosave: <see cref="Schedule"/> arms one call
/// <see cref="BookArticleSession.AutosaveDelay"/> after the last keystroke, replacing
/// any pending one; <see cref="Cancel"/> disarms. The app backs it with a UI-thread
/// timer; tests fire it by hand. Without one the session still saves on every
/// selection change, close and gate — autosave is a convenience, not the guarantee.
/// </summary>
public interface IAutosaveScheduler
{
    void Schedule(Action flush);
    void Cancel();
}

/// <summary>
/// The editing state of the one article open in the book window's detail pane. Port
/// of <c>BookArticleSession</c> (md.macOS BookWorkspace.swift), the part that is a
/// state machine rather than AppKit.
///
/// What survives the port intact, because each is a promise the tests pin: a
/// selection change saves the outgoing article and a failed save aborts the change
/// (the sidebar reverts on false); a write is refused — <c>Conflicted</c> — when the
/// file's (mtime, size) stamp no longer matches the one read, and a vanished file is
/// stale too; a clean flush never touches the file; deselecting fully detaches; Close
/// Book flushes and, if that fails, parks the buffer in a "(rescued)" sibling and says
/// so; a save stays in the encoding the file was read in; the flush gate saves
/// synchronously and vetoes on failure; while a document window owns the article the
/// session hands off and reclaims when it closes.
///
/// What Windows replaces: NSFileCoordinator / NSFilePresenter become a
/// FileSystemWatcher feeding <see cref="FileChanged"/>, <see cref="FileMoved"/> and
/// <see cref="FileDeleted"/> — the stamp check was always the guard against
/// uncoordinated writers, and it is kept byte for byte; NSDocumentController becomes
/// <see cref="IDocumentOwnership"/>; sudden-termination control has no analogue, so
/// the app calls <see cref="TerminateFlush"/> from its window-closing hook; the
/// per-article UndoManager becomes <see cref="UndoGeneration"/>, bumped on every load
/// and detach so the editor resets its stack and Ctrl+Z never crosses articles. The
/// security scope collapses to <see cref="Root"/>. Single-threaded by contract, like
/// the Swift <c>@MainActor</c> class: call it from the UI thread only.
/// </summary>
public sealed class BookArticleSession : IDisposable
{
    /// <summary>How long after the last keystroke the autosave fires.</summary>
    public static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1);

    private static readonly FileStamp Missing = new(DateTime.MinValue, -1);

    private readonly IArticleFileSystem fileSystem;
    private readonly IDocumentOwnership? ownership;
    private readonly IBookAlerts? alerts;
    private readonly IAutosaveScheduler? autosave;
    private FileStamp? diskStamp;
    private bool disposed;

    public BookArticleSession(
        IArticleFileSystem? fileSystem = null,
        IDocumentOwnership? ownership = null,
        IBookAlerts? alerts = null,
        IAutosaveScheduler? autosave = null)
    {
        this.fileSystem = fileSystem ?? LocalArticleFileSystem.Instance;
        this.ownership = ownership;
        this.alerts = alerts;
        this.autosave = autosave;
        BookFlushGate.Requested += OnFlushRequested;
    }

    /// <summary>Raised after any observable change (stage, text, dirty, conflict, error) — coarse, for view refresh.</summary>
    public event Action? Changed;

    public BookStage Stage { get; private set; } = BookStage.Empty;

    /// <summary>The article's text while editing. Views route edits through <see cref="Edit"/>; assigning would bypass dirty tracking.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The file changed on disk under unsaved local edits (or vanished). Autosave is suspended; the footer offers Reload / Keep My Version.</summary>
    public bool Conflicted { get; private set; }

    /// <summary>The last save failure, shown until a save succeeds. The session stays dirty so Retry or the next autosave can heal it.</summary>
    public string? SaveErrorText { get; private set; }

    public bool Dirty { get; private set; }

    /// <summary>The encoding the file was read in — what a save writes back in, unless the text outgrew it.</summary>
    public Encoding Encoding { get; private set; } = ArticleTextCodec.Utf8;

    /// <summary>Bumped on every load and detach: the editor discards its undo stack when it changes.</summary>
    public int UndoGeneration { get; private set; }

    /// <summary>The open book's root (standardized), or null. Relative article paths are computed against it.</summary>
    public string? Root { get; private set; }

    /// <summary>The path being edited in place, when there is one.</summary>
    public string? EditingPath => Stage.Kind == BookStageKind.Editing ? Stage.Path : null;

    /// <summary>The article's display name — titles the preview, print job and share actions. Empty when nothing is selected.</summary>
    public string Title => Stage.Path is { } path ? BookNaming.DisplayName(BookPaths.Name(path)) : "";

    // MARK: Book lifecycle

    /// <summary>Open the book at <paramref name="root"/>, closing any current one first. Swift's <c>openBook(unscopedRoot:)</c> — Windows has no scope to hold.</summary>
    public void OpenBook(string root)
    {
        CloseBook();
        Root = BookPaths.Standardize(root);
        Notify();
    }

    /// <summary>Final flush, then let go. A failed flush here still detaches — the writer closed the book — but is reported, never swallowed.</summary>
    public void CloseBook()
    {
        Detach(reportFailure: true);
        Root = null;
        Notify();
    }

    // MARK: Selection

    /// <summary>
    /// Move to <paramref name="path"/> (null deselects): flush the current article, then
    /// load the new one or step aside if a document window owns it. Returns false —
    /// leaving the current article in place — when the flush fails; the caller reverts
    /// its selection so no unsaved text is ever abandoned silently.
    /// </summary>
    public bool Select(string? path)
    {
        var target = path is null ? null : BookPaths.Standardize(path);
        if (Stage.Kind == BookStageKind.Editing && target is not null
            && string.Equals(Stage.Path, target, BookPaths.Comparison))
            return true;
        if (!FlushNow()) return false;
        Detach(reportFailure: false);
        if (target is null)
        {
            Notify();
            return true;
        }
        if (ownership?.Owns(target) == true) Stage = BookStage.Handoff(target);
        else Load(target);
        Notify();
        return true;
    }

    /// <summary>
    /// Detach unconditionally: the flush is attempted, and a failure — the write
    /// refused, or an unresolved conflict — parks the buffer in a rescue copy and says
    /// so (when <paramref name="reportFailure"/>), instead of discarding keystrokes
    /// behind an alert with no way out. Always completes.
    /// </summary>
    private void Detach(bool reportFailure)
    {
        if (!FlushNow() && reportFailure && Stage.Kind == BookStageKind.Editing && Dirty)
        {
            var path = Stage.Path!;
            var name = BookNaming.DisplayName(BookPaths.Name(path));
            var rescued = WriteRescueCopy(path);
            if (rescued is not null)
            {
                alerts?.PresentError(
                    "Could not save “" + name + "”",
                    "Your text was kept as “" + BookPaths.Name(rescued) + "” in the same folder.");
            }
            else
            {
                alerts?.PresentError(
                    "Could not save “" + name + "”",
                    SaveErrorText ?? "The article could not be written.");
            }
        }
        autosave?.Cancel();
        UndoGeneration++;
        Text = "";
        Stage = BookStage.Empty;
        Conflicted = false;
        SaveErrorText = null;
        diskStamp = null;
        SetDirty(false);
    }

    /// <summary>Read <paramref name="path"/> into the session. A read or decode failure leaves only the stage changed, to Unreadable.</summary>
    private void Load(string path)
    {
        byte[] data;
        try
        {
            data = fileSystem.ReadAllBytes(path);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            Stage = BookStage.Unreadable(path);
            return;
        }
        var decoded = ArticleTextCodec.Decode(data);
        if (decoded is null)
        {
            Stage = BookStage.Unreadable(path);
            return;
        }
        Text = decoded.Value.Text;
        Encoding = decoded.Value.Encoding;
        diskStamp = fileSystem.Stamp(path);
        UndoGeneration++;
        Stage = BookStage.Editing(path);
        Conflicted = false;
        SaveErrorText = null;
        SetDirty(false);
    }

    // MARK: Editing

    /// <summary>
    /// The one write path for keystrokes: mark dirty and re-arm the debounced autosave.
    /// While conflicted the autosave stays suspended — the writer must first choose
    /// Reload or Keep My Version.
    /// </summary>
    public void Edit(string newText)
    {
        if (Stage.Kind != BookStageKind.Editing || string.Equals(Text, newText, StringComparison.Ordinal)) return;
        Text = newText;
        SetDirty(true);
        if (!Conflicted) ScheduleAutosave();
        Notify();
    }

    private void ScheduleAutosave()
    {
        if (autosave is null) return;
        autosave.Cancel();
        autosave.Schedule(() => FlushNow());
    }

    private void SetDirty(bool value) => Dirty = value;

    // MARK: Saving

    /// <summary>
    /// Write the buffer to disk now, if there is anything unsaved. Bails out — flagging
    /// <see cref="Conflicted"/> — when the file on disk is no longer the one that was
    /// read. Returns true when nothing needed saving or the write landed.
    /// </summary>
    public bool FlushNow()
    {
        if (Stage.Kind != BookStageKind.Editing || !Dirty) return true;
        var path = Stage.Path!;
        // A document window may have opened this article mid-session. Save our text
        // first — an unedited document re-reads the file — then step aside.
        var owned = ownership?.Owns(path) == true;
        autosave?.Cancel();

        var (data, usedEncoding) = ArticleTextCodec.Encode(Text, Encoding);
        if (diskStamp is { } expected && (fileSystem.Stamp(path) ?? Missing) != expected)
        {
            Conflicted = true;
            Notify();
            return false;
        }
        try
        {
            fileSystem.WriteAllBytes(path, data);
            diskStamp = fileSystem.Stamp(path);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            SaveErrorText = e.Message.Length == 0 ? "The article could not be written." : e.Message;
            Notify();
            return false;
        }
        Encoding = usedEncoding;
        SaveErrorText = null;
        SetDirty(false);
        if (owned) Stage = BookStage.Handoff(path);
        Notify();
        return true;
    }

    /// <summary>
    /// The exit-time flush (the app's window-closing hook): when the regular write is
    /// refused right as the app exits — conflicted, file vanished, disk full — the
    /// keystrokes go to a rescue copy next to the article rather than nowhere.
    /// </summary>
    public void TerminateFlush()
    {
        if (!FlushNow() && Stage.Kind == BookStageKind.Editing && Dirty) WriteRescueCopy(Stage.Path!);
    }

    /// <summary>
    /// Last-resort save: the buffer goes to a fresh "&lt;name&gt; (rescued).md" sibling —
    /// "(rescued 2)", "(rescued 3)", … while the name is taken, never "(rescued 1)" and
    /// never overwriting anything. Returns the rescue path, null only when even that
    /// write failed or a hundred names were taken. A rescue is a copy, not a save: the
    /// buffer stays dirty.
    /// </summary>
    public string? WriteRescueCopy(string path)
    {
        var folder = System.IO.Path.GetDirectoryName(path) ?? "";
        var file = BookPaths.Name(path);
        var dot = file.LastIndexOf('.');
        string stem, ext;
        if (dot > 0 && dot < file.Length - 1)
        {
            stem = file[..dot];
            ext = file[(dot + 1)..];
        }
        else
        {
            stem = file;
            ext = "md";
        }
        var (data, _) = ArticleTextCodec.Encode(Text, Encoding);
        for (var attempt = 1; attempt <= 100; attempt++)
        {
            var name = attempt == 1
                ? stem + " (rescued)"
                : stem + " (rescued " + attempt.ToString(CultureInfo.InvariantCulture) + ")";
            var candidate = System.IO.Path.Combine(folder, name + "." + ext);
            if (fileSystem.FileExists(candidate)) continue;
            try
            {
                fileSystem.WriteAllBytes(candidate, data);
                return candidate;
            }
            catch (Exception e) when (IsFileFailure(e))
            {
                return null;
            }
        }
        return null;
    }

    // MARK: Conflicts

    /// <summary>"Keep My Version": write the buffer out unconditionally — no stamp check; the point is to overwrite whatever is on disk (or recreate a deleted file).</summary>
    public void ResolveConflictKeepingMine()
    {
        if (Stage.Kind != BookStageKind.Editing) return;
        Conflicted = false;
        var path = Stage.Path!;
        var (data, usedEncoding) = ArticleTextCodec.Encode(Text, Encoding);
        try
        {
            fileSystem.WriteAllBytes(path, data);
            Encoding = usedEncoding;
            diskStamp = fileSystem.Stamp(path);
            SaveErrorText = null;
            SetDirty(false);
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            SaveErrorText = e.Message;
        }
        Notify();
    }

    /// <summary>"Reload from Disk": discard the buffer. Clearing dirty <em>first</em> is what makes the detach's flush a no-op — writing the buffer is exactly what must not happen here.</summary>
    public void ResolveConflictReloading()
    {
        if (Stage.Kind != BookStageKind.Editing) return;
        var path = Stage.Path!;
        SetDirty(false);
        Detach(reportFailure: false);
        Load(path);
        Notify();
    }

    // MARK: Ownership (document windows)

    /// <summary>
    /// Re-evaluate who owns the selected article (the app calls this when any window
    /// is activated). Editing + a document appeared → save and hand off; handoff + the
    /// document closed → reclaim. A failed flush stays put: the buffer must remain
    /// visible with its error, not vanish behind the handoff pane.
    /// </summary>
    public void RecheckOwnership()
    {
        switch (Stage.Kind)
        {
            case BookStageKind.Editing:
            {
                var path = Stage.Path!;
                if (ownership?.Owns(path) == true && FlushNow() && Stage.Kind == BookStageKind.Editing)
                    Stage = BookStage.Handoff(path);
                break;
            }
            case BookStageKind.Handoff:
            {
                var path = Stage.Path!;
                if (ownership?.Owns(path) != true)
                {
                    Detach(reportFailure: false);
                    Load(path);
                }
                break;
            }
        }
        Notify();
    }

    /// <summary>The handoff pane's button: bring the owning document window to the front.</summary>
    public void ShowOwningWindow()
    {
        if (Stage.Kind == BookStageKind.Handoff) ownership?.ShowOwner(Stage.Path!);
    }

    /// <summary>
    /// The workspace is about to open the current article in a separate window: save,
    /// then step into handoff <em>before</em> the document opens so there is never a
    /// moment with two writers. False when the save failed (the open is abandoned).
    /// </summary>
    public bool HandOffForExternalOpen()
    {
        if (Stage.Kind != BookStageKind.Editing) return true;
        var path = Stage.Path!;
        if (!FlushNow()) return false;
        Detach(reportFailure: false);
        Stage = BookStage.Handoff(path);
        Notify();
        return true;
    }

    // MARK: File watcher events (the app marshals these to the UI thread)

    /// <summary>
    /// Something wrote the file. Our own writes fire this too, as do attribute-only
    /// changes; only a moved stamp is a real change — a conflict banner over identical
    /// bytes would invite a pointless, text-discarding Reload. Clean session: follow
    /// the disk silently. Dirty: keep both versions (theirs on disk, ours in memory),
    /// suspend autosave, let the writer choose.
    /// </summary>
    public void FileChanged()
    {
        if (Stage.Kind != BookStageKind.Editing) return;
        var path = Stage.Path!;
        if (fileSystem.Stamp(path) is { } current && diskStamp is { } expected && current == expected) return;
        if (Dirty)
        {
            Conflicted = true;
            autosave?.Cancel();
        }
        else
        {
            try
            {
                var decoded = ArticleTextCodec.Decode(fileSystem.ReadAllBytes(path));
                if (decoded is { } fresh)
                {
                    Text = fresh.Text;
                    Encoding = fresh.Encoding;
                    diskStamp = fileSystem.Stamp(path);
                }
            }
            catch (Exception e) when (IsFileFailure(e))
            {
                // Unreadable right now — keep what we have; the next event or save decides.
            }
        }
        Notify();
    }

    /// <summary>The file was renamed or moved under us: follow it.</summary>
    public void FileMoved(string newPath)
    {
        if (Stage.Kind != BookStageKind.Editing) return;
        Stage = BookStage.Editing(BookPaths.Standardize(newPath));
        Notify();
    }

    /// <summary>The file is gone. A dirty buffer stays on screen as a conflict ("Keep My Version" recreates the file); a clean one just detaches.</summary>
    public void FileDeleted()
    {
        if (Stage.Kind != BookStageKind.Editing) return;
        if (Dirty)
        {
            Conflicted = true;
            autosave?.Cancel();
        }
        else
        {
            Detach(reportFailure: false);
        }
        Notify();
    }

    // MARK: Plumbing

    private void OnFlushRequested(BookFlushGate gate)
    {
        if (!FlushNow()) gate.Vetoed = true;
    }

    private void Notify() => Changed?.Invoke();

    private static bool IsFileFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or System.Security.SecurityException;

    /// <summary>Unsubscribes from the flush gate. Does not flush — call <see cref="CloseBook"/> for that, as the window does.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        BookFlushGate.Requested -= OnFlushRequested;
    }
}
