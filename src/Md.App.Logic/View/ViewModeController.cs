using Md.App.Logic.Preview;
using Md.Core.Document;
using Md.Core.Markdown;

namespace Md.App.Logic.View;

/// <summary>
/// The Mac's <c>setMode</c> / <c>applyViewModeMemory</c> and the Contents / Notes jumps
/// (shell-design.md §5.2, §5.6; macOS §6.4, §6.5, §6.9), line for line. The single writer of a
/// mode preference and the only caller of <c>ViewModeMemory.Remember</c>.
/// </summary>
/// <remarks>
/// The identity of the open file is <c>ViewModeMemory.IdentityFor(path)</c> — Core's own
/// canonicalisation (full path, links and junctions resolved, upper-cased on Windows), not the
/// design's <c>Sha256Prefix("file:" + FileIdentity.Canonical(path))</c>. One function, because
/// <c>BookArticleOpens.Mark</c> / <c>ClaimOpen</c> take a path and hash it with exactly this one:
/// canonicalising the two halves differently would silently break the book exemption whenever the
/// navigator and the window spelled the same path differently, which is precisely the case the
/// canonicalisation exists for. Core's own note gives the second reason — case folding survives a
/// case-only rename, where the true-case spelling <c>GetFinalPathNameByHandle</c> returns does not.
/// <see cref="IFileIdentity"/> stays what it was for: which window owns which file.
/// </remarks>
public sealed class ViewModeController
{
    readonly DocumentWindowState _state;
    readonly IViewModeStore _memory;
    readonly Func<Guid> _newId;

    /// <summary>
    /// The identity of the file the window is on — Swift's <c>fileURL.map(identity)</c>, cached
    /// rather than re-hashed on every menu pick. That makes <see cref="ApplyMemory"/>'s contract
    /// load-bearing: it must be called on <b>every</b> change of the document's file, which is
    /// exactly when the Mac's <c>.onChange(of: fileURL)</c> fires. Skip one and
    /// <see cref="SetMode"/> would remember under the previous file.
    /// </summary>
    string? _identity;

    /// <param name="newId">
    /// The id stamped on a jump request. Injectable so a test can pin "performed once per id" and
    /// "a newer request replaces the older" without guessing GUIDs.
    /// </param>
    public ViewModeController(DocumentWindowState state, IViewModeStore memory, Func<Guid>? newId = null)
    {
        _state = state;
        _memory = memory;
        _newId = newId ?? Guid.NewGuid;
    }

    /// <summary>
    /// The View menu and Ctrl+1/2/3. In Zen the three commands drive Zen's own write/read switch
    /// instead and store <b>nothing</b> — not the mode, not the memory (§5.4).
    /// </summary>
    public void Select(ViewMode mode)
    {
        if (_state.ZenActive)
        {
            _state.ZenReading = mode == ViewMode.Preview;
            return;
        }
        SetMode(mode);
    }

    /// <summary>
    /// The single writer of a preference: drop any nudge, store the RAW mode, and remember it for
    /// this file unless the window is a book article. Called by <see cref="Select"/> and by
    /// <see cref="ApplyMemory"/>'s open-rule branch; never by Zen and never by a nudge.
    /// </summary>
    public void SetMode(ViewMode mode)
    {
        _state.NavigationMode = null;
        _state.StoredMode = mode;
        if (_state.IsBookArticle || _identity is not { } identity) return;
        ViewModeMemory.Remember(mode, identity, _memory);
    }

    /// <summary>
    /// Runs on window appearance and on every identity change (Save of an untitled document, Save As,
    /// Rename, Move To) — the Mac's <c>.onChange(of: fileURL, initial: true)</c>.
    /// </summary>
    /// <param name="path">The document's file, or null while it has none (untitled, or an Example).</param>
    /// <param name="text">
    /// The document text, for the open rule's <c>isEmptyDocument</c>. The <i>text</i>, never
    /// "has no file": an Example has content and no file and must open in Split.
    /// </param>
    public void ApplyMemory(string? path, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var identity = path is null ? null : ViewModeMemory.IdentityFor(path);
        _identity = identity;

        // Claimed BEFORE the early return: the mark is consumed on the first appearance of the
        // window that the book opened, whatever the window decides afterwards.
        if (path is not null && BookArticleOpens.ClaimOpen(path)) _state.MarkBookArticle();
        if (_state.IsBookArticle)
        {
            _state.LastIdentity = IdentityMemo.Of(identity);
            return;
        }

        // Same file as last time (a Save over the same path): nothing to decide.
        if (_state.LastIdentity.Ran && string.Equals(_state.LastIdentity.Value, identity, StringComparison.Ordinal)) return;

        var hadNoIdentity = _state.LastIdentity.RanWithNoIdentity;
        _state.LastIdentity = IdentityMemo.Of(identity);

        // First save of an untitled document: MIGRATE the mode the writer is already in to the new
        // identity, raw and uncoerced. Re-deciding here would drop them into Split mid-sentence.
        if (hadNoIdentity && identity is not null)
        {
            ViewModeMemory.Remember(_state.StoredMode, identity, _memory);
            return;
        }

        // A different document (including Save As / Rename / Move To of a saved one): drop any nudge
        // and re-run the open rule for the new identity.
        _state.NavigationMode = null;
        SetMode(ViewModeRule.OpenViewMode(
            remembered: identity is null ? null : ViewModeMemory.Lookup(identity, _memory),
            isEmptyDocument: text.Length == 0,
            hasFileIdentity: identity is not null,
            isWide: DocumentWindowState.IsWide));
    }

    /// <summary>
    /// Contents: Split moves both panes, Edit only the caret, Preview only the preview — a reader is
    /// never dropped into the source, so a heading NEVER nudges (§5.6).
    /// </summary>
    public void JumpToHeading(OutlineEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var displayed = _state.EffectiveMode;
        if (displayed != ViewMode.Edit) _state.PreviewNavigation = new PreviewNavigation(_newId(), entry.Slug);
        if (displayed != ViewMode.Preview) _state.EditorJump = new EditorJump(_newId(), entry.Line);
    }

    /// <summary>
    /// Notes: a note never renders, so the editor must be visible — nudge Preview → Edit (never from
    /// Split or Edit). The nudge is assigned only when non-null: writing null back would cancel the
    /// nudge a previous note jump set.
    /// </summary>
    public void JumpToNote(NoteEntry note)
    {
        ArgumentNullException.ThrowIfNull(note);
        if (ViewModeRule.NavigationNudge(_state.EffectiveMode, ViewMode.Edit) is { } nudge) _state.NavigationMode = nudge;
        _state.EditorJump = new EditorJump(_newId(), note.Line);
    }
}
