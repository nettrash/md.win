namespace Md.App.Logic;

/// <summary>
/// Every user-facing string the design fixes verbatim (shell-final.md), shared by the work
/// packages so no dialog spells its own. Copied from md.macOS 1.4 byte for byte: U+2026 ellipsis,
/// U+2014 em dash, U+00B7 middle dot, U+201C/U+201D curly quotes and U+2019 where the Mac has
/// them — written as escapes below so a font or an editor cannot quietly swap them. Menu item
/// titles are not here: they live in <c>Commands.CommandTable</c> (WP1).
/// </summary>
public static class Strings
{
    public const string AppName = "md";
    public const string Ellipsis = "\u2026";
    public const string EmDash = "\u2014";

    /// <summary>Untitled windows are numbered per process: "Untitled", "Untitled 2", … (§6.7).</summary>
    public const string Untitled = "Untitled";
    public static string UntitledNumbered(int ordinal) => ordinal <= 1 ? Untitled : $"{Untitled} {ordinal}";

    /// <summary>Appended to the window title while the document differs from its last explicit save (§6.7).</summary>
    public const string EditedSuffix = " \u2014 Edited";

    /// <summary>File ▸ Duplicate's window title (§6.4).</summary>
    public static string DuplicateTitle(string name) => $"{name} copy";

    /// <summary>The Book window's title with no book, and its Window-menu row.</summary>
    public const string Book = "Book";

    /// <summary>The editor's placeholder, same face, size and inset as the text (§3.1).</summary>
    public const string EditorPlaceholder = "# Start writing\u2026";

    /// <summary>The footer, U+00B7, no pluralisation (§5.5).</summary>
    public static string Footer(int words, int characters) => $"{words} words \u00B7 {characters} characters";

    /// <summary>A note whose first non-empty line is missing (§5.6).</summary>
    public const string EmptyNote = "(empty note)";

    public static class Buttons
    {
        public const string OK = "OK";
        public const string Save = "Save";
        public const string DontSave = "Don't Save";
        public const string Cancel = "Cancel";
        public const string Replace = "Replace";
        public const string Create = "Create";
        public const string Delete = "Delete";
        public const string Retry = "Retry";
        public const string SaveAs = "Save As\u2026";
        public const string ReloadFromDisk = "Reload from Disk";
        public const string KeepMyVersion = "Keep My Version";
        public const string Done = "Done";
        public const string ShowWindow = "Show Window";
        public const string OpenBook = "Open Book\u2026";
        public const string ClearMenu = "Clear Menu";
    }

    /// <summary>§6.3–§6.6: the document session's alerts, the conflict and save-error bars, the close dialog, rescue copies.</summary>
    public static class Documents
    {
        public const string CouldNotOpen = "The document could not be opened.";
        public const string TextPackUnreadable = "The TextPack could not be read.";

        /// <summary>The conflict InfoBar's title; its two buttons are <see cref="Buttons.ReloadFromDisk"/> / <see cref="Buttons.KeepMyVersion"/>.</summary>
        public const string FileChangedOnDisk = "The file changed on disk.";

        /// <summary>The save-error InfoBar: "Couldn’t save — {error}" (U+2019, U+2014); Retry is the action button, Save As… sits in the content.</summary>
        public static string CouldNotSave(string error) => $"Couldn\u2019t save \u2014 {error}";

        /// <summary>The close dialog for an untitled, dirty document; buttons Save / Don't Save / Cancel.</summary>
        public static string SaveChangesQuestion(string title) => $"Do you want to save the changes made to the document \u201C{title}\u201D?";

        /// <summary>"{stem} (rescued){ext}", then "(rescued 2)", … — 100 attempts before giving up (§6.3). <paramref name="extensionWithDot"/> as <c>Path.GetExtension</c> returns it.</summary>
        public static string RescueCopyName(string stem, string extensionWithDot, int attempt) =>
            attempt <= 1 ? $"{stem} (rescued){extensionWithDot}" : $"{stem} (rescued {attempt}){extensionWithDot}";

        public static string RescueFailedTitle(string name) => $"Could not save \u201C{name}\u201D";
        public static string RescueKeptMessage(string file) => $"Your text was kept as \u201C{file}\u201D in the same folder.";

        /// <summary>Open Recent: the MRU token no longer resolves (§6.6).</summary>
        public static string FileNotFound(string name) => $"The file \u201C{name}\u201D could not be found.";

        /// <summary>
        /// The fallback for a save failure the file system gave no message of its own for. Two
        /// wordings because one session serves both surfaces (§6.3): a document window says
        /// "document", the book's detail pane says "article" — the Mac's own string.
        /// </summary>
        public const string CouldNotWriteDocument = "The document could not be written.";

        public const string CouldNotWriteArticle = "The article could not be written.";

        public const string RenameTitle = "Rename";
        public const string RenameMessage = "Only the name changes \u2014 the file extension is kept.";
        public const string CouldNotRename = "Could not rename";
        public const string InvalidNameMessage = "A name cannot contain \\ / : * ? \" < > | or end with a dot or a space.";
    }

    /// <summary>§7: alert titles and messages of the exports, the print overlay and the pickers' type labels.</summary>
    public static class Exports
    {
        public const string CouldNotGeneratePdf = "Could not generate PDF";
        public const string CouldNotExportPdf = "Could not export PDF";
        public const string CouldNotExportHtml = "Could not export HTML";
        public const string CouldNotExportLaTeX = "Could not export LaTeX";
        public const string CouldNotExportSvg = "Could not export SVG";
        public const string CouldNotExportTextBundle = "Could not export TextBundle";
        public const string CouldNotExportEpub = "Could not export EPUB";

        public const string PageNotCaptured = "The rendered page could not be captured.";
        public const string PdfPagesNotProduced = "The PDF pages could not be produced.";
        public const string RichImagesNotCaptured = "The rich-content images could not be captured.";
        public const string DiagramNotCaptured = "This diagram couldn't be captured \u2014 it may have failed to render.";

        /// <summary>The FolderPicker's message for Export ▸ TextBundle… (§7.7).</summary>
        public const string ChooseTextBundleFolder = "Choose where to keep the TextBundle";
        public static string FolderExists(string name) => $"A folder named \u201C{name}\u201D already exists.";

        /// <summary>Print overlay: re-show the dialog / dismiss (§7.2).</summary>
        public const string PrintAgain = "Print\u2026";

        // FileSavePicker type labels, in the Mac's order (§6.1, §7).
        public const string MarkdownDocument = "Markdown Document";
        public const string PlainText = "Plain Text";
        public const string PlantUmlDiagram = "PlantUML Diagram";
        public const string GraphvizDotGraph = "Graphviz DOT Graph";
        public const string Pdf = "PDF";
        public const string Html = "HTML";
        public const string Epub = "EPUB";
        public const string Svg = "SVG";
        public const string LaTeX = "LaTeX";
    }

    /// <summary>§8: the Book window's prompts, placeholders, tooltips and alerts.</summary>
    public static class Books
    {
        public const string NoBookOpen = "No Book Open";
        public const string NoBookOpenMessage = "A book is a folder: its subfolders are chapters and its Markdown files are articles.";

        public const string NewBookTitle = "New Book";
        public const string NewBookDefaultName = "My Book";
        public const string NewBookMessage = "Name your book and choose where to keep it. Chapters are folders inside it; articles are Markdown files.";
        public const string CouldNotCreateBook = "Could not create book";
        public const string ChooseExampleBookFolder = "Choose where to keep the example book. It is an ordinary book folder \u2014 chapters inside, Markdown articles in each \u2014 yours to edit.";
        public const string ChooseBookFolder = "Choose a folder to open as a book. Its subfolders are chapters; its Markdown files are articles.";
        public const string CouldNotUnpackExampleBook = "Could not unpack example book";
        public const string CouldNotReorder = "Could not reorder";

        public const string NewChapterTitle = "New Chapter";
        public const string NewChapterMessage = "A chapter is a folder. Start the name with a number (\"02 \u2026\") to place it in the reading order.";
        public const string NewArticleTitle = "New Article";
        public const string NewArticleMessage = "A Markdown file is created, started with a matching heading.";
        public const string RenameMessage = "Only the name changes \u2014 the ordering number and the file extension are kept.";
        public static string DeleteQuestion(string name) => $"Delete \u201C{name}\u201D?";
        public const string DeleteChapterMessage = "The chapter folder and every article in it will be deleted.";
        public const string DeleteArticleMessage = "The article file will be deleted.";

        // Sidebar rows and context menu (§8.3).
        public const string NewArticleRow = "New Article\u2026";
        public const string NewChapterButton = "New Chapter\u2026";
        public const string OpenInNewWindow = "Open in New Window";
        public const string RenameItem = "Rename\u2026";
        public const string MoveUp = "Move Up";
        public const string MoveDown = "Move Down";
        public const string DeleteItem = "Delete\u2026";

        // Tooltips (§8.3, §8.4).
        public const string NewChapterTooltip = "Create a new chapter folder in the book";
        public const string ViewModeTooltip = "Switch between editing, split and preview";
        public const string PreviousArticleTooltip = "The previous article in reading order (Ctrl+Alt+Up)";
        public const string NextArticleTooltip = "The next article in reading order (Ctrl+Alt+Down)";
        public const string ContentsTooltip = "Jump to a heading";
        public const string NotesTooltip = "Jump to a private author note";
        public const string ShareTooltip = "Compile the whole book into a PDF, an EPUB, or print it";
        public const string OpenArticlesInSeparateWindows = "Open Articles in Separate Windows";

        // Detail-pane stages (§8.5).
        public const string HandoffTitle = "Open in Its Own Window";
        public static string HandoffMessage(string title) => $"\u201C{title}\u201D is open as a document window, and that window owns the file while it stays open. Close it to write here again.";
        public const string UnreadableTitle = "Could Not Read the Article";
        public static string UnreadableMessage(string title) => $"\u201C{title}\u201D could not be read. It may have been moved or deleted outside the book.";
        public const string EmptyTitle = "Select an Article";
        public const string EmptyMessage = "Choose an article in the sidebar to write here. Ctrl+Alt+Up and Ctrl+Alt+Down move through the book in reading order.";

        // Outputs (§8.7).
        public const string CouldNotCompileBook = "Could not compile book";
        public const string NoBookAccessible = "No book is open, or the book folder is not accessible.";
        public static string ArticleUnreadable(string name) => $"The article \"{name}\" could not be read.";
    }

    /// <summary>§5.4: the Zen capsule's tooltips.</summary>
    public static class Zen
    {
        public const string Write = "Write";
        public const string Read = "Read";
        public const string Exit = "Exit Zen Mode";
    }

    /// <summary>§3.5: the find bar's buttons.</summary>
    public static class Find
    {
        public const string Next = "Next";
        public const string Previous = "Previous";
        public const string Done = "Done";
    }

    /// <summary>§2.8: Help ▸ md Help / Privacy Policy / About md.</summary>
    public static class Help
    {
        public const string SupportUrl = "https://nettrash.me/msstore/md/support.html";
        public const string PrivacyUrl = "https://nettrash.me/msstore/md/privacy.html";
        public const string Copyright = "\u00A9 2026 nettrash. MIT licensed.";

        /// <summary>§2.8: the line above <see cref="Engines"/> in the About dialog. "Bundled" and "open source" are the family's wording (facts/windows-facts.md); the app never claims it has no third-party dependencies, and never that the licence texts are published.</summary>
        public const string EnginesHeading = "Bundled open source engines:";

        /// <summary>The bundled engines, as About lists them. Never the phrase "no third-party dependencies".</summary>
        public static readonly IReadOnlyList<string> Engines =
        [
            "KaTeX 0.17.0 + mhchem",
            "Mermaid 11.16.0",
            "Graphviz 14.1.1 via Viz.js 3.24.0",
            "PlantUML 1.2026.4beta4",
            "highlight.js 11.11.1",
        ];
    }
}
