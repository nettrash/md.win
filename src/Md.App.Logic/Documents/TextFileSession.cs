using Md.App.Logic.Seams;
using Md.Core.Document;

namespace Md.App.Logic.Documents;

/// <summary>
/// What the surface hosting a session should show. Swift's <c>BookArticleSession.Stage</c> plus the
/// two states only a document window has: <see cref="Untitled"/> (a new document, an example, a
/// duplicate, an imported bundle) and <see cref="Empty"/> (a book pane with nothing selected).
/// Closed: these five are every state a session can be in.
/// </summary>
public abstract record Stage
{
    private protected Stage() { }

    /// <summary>No file yet: Save routes to Save As, closing while dirty asks.</summary>
    public sealed record Untitled : Stage;

    /// <summary>Editing the file at <paramref name="Path"/> in place.</summary>
    public sealed record Editing(string Path) : Stage;

    /// <summary>A document window owns the file; this session has stepped aside so there is never a second writer.</summary>
    public sealed record Handoff(string Path) : Stage;

    /// <summary>The file could not be read (moved or deleted outside the book).</summary>
    public sealed record Unreadable(string Path) : Stage;

    /// <summary>Nothing selected — the book pane's placeholder.</summary>
    public sealed record Empty : Stage;

    /// <summary>The file this stage refers to, or null for <see cref="Untitled"/> and <see cref="Empty"/>.</summary>
    public string? FilePath => this switch
    {
        Editing e => e.Path,
        Handoff h => h.Path,
        Unreadable u => u.Path,
        _ => null,
    };
}

/// <summary>Which surface the session serves. The two differ in three rows only; everything else is one state machine.</summary>
public enum SessionRole
{
    /// <summary>A document window: "— Edited" survives an autosave, and a deleted file leaves the buffer and the path in place.</summary>
    Document,

    /// <summary>The book window's detail pane: every successful save is the save, and a deleted article detaches.</summary>
    BookArticle,
}

/// <summary>
/// One state machine for documents and book articles (§6.3) — the port of macOS's
/// <c>BookArticleSession</c> widened to carry what NSDocument gave the document window for free.
///
/// The promises, each pinned by a test: a write is refused — <see cref="Conflicted"/> — when the
/// file's (mtime, size) stamp no longer matches the one read, and a vanished file is stale too; a
/// clean flush never touches the file; a save stays in the encoding, BOM and line endings the file
/// was read with; a selection change saves the outgoing article and a failed save aborts the change;
/// closing with an unsavable buffer parks it in a "(rescued)" sibling and says so; while a document
/// window owns the article the session hands off and reclaims when that window closes.
///
/// Two kinds of "dirty", because the Mac has two. <see cref="IsDirty"/> is the window title's
/// "— Edited": it means the text differs from the last <em>explicit</em> save, and an autosave leaves
/// it alone, exactly as NSDocument's autosave-in-place does. The private "unsaved" flag means the
/// bytes on disk are behind the buffer; it is what arms the autosave and what a rescue copy is for.
/// A book article has only one notion, so for that role every successful save clears both — which is
/// what the fourteen macOS session tests pin.
///
/// Single-threaded by contract, like the Swift <c>@MainActor</c> class: the watcher marshals its
/// events through <see cref="IUiThread"/> before they reach here, and the scheduler's timers are
/// UI-thread timers, so nothing in here locks.
/// </summary>
public sealed class TextFileSession : IDisposable
{
    /// <summary>How long after the last keystroke the autosave fires (§6.3).</summary>
    public static readonly TimeSpan AutosaveDelay = TimeSpan.FromSeconds(1);

    // The stamp a missing file compares as: never equal to a real one, so a vanished file is stale.
    static readonly FileStamp Missing = new(DateTime.MinValue, -1);

    readonly IFileSystem fs;
    readonly IFileWatcher watcher;
    readonly IScheduler scheduler;
    readonly IDocumentRegistry registry;
    readonly IFileIdentity identity;
    IDisposable? autosave;
    bool unsaved;
    bool disposed;

    /// <param name="role">Document window or book pane; see <see cref="SessionRole"/>.</param>
    /// <param name="windowId">
    /// The window that owns the file, when there is one. A document window passes its id and the
    /// session keeps the registry's path→window entry up to date; the book pane passes none and only
    /// asks the registry who owns what.
    /// </param>
    public TextFileSession(
        IFileSystem fileSystem,
        IFileWatcher fileWatcher,
        IScheduler scheduler,
        IDocumentRegistry registry,
        IFileIdentity identity,
        SessionRole role = SessionRole.Document,
        Guid? windowId = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(fileWatcher);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(identity);
        fs = fileSystem;
        watcher = fileWatcher;
        this.scheduler = scheduler;
        this.registry = registry;
        this.identity = identity;
        Role = role;
        WindowId = windowId;
        watcher.Changed += OnFileChanged;
        watcher.Renamed += OnFileRenamed;
        watcher.Deleted += OnFileDeleted;
    }

    // ---- state ----

    public SessionRole Role { get; }

    /// <summary>The window whose registry entry this session maintains, or null for the book pane.</summary>
    public Guid? WindowId { get; }

    public Stage Stage { get; private set; } = new Stage.Untitled();

    /// <summary>The buffer, always LF (§3.2). Views route edits through <see cref="Edit"/>; assigning would bypass the dirty tracking.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The display name: a file's stem, a bundle's stem, or the untitled name the window was given.</summary>
    public string Title { get; private set; } = Strings.Untitled;

    /// <summary>Encoding, BOM and line endings to write back with (§6.3).</summary>
    public TextFileDressing Dressing { get; private set; } = TextFileDressing.Default;

    /// <summary>The window title's "— Edited": differs from the last explicit save or open. An autosave never clears it (§6.7).</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The bytes on disk are behind the buffer — what arms the autosave and what a rescue copy would park.</summary>
    public bool HasUnsavedChanges => unsaved;

    /// <summary>The file changed on disk under unsaved edits (or vanished). Autosave is suspended; the bar offers Reload / Keep My Version.</summary>
    public bool Conflicted { get; private set; }

    /// <summary>The last save failure, shown until a save succeeds. The session stays unsaved so Retry or the next autosave can heal it.</summary>
    public string? SaveErrorText { get; private set; }

    /// <summary>The <c>.textpack</c> or <c>.textbundle</c> this untitled document was imported from (§6.5); display only, never written back.</summary>
    public string? ImportedFrom { get; private set; }

    /// <summary>What Revert to Saved goes back to: the text of the last explicit save or open.</summary>
    public string LastSavedText { get; private set; } = "";

    /// <summary>The (mtime, size) of the file as of the last read or write; null when there is no file.</summary>
    public FileStamp? DiskStamp { get; private set; }

    /// <summary>Bumped on every load and detach: the editor clears its undo stack so Ctrl+Z never crosses files.</summary>
    public int UndoGeneration { get; private set; }

    /// <summary>The canonical spelling of the file, the registry's key; null when there is no file.</summary>
    public string? CanonicalPath { get; private set; }

    /// <summary>The file being edited in place, when there is one.</summary>
    public string? EditingPath => (Stage as Stage.Editing)?.Path;

    /// <summary>Whether the document has a file at all (Save writes it; Save As, Rename, Move To and Revert need it).</summary>
    public bool HasFileIdentity => Stage.FilePath is not null;

    // ---- events ----

    /// <summary>Any observable change (stage, text, dirty, conflict, error) — coarse, for a view refresh.</summary>
    public event Action? Changed;

    /// <summary>The buffer was replaced from outside the editor (revert, silent reload, conflict resolution): assign it and restore the clamped selection (§3.2).</summary>
    public event Action<string>? TextReplacedExternally;

    /// <summary>The file the session edits changed (open, Save As, rename, detach) — null when it now has none. The view-mode memory re-decides on this (§5.2).</summary>
    public event Action<string?>? IdentityChanged;

    /// <summary>A modal the session cannot show itself: (title, message). Raised for a failed open and for the rescue-copy report (§6.3).</summary>
    public event Action<string, string>? AlertRequested;

    // ---- opening ----

    /// <summary>
    /// A document with no file: New, an example (dirty, so closing offers to save), Duplicate.
    /// </summary>
    public void OpenUntitled(string text, string? title = null, bool dirty = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        CancelAutosave();
        watcher.Stop();
        Text = LineEndings.Normalize(text);
        Dressing = TextFileDressing.Default;
        Title = title ?? Strings.Untitled;
        ImportedFrom = null;
        Stage = new Stage.Untitled();
        DiskStamp = null;
        CanonicalPath = null;
        Conflicted = false;
        SaveErrorText = null;
        LastSavedText = Text;
        IsDirty = dirty;
        unsaved = dirty;
        UndoGeneration++;
        IdentityChanged?.Invoke(null);
        Notify();
    }

    /// <summary>
    /// Open a path: a plain file is edited in place; a <c>.textpack</c> or a <c>.textbundle</c>
    /// folder is imported read-only (§6.5). False means nothing was opened and
    /// <see cref="AlertRequested"/> has just carried the reason.
    /// </summary>
    public bool Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var load = DocumentLoader.Load(path, fs);
        if (load.Document is not { } document)
        {
            AlertRequested?.Invoke(load.AlertTitle ?? Strings.Documents.CouldNotOpen, load.ErrorText);
            return false;
        }
        // The loader's dressing, not just its encoding: LoadedDocument.Text is already LF, so
        // re-detecting the newline here would read Lf off every bundle and quietly turn a CRLF
        // writer's first Save As into an LF file. ImportBundle's own detection is for callers who
        // still hold the raw text.
        if (document.Kind != DocumentKind.PlainText)
            return AdoptImport(document.Path, document.Text, document.Dressing);

        CancelAutosave();
        Text = document.Text;
        Dressing = document.Dressing;
        Title = DisplayNameOf(path);
        ImportedFrom = null;
        Stage = new Stage.Editing(path);
        DiskStamp = fs.Stamp(path);
        Conflicted = false;
        SaveErrorText = null;
        LastSavedText = Text;
        IsDirty = false;
        unsaved = false;
        UndoGeneration++;
        watcher.Watch(path);
        Register(path);
        Notify();
        return true;
    }

    /// <summary>
    /// Adopt an already-decoded bundle as an untitled document titled after the bundle (§6.5).
    /// Bundles are never written back — their <c>assets/</c> would be lost — so Save asks for a new
    /// <c>.md</c> location and the window owns no file.
    /// </summary>
    public bool ImportBundle(string bundlePath, string text, TextEncoding encoding)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundlePath);
        ArgumentNullException.ThrowIfNull(text);
        // Detected here, before the normalise below: this overload takes the bundle's text as it
        // was decoded, so its own convention is still visible in the argument.
        return AdoptImport(bundlePath, text, new TextFileDressing(encoding, false, LineEndings.Detect(text)));
    }

    bool AdoptImport(string bundlePath, string text, TextFileDressing dressing)
    {
        CancelAutosave();
        watcher.Stop();
        Text = LineEndings.Normalize(text);
        Dressing = dressing;
        Title = FileNames.StemOf(bundlePath);
        ImportedFrom = bundlePath;
        Stage = new Stage.Untitled();
        DiskStamp = null;
        CanonicalPath = null;
        Conflicted = false;
        SaveErrorText = null;
        LastSavedText = Text;
        IsDirty = false;
        unsaved = false;
        UndoGeneration++;
        IdentityChanged?.Invoke(null);
        Notify();
        return true;
    }

    /// <summary>
    /// The book pane's selection change (null deselects): flush the current article, then load the
    /// new one or step aside if a document window owns it. False — the current article left in
    /// place — when the flush failed; the sidebar reverts its selection so no unsaved text is ever
    /// abandoned silently.
    /// </summary>
    public bool Select(string? path)
    {
        var target = path is null ? null : FileNames.Standardize(path);
        if (Stage is Stage.Editing current && target is not null && FileNames.SamePath(current.Path, target)) return true;
        if (!FlushNow(explicitSave: false)) return false;
        Detach(reportFailure: false);
        if (target is null)
        {
            Notify();
            return true;
        }
        if (OwnedByAnother(target)) SetHandoff(target);
        else Load(target);
        Notify();
        return true;
    }

    // ---- editing ----

    /// <summary>
    /// The one write path for keystrokes. While conflicted the autosave stays suspended — the writer
    /// must first choose Reload or Keep My Version.
    /// </summary>
    public void Edit(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Stage is not (Stage.Editing or Stage.Untitled)) return;
        if (string.Equals(Text, text, StringComparison.Ordinal)) return;
        Text = text;
        IsDirty = true;
        unsaved = true;
        if (Stage is Stage.Editing && !Conflicted) ScheduleAutosave();
        Notify();
    }

    // ---- saving ----

    /// <summary>
    /// Write the buffer now. Refuses — flagging <see cref="Conflicted"/> — when the file on disk is
    /// no longer the one that was read. True when the write landed or there was nothing to write.
    /// </summary>
    /// <param name="explicitSave">
    /// Ctrl+S, Revert, Keep My Version: the ones that clear "— Edited". An autosave passes false and
    /// leaves the title's Edited marker alone, as NSDocument's autosave-in-place does (§6.7). An
    /// explicit save with nothing to write still clears the marker — otherwise a document that has
    /// only ever been autosaved could never lose it.
    /// </param>
    public bool FlushNow(bool explicitSave)
    {
        if (Stage is not Stage.Editing editing) return true;
        var path = editing.Path;
        if (!unsaved)
        {
            if (explicitSave)
            {
                MarkExplicitlySaved();
                Notify();
            }
            return true;
        }

        // A document window may have opened this article mid-session. Save our text first — an
        // unedited document window re-reads the file — then step aside.
        var owned = OwnedByAnother(path);
        CancelAutosave();

        var bytes = Dressing.Dress(Text, out var used);
        if (DiskStamp is { } expected && (fs.Stamp(path) ?? Missing) != expected)
        {
            Conflicted = true;
            Notify();
            return false;
        }
        try
        {
            fs.WriteAllBytesInPlace(path, bytes);
            DiskStamp = fs.Stamp(path);
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            SaveErrorText = FileFailure.Message(e, WriteFailureFallback);
            Notify();
            return false;
        }
        Dressing = Dressing.WithEncoding(used);
        SaveErrorText = null;
        unsaved = false;
        if (explicitSave || Role == SessionRole.BookArticle) MarkExplicitlySaved();
        if (owned) SetHandoff(path);
        Notify();
        return true;
    }

    /// <summary>
    /// Write to a new path and edit that from now on; the old file stays where it was (§6.3). The
    /// encoding and line endings travel with the text. False leaves the session on the old file with
    /// <see cref="SaveErrorText"/> set — the caller deletes the empty file the picker created (§6.1).
    /// </summary>
    public bool SaveAs(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        CancelAutosave();
        var bytes = Dressing.Dress(Text, out var used);
        try
        {
            fs.WriteAllBytesInPlace(path, bytes);
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            SaveErrorText = FileFailure.Message(e, WriteFailureFallback);
            Notify();
            return false;
        }
        Dressing = Dressing.WithEncoding(used);
        Stage = new Stage.Editing(path);
        Title = DisplayNameOf(path);
        ImportedFrom = null;
        DiskStamp = fs.Stamp(path);
        Conflicted = false;
        SaveErrorText = null;
        unsaved = false;
        MarkExplicitlySaved();
        watcher.Watch(path);
        Register(path);
        Notify();
        return true;
    }

    /// <summary>
    /// The file moved or was renamed — by us (Rename…, Move To…) or under us (the watcher). The
    /// session follows it: new stage, new title, new watch, new registry entry, and
    /// <see cref="IdentityChanged"/> so the view-mode memory re-decides (§5.2).
    /// </summary>
    public void Retarget(string newPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(newPath);
        if (Stage is not Stage.Editing) return;
        Stage = new Stage.Editing(newPath);
        Title = DisplayNameOf(newPath);
        DiskStamp = fs.Stamp(newPath);
        watcher.Watch(newPath);
        Register(newPath);
        Notify();
    }

    /// <summary>Revert to Saved: the buffer goes back to the last explicit save and is written out (§6.3).</summary>
    public void RevertToSaved()
    {
        if (Stage is not Stage.Editing) return;
        if (!string.Equals(Text, LastSavedText, StringComparison.Ordinal))
        {
            Text = LastSavedText;
            unsaved = true;
            UndoGeneration++;
        }
        FlushNow(explicitSave: true);
        TextReplacedExternally?.Invoke(Text);
        Notify();
    }

    /// <summary>
    /// Let go of the file. The flush is attempted, and a failure — the write refused, or an
    /// unresolved conflict — parks the buffer in a rescue copy and says so (when
    /// <paramref name="reportFailure"/>) instead of discarding keystrokes behind an alert with no
    /// way out. Always completes.
    /// </summary>
    public void Detach(bool reportFailure)
    {
        if (!FlushNow(explicitSave: false) && reportFailure && Stage is Stage.Editing editing && unsaved)
        {
            var name = DisplayNameOf(editing.Path);
            var rescued = WriteRescueCopy();
            AlertRequested?.Invoke(
                Strings.Documents.RescueFailedTitle(name),
                rescued is not null
                    ? Strings.Documents.RescueKeptMessage(FileNames.NameOf(rescued))
                    : SaveErrorText ?? WriteFailureFallback);
        }
        CancelAutosave();
        watcher.Stop();
        Unregister();
        UndoGeneration++;
        Text = "";
        Stage = new Stage.Empty();
        Title = "";
        Dressing = TextFileDressing.Default;
        ImportedFrom = null;
        Conflicted = false;
        SaveErrorText = null;
        DiskStamp = null;
        LastSavedText = "";
        IsDirty = false;
        unsaved = false;
        IdentityChanged?.Invoke(null);
        Notify();
    }

    /// <summary>The window's close path (§6.3): flush, rescue on failure, report, let go. Never blocks the close.</summary>
    public void CloseFlush() => Detach(reportFailure: true);

    /// <summary>
    /// The exit-time flush: when the regular write is refused right as the app exits — conflicted,
    /// file vanished, disk full — the keystrokes go to a rescue copy rather than nowhere.
    /// </summary>
    public void TerminateFlush()
    {
        if (!FlushNow(explicitSave: false) && Stage is Stage.Editing && unsaved) WriteRescueCopy();
    }

    /// <summary>
    /// Last-resort save into a fresh <c>"{name} (rescued).{ext}"</c> sibling. Null when there is no
    /// file, the write failed, or a hundred names were taken. A rescue is a copy, not a save: the
    /// buffer stays dirty.
    /// </summary>
    public string? WriteRescueCopy()
    {
        if (Stage is not Stage.Editing editing) return null;
        return RescueCopy.Write(fs, editing.Path, Dressing.Dress(Text, out _));
    }

    // ---- conflicts ----

    /// <summary>"Keep My Version": write the buffer out unconditionally — no stamp check; the point is to overwrite whatever is on disk, or recreate a deleted file.</summary>
    public void ResolveConflictKeepingMine()
    {
        if (Stage is not Stage.Editing editing) return;
        Conflicted = false;
        var bytes = Dressing.Dress(Text, out var used);
        try
        {
            fs.WriteAllBytesInPlace(editing.Path, bytes);
            Dressing = Dressing.WithEncoding(used);
            DiskStamp = fs.Stamp(editing.Path);
            SaveErrorText = null;
            unsaved = false;
            MarkExplicitlySaved();
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            SaveErrorText = FileFailure.Message(e, WriteFailureFallback);
        }
        Notify();
    }

    /// <summary>
    /// "Reload from Disk": discard the buffer. Clearing the unsaved flag <em>first</em> is what makes
    /// the book's detach a no-op — writing the buffer is exactly what must not happen here. A
    /// document window keeps its window: it re-reads in place, and if the file is gone it keeps the
    /// text and lets the next save recreate the file.
    /// </summary>
    public void ResolveConflictReloading()
    {
        if (Stage is not Stage.Editing editing) return;
        var path = editing.Path;
        unsaved = false;
        IsDirty = false;
        if (Role == SessionRole.BookArticle)
        {
            Detach(reportFailure: false);
            Load(path);
        }
        else if (!Reload(path, resetUndo: true))
        {
            Conflicted = false;
            DiskStamp = null;
            unsaved = true;
            IsDirty = true;
        }
        Notify();
    }

    // ---- ownership ----

    /// <summary>
    /// Re-evaluate who owns the file (the app calls this when any window is activated). Editing + a
    /// document window appeared → save and hand off; handoff + that window closed → reclaim. A failed
    /// flush stays put: the buffer must remain visible with its error, not vanish behind the handoff
    /// pane.
    /// </summary>
    public void RecheckOwnership()
    {
        switch (Stage)
        {
            case Stage.Editing editing:
                if (OwnedByAnother(editing.Path) && FlushNow(explicitSave: false) && Stage is Stage.Editing)
                    SetHandoff(editing.Path);
                break;
            case Stage.Handoff handoff:
                if (!OwnedByAnother(handoff.Path))
                {
                    Detach(reportFailure: false);
                    Load(handoff.Path);
                }
                break;
        }
        Notify();
    }

    /// <summary>
    /// The pane is about to open its article in a separate window: save, then step into handoff
    /// <em>before</em> the document opens so there is never a moment with two writers. False when the
    /// save failed and the open must be abandoned.
    /// </summary>
    public bool HandOffForExternalOpen()
    {
        if (Stage is not Stage.Editing editing) return true;
        var path = editing.Path;
        if (!FlushNow(explicitSave: false)) return false;
        Detach(reportFailure: false);
        SetHandoff(path);
        Notify();
        return true;
    }

    /// <summary>The window id that owns the file, for the handoff pane's "Show Window" button.</summary>
    public Guid? OwningWindow => Stage.FilePath is { } path ? registry.Owning(Canonicalize(path)) : null;

    // ---- watcher events (marshalled to the UI thread by the adapter) ----

    void OnFileChanged(string path)
    {
        if (Stage is not Stage.Editing editing) return;
        // Our own write fires this, and so does an attribute-only touch; only a moved stamp is a
        // real change. A conflict banner over identical bytes would invite a pointless,
        // text-discarding Reload.
        if (fs.Stamp(editing.Path) is { } current && DiskStamp is { } expected && current == expected) return;
        if (unsaved)
        {
            Conflicted = true;
            CancelAutosave();
        }
        else
        {
            Reload(editing.Path, resetUndo: false);
        }
        Notify();
    }

    void OnFileRenamed(string oldPath, string newPath) => Retarget(newPath);

    void OnFileDeleted(string path)
    {
        if (Stage is not Stage.Editing) return;
        CancelAutosave();
        if (unsaved)
        {
            Conflicted = true;                       // "Keep My Version" recreates the file
        }
        else if (Role == SessionRole.BookArticle)
        {
            Detach(reportFailure: false);
            return;                                  // Detach notified already
        }
        else
        {
            // NSDocument keeps the window: the buffer and the path stay, the document is dirty
            // again, and the next save recreates the file (no stamp to be stale against).
            DiskStamp = null;
            unsaved = true;
            IsDirty = true;
        }
        Notify();
    }

    // ---- internals ----

    void Load(string path)
    {
        if (DocumentLoader.Load(path, fs).Document is not { Kind: DocumentKind.PlainText } document)
        {
            Stage = new Stage.Unreadable(path);
            Title = DisplayNameOf(path);
            return;
        }
        Text = document.Text;
        Dressing = document.Dressing;
        Title = DisplayNameOf(path);
        DiskStamp = fs.Stamp(path);
        UndoGeneration++;
        Stage = new Stage.Editing(path);
        Conflicted = false;
        SaveErrorText = null;
        LastSavedText = Text;
        IsDirty = false;
        unsaved = false;
        watcher.Watch(path);
        Register(path);
        TextReplacedExternally?.Invoke(Text);
    }

    // The silent follow-the-disk of a clean session: text, dressing and stamp, nothing else. The
    // undo stack survives unless the caller is discarding the buffer on purpose.
    bool Reload(string path, bool resetUndo)
    {
        if (DocumentLoader.Load(path, fs).Document is not { Kind: DocumentKind.PlainText } document) return false;
        Text = document.Text;
        Dressing = document.Dressing;
        DiskStamp = fs.Stamp(path);
        Conflicted = false;
        SaveErrorText = null;
        LastSavedText = Text;
        IsDirty = false;
        unsaved = false;
        if (resetUndo) UndoGeneration++;
        TextReplacedExternally?.Invoke(Text);
        return true;
    }

    void SetHandoff(string path)
    {
        Stage = new Stage.Handoff(path);
        Title = DisplayNameOf(path);
    }

    void ScheduleAutosave()
    {
        CancelAutosave();
        autosave = scheduler.After(AutosaveDelay, () => FlushNow(explicitSave: false));
    }

    void CancelAutosave()
    {
        autosave?.Dispose();
        autosave = null;
    }

    void MarkExplicitlySaved()
    {
        IsDirty = false;
        LastSavedText = Text;
    }

    void Register(string path)
    {
        var canonical = Canonicalize(path);
        CanonicalPath = canonical;
        if (WindowId is { } id) registry.Register(id, canonical);
        IdentityChanged?.Invoke(path);
    }

    void Unregister()
    {
        if (CanonicalPath is null) return;
        CanonicalPath = null;
        if (WindowId is { } id) registry.Unregister(id);
    }

    bool OwnedByAnother(string path)
    {
        var owner = registry.Owning(Canonicalize(path));
        return owner is { } id && (WindowId is not { } mine || id != mine);
    }

    string Canonicalize(string path)
    {
        try
        {
            return identity.Canonical(path);
        }
        catch (Exception e) when (FileFailure.IsOne(e))
        {
            return path;
        }
    }

    // A book article's title drops its ordering prefix ("01-Scene.md" → "Scene"); a document's does not.
    string DisplayNameOf(string path) => Role == SessionRole.BookArticle
        ? Md.Core.Book.BookNaming.DisplayName(FileNames.NameOf(path))
        : FileNames.StemOf(path);

    string WriteFailureFallback => Role == SessionRole.BookArticle
        ? Strings.Documents.CouldNotWriteArticle
        : Strings.Documents.CouldNotWriteDocument;

    void Notify() => Changed?.Invoke();

    /// <summary>Unsubscribes from the watcher and drops the registry entry. Does not flush — call <see cref="CloseFlush"/> for that, as the window does.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        watcher.Changed -= OnFileChanged;
        watcher.Renamed -= OnFileRenamed;
        watcher.Deleted -= OnFileDeleted;
        CancelAutosave();
        Unregister();
    }
}
