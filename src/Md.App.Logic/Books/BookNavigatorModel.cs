// The book workspace's brain (shell-design.md §8.5, book.md §13.7 verbatim): what is selected, what
// a click / rename / move / delete does to the selection, and what gets remembered. Every rule that
// can be tested without a window lives here; BookWindow only renders the answers.
//
// The Swift original is a SwiftUI view whose `selection` is @State with an .onChange handler, and
// two of its behaviours are consequences of that and not of the algorithm:
//   * onChange fires on the NET change of a synchronous body — performManaged assigns nil and then
//     the new selection, and only one handler call follows. That is why the assignments below are
//     silent and each public entry point dispatches once at the end.
//   * a revert inside the handler (`selection = session.editingURL`) fires onChange a second time,
//     whose only lasting effect is re-writing md.bookLastArticle with the path it already holds.
//     Not reproduced: the value would not change.
using Md.App.Logic.Documents;
using Md.App.Logic.Settings;
using Md.Core.Book;
using Md.Core.Document;

namespace Md.App.Logic.Books;

/// <summary>
/// The half of the in-place editor the navigator drives. <c>Documents.TextFileSession</c> in the
/// app (through <see cref="TextFileSessionArticles"/>); a fake in the tests, because the navigator's
/// contract is "a failed flush aborts the operation" and that has to be provable without a disk
/// that refuses writes.
/// </summary>
public interface IBookArticleSession
{
    /// <summary>The article being edited in place, or null (handoff, unreadable and empty all answer null).</summary>
    string? EditingPath { get; }

    /// <summary>Flush the outgoing article, then load <paramref name="path"/> (null deselects). False = the flush failed and nothing moved.</summary>
    bool Select(string? path);

    /// <summary>
    /// Let go of the article unconditionally — the book was closed. Unlike <see cref="Select"/> this
    /// always completes: a failed flush parks the buffer in a rescue copy and reports it rather than
    /// keeping an article open in a book that is no longer there.
    /// </summary>
    void Detach(bool reportFailure);

    /// <summary>Save and step aside before a document window opens the article. False = the save failed and the open must be abandoned.</summary>
    bool HandOffForExternalOpen();
}

/// <summary><see cref="IBookArticleSession"/> over WP2's session in <see cref="SessionRole.BookArticle"/> mode.</summary>
public sealed class TextFileSessionArticles(TextFileSession session) : IBookArticleSession
{
    public string? EditingPath => session.EditingPath;

    public bool Select(string? path) => session.Select(path);

    public void Detach(bool reportFailure) => session.Detach(reportFailure);

    public bool HandOffForExternalOpen() => session.HandOffForExternalOpen();
}

/// <summary>
/// Selection, listing and the four management operations of the book window.
///
/// The invariants worth naming, because each is a promise the tests pin: a failed flush never loses
/// text — it aborts the selection change and the whole managed operation, and the sidebar goes back
/// to the article that would not save; the selection follows its article through a rename or a
/// reorder (<see cref="Destination"/>) and falls to a neighbour when it is deleted
/// (<see cref="BookFolder.DeletionNeighbor"/>, computed while the row still exists); the last
/// article written in is remembered relative to the root, so a moved book still reopens where the
/// writer left off; and while articles open in their own windows the sidebar is a launcher with no
/// selection at all.
/// </summary>
public sealed class BookNavigatorModel
{
    readonly IBookArticleSession session;
    readonly ISettingsStore settings;
    readonly IBookFolderFileSystem files;
    readonly IBookListing listing;
    string? selection;

    public BookNavigatorModel(
        IBookArticleSession session,
        ISettingsStore settings,
        IBookFolderFileSystem? files = null,
        IBookListing? listing = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(settings);
        this.session = session;
        this.settings = settings;
        this.files = files ?? LocalBookFolderFileSystem.Instance;
        this.listing = listing ?? DirectoryBookListing.Instance;
    }

    /// <summary>The open book's root folder, or null. Set by <see cref="OpenBook"/>; the app resolves the grant first.</summary>
    public string? Root { get; private set; }

    /// <summary>The last listing. Null when there is no book or the folder could not be read — both show the empty state.</summary>
    public Book? Book { get; private set; }

    /// <summary>The selected article, or null. Always a path as the listing spells it.</summary>
    public string? Selection => selection;

    /// <summary>Raised after any change to <see cref="Book"/> or <see cref="Selection"/>: the window re-renders the sidebar and the detail pane.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised at the top of every selection change, before the session moves. The window clears the
    /// one-shot Contents / Notes jump requests here, so a jump never replays into the next article.
    /// </summary>
    public event Action? SelectionChanging;

    /// <summary>An article should be opened in its own document window (after the handoff has already happened).</summary>
    public event Action<string>? OpenRequested;

    /// <summary>(title, message) for the modal alert — "Could not rename", "Could not reorder", "Could not delete".</summary>
    public event Action<string, string>? AlertRequested;

    /// <summary>
    /// The app-wide "Open Articles in Separate Windows" preference (§8.4). Writing it re-lists,
    /// because the sidebar stops holding a selection the moment it goes on.
    /// </summary>
    public bool OpensInSeparateWindows
    {
        get => settings.GetBool(SettingsKeys.BookOpensInSeparateWindows, false);
        set
        {
            if (OpensInSeparateWindows == value) return;
            settings.SetBool(SettingsKeys.BookOpensInSeparateWindows, value);
            Reload();
        }
    }

    /// <summary>The remembered article, <c>/</c>-separated and relative to the root (§9); "" when nothing is remembered.</summary>
    public string LastArticlePath => settings.GetString(SettingsKeys.BookLastArticle) ?? "";

    /// <summary>Previous / Next over the current listing and selection.</summary>
    public BookStepper Stepper => BookStepper.For(Book, selection, OpensInSeparateWindows);

    // MARK: Book lifecycle

    /// <summary>
    /// Open the book at <paramref name="root"/>: list it, pick a selection and hand it to the
    /// session. The dispatch is unconditional, which is Swift's <c>onAppear</c> calling
    /// <c>selectionChanged(selection)</c> by hand — a restored selection that happens to equal the
    /// one the reload computed would otherwise never reach the session.
    /// </summary>
    public void OpenBook(string root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Root = BookPaths.Standardize(root);
        ReloadCore(null);
        SelectionChanged(selection);
        Changed?.Invoke();
    }

    /// <summary>
    /// Close the book: let go of the article, forget the listing. The folder is untouched. The
    /// detach reports a failed final save (Swift's <c>closeBook</c> is <c>detach(reportFailure: true)</c>)
    /// rather than leaving an article open in a book that is no longer there.
    /// </summary>
    public void CloseBook()
    {
        SelectionChanging?.Invoke();
        session.Detach(reportFailure: true);
        selection = null;
        Book = null;
        Root = null;
        Changed?.Invoke();
    }

    // MARK: Listing and selection

    /// <summary>
    /// Re-list the book and re-point the selection: the caller's preference if it still exists, else
    /// the current selection re-spelled as the listing spells it, else the remembered article, else
    /// the first article in reading order. Dispatches to the session only when the net selection
    /// moved — the SwiftUI onChange rule.
    /// </summary>
    public void Reload(string? preferring = null)
    {
        var before = selection;
        ReloadCore(preferring);
        if (!SamePath(before, selection)) SelectionChanged(selection);
        Changed?.Invoke();
    }

    void ReloadCore(string? preferring)
    {
        Book = Root is null ? null : BookModel.Load(Root, listing);
        if (Book is null)
        {
            selection = null;
            return;
        }
        // A launcher never auto-selects: clicking a row opens a window, so a highlighted row would
        // be a lie about where the writing goes.
        if (OpensInSeparateWindows)
        {
            selection = null;
            return;
        }
        var order = BookModel.ReadingOrder(Book);
        if (Existing(order, preferring) is { } preferred)
        {
            selection = preferred;
            return;
        }
        if (Existing(order, selection) is { } current)
        {
            selection = current;
            return;
        }
        var remembered = LastArticlePath.Length == 0
            ? null
            : Existing(order, BookFolder.PathIn(Book.Root, LastArticlePath));
        selection = remembered ?? (order.Count > 0 ? order[0].Path : null);
    }

    /// <summary>
    /// A row was clicked (or a step landed): move the selection there. A no-op when the value has
    /// not changed, because a SwiftUI List writes its binding only on a real change and the session
    /// is idempotent anyway.
    /// </summary>
    public void Select(string? path)
    {
        if (SamePath(selection, path)) return;
        selection = path;
        SelectionChanged(path);
        Changed?.Invoke();
    }

    /// <summary>
    /// The selection handler itself (book.md §13.7). Public because <see cref="OpenBook"/> and
    /// <see cref="PerformManaged"/> call it by hand where SwiftUI's onChange would not fire.
    /// </summary>
    public void SelectionChanged(string? path)
    {
        SelectionChanging?.Invoke();
        if (path is null)
        {
            // A failed flush on a deselect keeps the unsaved article on screen with its error.
            if (!session.Select(null)) selection = session.EditingPath;
            return;
        }
        if (OpensInSeparateWindows)
        {
            // The click is a gesture, not a selection: the highlight clears and a window opens.
            selection = null;
            OpenInWindow(path);
            return;
        }
        if (session.Select(path)) settings.SetString(SettingsKeys.BookLastArticle, RelativePath(path));
        else selection = session.EditingPath;
    }

    /// <summary>
    /// <paramref name="path"/> relative to the book root, <c>/</c>-separated — the Mac's spelling of
    /// <c>md.bookLastArticle</c>, kept because it is the one form that survives the book folder
    /// being moved or renamed behind the grant. "" when there is no book or the path is outside it.
    /// </summary>
    public string RelativePath(string path)
    {
        if (Root is null) return "";
        var relative = BookFolder.RelativePath(Root, path);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    // MARK: Stepping

    /// <summary>Ctrl+Alt+Up / the up chevron.</summary>
    public void StepPrevious()
    {
        if (Stepper.Previous() is { } target) Select(target);
    }

    /// <summary>Ctrl+Alt+Down / the down chevron.</summary>
    public void StepNext()
    {
        if (Stepper.Next() is { } target) Select(target);
    }

    // MARK: Managed operations

    /// <summary>
    /// The frame every mutation runs in: detach the session first (so nothing is being written while
    /// files move under it), run the operation, re-list preferring whatever it points at, then
    /// dispatch the selection once. A failed flush aborts before anything is touched — that is the
    /// promise that "the article that will not save is never left behind".
    /// </summary>
    /// <param name="operation">
    /// Takes the selection as it was, returns where the selection should end up (null = let the
    /// reload decide). Returning the input is how a failed operation leaves everything where it was.
    /// </param>
    public bool PerformManaged(Func<string?, string?> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var previous = selection;
        SelectionChanging?.Invoke();
        if (!session.Select(null))
        {
            selection = session.EditingPath;
            Changed?.Invoke();
            return false;
        }
        selection = null;
        ReloadCore(operation(previous));
        SelectionChanged(selection);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// New Chapter…: a folder in the root. It does not detach the session — no article moves.
    /// A collision stays quiet, as Swift is; an unusable <em>name</em> does not, because §8.3 asks
    /// all three name prompts to vet with <see cref="FileNames.Validate"/> and say so. Windows
    /// refuses a far wider set than the Mac (nine characters, a trailing dot or space, the device
    /// names), and a silent no-op on "CON" reads as a broken button rather than as a refusal.
    /// </summary>
    public void CreateChapter(string typedName)
    {
        if (Root is null) return;
        var name = BookFolder.TrimmedPromptName(typedName);
        if (name.Length == 0) return;
        // The name that lands on disk is exactly what was typed — that is what Core vets too.
        if (!Refuse(name)) return;
        BookFolder.CreateChapter(Root, name, files);
        Reload();
    }

    /// <summary>New Article…: a seeded Markdown file in <paramref name="folder"/>, selected when it lands.</summary>
    public void CreateArticle(string typedName, string folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var name = BookFolder.TrimmedPromptName(typedName);
        if (name.Length == 0) return;
        // The FILE NAME is vetted, never the typed stem: ".md" is part of what Windows refuses,
        // and Core's CreateArticle vets the same string. Validating the stem alone would refuse
        // "Draft " — a name Core creates happily, because the space stops being trailing.
        if (!Refuse(name + ".md")) return;
        PerformManaged(previous => BookFolder.CreateArticle(folder, name, files) ?? previous);
    }

    /// <summary>False (and an alert already raised) when <paramref name="fileName"/> is one Windows will not hold (§8.3, §6.4).</summary>
    bool Refuse(string fileName)
    {
        if (FileNames.Validate(fileName)) return true;
        Alert(Strings.Documents.CouldNotRename, Strings.Documents.InvalidNameMessage);
        return false;
    }

    /// <summary>Move Up / Move Down for the item at <paramref name="path"/>; <paramref name="delta"/> is -1 or +1.</summary>
    public void Move(string path, int delta)
    {
        if (Book is null) return;
        if (BookSidebarModel.CommandsFor(Book, path) is not { } commands) return;
        Move(commands.Index, commands.Index + delta, commands.Siblings, commands.Folder);
    }

    /// <summary>
    /// Renumber a sibling group so the item at <paramref name="from"/> lands at <paramref name="to"/>.
    /// The plan is materialised as renames and applied two-phase, so a swap ("01-a" ↔ "02-a") cannot
    /// collide midway; the selection is remapped through the same plan.
    /// </summary>
    public void Move(int from, int to, IReadOnlyList<string> siblings, string folder)
    {
        ArgumentNullException.ThrowIfNull(siblings);
        ArgumentNullException.ThrowIfNull(folder);
        PerformManaged(previous =>
        {
            var plan = RenumberPlan.Plan(siblings, from, to);
            if (!RenumberPlan.Apply(plan, folder, files, out var failure))
            {
                Alert(Strings.Books.CouldNotReorder, failure);
                return previous;
            }
            return previous is null ? null : Destination.Of(previous, folder, plan);
        });
    }

    /// <summary>
    /// Rename…: only the readable half of the name changes — the ordering prefix and the extension
    /// are kept. An invalid name and a collision both come back as an alert, and the selection stays
    /// where it was.
    /// </summary>
    public void PerformRename(string path, string typedStem)
    {
        ArgumentNullException.ThrowIfNull(path);
        var stem = BookFolder.TrimmedPromptName(typedStem);
        if (stem.Length == 0) return;
        PerformManaged(previous =>
        {
            var name = BookPaths.Name(path);
            switch (BookFolder.RenameItem(path, stem, out var failure, files))
            {
                case RenameOutcome.InvalidName:
                    Alert(Strings.Documents.CouldNotRename, Strings.Documents.InvalidNameMessage);
                    return previous;
                case RenameOutcome.Failed:
                    Alert(Strings.Documents.CouldNotRename, failure);
                    return previous;
            }
            if (previous is null) return null;
            var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
            if (folder is null) return previous;
            var plan = new[] { new RenamePair(name, BookNaming.RenamedName(name, stem)) };
            return Destination.Of(previous, folder, plan);
        });
    }

    /// <summary>
    /// Delete…: the file, or the chapter folder with everything in it. The neighbour the selection
    /// falls to is computed <em>before</em> the delete, while the book snapshot still has the row.
    /// </summary>
    public void PerformDelete(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        PerformManaged(previous =>
        {
            var neighbor = Book is null ? null : BookFolder.DeletionNeighbor(Book, path);
            if (!BookFolder.DeleteItem(path, out var failure, files))
            {
                Alert(Strings.Books.CouldNotDelete, failure);
                return previous;
            }
            if (previous is null) return null;
            var deleted = BookPaths.Standardize(path);
            var standing = BookPaths.Standardize(previous);
            var died = string.Equals(standing, deleted, BookPaths.Comparison) || BookPaths.IsInside(standing, deleted);
            return died ? neighbor : previous;
        });
    }

    // MARK: Separate windows

    /// <summary>
    /// Open <paramref name="path"/> in its own document window. When it is the article being edited
    /// here, the session saves and steps aside <em>first</em> — there is never a moment with two
    /// writers — and a failed save abandons the open. The mark is what makes the window that lands
    /// on the file exempt from the per-file view-mode memory (Core's <c>BookArticleOpens</c>).
    /// </summary>
    public bool OpenInWindow(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (session.EditingPath is { } editing && FileNames.SamePath(editing, path)
            && !session.HandOffForExternalOpen())
        {
            return false;
        }
        BookArticleOpens.Mark(path);
        OpenRequested?.Invoke(path);
        return true;
    }

    // MARK: Plumbing

    void Alert(string title, string? message) => AlertRequested?.Invoke(title, message ?? "");

    static string? Existing(IReadOnlyList<BookArticle> order, string? path) =>
        path is not null && BookFolder.OrderIndex(order, path) is { } index ? order[index].Path : null;

    // Both sides are listing paths (or null), so this is the cheap comparison and never the one that
    // throws on a path the file system would reject.
    static bool SamePath(string? a, string? b) =>
        a is null || b is null ? a is null && b is null : string.Equals(a, b, BookPaths.Comparison);
}
