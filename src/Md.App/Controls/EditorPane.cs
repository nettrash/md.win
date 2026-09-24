// The editor (shell-design.md §3): one TextBox on paper, the Tab key WinUI has no property for,
// the template ScrollViewer the scroll sync needs, the deferred caret jump, the \r adapter that
// keeps the model in LF, and the two SmartTyping hooks (§3.6, docs/smart-typing.md §3.3). Everything
// decidable without Windows is in Md.App.Logic.View / .Documents / .Text; this file is the adapter.
using Md.App.Logic;
using Md.App.Logic.Documents;
using Md.App.Logic.Preview;
using Md.App.Logic.Settings;
using Md.App.Logic.Text;
using Md.App.Logic.View;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Md.App.Controls;

/// <summary>
/// The typewriter-on-paper editor. Owns the <see cref="TextBox"/> and nothing else: the text it hands
/// out and takes in is always LF (§3.2), the caret jump is performed once per id and one dispatcher
/// turn late (§3.3), the scroll half reports and applies fractions through
/// <see cref="ScrollSync"/> (§3.4), and Enter and the typed letter go through SmartTyping (§3.6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Typing (docs/smart-typing.md §3.3 "Windows", shell-design.md §3.6).</b> The inputs are
/// <c>_box.Text</c> exactly as the control reports it — CR line ends, which §0.1 accepts, so nothing
/// is converted on the way in — and <c>SelectionStart</c> / <c>SelectionLength</c>, which index that
/// same text. Every decision and every piece of state is <see cref="TypingHooks"/>'s, tested off
/// Windows against a fake TextBox that raises the same events in the same order; this control holds
/// no typing state of its own. What it does is read the box at each event, ask, and apply the answer
/// with the <c>Select</c> + <c>SelectedText</c> pair the Tab key uses, which keeps the control's own
/// undo stack (a <c>Text =</c> assignment clears it).
/// </para>
/// <para>
/// <b>Enter</b> is <c>PreviewKeyDown</c>, before the control acts: <c>VirtualKey.Enter</c> with no modifier asks the hooks; a non-null
/// edit is applied as one replacement (one undo unit) and the key is marked handled, so the control
/// never adds its own newline. Shift+Enter, Ctrl+Enter, an edit the function declines and the
/// feature turned off all fall through to the control's plain newline (§3.6).
/// </para>
/// <para>
/// <b>The letter</b> is decided in <c>BeforeTextChanging</c>, which carries the text the box is
/// about to hold, and applied in <c>TextChanging</c>, which is raised synchronously once it holds it
/// and before it is rendered. The control inserts the typed text itself (the first undo unit of
/// §3.5), then the insertion is replaced by the same text with its first scalar capitalized (the
/// second), and the caret is put after it — so <b>Ctrl+Z immediately after a capital restores the
/// lowercase letter</b>, keeps the caret, and — being the edit that removes the capital — arms the
/// override at its offset (smart-typing.md §3.4). The edit is never cancelled: whatever a real
/// control does with the pre-change notification, the keystroke itself cannot be lost, and the
/// worst surprise is a missing capital.
/// </para>
/// <para>
/// <b>Tracking.</b> The hooks follow the last capital md produced through every later edit and arm
/// the override when it is removed (§3.4). Every change the control reports — typing, Backspace,
/// Delete, Cut, a paste, a composition update, Undo, Redo — reaches them through
/// <c>BeforeTextChanging</c> and is tracked whether or not it is judged; the two edits this control
/// makes itself run under the hooks' <c>Applying</c> flag and are reported by the calls that decide
/// them. Nothing here keeps an offset.
/// </para>
/// <para>
/// <b>What is not typing.</b> A composition (<c>TextCompositionStarted</c> … <c>Ended</c>) skips both
/// features. A paste skips the capital — announced by the control's <c>Paste</c> event for Ctrl+V and
/// the context menu, and by <see cref="Paste"/> for the Edit row, which does not rely on
/// <c>PasteFromClipboard()</c> raising that event. Undo and Redo are history, not typing: announced
/// from <c>PreviewKeyDown</c> for Ctrl+Z / Ctrl+Y (before the control acts on the chord) and by
/// <see cref="Undo"/> / <see cref="Redo"/> for the Edit rows, so the change they produce is not
/// judged — a Redo that re-inserts a letter is not capitalized into a new edit that would wipe the
/// redo stack — though it is tracked like any other. Both flags settle on the next dispatcher turn.
/// The session's echo of every keystroke arrives through <see cref="SetText"/> as identical text,
/// which is not a replacement and leaves the tracking as it is; an external replacement the text
/// cannot show — another article with the same text, a reload the disk agrees with — reaches the
/// hooks through <see cref="ResetHistory"/>, which the windows call on the session's
/// <c>UndoGeneration</c> in place of the control's bare <c>ClearUndoRedoHistory()</c>. Backspace and
/// Delete are announced from <c>PreviewKeyDown</c> too, so the hooks know which of two identical
/// units the key removed.
/// </para>
/// </remarks>
public sealed class EditorPane : UserControl
{
    /// <summary>15 pt × 4/3. The Mac's American Typewriter is not on Windows; Lucida Sans Typewriter stands in (§10).</summary>
    public const double FontSizeEpx = 20;

    readonly TextBox _box = new();
    ScrollViewer? _scroller;
    ScrollSync? _sync;
    ScrollSyncGuard? _guard;
    // app-api.md §WP4: the "performed once per id" rule is Md.App.Logic's, not a second copy of it
    // here — a bare `Guid? _lastJumpId` said the same thing in a file no test off Windows can reach.
    readonly EditorJumpTracker _jumps = new();

    // §3.6: the whole state of the typing hooks — the tracked capital and the override
    // (smart-typing.md §3.4), the plan between the two events, the settings and the flags — lives
    // in Md.App.Logic, where a fake TextBox drives it through the same sequence, echo, Cut and
    // Undo / Redo included.
    readonly TypingHooks _hooks = new();
    ISettingsStore? _settings;

    public EditorPane()
    {
        _box.AcceptsReturn = true;
        _box.TextWrapping = TextWrapping.Wrap;
        // The only two auto-correct sources WinUI has; Markdown punctuation must stay literal.
        _box.IsSpellCheckEnabled = false;
        _box.IsTextPredictionEnabled = false;
        _box.FontFamily = new FontFamily(PaneTypography.Family);
        _box.FontSize = FontSizeEpx;
        // textContainerInset 16 × 16 with lineFragmentPadding 0.
        _box.Padding = new Thickness(16);
        _box.BorderThickness = new Thickness(0);
        _box.PlaceholderText = Strings.EditorPlaceholder;
        _box.HorizontalAlignment = HorizontalAlignment.Stretch;
        _box.VerticalAlignment = VerticalAlignment.Stretch;
        // Vertical only: a wrapped editor must never scroll sideways.
        ScrollViewer.SetHorizontalScrollBarVisibility(_box, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);

        _box.TextChanged += OnTextChanged;
        // §3.6: Enter tunnels here before the control inserts its own newline; Tab stays in KeyDown.
        _box.PreviewKeyDown += OnPreviewEnter;
        _box.KeyDown += OnKeyDown;
        // §3.6: decided before the change, applied synchronously after it and before it is drawn.
        _box.BeforeTextChanging += OnBeforeTextChanging;
        _box.TextChanging += OnTextChanging;
        // Ctrl+Z / Ctrl+Y tunnel here before the control undoes or redoes: the change is history.
        _box.PreviewKeyDown += OnPreviewKeyDown;
        _box.TextCompositionStarted += (_, _) => _hooks.CompositionStarted();
        _box.TextCompositionEnded += (_, _) => _hooks.CompositionEnded();
        // A paste is never capitalized (§3.3): Ctrl+V and the context menu announce it here.
        _box.Paste += (_, _) => AnnouncePaste();
        Content = _box;

        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    /// <summary>Every keystroke, already normalised to LF. The owner compares with the session's text before writing (§3.2).</summary>
    public event Action<string>? TextEdited;

    /// <summary>
    /// The <see cref="TextBox"/> itself, for the Edit rows the control performs unaided (Cut / Copy /
    /// Delete / Select All), the <c>CanUndo</c> / <c>CanRedo</c> the snapshot publishes and the
    /// selection the Find bar reads. Undo, Redo and Paste go through <see cref="Undo"/>,
    /// <see cref="Redo"/> and <see cref="Paste"/>, and the fresh history of a new
    /// <c>UndoGeneration</c> through <see cref="ResetHistory"/>, never through this: the typing hooks
    /// have to be told first.
    /// </summary>
    public TextBox Control => _box;

    /// <summary>The document text, LF whatever the control reports.</summary>
    public string Text => EditorText.FromTextBox(_box.Text);

    public bool HasSelection => _box.SelectionLength > 0;

    /// <summary>
    /// The settings seam the two typing toggles are read through (<c>md.continueLists</c>,
    /// <c>md.capitalizeSentences</c>; smart-typing.md §3.1). Both are re-read on every
    /// <see cref="ISettingsStore.Changed"/> for their keys, so a toggle takes effect in every open
    /// editor at once; null unsubscribes — the owner calls that when its window closes, because the
    /// store outlives the window.
    /// </summary>
    public void UseSettings(ISettingsStore? settings)
    {
        if (_settings is not null) _settings.Changed -= OnSettingChanged;
        _settings = settings;
        if (settings is null) return;
        settings.Changed += OnSettingChanged;
        ReadTypingSettings();
    }

    /// <summary>
    /// An external replace — Revert, Reload from Disk, an example, an article switch — or the
    /// session's echo of our own edit. Nothing happens when the text already matches (the echo),
    /// and the tracked capital or armed override survives it; text that differs is assigned, the
    /// selection survives clamped, and the tracking does not (§3.4: an external replacement clears
    /// both).
    /// </summary>
    public void SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!_hooks.ExternalText(Text, text)) return;

        var start = _box.SelectionStart;
        var length = _box.SelectionLength;
        _hooks.Replacing(() => _box.Text = EditorText.ToTextBox(text));

        (start, length) = EditorText.ClampSelection(start, length, _box.Text.Length);
        _box.Select(start, length);
    }

    /// <summary>
    /// A new <c>UndoGeneration</c> — a fresh document, Revert, Reload from Disk, an article switch:
    /// the control's history is cleared, and the hooks forget the tracked capital and the override
    /// first (§3.4: an external replacement clears both), because the text the session then renders
    /// may equal the one in the box — another article holding the same text, a reload the disk
    /// agrees with — and <see cref="SetText"/> would rightly see the echo. The one <c>Text =</c>
    /// stays SetText's.
    /// </summary>
    public void ResetHistory()
    {
        _hooks.Reset();
        _box.ClearUndoRedoHistory();
    }

    /// <summary>Edit ▸ Undo: announced as history first, so the change is not judged as typing (§3.5).</summary>
    public void Undo()
    {
        AnnounceHistory();
        _box.Undo();
    }

    /// <summary>Edit ▸ Redo: announced as history first, so a re-inserted letter is not capitalized into a new edit; the change is still tracked (§3.4).</summary>
    public void Redo()
    {
        AnnounceHistory();
        _box.Redo();
    }

    /// <summary>Edit ▸ Paste: announced first, whether or not <c>PasteFromClipboard()</c> raises the control's <c>Paste</c> event.</summary>
    public void Paste()
    {
        AnnouncePaste();
        _box.PasteFromClipboard();
    }

    /// <summary>
    /// Put the caret at the start of a 0-based parser line and focus the editor. Once per id, and one
    /// dispatcher turn late: a Notes jump may have just switched Preview → Edit, and the pane is not
    /// laid out yet.
    /// </summary>
    public void ApplyJump(EditorJump jump, Action<Guid> onHandled)
    {
        ArgumentNullException.ThrowIfNull(onHandled);
        if (!_jumps.Claim(jump)) return;
        DispatcherQueue.TryEnqueue(() =>
        {
            // SelectionStart indexes the string the control reports (with \r), and lines are 1:1
            // between that and the LF model, so the offset is computed over box.Text as it stands.
            var offset = LineOffsets.OffsetOfLine(jump.Line, _box.Text);
            _box.Focus(FocusState.Programmatic);
            _box.Select(offset, 0);
            onHandled(jump.Id);
        });
    }

    /// <summary>Hand the pane its half of the scroll link; re-handed on every rebuild so a recreated pane stays registered.</summary>
    public void Attach(ScrollSync sync, ScrollSyncGuard guard)
    {
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(guard);
        _sync = sync;
        _guard = guard;
        sync.ScrollEditor = ApplyFraction;
    }

    /// <summary>The Find bar's hit: select it and give the editor focus so the selection is visible.</summary>
    public void SelectRange(int start, int length)
    {
        var reported = _box.Text.Length;
        start = Math.Clamp(start, 0, reported);
        length = Math.Clamp(length, 0, reported - start);
        _box.Focus(FocusState.Programmatic);
        _box.Select(start, length);
    }

    /// <summary>
    /// The find bar's Replace and Replace All (§3.5): <c>[start, start + length)</c> of the string
    /// the control reports becomes <paramref name="replacement"/>, with the caret after it. One
    /// <c>Select</c> + <c>SelectedText</c> pair — the Tab key's undo-preserving path, never
    /// <c>Text =</c>, which would clear the history — so the control records exactly <b>one</b> undo
    /// unit however many hits the span covers, and the <c>TextChanged</c> it raises carries the new
    /// text to the session like any other keystroke: undo, autosave, the dirty flag, the clobber
    /// guard and the word count all see it, and the session's echo comes back as identical text.
    /// The typing hooks are told first that this is an external edit, so the replacement goes in
    /// exactly as the writer typed it and no stale capital is left pointing into moved text.
    /// </summary>
    public void ReplaceRange(int start, int length, string replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        var reported = _box.Text.Length;
        start = Math.Clamp(start, 0, reported);
        length = Math.Clamp(length, 0, reported - start);
        _hooks.ExternalEdit(() =>
        {
            _box.Select(start, length);
            _box.SelectedText = replacement;
            _box.Select(start + replacement.Length, 0);
        });
    }

    public void FocusEditor() => _box.Focus(FocusState.Programmatic);

    void ApplyTheme()
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var palette = Palette.For(dark);
        // Clear backgrounds: the pane Grid paints paper, and the default template's fill, border and
        // focus underline are already off through the TextControl* resources in App.xaml.
        _box.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _box.BorderBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        _box.Foreground = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        _box.PlaceholderForeground = PaneBrushes.Get("PaperInkTertiaryBrush", palette.InkTertiary);
        _box.SelectionHighlightColor = new SolidColorBrush(PaneBrushes.ToColor(palette.Accent));
        // Both halves of §3.1's selection row: WinUI paints SelectionHighlightColor only while the
        // box has focus, and §3.5's Replace keeps focus in the find bar's replacement box on
        // purpose — so without this the hit the next Enter will replace is selected but invisible.
        _box.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(PaneBrushes.ToColor(palette.Accent));
    }

    void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        // Our own assignment is not an edit; the Mac's updateNSView guard, from the other side.
        if (_hooks.IsReplacing) return;
        TextEdited?.Invoke(Text);
    }

    /// <summary>
    /// Enter, in the tunnelling <c>PreviewKeyDown</c>: a multi-line TextBox inserts its own newline
    /// for Enter, and a control that handles a key itself never raises it to a <c>KeyDown +=</c>
    /// handler, or raises it only after — so the decision is made here, before the control acts,
    /// where Ctrl+Z / Ctrl+Y and the deletion keys are already announced.
    /// </summary>
    void OnPreviewEnter(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        // Handled only when SmartTyping made an edit; otherwise the control inserts its newline.
        // A modifier is never ours: Shift+Enter is the plain newline of §3.6, and Ctrl+Shift+Enter
        // is the Zen accelerator on the window root, which a handled key would starve.
        if (TryContinueOnEnter()) e.Handled = true;
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // There is no AcceptsTab in WinUI (that is WPF): without this, Tab moves focus out.
        if (e.Key != VirtualKey.Tab || ShiftIsDown()) return;
        _box.SelectedText = "\t";
        _box.SelectionStart += 1;
        _box.SelectionLength = 0;
        e.Handled = true;
    }

    // ── §3.6: the SmartTyping hooks ───────────────────────────────────────────────────────────

    /// <summary>
    /// Enter: the list / quote / table continuation, as one undoable replacement. False = not ours.
    /// Internal so the in-app self-test (§11.4) drives the very method the key does.
    /// </summary>
    internal bool TryContinueOnEnter()
    {
        if (ShiftIsDown() || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)) return false;
        if (_hooks.Enter(_box.Text, _box.SelectionStart, _box.SelectionLength) is not { } e) return false;
        Apply(e.Location, e.Length, e.Replacement, e.Caret);
        return true;
    }

    void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Not handled: the control performs the undo, redo or deletion itself; it is only told what
        // the change about to be reported is — history, or a shrink on one side of the caret. The
        // context menu's Undo / Redo do not pass here.
        if (TypingHooks.IsHistoryChord((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu)))
        {
            AnnounceHistory();
            return;
        }
        var direction = TypingHooks.DeletionKey((int)e.Key);
        if (direction != DeleteDirection.Unknown) AnnounceDeletion(direction);
    }

    void OnBeforeTextChanging(TextBox sender, TextBoxBeforeTextChangingEventArgs e) =>
        _hooks.BeforeTextChanging(_box.Text, _box.SelectionStart, _box.SelectionLength, e.NewText);

    void OnTextChanging(TextBox sender, TextBoxTextChangingEventArgs e)
    {
        // The typed text is in the box (undo unit one); replace it with the capitalized spelling
        // (undo unit two) — the hooks track the capital where it now stands (§3.4).
        if (_hooks.TextChanging(_box.Text, _box.SelectionStart, _box.SelectionLength) is not { } plan) return;
        Apply(plan.Position, plan.Inserted.Length, plan.Replacement, plan.Position + plan.Replacement.Length);
    }

    /// <summary>
    /// One replacement through the selection — the Tab key's undo-preserving path, never
    /// <c>Text =</c> — with the re-entrancy flag up so the events it raises are not judged again.
    /// </summary>
    void Apply(int location, int length, string replacement, int caret) =>
        _hooks.Applying(() =>
        {
            _box.Select(location, length);
            _box.SelectedText = replacement;
            _box.Select(caret, 0);
        });

    /// <summary>The flag is consumed by the change the paste announces, and settled a turn later in case the clipboard had nothing to give.</summary>
    void AnnouncePaste()
    {
        _hooks.AnnouncePaste();
        _ = DispatcherQueue.TryEnqueue(_hooks.SettlePaste);
    }

    /// <summary>Settled a turn later, never consumed: one history step may report more than one change, and one with nothing to do reports none.</summary>
    void AnnounceHistory()
    {
        _hooks.AnnounceHistory();
        _ = DispatcherQueue.TryEnqueue(_hooks.SettleHistory);
    }

    /// <summary>Settled a turn later like the history flag: a Backspace at the start of the text reports no change, and a key repeat announces again.</summary>
    void AnnounceDeletion(DeleteDirection direction)
    {
        _hooks.AnnounceDeletion(direction);
        _ = DispatcherQueue.TryEnqueue(_hooks.SettleDeletion);
    }

    /// <summary>
    /// One settings store stands behind every window, so this arrives on whichever window's thread
    /// toggled the switch. Read back there, <c>TypingHooks.Configure</c> would write this editor's
    /// typing state — the two bools and the tracked capital it clears — from a thread that is not
    /// the one <c>BeforeTextChanging</c> runs on, underneath a writer mid-word in this window. Both
    /// window classes post the same event to their own UI thread; the pane uses the queue it
    /// already settles the paste and history flags on.
    /// </summary>
    void OnSettingChanged(string key)
    {
        if (TypingSettings.IsKey(key)) _ = DispatcherQueue.TryEnqueue(ReadTypingSettings);
    }

    void ReadTypingSettings()
    {
        if (_settings is null) return;
        _hooks.Configure(TypingSettings.Read(_settings));
    }

    void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scroller ??= FindScroller(_box);
        if (_scroller is null) return;
        _scroller.ViewChanged -= OnViewChanged;
        _scroller.ViewChanged += OnViewChanged;
    }

    void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scroller is not { } sv || _sync is null) return;
        if (_guard is { IsEcho: true }) return;
        if (sv.ScrollableHeight <= 0) return;
        _sync.EditorDidScroll(sv.VerticalOffset / sv.ScrollableHeight);
    }

    void ApplyFraction(double fraction)
    {
        if (_scroller is not { } sv || _guard is null) return;
        using (_guard.Applying())
        {
            sv.ChangeView(null, ScrollSync.Clamp(fraction) * sv.ScrollableHeight, null, disableAnimation: true);
        }
    }

    /// <summary>
    /// The template part the WinUI TextBox scrolls with. Named lookup first, then the first
    /// ScrollViewer in the subtree — the fallback is what keeps the sync alive if the part is ever
    /// renamed (§12, risk 1).
    /// </summary>
    static ScrollViewer? FindScroller(DependencyObject root) =>
        Descendant(root, sv => sv.Name == "ContentElement") ?? Descendant(root, _ => true);

    static ScrollViewer? Descendant(DependencyObject root, Func<ScrollViewer, bool> match)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv && match(sv)) return sv;
            if (Descendant(child, match) is { } found) return found;
        }
        return null;
    }

    static bool ShiftIsDown() => IsDown(VirtualKey.Shift);

    static bool IsDown(VirtualKey key) =>
        (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}

/// <summary>
/// The paper theme, read from <c>App.xaml</c>'s theme dictionaries at run time with
/// <see cref="Palette"/> as the typed fallback (§10, §11.2): a code-behind lookup, because
/// <c>{ThemeResource}</c> in XAML is the one thing <c>tools/xamlcheck</c> cannot prove.
/// </summary>
/// <remarks>Lives beside the editor because the editor is the first control that needs it; move it to its own file the moment a second package does.</remarks>
internal static class PaneBrushes
{
    /// <summary>
    /// The dictionary first, then <see cref="Palette"/>. The fallback is not a rarely-taken path to
    /// be sorry about: <c>PaletteTests</c> reads App.xaml and fails when its hex values drift from
    /// Palette's, so the two branches paint the same colour by construction. Every caller re-runs
    /// this from <c>ActualThemeChanged</c>, so a switch re-tints on either branch — which is what the
    /// day-1 "no white flash on theme switch" check (§13.4, stage 4) confirms on a real Windows.
    /// </summary>
    public static Brush Get(string key, uint fallbackArgb)
    {
        if (Application.Current?.Resources is { } resources && resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(ToColor(fallbackArgb));
    }

    public static Color ToColor(uint argb)
    {
        var (a, r, g, b) = Palette.Channels(argb);
        return new Color { A = a, R = r, G = g, B = b };
    }
}

/// <summary>
/// The prose sizes §10 fixes, in effective pixels (the Mac's points × 4/3). Menus, dialogs and
/// CommandBar labels keep Segoe UI Variable and are not here.
/// </summary>
internal static class PaneTypography
{
    /// <summary>
    /// No font is bundled. Lucida Sans Typewriter is Windows's own typewriter face — Regular, Bold
    /// and Italic all ship with the OS, which markdown prose needs all three of — and it stands in
    /// for the Mac's American Typewriter here and in <c>ScreenHtml.FontStyle</c>, so the editor and
    /// the preview agree with each other on screen. The README states the stand-in plainly.
    /// </summary>
    public const string Family = "Lucida Sans Typewriter";

    /// <summary>11 pt — the footer.</summary>
    public const double FooterEpx = 14.7;

    /// <summary>13 pt — the find bar and the book sidebar rows.</summary>
    public const double SmallEpx = 17.3;
}
