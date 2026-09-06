using Md.Core.Document;

namespace Md.App.Logic.View;

/// <summary>
/// Whether the per-file memory hook has run for this window, and with which identity — the Swift
/// <c>lastIdentity: String??</c> (shell-design.md §5.1). Three states, and all three matter:
/// never ran (a fresh window), ran on a document with no file (untitled, or an Example), and ran
/// on a file. The middle one is what makes the first Ctrl+S a <i>migration</i> instead of a
/// re-decision, so it cannot be flattened into a plain <c>string?</c>.
/// </summary>
public readonly record struct IdentityMemo
{
    IdentityMemo(bool ran, string? value)
    {
        Ran = ran;
        Value = value;
    }

    /// <summary>The hook has never run for this window (<c>default</c>, so a new state starts here).</summary>
    public static readonly IdentityMemo NeverRan = default;

    public bool Ran { get; }

    /// <summary>The identity the hook last saw; null when it ran on a document with no file.</summary>
    public string? Value { get; }

    public static IdentityMemo Of(string? identity) => new(true, identity);

    /// <summary>Swift's <c>lastIdentity == .some(String?.none)</c> — the untitled-document case.</summary>
    public bool RanWithNoIdentity => Ran && Value is null;
}

// ─────────────────────────── WP5 SHAPES, DECLARED HERE FOR NOW ───────────────────────────
// WP5 owns Preview/PreviewNavigation.cs and Preview/EditorJump.cs (§11.1) with exactly these two
// shapes (§3.3, §5.6). They live here until that package lands so the window state can carry the
// one-shot requests; integrating is deleting the two records below and adding
// `using Md.App.Logic.Preview;` to this file.

/// <summary>A request to scroll the preview to a heading slug, performed once per <see cref="Id"/>.</summary>
public readonly record struct PreviewNavigation(Guid Id, string Slug);

/// <summary>A request to put the caret at the start of a 0-based parser line, performed once per <see cref="Id"/>.</summary>
public readonly record struct EditorJump(Guid Id, int Line);
// ─────────────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Everything one document window remembers about how it is showing its document
/// (shell-design.md §5.1): the raw mode preference, Zen, whether the per-file memory hook has run,
/// the sticky book-article exemption, the transient navigation nudge, the two one-shot jump
/// requests and the derived text the footer and the Go menu read.
///
/// Persisted only through <c>session.json</c> (§1.6) — never through <c>md.viewModeMemory</c>,
/// which is per <i>file</i>. <see cref="Changed"/> fires when any of it actually changes, so the
/// window relays out and the menus re-evaluate without polling.
/// </summary>
public sealed class DocumentWindowState
{
    ViewMode _storedMode = ViewModes.WindowDefault;
    bool _zenActive;
    bool _zenReading;
    ViewMode? _navigationMode;
    PreviewNavigation? _previewNavigation;
    EditorJump? _editorJump;
    DerivedText _derived = DerivedText.Empty;

    /// <summary>
    /// A desktop window is always wide (§5.1): every rule is asked with <c>isWide: true</c> and
    /// Split handles a narrow window by stacking its two panes (§5.3), exactly as the Mac does.
    /// </summary>
    public const bool IsWide = true;

    public event Action? Changed;

    /// <summary>The RAW preference — never <see cref="ViewModeRule.EffectiveMode"/>'s output. Default Split.</summary>
    public ViewMode StoredMode
    {
        get => _storedMode;
        set => Set(ref _storedMode, value);
    }

    /// <summary>Zen is on for this window. Neither this nor <see cref="ZenReading"/> ever reaches the per-file memory.</summary>
    public bool ZenActive
    {
        get => _zenActive;
        set => Set(ref _zenActive, value);
    }

    /// <summary>False = writing (the editor), true = reading (the preview). Zen's own switch, stored nowhere.</summary>
    public bool ZenReading
    {
        get => _zenReading;
        set => Set(ref _zenReading, value);
    }

    /// <summary>The transient nudge a Notes jump sets; cleared by any explicit mode pick and never stored.</summary>
    public ViewMode? NavigationMode
    {
        get => _navigationMode;
        set => Set(ref _navigationMode, value);
    }

    /// <summary>Set by <see cref="ViewModeController.ApplyMemory"/>; read by <see cref="ViewModeController.SetMode"/>.</summary>
    public IdentityMemo LastIdentity { get; set; } = IdentityMemo.NeverRan;

    /// <summary>
    /// This window is showing a book article, so the per-file memory neither decides its mode nor
    /// records it. Sticky for the window's life — Save As included (the Mac is the source of truth;
    /// Android clears its flag there and is the odd one out).
    /// </summary>
    public bool IsBookArticle { get; private set; }

    public PreviewNavigation? PreviewNavigation
    {
        get => _previewNavigation;
        set => Set(ref _previewNavigation, value);
    }

    public EditorJump? EditorJump
    {
        get => _editorJump;
        set => Set(ref _editorJump, value);
    }

    /// <summary>Word/character counts, outline, notes and diagrams — recomputed by <see cref="DerivedTextScheduler"/>.</summary>
    public DerivedText Derived
    {
        get => _derived;
        set => Set(ref _derived, value);
    }

    /// <summary>What the window actually shows: the nudge when one is in force, else the preference.</summary>
    public ViewMode EffectiveMode => ViewModeRule.DisplayedMode(StoredMode, NavigationMode, IsWide);

    /// <summary>One-way: the exemption is claimed once and never given back.</summary>
    public void MarkBookArticle()
    {
        if (IsBookArticle) return;
        IsBookArticle = true;
        Changed?.Invoke();
    }

    /// <summary>Clear the request only when it is still the one that was performed — a newer one may have replaced it.</summary>
    public void PreviewNavigationHandled(Guid id)
    {
        if (PreviewNavigation is { } nav && nav.Id == id) PreviewNavigation = null;
    }

    /// <summary>Same id guard as <see cref="PreviewNavigationHandled"/>: a pane recreated by a mode change would otherwise replay a stale jump.</summary>
    public void EditorJumpHandled(Guid id)
    {
        if (EditorJump is { } jump && jump.Id == id) EditorJump = null;
    }

    void Set<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Changed?.Invoke();
    }
}
