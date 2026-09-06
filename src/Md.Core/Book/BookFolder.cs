using Md.Core.Document;
using Md.Core.Text;

namespace Md.Core.Book;

/// <summary>
/// The folder operations <see cref="BookFolder"/> performs, behind a seam so the rules
/// are testable against a fake and the app can route through whatever storage API
/// granted the book folder. Extends the two seams that already exist: reads and writes
/// come from <see cref="IArticleFileSystem"/>, moves from <see cref="IBookFileMover"/>
/// (which is also what the reorder executor uses). Every method throws on failure; the
/// caller turns the message into the alert.
/// </summary>
public interface IBookFolderFileSystem : IArticleFileSystem, IBookFileMover
{
    /// <summary>Whether <paramref name="path"/> is an existing directory — <see cref="IArticleFileSystem.FileExists"/> answers true for both kinds.</summary>
    bool DirectoryExists(string path);

    /// <summary>Create one directory. The parent must exist: Swift passes <c>withIntermediateDirectories: false</c>.</summary>
    void CreateDirectory(string path);

    /// <summary>Delete a file, or a folder with everything in it. Permanent — there is no Recycle Bin here, as there is no Trash on the Mac.</summary>
    void Delete(string path);
}

/// <summary><see cref="IBookFolderFileSystem"/> over System.IO.</summary>
public sealed class LocalBookFolderFileSystem : IBookFolderFileSystem
{
    public static LocalBookFolderFileSystem Instance { get; } = new();

    /// <summary>Swift's <c>fileExists(atPath:)</c>: true for a folder too, so nothing is ever written over one.</summary>
    public bool FileExists(string path) => File.Exists(path) || Directory.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    public void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(path, data);

    public FileStamp? Stamp(string path) => LocalArticleFileSystem.Instance.Stamp(path);

    // Directory.CreateDirectory creates the whole chain; the parent check in
    // CreateChapter is what keeps this to Foundation's one-level contract.
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void Delete(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else File.Delete(path);
    }

    public void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }
}

/// <summary>How a rename ended — the four answers Swift's <c>Bool</c> flattens into two.</summary>
public enum RenameOutcome
{
    /// <summary>The item was renamed.</summary>
    Renamed,
    /// <summary>The name on disk was already the requested one; nothing was touched (Swift returns true here).</summary>
    Unchanged,
    /// <summary>The name is empty or unusable on this OS — show the "A name cannot contain …" message. Swift only rejects "/" and ":".</summary>
    InvalidName,
    /// <summary>The move failed; the error text is the caller's out parameter.</summary>
    Failed,
}

/// <summary>One sibling group as the management menu needs it (Swift's <c>articleContext</c>).</summary>
/// <param name="Name">The row's name — <see cref="BookArticle.Name"/> or <see cref="BookChapter.Name"/>, ordering prefix kept.</param>
/// <param name="Folder">The folder the renames happen in: the book root for a root article or a chapter, the chapter folder for its articles.</param>
/// <param name="Names">Every sibling's on-disk name in displayed order — the input <see cref="RenumberPlan.Plan"/> takes.</param>
/// <param name="Index">This item's position in <paramref name="Names"/>.</param>
public sealed record BookSiblings(string Name, string Folder, IReadOnlyList<string> Names, int Index);

/// <summary>
/// The book folder's file operations — port of the <c>BookLibrary</c> half of md.macOS
/// BookNavigator.swift that touches disk (create, rename, delete) plus the pure
/// selection helpers the navigator computes around them. Listing is
/// <see cref="BookModel.Load"/>, reordering is <see cref="RenumberPlan"/>, the in-place
/// editor is <see cref="BookArticleSession"/>; this is everything else.
///
/// Failures are returned, never presented: Md.Core holds no alert text (Md.App.Logic's
/// <c>Strings</c> does), so each operation reports what happened and the window decides
/// what to say. Swift's security scope has no Windows analogue — the app hands over a
/// folder path it already has rights to.
/// </summary>
public static class BookFolder
{
    /// <summary>Reserved on Windows in every folder, with or without an extension ("CON.md" is the console).</summary>
    private static readonly IReadOnlySet<string> ReservedDeviceNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>The nine characters Win32 refuses in a file name — the set Strings.Documents.InvalidNameMessage promises.</summary>
    private const string InvalidNameCharacters = "\\/:*?\"<>|";

    /// <summary>Every new article starts as UTF-8 without a BOM, like Swift's <c>Data("# \(name)\n".utf8)</c>.</summary>
    private static readonly System.Text.Encoding SeedEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    // MARK: Names

    /// <summary>
    /// Whether <paramref name="name"/> can be a file or folder name here. Swift rejects
    /// only "/" and ":" (and only on rename, because those are the two HFS/POSIX
    /// separators); Windows rejects nine characters, every control character, a trailing
    /// dot or space, and the device names — so the port vets all three prompts with this.
    /// A leading dot is <em>not</em> rejected: it is legal on both systems, and the item
    /// then disappears from the listing on both, which is the Mac's behaviour to the byte.
    /// </summary>
    public static bool IsValidName(string name)
    {
        if (name.Length == 0) return false;
        foreach (var c in name)
        {
            if (c < ' ' || c == (char)0x7F) return false;
            if (InvalidNameCharacters.Contains(c, StringComparison.Ordinal)) return false;
        }
        if (name[^1] == '.' || name[^1] == ' ') return false;
        var dot = name.IndexOf('.');
        var stem = dot < 0 ? name : name[..dot];
        return !ReservedDeviceNames.Contains(AsciiUpper(stem));
    }

    /// <summary>ASCII-only case fold: the device names are ASCII, and a culture fold would turn a Turkish "ı" into something the file system never meant.</summary>
    private static string AsciiUpper(string s)
    {
        Span<char> buffer = s.Length <= 8 ? stackalloc char[s.Length] : new char[s.Length];
        for (var i = 0; i < s.Length; i++) buffer[i] = s[i] is >= 'a' and <= 'z' ? (char)(s[i] - 32) : s[i];
        return new string(buffer);
    }

    /// <summary>What the three prompts do with what was typed: Swift's <c>trimmingCharacters(in: .whitespaces)</c> — Foundation's WS set, so a no-break space goes too, and a newline stays.</summary>
    public static string TrimmedPromptName(string typed) => Whitespace.TrimWS(typed);

    // MARK: Creation

    /// <summary>
    /// Create a chapter folder in the book root. Quiet on failure, as Swift is — the
    /// folder may already exist or the name may be unusable, and Explorer is the recovery
    /// tool. One level only: an intermediate folder is never created, so a name carrying a
    /// separator fails instead of quietly nesting.
    /// </summary>
    public static bool CreateChapter(string root, string name, IBookFolderFileSystem? files = null)
    {
        files ??= LocalBookFolderFileSystem.Instance;
        // The folder the OS will hold is spelled exactly `name`, so that is what is
        // vetted. Without this a chapter called "CON" or "PRN" would go to Win32's
        // device namespace instead of the book (see IsValidName).
        if (!IsValidName(name)) return false;
        try
        {
            var path = Path.Combine(root, name);
            // Swift appends a path component, which cannot escape; Path.Combine returns a
            // rooted second argument whole, so a name like "\\server\share" would be
            // created outside the folder we hold rights to. Refuse anything not inside.
            if (!BookPaths.IsInside(BookPaths.Standardize(path), BookPaths.Standardize(root))) return false;
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            // withIntermediateDirectories: false, and Foundation fails on an existing
            // folder too — Directory.CreateDirectory would silently accept both.
            if (parent is null || !files.DirectoryExists(parent) || files.FileExists(path)) return false;
            files.CreateDirectory(path);
            return true;
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            return false;
        }
    }

    /// <summary>
    /// Create "&lt;name&gt;.md" in <paramref name="folder"/> (the root or a chapter), seeded
    /// with the matching level-1 heading so it renders sensibly the moment it opens.
    /// Never overwrites: an existing name — a folder's included — returns null. Quiet on
    /// failure, as Swift is. The returned path is the one that was written, not a
    /// standardized spelling, so the caller can select it as it selects listing paths.
    /// </summary>
    public static string? CreateArticle(string folder, string name, IBookFolderFileSystem? files = null)
    {
        files ??= LocalBookFolderFileSystem.Instance;
        if (name.Length == 0) return null;
        // The FILE NAME is vetted, not the typed stem: what Windows refuses is the name
        // that lands on disk, and the extension is part of it. "Draft " is legal (the
        // trailing space stops being trailing once ".md" follows, and Swift creates it);
        // "CON" is not — File.WriteAllBytes("CON.md") writes to the console device and
        // reports success, so without this the article is never created and the caller
        // is handed a path to a file that does not exist.
        if (!IsValidName(name + ".md")) return null;
        try
        {
            // appendingPathExtension("md") is unconditional in Swift: "Scene.md" typed
            // into the prompt becomes "Scene.md.md", which is what the Mac creates.
            var path = Path.Combine(folder, name + ".md");
            // As in CreateChapter: an absolute name must not walk out of the book.
            if (!BookPaths.IsInside(BookPaths.Standardize(path), BookPaths.Standardize(folder))) return null;
            if (files.FileExists(path)) return null;
            files.WriteAllBytes(path, SeedEncoding.GetBytes("# " + name + "\n"));
            return path;
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            return null;
        }
    }

    // MARK: Rename and delete

    /// <summary>
    /// Rename the article or chapter at <paramref name="path"/> to the display name
    /// <paramref name="stem"/>: the ordering prefix and the extension survive
    /// (<see cref="BookNaming.RenamedName"/>), so only the readable half changes. A name
    /// that is already what was asked for is <see cref="RenameOutcome.Unchanged"/> and
    /// touches nothing; a collision comes back as <see cref="RenameOutcome.Failed"/> with
    /// the OS message.
    /// </summary>
    public static RenameOutcome RenameItem(string path, string stem, out string? failure, IBookFolderFileSystem? files = null)
    {
        files ??= LocalBookFolderFileSystem.Instance;
        failure = null;
        if (!IsValidName(stem)) return RenameOutcome.InvalidName;
        var name = BookPaths.Name(path);
        var renamed = BookNaming.RenamedName(name, stem);
        if (string.Equals(renamed, name, StringComparison.Ordinal)) return RenameOutcome.Unchanged;
        var folder = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        // Only a volume root has no parent, and a volume root is not an item in a book.
        if (folder is null) return RenameOutcome.Failed;
        var target = Path.Combine(folder, renamed);
        try
        {
            if (string.Equals(renamed, name, StringComparison.OrdinalIgnoreCase))
            {
                // A case-only rename on a case-insensitive volume: the destination
                // resolves to the source. Measured on APFS with .NET 10, File.Move and
                // Directory.Move both accept that; on Windows Directory.Move has been
                // seen to refuse it outright. Go through the reorder executor's hidden
                // staging name, so the rename lands the same way on every volume.
                var temp = Path.Combine(folder, RenumberPlan.TempName());
                files.Move(path, temp);
                try
                {
                    files.Move(temp, target);
                }
                catch (Exception)
                {
                    // Nothing may be left under a temporary name.
                    try { files.Move(temp, path); } catch (Exception) { /* the alert names the first failure */ }
                    throw;
                }
            }
            else
            {
                files.Move(path, target);
            }
            return RenameOutcome.Renamed;
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            failure = e.Message;
            return RenameOutcome.Failed;
        }
    }

    /// <summary>
    /// Delete the article (a file) or chapter (a folder, with everything in it) at
    /// <paramref name="path"/>. Permanent — Swift's <c>removeItem</c> does not go through
    /// the Trash and this does not go through the Recycle Bin. Asking first is the
    /// caller's job. A path that is already gone fails, as Foundation's does, rather than
    /// reporting a delete that never happened.
    /// </summary>
    public static bool DeleteItem(string path, out string? failure, IBookFolderFileSystem? files = null)
    {
        files ??= LocalBookFolderFileSystem.Instance;
        failure = null;
        try
        {
            // File.Delete is a no-op for a missing file; Foundation throws. Check first,
            // and report it with the BCL's own wording rather than inventing one.
            if (!files.FileExists(path))
            {
                failure = new FileNotFoundException(null, path).Message;
                return false;
            }
            files.Delete(path);
            return true;
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            failure = e.Message;
            return false;
        }
    }

    // MARK: Reading

    /// <summary>
    /// An article's text for a compile or a structured export, or null when it cannot be
    /// read. DIVERGENCE, deliberate: Swift decodes UTF-8 then Latin-1 here — a looser,
    /// separate decoder from the editor's <see cref="PlainTextCodec"/> — so a Windows-1251
    /// article the book editor opens as Cyrillic compiles into the PDF/EPUB/LaTeX as
    /// Latin-1 mojibake on the Mac. This port reads both sides through the codec, so a
    /// legacy article compiles as what it says. Latin-1 is still the last resort inside
    /// the codec, so nothing that compiled before fails now.
    /// </summary>
    public static string? ReadArticle(string path, IBookFolderFileSystem? files = null)
    {
        files ??= LocalBookFolderFileSystem.Instance;
        try
        {
            return PlainTextCodec.Decode(files.ReadAllBytes(path))?.Text;
        }
        catch (Exception e) when (IsFileFailure(e))
        {
            return null;
        }
    }

    // MARK: Selection (pure)

    /// <summary>
    /// Where <paramref name="path"/> sits in a reading order, compared on standardized
    /// paths: Previous / Next step through this, and the reload's selection fallback
    /// re-points the selection with it. Swift compares <c>standardizedFileURL.path</c> for
    /// the same reason a raw string compare will not do — the selection and the listing
    /// can spell one article two ways. Null when it is not in the order, or nothing is
    /// selected.
    /// </summary>
    public static int? OrderIndex(IReadOnlyList<BookArticle> order, string? path)
    {
        if (path is null) return null;
        var target = BookPaths.Standardize(path);
        for (var index = 0; index < order.Count; index++)
        {
            if (string.Equals(BookPaths.Standardize(order[index].Path), target, BookPaths.Comparison)) return index;
        }
        return null;
    }

    /// <summary>
    /// The article the selection should fall to once everything at or under
    /// <paramref name="path"/> is deleted: the first survivor after the deleted block in
    /// reading order, else the last one before it. Computed <em>before</em> the delete,
    /// while the book snapshot still has the row. Null when nothing in the book dies with
    /// it (an empty chapter, or a path from another book).
    /// </summary>
    public static string? DeletionNeighbor(Book book, string path)
    {
        var deleted = BookPaths.Standardize(path);
        var order = BookModel.ReadingOrder(book).Select(article => BookPaths.Standardize(article.Path)).ToList();
        bool Dies(string candidate) =>
            string.Equals(candidate, deleted, BookPaths.Comparison) || BookPaths.IsInside(candidate, deleted);
        var first = order.FindIndex(Dies);
        if (first < 0) return null;
        for (var index = first; index < order.Count; index++)
        {
            if (!Dies(order[index])) return order[index];
        }
        return first > 0 ? order[first - 1] : null;
    }

    /// <summary>
    /// <paramref name="path"/> relative to the book root — the durable form of "where I
    /// was writing", remembered across launches. Empty when the path is not inside the
    /// book (Swift returns "" for that, and for having no book open). Separators are the
    /// platform's, so <see cref="PathIn"/> reads back what this wrote.
    /// </summary>
    public static string RelativePath(string root, string path)
    {
        var rootPath = BookPaths.Standardize(root);
        var full = BookPaths.Standardize(path);
        if (!BookPaths.IsInside(full, rootPath)) return "";
        var start = rootPath.EndsWith(Path.DirectorySeparatorChar) ? rootPath.Length : rootPath.Length + 1;
        return full[start..];
    }

    /// <summary>
    /// The inverse: the absolute path of a remembered relative path, or null when it is
    /// empty or does not land inside the book. The guard matters — a stored value from
    /// another book, or an absolute path, would otherwise walk straight out of the folder
    /// the app has rights to (<c>Path.Combine</c> returns a rooted second argument whole).
    /// </summary>
    public static string? PathIn(string root, string relativePath)
    {
        if (relativePath.Length == 0) return null;
        try
        {
            var rootPath = BookPaths.Standardize(root);
            var full = BookPaths.Standardize(Path.Combine(rootPath, relativePath));
            return BookPaths.IsInside(full, rootPath) ? full : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// The sibling group of the article or chapter at <paramref name="path"/> — its row
    /// name, the folder its renames happen in, every sibling's on-disk name in displayed
    /// order and its own index: everything Rename…, Move Up and Move Down need, recovered
    /// from the book snapshot (Swift's <c>articleContext</c>, plus the chapter group the
    /// sidebar header's menu passes by hand). Null when the path is not in the book.
    /// </summary>
    public static BookSiblings? SiblingsOf(Book book, string path)
    {
        var target = BookPaths.Standardize(path);

        BookSiblings? Among(IReadOnlyList<BookArticle> articles, string folder)
        {
            for (var index = 0; index < articles.Count; index++)
            {
                if (!string.Equals(BookPaths.Standardize(articles[index].Path), target, BookPaths.Comparison)) continue;
                return new BookSiblings(
                    articles[index].Name,
                    folder,
                    articles.Select(article => BookPaths.Name(article.Path)).ToList(),
                    index);
            }
            return null;
        }

        if (Among(book.Articles, book.Root) is { } root) return root;
        foreach (var chapter in book.Chapters)
        {
            if (Among(chapter.Articles, chapter.Path) is { } hit) return hit;
        }
        // A chapter's siblings are the book's chapter list, renumbered inside the root.
        for (var index = 0; index < book.Chapters.Count; index++)
        {
            if (!string.Equals(BookPaths.Standardize(book.Chapters[index].Path), target, BookPaths.Comparison)) continue;
            return new BookSiblings(
                book.Chapters[index].Name,
                book.Root,
                book.Chapters.Select(chapter => chapter.Name).ToList(),
                index);
        }
        return null;
    }

    private static bool IsFileFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or NotSupportedException
            or ArgumentException or System.Security.SecurityException;
}
