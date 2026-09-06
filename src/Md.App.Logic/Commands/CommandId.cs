namespace Md.App.Logic.Commands;

/// <summary>
/// Every command the shell defines (shell-final.md §2, §13.2). One id per row of the menu tables —
/// including the rows that expand into many menu items at run time (<see cref="OpenRecentEntry"/>,
/// <see cref="Example"/>, <see cref="Contents"/>, <see cref="Notes"/>, <see cref="ExportDiagramSvg"/>,
/// <see cref="PdfPageSize"/>, <see cref="ActivateWindow"/>): the id names the *command*, the argument
/// passed to <see cref="CommandDispatcher.TryInvoke"/> names which row. Ids are never persisted, so
/// the numeric values may move; the names are the contract every work package codes against.
/// </summary>
public enum CommandId
{
    // File (§2.2)
    New,
    Open,
    OpenRecentEntry,
    ClearRecent,
    OpenTextBundleFolder,
    Example,
    ExampleBook,
    Close,
    Save,
    SaveAs,
    Duplicate,
    Rename,
    MoveTo,
    RevertToSaved,
    Print,
    ShareSource,
    ShareRenderedPdf,
    ExportPdf,
    ExportHtml,
    ExportEpub,
    ExportLaTeX,
    ExportTextBundle,
    ExportDiagramSvg,
    PdfPageSize,
    Exit,

    // Edit (§2.4)
    Undo,
    Redo,
    Cut,
    Copy,
    Paste,
    Delete,
    SelectAll,
    Find,
    FindNext,
    FindPrevious,
    UseSelectionForFind,

    // View (§2.5)
    ViewEdit,
    ViewSplit,
    ViewPreview,
    ZenMode,
    ShowSidebar,
    FullScreen,

    // Book (§2.6)
    NewBook,
    OpenBook,
    ShowBook,
    CloseBook,
    ShareBookPdf,
    PrintBook,
    ExportBookPdf,
    ExportBookEpub,
    ExportBookLaTeX,

    // Go (§2.7)
    PreviousArticle,
    NextArticle,
    Contents,
    Notes,

    // Window and Help (§2.8)
    Minimize,
    Zoom,
    ActivateWindow,
    Help,
    PrivacyPolicy,
    About,

    /// <summary>Not a menu row: a root accelerator that is enabled only while Zen is on or the find bar is open (§2.9), so the editor keeps Esc otherwise.</summary>
    Escape,
}

/// <summary>
/// What the menu builder makes of a <see cref="CommandSpec"/>. The WinUI types are named for
/// orientation only — <c>Md.App.Logic</c> never references them (§13.1).
/// </summary>
public enum CommandKind
{
    /// <summary><c>MenuFlyoutItem</c>.</summary>
    Item,

    /// <summary><c>ToggleMenuFlyoutItem</c>; the tick comes from <see cref="CommandEnablement.IsChecked"/>.</summary>
    Toggle,

    /// <summary>One <c>MenuFlyoutItem</c> per snapshot row (Open Recent, Examples, Contents, Notes, Diagram as SVG).</summary>
    DynamicItems,

    /// <summary>One <c>ToggleMenuFlyoutItem</c> per snapshot row, ticked on the current one (the Window list).</summary>
    DynamicToggles,

    /// <summary>One <c>RadioMenuFlyoutItem</c> per snapshot row, sharing <see cref="CommandTable.PdfPageSizeGroupName"/> (PDF Page Size).</summary>
    DynamicRadios,

    /// <summary>No menu row at all — the command exists only as a root accelerator (<see cref="CommandId.Escape"/>).</summary>
    Accelerator,
}
