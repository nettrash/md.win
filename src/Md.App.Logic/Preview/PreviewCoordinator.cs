using Md.App.Logic.Seams;

namespace Md.App.Logic.Preview;

/// <summary>
/// Md.Core's HTML writer as the coordinator needs it. It stays a seam now that
/// <c>Md.Core.Markdown.MarkdownHtml</c> exists (core-api.md Part B) so a coordinator test can hand
/// in its own writer and assert what was rendered without going through Core; the app's
/// implementation is <c>App.CoreDocumentHtml</c>, whose body is the one line
/// <c>MarkdownHtml.Document(source, title, dark)</c>, set into <see cref="DocumentHtml.Default"/>
/// at start-up.
/// </summary>
public interface IDocumentHtml
{
    /// <summary>The whole page, Core's bytes, <c>export: false</c>. The Windows font style is added by the caller.</summary>
    string Document(string source, string title, bool dark);
}

/// <summary>
/// Where a <see cref="PreviewCoordinator"/> built with the two-argument constructor gets its HTML.
/// Md.App sets this once at start-up; a test hands its own writer to the three-argument constructor
/// instead, so nothing here leaks between tests.
/// </summary>
public static class DocumentHtml
{
    sealed class NotWired : IDocumentHtml
    {
        public string Document(string source, string title, bool dark) =>
            throw new InvalidOperationException(
                "No HTML writer: set Md.App.Logic.Preview.DocumentHtml.Default (or pass an IDocumentHtml to PreviewCoordinator) before the first Update.");
    }

    public static IDocumentHtml Default { get; set; } = new NotWired();
}

/// <summary>
/// The Mac coordinator, verbatim (§4.5) — the one piece of preview behaviour that is pure policy, so
/// it lives here and <c>PreviewHost</c> only owns the WebView2.
///
/// The shape: the whole page is regenerated on the host for every change (no DOM patching, ever),
/// the first load and a change of document are immediate, everything else is coalesced by a 350 ms
/// trailing debounce into one <c>Reload()</c> that first reads <c>window.scrollY</c> and restores it
/// when the new page has loaded. That debounce is also what keeps Mermaid, Graphviz and PlantUML
/// from re-running on every keystroke.
///
/// Two Windows additions to the Mac's four methods, both from Android's coordinator:
/// <see cref="Show"/>/<see cref="Hide"/> carry "stale while collapsed" — in Edit mode the WebView2
/// stays in the tree, the coordinator only records that the page is older than
/// <see cref="IPreviewSurface.Html"/>, and the next show reloads once — and a navigation that
/// arrives mid-load is parked and performed when the load finishes.
/// </summary>
public sealed class PreviewCoordinator
{
    /// <summary>The Mac's 0.35 s.</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(350);

    readonly IPreviewSurface _surface;
    readonly IScheduler _scheduler;
    readonly IDocumentHtml _html;

    (bool Dark, string Title, string Text)? _lastKey;
    string? _lastToken;
    Guid? _lastNavigationId;
    (PreviewNavigation Navigation, Action<Guid>? OnHandled)? _parked;
    IDisposable? _debounce;
    bool _loadedOnce;
    bool _loading;
    bool _stale;

    public PreviewCoordinator(IPreviewSurface surface, IScheduler scheduler)
        : this(surface, scheduler, DocumentHtml.Default) { }

    public PreviewCoordinator(IPreviewSurface surface, IScheduler scheduler, IDocumentHtml documentHtml)
    {
        _surface = surface ?? throw new ArgumentNullException(nameof(surface));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        _html = documentHtml ?? throw new ArgumentNullException(nameof(documentHtml));
        _surface.NavigationCompleted += OnNavigationCompleted;
    }

    /// <summary>The position the next successful load restores, in CSS pixels; 0 means "do not restore".</summary>
    public double SavedScrollY { get; private set; }

    /// <summary>True while the served HTML is newer than the page — the pane was collapsed when it changed.</summary>
    public bool IsStale => _stale;

    /// <summary>
    /// New text, title or appearance. <paramref name="token"/> identifies the document: document
    /// windows pass null, the Book pane passes the article's path, and a change of it is a fresh
    /// page at the top rather than a scroll-preserving reload.
    /// </summary>
    public void Update(string text, string title, bool dark, string? token)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(title);

        var newDocument = !string.Equals(token, _lastToken, StringComparison.Ordinal);

        // The Mac's "\(dark)|\(title)|\(text)" dedupe: a re-render that would produce the same page
        // is not one (tuple equality over strings is ordinal). A new document reloads even when the
        // text is identical, because the token moved.
        var key = (dark, title, text);
        if (!newDocument && _lastKey == key) return;

        // Write first, remember after. If the writer throws — Md.Core on some input, or this
        // assembly's own NotWired default before Md.App has set DocumentHtml.Default — a key
        // committed up front would make the identical retry a no-op and that document would never
        // preview again.
        var html = ScreenHtml.WithWindowsFonts(_html.Document(text, title, dark));

        _lastToken = token;
        _lastKey = key;
        _surface.Html = html;
        CancelDebounce();

        // Collapsed pane: the HTML is current, the page is not. Show() reloads it once.
        if (!_surface.IsShown)
        {
            _stale = true;
            return;
        }

        if (!_loadedOnce || newDocument)
        {
            _loadedOnce = true;
            SavedScrollY = 0;
            LoadIndex();
            return;
        }

        _debounce = _scheduler.After(DebounceDelay, () => _ = ReloadPreservingScrollAsync());
    }

    /// <summary>The pane became visible again: reload once if the page is behind the HTML.</summary>
    public void Show()
    {
        if (!_stale) return;
        _loadedOnce = true;
        SavedScrollY = 0;
        LoadIndex();
    }

    /// <summary>
    /// The pane was collapsed. A reload that was still waiting is dropped — reloading a control
    /// nobody can see costs a full engine pass — and the page is left marked stale so
    /// <see cref="Show"/> does it once, later.
    /// </summary>
    public void Hide()
    {
        if (_debounce is null) return;
        CancelDebounce();
        _stale = true;
    }

    /// <summary>
    /// Scroll to a heading. Performed once per <see cref="PreviewNavigation.Id"/>; a request that
    /// arrives while a load is in flight is parked and performed when that load finishes, because a
    /// script evaluated against the outgoing page scrolls nothing.
    /// </summary>
    public void Navigate(PreviewNavigation navigation, Action<Guid>? onHandled = null)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        if (navigation.Id == _lastNavigationId) return;
        _lastNavigationId = navigation.Id;

        if (_loading)
        {
            _parked = (navigation, onHandled);
            return;
        }

        Perform(navigation, onHandled);
    }

    void Perform(PreviewNavigation navigation, Action<Guid>? onHandled)
    {
        _ = _surface.EvalAsync(Scripts.JumpToSlug(navigation.Slug));
        if (onHandled is not null) _scheduler.Post(() => onHandled(navigation.Id));
    }

    void LoadIndex()
    {
        BeginLoad();
        _surface.Navigate(AssetOrigin.IndexUrl);
    }

    /// <summary>The page is about to become the served HTML, so it is no longer behind it.</summary>
    void BeginLoad()
    {
        _loading = true;
        _stale = false;
    }

    async Task ReloadPreservingScrollAsync()
    {
        _debounce = null;
        double y;
        try
        {
            y = JsonScript.Number(await _surface.EvalAsync(Scripts.ScrollY).ConfigureAwait(true)) ?? 0;
        }
        catch (Exception)
        {
            // A page that cannot answer has no position worth keeping; reload anyway, as the Mac does
            // when evaluateJavaScript hands back an error.
            y = 0;
        }

        SavedScrollY = y;
        BeginLoad();
        _surface.Reload();
    }

    void OnNavigationCompleted(bool isSuccess)
    {
        _loading = false;

        // A failed load has no DOM to scroll and no page to jump into; the parked request stays
        // parked for the load that succeeds.
        if (!isSuccess) return;

        if (SavedScrollY > 0) _ = _surface.EvalAsync(Scripts.ScrollTo(SavedScrollY));

        if (_parked is not { } parked) return;
        _parked = null;
        // Straight to Perform: the id was recorded when the request was parked.
        Perform(parked.Navigation, parked.OnHandled);
    }

    void CancelDebounce()
    {
        _debounce?.Dispose();
        _debounce = null;
    }
}
