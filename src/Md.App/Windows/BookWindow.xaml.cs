// The Book window (shell-design.md §8): one app-wide window over one book folder, hosting one
// TextFileSession in article mode. Every rule it obeys lives in Md.App.Logic.Books — which rows the
// sidebar has, where the selection goes after a rename, whether a step is possible, what an output
// compiles — so this file is wiring and rendering, which is the half a Mac cannot compile.
//
// The one shape worth naming: everything flows through Render(). The navigator, the session and the
// derived-text scheduler all raise a change event; Render reads the three of them and applies the
// result to the controls. Nothing else writes to a control, so there is no path where the sidebar,
// the toolbar and the detail pane can disagree about which article is open.
using Md.App.Book;
using Md.App.Controls;
using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Commands;
using Md.App.Logic.Documents;
using Md.App.Logic.Preview;
using Md.App.Logic.Seams;
using Md.App.Logic.Settings;
using Md.App.Logic.Text;
using Md.App.Logic.View;
using Md.App.Menus;
using Md.App.Services;
using Md.Core.Document;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using CoreBook = Md.Core.Book.Book;

// NOTE FOR WP3, and the reason this type is not in a namespace called "Md.App.Windows": declaring
// one makes the identifier `Windows` inside `namespace Md.App` resolve to it, and every
// `Windows.Storage` / `Windows.UI` / `Windows.ApplicationModel` reference in App.xaml.cs,
// WebViewEnvironment.cs, PreviewHost.cs and AboutDialog.cs stops compiling ("the type or namespace
// name 'Storage' does not exist in the namespace 'Md.App.Windows'"). The folder is Windows/, as
// §11.2 asks; the namespace stays Md.App. DocumentWindow, WindowManager and TitleBarTint must do
// the same, or every one of those files needs a `global::` prefix.
namespace Md.App;

/// <summary>
/// Everything the Book window needs from the app. A record rather than a service locator so the
/// window's dependencies are the list of things WP3 must have built by the time it shows it.
/// </summary>
/// <param name="Outputs">WP6's export pipeline behind <see cref="IBookOutputs"/>.</param>
/// <param name="OpenPath">WindowManager.OpenPath — Open in New Window, after the handoff.</param>
/// <param name="ActivateWindow">WindowManager.Activate — the handoff pane's "Show Window".</param>
/// <param name="Decorate">
/// Optional: WP3's title-bar tint (<c>TitleBarTint</c> is its file, not this package's), run once
/// the window exists.
/// </param>
internal sealed record BookWindowServices(
    ISettingsStore Settings,
    IAlerts Alerts,
    IPickers Pickers,
    IDocumentRegistry Registry,
    IScheduler Scheduler,
    IClock Clock,
    IUiThread UiThread,
    IFileSystem FileSystem,
    IFileIdentity Identity,
    IWordCounter WordCounter,
    IBookOutputs Outputs,
    Action<string> OpenPath,
    Action<Guid> ActivateWindow,
    IReadOnlyList<Example> Examples,
    Action<Window>? Decorate = null);

internal sealed partial class BookWindow : Window
{
    // Segoe Fluent Icons for the Mac's SF Symbols (§10).
    const string EditGlyph = "\uE70F";
    const string SplitGlyph = "\uE9B3";
    const string PreviewGlyph = "\uE7B3";
    const string PreviousGlyph = "\uE70E";
    const string NextGlyph = "\uE70D";
    const string ContentsGlyph = "\uE8FD";
    const string NotesGlyph = "\uE70B";
    const string ShareGlyph = "\uE72D";
    const string NewChapterGlyph = "\uE8F4";
    const string BooksGlyph = "\uE736";
    const string WindowGlyph = "\uE7C4";
    const string WarningGlyph = "\uE7BA";

    // §8.1: the sidebar is 240 epx and does not resize — WinUI ships no splitter and the toolkits
    // are off limits, so the Mac's 200–320 range collapses to its ideal (§14 row 22).
    const double PaneWidth = 240;
    const double PlaceholderIconEpx = 36;
    const double PlaceholderTitleEpx = 22.7;
    const double PlaceholderMessageEpx = 16;
    const double PlaceholderMaxWidth = 420;

    readonly BookWindowServices _services;
    readonly BookLibraryHost _library;
    readonly FileSystemWatcherAdapter _watcher;
    readonly TextFileSession _session;
    readonly BookNavigatorModel _navigator;
    readonly BookOutput _output;
    readonly BookFlushGateBinding _gate;
    readonly DocumentWindowState _state;
    readonly ViewModeController _viewModes;
    readonly DerivedTextScheduler _derived;
    readonly ScrollSyncGuard _scrollGuard;
    readonly ArticlePanes _panes = new();
    readonly PreviewHost _preview = new();
    readonly FooterBar _counts = new();
    readonly PreviewCoordinator _coordinator;
    readonly BookSidebarBuilder _sidebar;
    readonly CommandDispatcher _commands;
    readonly MenuBarBuilder _menus;

    CoreBook? _renderedBook;
    ViewMode _storedMode;
    string? _derivedText;
    int _undoGeneration;
    bool _syncingSelection;
    bool _rendering;
    bool _closing;

    public BookWindow(BookWindowServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        _services = services;
        InitializeComponent();

        _library = new BookLibraryHost(services.Settings, services.Pickers, services.Alerts);
        _watcher = new FileSystemWatcherAdapter(services.UiThread);
        // windowId: null — the book pane never owns a file in the registry; it only asks who does,
        // which is what makes the handoff one-directional (§8.6).
        _session = new TextFileSession(
            services.FileSystem, _watcher, services.Scheduler, services.Registry, services.Identity,
            SessionRole.BookArticle);
        _navigator = new BookNavigatorModel(new TextFileSessionArticles(_session), services.Settings);
        _output = new BookOutput(services.Outputs, services.Settings, () => _navigator.Book);
        _gate = new BookFlushGateBinding(() => _session.FlushNow(explicitSave: false));

        _state = new DocumentWindowState();
        // Before anything reads it: an article is exempt from the per-file memory both ways, and
        // md.bookViewMode is the one mode the whole book shares (§8.5, §9).
        _state.MarkBookArticle();
        _storedMode = ViewModes.FromRawValue(services.Settings.GetString(SettingsKeys.BookViewMode)) ?? ViewModes.WindowDefault;
        _state.StoredMode = _storedMode;
        // A store that is provably not md.viewModeMemory: IsBookArticle already stops SetMode from
        // writing, and this makes "books never touch the per-file memory" true by construction.
        _viewModes = new ViewModeController(_state, new InMemoryViewModeStore());

        _derived = new DerivedTextScheduler(services.Scheduler, services.UiThread, services.WordCounter);
        _scrollGuard = new ScrollSyncGuard(services.Clock);
        _coordinator = new PreviewCoordinator(_preview.Surface, services.Scheduler);
        _sidebar = new BookSidebarBuilder(new BookSidebarActions(
            OpenInWindow: path => _navigator.OpenInWindow(path),
            Rename: path => _ = PromptRenameAsync(path),
            Move: (path, delta) => _navigator.Move(path, delta),
            Delete: path => _ = PromptDeleteAsync(path),
            NewArticle: folder => _ = PromptNewArticleAsync(folder)));

        _commands = new CommandDispatcher(services.Clock, Snapshot);
        _menus = new MenuBarBuilder(_commands, new MenuBarSources(services.Examples, NotePreview.Of));

        BuildChrome();
        WireEvents();
        RegisterCommands();
        ApplyTheme();
        SizeWindow();
        services.Decorate?.Invoke(this);
    }

    /// <summary>The dispatcher the menu bar and the accelerators fire through; WP3 registers the File / Edit / Window / Help rows on it.</summary>
    public CommandDispatcher Commands => _commands;

    /// <summary>
    /// The article being written, or null. File ▸ Print… and File ▸ Share act on <em>this</em> from
    /// the Book window, never on the whole book (book.md §14) — and with no file path, so Share ▸
    /// Source… offers a copy rather than the live file, which is the Mac's nil <c>fileURL</c>.
    /// WP6 registers those commands on <see cref="Commands"/> and reads them from here.
    /// </summary>
    public (string Text, string Title)? ActiveArticle =>
        _session.Stage is Stage.Editing ? (_session.Text, _session.Title) : null;

    /// <summary>
    /// The Mac's <c>didBecomeKeyNotification</c> observer: WindowManager calls this when <em>any</em>
    /// window is activated, so the pane steps aside the moment a document window takes the article
    /// and reclaims it the moment that window closes (§8.6).
    /// </summary>
    public void RecheckOwnership()
    {
        _session.RecheckOwnership();
        Render();
    }

    /// <summary>Book ▸ Close Book, and the window's own close route: flush, let go of the grant, close.</summary>
    public async Task<bool> RequestCloseAsync()
    {
        // CloseBook detaches with reportFailure, so a final save that will not land is parked in a
        // rescue copy and named — the close is never blocked (§1.4, §8.5).
        _navigator.CloseBook();
        await Task.CompletedTask;
        return true;
    }

    // ───────────────────────────────── chrome ─────────────────────────────────

    void BuildChrome()
    {
        Title = Strings.Book;
        MenuSlot.Content = _menus.Build();

        Split.DisplayMode = SplitViewDisplayMode.Inline;
        Split.PanePlacement = SplitViewPanePlacement.Left;
        Split.OpenPaneLength = PaneWidth;
        Split.IsPaneOpen = true;

        Sidebar.SelectionMode = ListViewSelectionMode.Single;

        SidebarBar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed;
        SidebarBar.OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed;
        Button(NewChapterButton, NewChapterGlyph, Strings.Books.NewChapterButton, Strings.Books.NewChapterTooltip);

        DetailBar.DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed;
        DetailBar.OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed;
        Toggle(EditModeButton, EditGlyph, ViewMode.Edit.Label(), Strings.Books.ViewModeTooltip);
        Toggle(SplitModeButton, SplitGlyph, ViewMode.Split.Label(), Strings.Books.ViewModeTooltip);
        Toggle(PreviewModeButton, PreviewGlyph, ViewMode.Preview.Label(), Strings.Books.ViewModeTooltip);
        Button(PreviousArticleButton, PreviousGlyph, CommandTable.For(CommandId.PreviousArticle).Title, Strings.Books.PreviousArticleTooltip);
        Button(NextArticleButton, NextGlyph, CommandTable.For(CommandId.NextArticle).Title, Strings.Books.NextArticleTooltip);
        Button(ContentsButton, ContentsGlyph, CommandTable.For(CommandId.Contents).Title, Strings.Books.ContentsTooltip);
        Button(NotesButton, NotesGlyph, CommandTable.For(CommandId.Notes).Title, Strings.Books.NotesTooltip);
        Button(ShareButton, ShareGlyph, Strings.Books.ShareAsPdf, Strings.Books.ShareTooltip);

        ContentsButton.Flyout = BuildJumpFlyout(BuildContentsRows);
        NotesButton.Flyout = BuildJumpFlyout(BuildNotesRows);
        ShareButton.Flyout = BuildShareFlyout();

        // The detail pane: the same editor + preview composite a document window uses, so an article
        // is written in exactly the surface a document is (§8.5).
        _panes.SetPreview(_preview);
        _panes.AttachScrollSync(_scrollGuard);
        _panes.ScrollSync.ScrollPreview = _preview.ApplyScrollFraction;
        PanesSlot.Content = _panes;
        FooterCountsSlot.Content = _counts;

        StagePlaceholder.Spacing = 12;
        StagePlaceholder.Padding = new Thickness(24);
        EmptyState.Spacing = 12;
        EmptyState.Padding = new Thickness(24);
        StageIcon.FontSize = PlaceholderIconEpx;
        EmptyIcon.FontSize = PlaceholderIconEpx;
        EmptyIcon.Glyph = BooksGlyph;
        Typography(StageTitle, PlaceholderTitleEpx);
        Typography(EmptyTitle, PlaceholderTitleEpx);
        Typography(StageMessage, PlaceholderMessageEpx);
        Typography(EmptyMessage, PlaceholderMessageEpx);
        EmptyTitle.Text = Strings.Books.NoBookOpen;
        EmptyMessage.Text = Strings.Books.NoBookOpenMessage;
        OpenBookButton.Content = Strings.Buttons.OpenBook;

        Footer.Padding = new Thickness(12, 5, 12, 5);
        FooterStatus.Orientation = Orientation.Horizontal;
        FooterStatus.Spacing = 12;
        FooterIcon.Glyph = WarningGlyph;
        FooterIcon.FontSize = 14;
        Typography(FooterText, PaneTypography.FooterEpx);
        FooterText.TextTrimming = TextTrimming.CharacterEllipsis;

        Info.IsOpen = false;
        Info.IsClosable = false;

        // §7.1: the export renderer lives off-canvas inside a visible window, because Chromium
        // throttles a page by IsVisible and a never-activated window is never shown at all.
        Canvas.SetLeft(ExportCanvas, -10000);
        Canvas.SetTop(ExportCanvas, 0);
        PrintOverlaySlot.Visibility = Visibility.Collapsed;
    }

    static void Button(AppBarButton button, string glyph, string label, string tooltip)
    {
        button.Icon = new FontIcon { Glyph = glyph };
        button.Label = label;
        ToolTipService.SetToolTip(button, tooltip);
    }

    static void Toggle(AppBarToggleButton button, string glyph, string label, string tooltip)
    {
        button.Icon = new FontIcon { Glyph = glyph };
        button.Label = label;
        ToolTipService.SetToolTip(button, tooltip);
    }

    static void Typography(TextBlock block, double size)
    {
        block.FontFamily = new FontFamily(PaneTypography.Family);
        block.FontSize = size;
        block.TextWrapping = TextWrapping.Wrap;
        block.TextAlignment = TextAlignment.Center;
        block.MaxWidth = PlaceholderMaxWidth;
    }

    // ───────────────────────────────── wiring ─────────────────────────────────

    void WireEvents()
    {
        Root.Loaded += (_, _) => _ = OpenStoredBookAsync();
        Root.ActualThemeChanged += (_, _) =>
        {
            ApplyTheme();
            _renderedBook = null;                       // the rows carry brushes; rebuild them
            Render();
        };

        Sidebar.SelectionChanged += OnSidebarSelectionChanged;
        Sidebar.DoubleTapped += (_, _) =>
        {
            if (SelectedRow() is { IsArticle: true, Path: { } path }) _navigator.OpenInWindow(path);
        };

        NewChapterButton.Click += (_, _) => _ = PromptNewChapterAsync();
        OpenBookButton.Click += (_, _) => _ = OpenBookAsync();
        StageButton.Click += (_, _) => ShowOwningWindow();

        EditModeButton.Click += (_, _) => _viewModes.Select(ViewMode.Edit);
        SplitModeButton.Click += (_, _) => _viewModes.Select(ViewMode.Split);
        PreviewModeButton.Click += (_, _) => _viewModes.Select(ViewMode.Preview);
        PreviousArticleButton.Click += (_, _) => _navigator.StepPrevious();
        NextArticleButton.Click += (_, _) => _navigator.StepNext();

        FooterPrimaryButton.Click += (_, _) => OnFooterPrimary();
        FooterSecondaryButton.Click += (_, _) => _session.ResolveConflictKeepingMine();

        _panes.Editor.TextEdited += text => _session.Edit(text);
        _panes.LayoutChanged += _ => Render();

        _navigator.Changed += Render;
        _navigator.SelectionChanging += () =>
        {
            // A one-shot jump must never replay into the next article (book.md §13.7).
            _state.PreviewNavigation = null;
            _state.EditorJump = null;
        };
        _navigator.OpenRequested += path => _services.OpenPath(path);
        _navigator.AlertRequested += Warn;

        _session.Changed += Render;
        _session.AlertRequested += Warn;
        _output.AlertRequested += Warn;

        _derived.Changed += derived =>
        {
            _state.Derived = derived;
            _counts.Update(derived);
            Publish();
        };

        _state.Changed += OnStateChanged;
        _services.Settings.Changed += OnSettingChanged;
        _services.Registry.Changed += OnRegistryChanged;

        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated) RecheckOwnership();
            else _session.FlushNow(explicitSave: false);
        };

        AppWindow.Changed += (_, args) =>
        {
            // §1.3: Win+Up / Win+Down / the caption buttons change the presenter without going
            // through the command, and View's row text ("Enter" vs "Exit Full Screen") is read from
            // the snapshot — so the snapshot has to be re-published when the system moves it.
            if (args.DidPresenterChange) Publish();
        };

        AppWindow.Closing += (sender, args) =>
        {
            if (_closing) return;
            // A system affordance (the close button, Alt+F4, the taskbar) is cancelled and re-run
            // through the close policy, because AppWindow.Closing cannot await one (§1.4).
            args.Cancel = true;
            _closing = true;
            _ = CloseFromAffordanceAsync();
        };

        Closed += (_, _) => Cleanup();
    }

    async Task CloseFromAffordanceAsync()
    {
        if (await RequestCloseAsync()) Close();
        else _closing = false;
    }

    void OnRegistryChanged() => _services.UiThread.Post(RecheckOwnership);

    void OnStateChanged()
    {
        // The single writer of md.bookViewMode: the mode is app-wide, and a nudge stores nothing.
        if (_state.StoredMode != _storedMode)
        {
            _storedMode = _state.StoredMode;
            _services.Settings.SetString(SettingsKeys.BookViewMode, _storedMode.RawValue());
        }
        Render();
    }

    void OnSettingChanged(string key)
    {
        // Another window opened or closed a book: re-list against whatever md.bookBookmark now says.
        if (key == SettingsKeys.BookBookmark) _ = OpenStoredBookAsync();
        else if (key == SettingsKeys.BookOpensInSeparateWindows) Render();
    }

    void Cleanup()
    {
        // Both of these are app-wide and outlive the window: left attached, the closed window is
        // kept alive and RecheckOwnership runs against a disposed session on the next activation.
        _services.Settings.Changed -= OnSettingChanged;
        _services.Registry.Changed -= OnRegistryChanged;
        _derived.Cancel();
        _session.CloseFlush();
        _gate.Dispose();
        _session.Dispose();
        _watcher.Dispose();
        StoreWindowSize();
    }

    // ───────────────────────────────── the book ─────────────────────────────────

    async Task OpenStoredBookAsync()
    {
        var root = await _library.ResolveAsync();
        if (root is null)
        {
            _navigator.CloseBook();
            Render();
            return;
        }
        _navigator.OpenBook(root);
        Render();
    }

    async Task OpenBookAsync()
    {
        if (await _library.ChooseBookAsync()) await OpenStoredBookAsync();
    }

    /// <summary>Book ▸ New Book…, Open Book…, Example Book… all end here: the setting changed, so re-list.</summary>
    public async Task NewBookAsync()
    {
        if (await _library.NewBookAsync()) await OpenStoredBookAsync();
    }

    public async Task OpenBookCommandAsync() => await OpenBookAsync();

    public async Task UnpackExampleBookAsync()
    {
        if (await _library.UnpackExampleBookAsync()) await OpenStoredBookAsync();
    }

    /// <summary>
    /// Book ▸ Close Book: the grant goes and the window goes with it — §2.6 ("<c>BookLibraryHost.CloseBook()</c>;
    /// close the Book window") and §1.4 route 2, which lists Close Book among the commands that run the
    /// close policy and then call <c>Window.Close()</c>. It is the Mac's
    /// <c>BookLibrary.closeBook(); dismissWindow(id:)</c>. The empty state is for Show Book with no
    /// book open, not for the window a writer has just closed the book in.
    /// </summary>
    public async Task CloseBookCommandAsync()
    {
        // The setting change re-lists this window (and any other) before it goes, so the two never
        // disagree about whether a book is open.
        _library.CloseBook();
        if (_closing) return;
        _closing = true;
        if (await RequestCloseAsync()) Close();
        else _closing = false;
    }

    // ───────────────────────────────── prompts ─────────────────────────────────

    async Task PromptNewChapterAsync()
    {
        var name = await _services.Alerts.PromptNameAsync(
            Strings.Books.NewChapterTitle, Strings.Books.NewChapterMessage, "", Strings.Buttons.Create);
        if (name is not null) _navigator.CreateChapter(name);
    }

    async Task PromptNewArticleAsync(string folder)
    {
        var name = await _services.Alerts.PromptNameAsync(
            Strings.Books.NewArticleTitle, Strings.Books.NewArticleMessage, "", Strings.Buttons.Create);
        if (name is not null) _navigator.CreateArticle(name, folder);
    }

    async Task PromptRenameAsync(string path)
    {
        var current = Md.Core.Book.BookNaming.DisplayName(Md.Core.Book.BookPaths.Name(path));
        var stem = await _services.Alerts.PromptNameAsync(
            Strings.Documents.RenameTitle, Strings.Books.RenameMessage, current, Strings.Documents.RenameTitle);
        if (stem is not null) _navigator.PerformRename(path, stem);
    }

    async Task PromptDeleteAsync(string path)
    {
        var commands = BookSidebarModel.CommandsFor(_navigator.Book, path);
        if (commands is null) return;
        var confirmed = await _services.Alerts.ConfirmDeleteAsync(
            Strings.Books.DeleteQuestion(commands.Name), commands.DeleteMessage);
        if (confirmed) _navigator.PerformDelete(path);
    }

    void Warn(string title, string message) => _ = _services.Alerts.WarnAsync(title, message);

    // ───────────────────────────────── rendering ─────────────────────────────────

    void Render()
    {
        // Applying state can move it: setting ArticlePanes.Mode may change the layout, which raises
        // LayoutChanged. The nested call has nothing left to do — the change it would react to has
        // already been applied by the time the handler runs — so it is dropped rather than allowed
        // to recurse.
        if (_rendering) return;
        _rendering = true;
        try
        {
            RenderCore();
        }
        finally
        {
            _rendering = false;
        }
    }

    void RenderCore()
    {
        var book = _navigator.Book;
        var hasBook = book is not null;
        Split.Visibility = hasBook ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasBook ? Visibility.Collapsed : Visibility.Visible;

        RenderTitle(book);
        RenderSidebar(book);
        RenderStage();
        RenderToolbar();
        RenderFooter();
        RenderMinimumSize(hasBook);
        Publish();
    }

    void RenderTitle(CoreBook? book)
    {
        var name = book?.Name ?? Strings.Book;
        Title = _session.Stage is Stage.Editing ? $"{name} {Strings.EmDash} {_session.Title}" : name;
    }

    void RenderSidebar(CoreBook? book)
    {
        if (!ReferenceEquals(_renderedBook, book))
        {
            _renderedBook = book;
            _syncingSelection = true;
            try
            {
                Sidebar.Items.Clear();
                foreach (var item in _sidebar.Build(book, Root.ActualTheme)) Sidebar.Items.Add(item);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        var index = BookSidebarModel.IndexOfArticle(Rows(), _navigator.Selection);
        if (Sidebar.SelectedIndex == index) return;
        _syncingSelection = true;
        try
        {
            Sidebar.SelectedIndex = index;
            if (index >= 0) Sidebar.ScrollIntoView(Sidebar.Items[index]);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    void RenderStage()
    {
        var editing = _session.Stage is Stage.Editing;
        PanesSlot.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        StagePlaceholder.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;

        if (editing)
        {
            if (_undoGeneration != _session.UndoGeneration)
            {
                _undoGeneration = _session.UndoGeneration;
                // A fresh undo stack per article: Ctrl+Z must never cross an article boundary (§8.5).
                _panes.Editor.Control.ClearUndoRedoHistory();
            }
            _panes.Editor.SetText(_session.Text);
            _panes.Mode = _state.EffectiveMode;
            // Only on a real change: TextChanged restarts the 250 ms debounce, so calling it from
            // every render (a theme switch, an ownership recheck) would keep the counter from ever
            // landing.
            if (!string.Equals(_derivedText, _session.Text, StringComparison.Ordinal))
            {
                _derivedText = _session.Text;
                _derived.TextChanged(_session.Text);
            }

            _preview.Visibility = SplitLayout.ShowsPreview(_panes.Layout) ? Visibility.Visible : Visibility.Collapsed;
            if (SplitLayout.ShowsPreview(_panes.Layout)) _coordinator.Show();
            else _coordinator.Hide();
            // The article's path is the document token: switching articles is a fresh page load, so
            // there is no flash of the previous article and no inherited scroll (§8.5).
            _coordinator.Update(_session.Text, _session.Title, Root.ActualTheme == ElementTheme.Dark, _session.EditingPath);

            if (_state.EditorJump is { } jump) _panes.Editor.ApplyJump(jump, _state.EditorJumpHandled);
            if (_state.PreviewNavigation is { } navigation)
            {
                // WP4 declares its own PreviewNavigation placeholder in View/DocumentWindowState.cs
                // (its integration note 1); until that file re-types it, the two records are carried
                // across by hand rather than by editing another package's file.
                _coordinator.Navigate(new Md.App.Logic.Preview.PreviewNavigation(navigation.Id, navigation.Slug), _state.PreviewNavigationHandled);
            }
            return;
        }

        _coordinator.Hide();
        _derived.Cancel();
        _derivedText = null;
        _state.Derived = DerivedText.Empty;
        _counts.Update(DerivedText.Empty);

        switch (_session.Stage)
        {
            case Stage.Handoff:
                StageIcon.Glyph = WindowGlyph;
                StageTitle.Text = Strings.Books.HandoffTitle;
                StageMessage.Text = Strings.Books.HandoffMessage(_session.Title);
                StageButton.Content = Strings.Buttons.ShowWindow;
                StageButton.Visibility = Visibility.Visible;
                break;
            case Stage.Unreadable:
                StageIcon.Glyph = WarningGlyph;
                StageTitle.Text = Strings.Books.UnreadableTitle;
                StageMessage.Text = Strings.Books.UnreadableMessage(_session.Title);
                StageButton.Visibility = Visibility.Collapsed;
                break;
            default:
                StageIcon.Glyph = EditGlyph;
                StageTitle.Text = Strings.Books.EmptyTitle;
                StageMessage.Text = Strings.Books.EmptyMessage;
                StageButton.Visibility = Visibility.Collapsed;
                break;
        }
    }

    void RenderToolbar()
    {
        var editing = _session.Stage is Stage.Editing;
        var mode = _state.EffectiveMode;
        EditModeButton.IsEnabled = editing;
        SplitModeButton.IsEnabled = editing;
        PreviewModeButton.IsEnabled = editing;
        EditModeButton.IsChecked = mode == ViewMode.Edit;
        SplitModeButton.IsChecked = mode == ViewMode.Split;
        PreviewModeButton.IsChecked = mode == ViewMode.Preview;

        var stepper = _navigator.Stepper;
        PreviousArticleButton.IsEnabled = stepper.CanPrevious;
        NextArticleButton.IsEnabled = stepper.CanNext;

        ContentsButton.IsEnabled = editing && _state.Derived.Outline.Count > 0;
        NotesButton.IsEnabled = editing && _state.Derived.Notes.Count > 0;
        ShareButton.IsEnabled = _navigator.Book is not null;
    }

    void RenderFooter()
    {
        if (_session.Conflicted)
        {
            FooterStatus.Visibility = Visibility.Visible;
            FooterIcon.Visibility = Visibility.Visible;
            FooterText.Text = Strings.Documents.FileChangedOnDisk;
            FooterPrimaryButton.Content = Strings.Buttons.ReloadFromDisk;
            FooterPrimaryButton.Visibility = Visibility.Visible;
            FooterSecondaryButton.Content = Strings.Buttons.KeepMyVersion;
            FooterSecondaryButton.Visibility = Visibility.Visible;
            return;
        }
        if (_session.SaveErrorText is { } error)
        {
            FooterStatus.Visibility = Visibility.Visible;
            FooterIcon.Visibility = Visibility.Visible;
            FooterText.Text = Strings.Documents.CouldNotSave(error);
            FooterPrimaryButton.Content = Strings.Buttons.Retry;
            FooterPrimaryButton.Visibility = Visibility.Visible;
            FooterSecondaryButton.Visibility = Visibility.Collapsed;
            return;
        }
        // A routine autosave shows nothing at all — the Mac's footer is silent unless it has news.
        FooterStatus.Visibility = Visibility.Collapsed;
        FooterIcon.Visibility = Visibility.Collapsed;
        FooterPrimaryButton.Visibility = Visibility.Collapsed;
        FooterSecondaryButton.Visibility = Visibility.Collapsed;
    }

    void OnFooterPrimary()
    {
        if (_session.Conflicted) _session.ResolveConflictReloading();
        else _session.FlushNow(explicitSave: true);
    }

    void ShowOwningWindow()
    {
        if (_session.OwningWindow is { } id) _services.ActivateWindow(id);
    }

    void RenderMinimumSize(bool hasBook)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter) return;
        // §1.3: a book needs room for both columns; the empty state is a card and needs almost none.
        presenter.PreferredMinimumWidth = hasBook ? 700 : 260;
        presenter.PreferredMinimumHeight = hasBook ? 400 : 320;
    }

    void Publish() => _menus.Refresh(Snapshot());

    // ───────────────────────────────── selection ─────────────────────────────────

    IReadOnlyList<BookRow> Rows() =>
        Sidebar.Items.Select(item => (item as ListViewItem)?.Tag as BookRow)
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

    BookRow? SelectedRow() => (Sidebar.SelectedItem as ListViewItem)?.Tag as BookRow;

    void OnSidebarSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        var row = SelectedRow();
        if (row is { IsArticle: true, Path: { } path })
        {
            _navigator.Select(path);
            return;
        }
        // A chapter header and a "New Article…" row are not destinations: put the highlight straight
        // back where the navigator says it belongs, without telling the session anything.
        _syncingSelection = true;
        try
        {
            Sidebar.SelectedIndex = BookSidebarModel.IndexOfArticle(Rows(), _navigator.Selection);
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    // ───────────────────────────────── flyouts ─────────────────────────────────

    static MenuFlyout BuildJumpFlyout(Action<MenuFlyout> fill)
    {
        var flyout = new MenuFlyout();
        // FlyoutBase.Opening is the one "about to show" event this family of types has (Appendix A:
        // MenuFlyoutSubItem and MenuBarItem have none), so the rows are rebuilt here rather than on
        // every derived-text tick.
        flyout.Opening += (_, _) => fill(flyout);
        return flyout;
    }

    void BuildContentsRows(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        foreach (var entry in _state.Derived.Outline)
        {
            var row = new MenuFlyoutItem { Text = CommandTable.ContentsRowTitle(entry) };
            var target = entry;
            row.Click += (_, _) => _viewModes.JumpToHeading(target);
            flyout.Items.Add(row);
        }
    }

    void BuildNotesRows(MenuFlyout flyout)
    {
        flyout.Items.Clear();
        foreach (var note in _state.Derived.Notes)
        {
            var row = new MenuFlyoutItem { Text = NotePreview.Of(note.Text) };
            var target = note;
            row.Click += (_, _) => _viewModes.JumpToNote(target);
            flyout.Items.Add(row);
        }
    }

    MenuFlyout BuildShareFlyout()
    {
        var flyout = new MenuFlyout();

        var share = new MenuFlyoutItem { Text = Strings.Books.ShareAsPdf };
        share.Click += (_, _) => _ = _output.SharePdfAsync();
        flyout.Items.Add(share);

        var exportPdf = new MenuFlyoutItem { Text = Strings.Books.ExportAsPdf };
        exportPdf.Click += (_, _) => _ = _output.ExportPdfAsync();
        flyout.Items.Add(exportPdf);

        // The seven trim sizes, in PageSize.All order; the tick is re-read when the menu opens, so
        // the document window's copy of this picker and this one never disagree (they share the key).
        var sizes = new MenuFlyoutSubItem { Text = Strings.Books.PdfPageSizeLabel };
        foreach (var size in PageSize.All)
        {
            var id = size.Id;
            var row = new RadioMenuFlyoutItem { Text = size.Label, GroupName = CommandTable.PdfPageSizeGroupName };
            row.Click += (_, _) => _services.Settings.SetString(SettingsKeys.PdfPageSize, id);
            sizes.Items.Add(row);
        }
        flyout.Items.Add(sizes);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var epub = new MenuFlyoutItem { Text = Strings.Books.ExportAsEpub };
        epub.Click += (_, _) => _ = _output.ExportEpubAsync();
        flyout.Items.Add(epub);

        var latex = new MenuFlyoutItem { Text = Strings.Books.ExportAsLaTeX };
        latex.Click += (_, _) => _ = _output.ExportLaTeXAsync();
        flyout.Items.Add(latex);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var print = new MenuFlyoutItem { Text = Strings.Books.PrintRow };
        print.Click += (_, _) => _ = _output.PrintAsync();
        flyout.Items.Add(print);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var separate = new ToggleMenuFlyoutItem { Text = Strings.Books.OpenArticlesInSeparateWindows };
        separate.Click += (_, _) => _navigator.OpensInSeparateWindows = separate.IsChecked;
        flyout.Items.Add(separate);

        flyout.Opening += (_, _) =>
        {
            separate.IsChecked = _navigator.OpensInSeparateWindows;
            var current = PageSize.Named(_services.Settings.GetString(SettingsKeys.PdfPageSize)).Id;
            for (var i = 0; i < sizes.Items.Count; i++)
            {
                if (sizes.Items[i] is RadioMenuFlyoutItem radio) radio.IsChecked = PageSize.All[i].Id == current;
            }
        };

        return flyout;
    }

    // ───────────────────────────────── commands ─────────────────────────────────

    void RegisterCommands()
    {
        AcceleratorInstaller.Install(Root, _commands);

        _commands.Register(CommandId.NewBook, () => _ = NewBookAsync());
        _commands.Register(CommandId.OpenBook, () => _ = OpenBookAsync());
        _commands.Register(CommandId.ExampleBook, () => _ = UnpackExampleBookAsync());
        _commands.Register(CommandId.ShowBook, () => Activate());
        _commands.Register(CommandId.CloseBook, () => _ = CloseBookCommandAsync());

        _commands.Register(CommandId.ShareBookPdf, () => _ = _output.SharePdfAsync());
        _commands.Register(CommandId.PrintBook, () => _ = _output.PrintAsync());
        _commands.Register(CommandId.ExportBookPdf, () => _ = _output.ExportPdfAsync());
        _commands.Register(CommandId.ExportBookEpub, () => _ = _output.ExportEpubAsync());
        _commands.Register(CommandId.ExportBookLaTeX, () => _ = _output.ExportLaTeXAsync());

        _commands.Register(CommandId.PreviousArticle, _navigator.StepPrevious);
        _commands.Register(CommandId.NextArticle, _navigator.StepNext);
        _commands.Register(CommandId.Contents, argument =>
        {
            if (argument is Md.Core.Markdown.OutlineEntry entry) _viewModes.JumpToHeading(entry);
        });
        _commands.Register(CommandId.Notes, argument =>
        {
            if (argument is Md.Core.Markdown.NoteEntry note) _viewModes.JumpToNote(note);
        });

        _commands.Register(CommandId.ViewEdit, () => _viewModes.Select(ViewMode.Edit));
        _commands.Register(CommandId.ViewSplit, () => _viewModes.Select(ViewMode.Split));
        _commands.Register(CommandId.ViewPreview, () => _viewModes.Select(ViewMode.Preview));

        _commands.Register(CommandId.PdfPageSize, argument =>
        {
            if (argument is string id) _services.Settings.SetString(SettingsKeys.PdfPageSize, id);
        });

        // Full screen is the Book window's distraction-free room — there is no Zen here, because the
        // Mac's book pane publishes none (§8.1).
        _commands.Register(CommandId.FullScreen, ToggleFullScreen);
        _commands.Register(CommandId.ShowSidebar, () =>
        {
            Split.IsPaneOpen = !Split.IsPaneOpen;
            Publish();
        });
    }

    void ToggleFullScreen()
    {
        var full = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen;
        AppWindow.SetPresenter(full ? AppWindowPresenterKind.Overlapped : AppWindowPresenterKind.FullScreen);
        Publish();
    }

    /// <summary>
    /// What the menus and accelerators are enabled from. The Mac's book pane publishes an
    /// <c>activeDocument</c> with a <b>nil</b> fileURL, so File ▸ Print and File ▸ Share act on the
    /// article being written and Share ▸ Source… offers a copy, not the live file — <c>IsSaved</c>
    /// false is that nil (§8.5, book.md §14).
    /// </summary>
    public ShellSnapshot Snapshot()
    {
        var editing = _session.Stage is Stage.Editing;
        var stepper = _navigator.Stepper;
        var derived = _state.Derived;
        return ShellSnapshot.Empty with
        {
            HasDocument = editing,
            IsBookWindow = true,
            IsEditingArticle = editing,
            IsSaved = false,
            IsDirty = _session.IsDirty,
            HasBook = _library.HasBook,
            DisplayedMode = _state.EffectiveMode,
            Outline = derived.Outline,
            Notes = derived.Notes,
            Diagrams = derived.Diagrams.Select(d => new Md.App.Logic.Commands.DiagramRef(d.Ordinal, d.MenuTitle)).ToList(),
            CanPrevious = stepper.CanPrevious,
            CanNext = stepper.CanNext,
            EditorVisible = editing && SplitLayout.ShowsEditor(_panes.Layout),
            // §2.4: "editor visible && CanUndo / CanRedo" — the TextBox's own buffers, never
            // "an article is open", or Edit ▸ Undo is live on a document nobody has typed into.
            CanUndo = editing && _panes.Editor.Control.CanUndo,
            CanRedo = editing && _panes.Editor.Control.CanRedo,
            WindowTitles = _services.Registry.Windows.Select(w => (w.Id, w.Title, false)).ToList(),
            HasSelection = _panes.Editor.HasSelection,
            PdfPageSizeId = PageSize.Named(_services.Settings.GetString(SettingsKeys.PdfPageSize)).Id,
            SidebarOpen = Split.IsPaneOpen,
            IsFullScreen = AppWindow.Presenter.Kind == AppWindowPresenterKind.FullScreen,
        };
    }

    // ───────────────────────────────── theme and size ─────────────────────────────────

    void ApplyTheme()
    {
        var palette = Palette.For(Root.ActualTheme == ElementTheme.Dark);
        var paper = PaneBrushes.Get("PaperBackgroundBrush", palette.Paper);
        var secondary = PaneBrushes.Get("PaperBackgroundSecondaryBrush", palette.PaperSecondary);
        var ink = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        var inkSecondary = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);

        Root.Background = paper;
        SidebarRoot.Background = secondary;
        DetailRoot.Background = paper;
        StageRoot.Background = paper;
        Footer.Background = secondary;
        Body.Background = paper;

        StageIcon.Foreground = inkSecondary;
        EmptyIcon.Foreground = inkSecondary;
        StageTitle.Foreground = ink;
        EmptyTitle.Foreground = ink;
        StageMessage.Foreground = inkSecondary;
        EmptyMessage.Foreground = inkSecondary;
        FooterText.Foreground = inkSecondary;
        // The conflict and save-error line is the one place the paper palette is deliberately left:
        // an orange warning is the Mac's, and a paper-coloured warning is not a warning.
        FooterIcon.Foreground = new SolidColorBrush(Microsoft.UI.Colors.DarkOrange);

        _preview.ApplyTheme(Root.ActualTheme == ElementTheme.Dark);
    }

    void SizeWindow()
    {
        var (width, height) = ParseSize(
            _services.Settings.GetString(SettingsKeys.BookWindowSize),
            SettingsKeys.BookWindowSizeDefault);
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = Interop.NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Round(width * scale), (int)Math.Round(height * scale)));
    }

    void StoreWindowSize()
    {
        var scale = Root.XamlRoot?.RasterizationScale ?? 1;
        if (scale <= 0) scale = 1;
        var size = AppWindow.ClientSize;
        _services.Settings.SetString(
            SettingsKeys.BookWindowSize,
            $"{(int)Math.Round(size.Width / scale)}x{(int)Math.Round(size.Height / scale)}");
    }

    /// <summary>
    /// The "WxH" epx codec of §9. Local because WP3 owns <c>Windows/WindowPlacement.cs</c>, which is
    /// the file this belongs in the moment it exists.
    /// </summary>
    static (int Width, int Height) ParseSize(string? stored, string fallback)
    {
        foreach (var candidate in new[] { stored, fallback })
        {
            if (candidate is null) continue;
            var parts = candidate.Split('x');
            if (parts.Length != 2) continue;
            if (int.TryParse(parts[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var w)
                && int.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var h)
                && w > 0 && h > 0)
            {
                return (w, h);
            }
        }
        return (1000, 700);
    }
}
