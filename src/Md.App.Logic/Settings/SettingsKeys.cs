namespace Md.App.Logic.Settings;

/// <summary>
/// Every <c>ApplicationData.Current.LocalSettings.Values</c> key the app writes (shell-final.md §9),
/// plus the other names under which md keeps state on the machine. PRIVACY.md must enumerate
/// exactly this list: the eight keys, session.json, the FutureAccessList token, the MRU list and the
/// WebView2 user-data folder. Add a key here first; <see cref="All"/> is what the tests pin.
/// </summary>
public static class SettingsKeys
{
    /// <summary>string — the <c>v1</c> per-file view-mode codec (Core <c>ViewModeMemory</c>). Absent ⇒ <c>[]</c>.
    /// Written by <c>ViewModeController.SetMode</c> and the first-save migrate branch; read by <c>ViewModeMemory.Lookup</c>.
    /// ≤ 200 entries ≈ 4.6 KB, under the 8 KB per-value limit; identity = sha256("file:" + canonical path)[0..8] hex.</summary>
    public const string ViewModeMemory = "md.viewModeMemory";

    /// <summary>string — a <c>PageSize.Id</c>. Default <see cref="PdfPageSizeDefault"/>; an unknown id reads as A4.
    /// Written by the two PDF Page Size pickers; read by Share / Export PDF and Book PDF.</summary>
    public const string PdfPageSize = "md.pdfPageSize";

    /// <summary>string — the open book's folder path; <c>""</c> (or absent) = no book. The access grant itself
    /// is the FutureAccessList token <see cref="BookAccessToken"/>; this readable copy serves diagnosis and PRIVACY.
    /// Written by <c>BookLibraryHost.Store / CloseBook</c>; read by Book menu enablement and the Book window.</summary>
    public const string BookBookmark = "md.bookBookmark";

    /// <summary>bool — default <c>false</c>. The Book share menu's "Open Articles in Separate Windows" toggle;
    /// read by <c>BookNavigatorModel</c> and <c>BookStepper</c>.</summary>
    public const string BookOpensInSeparateWindows = "md.bookOpensInSeparateWindows";

    /// <summary>string — the last selected article, <c>/</c>-separated relative to the book root (the Mac's spelling);
    /// default <c>""</c>. Written by <c>BookNavigatorModel.SelectionChanged</c>; read by <c>Reload</c>.</summary>
    public const string BookLastArticle = "md.bookLastArticle";

    /// <summary>string — <c>edit</c> / <c>split</c> / <c>preview</c>; default <see cref="BookViewModeDefault"/>.
    /// App-wide, never per file; written and read by the Book pane.</summary>
    public const string BookViewMode = "md.bookViewMode";

    /// <summary>(Win) string — <c>"WxH"</c> in epx, the last document window size; absent ⇒ <see cref="DocumentWindowSizeDefault"/>.
    /// Written on <c>DocumentWindow</c> close; read when a new document window is placed.</summary>
    public const string DocumentWindowSize = "md.win.windowSize.document";

    /// <summary>(Win) string — <c>"WxH"</c> in epx, the last Book window size; absent ⇒ <see cref="BookWindowSizeDefault"/>.
    /// Written on <c>BookWindow</c> close; read by the Book window.</summary>
    public const string BookWindowSize = "md.win.windowSize.book";

    /// <summary>The eight LocalSettings keys, in §9 order.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        ViewModeMemory, PdfPageSize, BookBookmark, BookOpensInSeparateWindows,
        BookLastArticle, BookViewMode, DocumentWindowSize, BookWindowSize,
    ];

    public const string PdfPageSizeDefault = "a4";
    public const string BookViewModeDefault = "split";
    public const string DocumentWindowSizeDefault = "900x640";
    public const string BookWindowSizeDefault = "1000x700";

    // Not LocalSettings keys — the other names md stores state under (§9 "Not in LocalSettings").

    /// <summary>The <c>StorageApplicationPermissions.FutureAccessList</c> token holding the book folder grant (one fixed token; the list caps at 1000).</summary>
    public const string BookAccessToken = "md.book";

    /// <summary><c>LocalFolder\session.json</c> — saved documents' mode / Zen / placement and the Book window frame (§1.6).</summary>
    public const string SessionFileName = "session.json";

    /// <summary><c>LocalCacheFolder\WebView2</c> — the one user-data folder every WebView2 in the process shares (§4.1).</summary>
    public const string WebView2UserDataFolderName = "WebView2";
}
