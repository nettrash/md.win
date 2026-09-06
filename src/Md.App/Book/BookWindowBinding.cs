// The three services the Book window needs that need the Book window (shell-design.md §7.9, §8.1,
// §8.7). `WinUiAlerts`, `Pickers` and `DocumentExports` are all built around one `Window`, and
// `BookWindowServices` — the record BookWindow takes — wants them before that window exists.
//
// The knot is untied here rather than by handing the window a service locator: the binding is
// created empty, passed in the record, and bound to the window the moment its constructor returns.
// Nothing can call into it before then — the book is listed from `Root.Loaded`, every prompt comes
// from a click, and every output from a command — so the gap is not observable, and an unbound call
// is inert rather than a NullReferenceException in a menu handler.
using Md.App.Export;
using Md.App.Logic.Books;
using Md.App.Logic.Seams;
using Md.App.Services;
using Md.Core.Book;
using Md.Core.Document;
using Microsoft.UI.Xaml;

namespace Md.App.Book;

/// <summary>
/// The Book window's alerts, pickers and outputs, bound once the window exists. The
/// <see cref="IBookOutputs"/> half is the <c>BookExportOutputs</c> <see cref="BookOutput"/>'s
/// documentation names: every whole-book output is one <c>ExportPipeline</c> call on the Book
/// window's own renderer, tracked so the window's close waits for it (§1.4).
/// </summary>
internal sealed class BookWindowBinding : IAlerts, IPickers, IBookOutputs
{
    IAlerts? _alerts;
    IPickers? _pickers;
    DocumentExports? _exports;
    Action<Task>? _track;
    Action<bool>? _showOverlay;

    /// <summary>
    /// Bind to the window that owns them.
    /// </summary>
    /// <param name="showOverlay">
    /// Shows and hides the print overlay's host slot. A collapsed parent hides the WebView2 the
    /// print preview is drawn inside, and Chromium draws no preview at all for a hidden control
    /// (§7.2) — so Print Book turns the slot on for exactly as long as the sheet is up.
    /// </param>
    public void Bind(Window window, DocumentExports exports, Action<Task> track, Action<bool> showOverlay)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(showOverlay);

        var alerts = new WinUiAlerts(window);
        _alerts = alerts;
        _pickers = new Pickers(window, alerts);
        _exports = exports;
        _track = track;
        _showOverlay = showOverlay;
    }

    // ── IAlerts (§7.9) ────────────────────────────────────────────────────────────────────────

    public Task WarnAsync(string title, string message) =>
        _alerts?.WarnAsync(title, message) ?? Task.CompletedTask;

    public Task<string?> PromptNameAsync(string title, string message, string initial, string acceptLabel) =>
        _alerts?.PromptNameAsync(title, message, initial, acceptLabel) ?? Task.FromResult<string?>(null);

    public Task<bool> ConfirmDeleteAsync(string title, string message) =>
        _alerts?.ConfirmDeleteAsync(title, message) ?? Task.FromResult(false);

    public Task<CloseChoice> AskSaveChangesAsync(string title) =>
        _alerts?.AskSaveChangesAsync(title) ?? Task.FromResult(CloseChoice.Cancel);

    public Task<bool> ConfirmReplaceAsync(string message) =>
        _alerts?.ConfirmReplaceAsync(message) ?? Task.FromResult(false);

    // ── IPickers (§6.1) ───────────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> OpenFilesAsync(IReadOnlyList<string> extensions) =>
        _pickers?.OpenFilesAsync(extensions) ?? Task.FromResult<IReadOnlyList<string>>([]);

    public Task<string?> SaveFileAsync(string suggestedName, IReadOnlyList<(string Label, IReadOnlyList<string> Extensions)> choices, string defaultExtension) =>
        _pickers?.SaveFileAsync(suggestedName, choices, defaultExtension) ?? Task.FromResult<string?>(null);

    public Task<string?> PickFolderAsync(string? title) =>
        _pickers?.PickFolderAsync(title) ?? Task.FromResult<string?>(null);

    // ── IBookOutputs (§8.7) ───────────────────────────────────────────────────────────────────

    public Task SharePdfAsync(string source, string title, PageSize pageSize) =>
        Run(exports => exports.Pipeline.SharePdfAsync(source, title, pageSize));

    public Task ExportPdfAsync(string source, string title, PageSize pageSize) =>
        Run(exports => exports.Pipeline.ExportPdfAsync(source, title, pageSize));

    public Task PrintAsync(string source, string title) =>
        Run(async exports =>
        {
            _showOverlay?.Invoke(true);
            try
            {
                await exports.PrintAsync(source, title);
            }
            finally
            {
                _showOverlay?.Invoke(false);
            }
        });

    public Task ExportEpubAsync(StructuredBook book) =>
        Run(exports => exports.Pipeline.ExportBookEpubAsync(book));

    public Task ExportLaTeXAsync(StructuredBook book) =>
        Run(exports => exports.Pipeline.ExportBookLaTeXAsync(book));

    /// <summary>Start one output and let the window's close wait for it (§1.4).</summary>
    Task Run(Func<DocumentExports, Task> flow)
    {
        if (_exports is not { } exports) return Task.CompletedTask;
        var output = flow(exports);
        _track?.Invoke(output);
        return output;
    }
}
