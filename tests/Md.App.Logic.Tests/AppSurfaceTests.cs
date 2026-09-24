using Md.App.Logic;

namespace Md.App.Logic.Tests;

/// <summary>
/// The Md.App half of WP4 (<c>Controls/EditorPane.cs</c>, <c>ArticlePanes</c>, <c>ZenControls</c>,
/// <c>FindBar</c>, <c>FooterBar</c>) cannot be executed off Windows and has no test project of its
/// own — but it carries a dozen numbers and literals that shell-design.md §3, §5.4, §5.5 and §10 fix
/// verbatim, and a compile (which is all <c>tools/xamlcheck</c> gives) proves none of them.
///
/// So this suite pins the <b>source</b>, exactly as <see cref="PaletteTests"/> already pins
/// <c>App.xaml</c>'s hex values against <see cref="Md.App.Logic.Settings.Palette"/>: found through
/// <see cref="RepoFiles"/>, asserted as the design's own strings. A value that drifts fails here
/// rather than on a tester's machine four stages later. When any of these move, the design moved
/// first — change both.
/// </summary>
public sealed class AppSurfaceTests
{
    static string Source(string file) => File.ReadAllText(RepoFiles.At("src", "Md.App", "Controls", file));

    /// <summary>
    /// The file with its commentary removed. These controls document the very rules this suite
    /// pins, so "the source mentions AcceptsTab" and "the source mentions x:Bind" are true of a
    /// correct file; only the CODE may be searched for something forbidden.
    /// </summary>
    static string CodeOnly(string file)
    {
        var text = Source(file);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(?m)^\s*//.*$", string.Empty);
        return System.Text.RegularExpressions.Regex.Replace(text, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    static string XamlWithoutComments(string file)
    {
        var text = File.ReadAllText(RepoFiles.At("src", "Md.App", "Controls", file));
        return System.Text.RegularExpressions.Regex.Replace(text, @"<!--.*?-->", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    /// <summary>One method's body, by the signature that starts it, brace-matched — for rules about what a PARTICULAR method does, in what order.</summary>
    static string Method(string file, string signature)
    {
        var source = Source(file);
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{file} no longer declares `{signature}`.");
        var open = source.IndexOf('{', start);
        Assert.True(open >= 0, $"{signature} in {file} has no body.");
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }
        throw new Xunit.Sdk.XunitException($"{signature} in {file} is not brace-balanced.");
    }

    static void Pins(string file, string design, params string[] fragments)
    {
        var source = Source(file);
        foreach (var fragment in fragments)
            Assert.True(source.Contains(fragment, StringComparison.Ordinal),
                $"{file} no longer contains `{fragment}` — shell-design.md {design} fixes it.");
    }

    // ── §3.1 the TextBox configuration ────────────────────────────────────────────────────────

    [Fact]
    public void TheEditorIsLucidaSansTypewriterAtTwentyEffectivePixelsOnASixteenPixelInset()
    {
        // §10: 15 pt × 4/3 = 20 epx, and Lucida Sans Typewriter is American Typewriter's stand-in.
        Pins("EditorPane.cs", "§3.1, §10",
            "public const double FontSizeEpx = 20;",
            "_box.FontFamily = new FontFamily(PaneTypography.Family);",
            "_box.FontSize = FontSizeEpx;",
            "_box.Padding = new Thickness(16);");
    }

    [Fact]
    public void TheEditorTurnsOffBothAutoCorrectSourcesWinUiHas()
    {
        // Markdown punctuation must stay literal: smart quotes/dashes have no other switch here.
        Pins("EditorPane.cs", "§3.1",
            "_box.IsSpellCheckEnabled = false;",
            "_box.IsTextPredictionEnabled = false;");
    }

    [Fact]
    public void TheEditorWrapsAcceptsReturnAndScrollsVerticallyOnly()
    {
        Pins("EditorPane.cs", "§3.1",
            "_box.AcceptsReturn = true;",
            "_box.TextWrapping = TextWrapping.Wrap;",
            "ScrollViewer.SetHorizontalScrollBarVisibility(_box, ScrollBarVisibility.Disabled);",
            "ScrollViewer.SetVerticalScrollBarVisibility(_box, ScrollBarVisibility.Auto);");
    }

    [Fact]
    public void TheEditorHasNoBorderAndTakesItsPlaceholderFromStrings()
    {
        Assert.Equal("# Start writing\u2026", Strings.EditorPlaceholder);      // U+2026, one character
        Pins("EditorPane.cs", "§3.1",
            "_box.BorderThickness = new Thickness(0);",
            "_box.PlaceholderText = Strings.EditorPlaceholder;",
            "_box.PlaceholderForeground = PaneBrushes.Get(\"PaperInkTertiaryBrush\"");
    }

    [Fact]
    public void TabInsertsATabBecauseThereIsNoAcceptsTabInWinUi()
    {
        // AcceptsTab is WPF only; without the handler Tab moves focus out of the editor.
        Assert.DoesNotContain("AcceptsTab", CodeOnly("EditorPane.cs"), StringComparison.Ordinal);
        Pins("EditorPane.cs", "§3.1",
            "e.Key != VirtualKey.Tab",
            "_box.SelectedText = \"\\t\";",
            "_box.SelectionStart += 1;",
            "_box.SelectionLength = 0;",
            "e.Handled = true;");
    }

    [Fact]
    public void TheSelectionIsPaintedWhetherOrNotTheEditorHasFocus()
    {
        // §3.1's selection row, both halves. `SelectionHighlightColor` alone is painted only while
        // the TextBox itself has focus — and §3.5's Replace leaves focus in the replacement box on
        // purpose, so with that one brush every hit after the first is replaced blind: the writer
        // cannot see which hit the next Enter will act on, nor tell a wrap-around from a step
        // forward. The design's table carries both, because App.xaml's TextControl* overrides do
        // not reach either.
        Pins("EditorPane.cs", "§3.1, §3.5",
            "_box.SelectionHighlightColor = new SolidColorBrush(PaneBrushes.ToColor(palette.Accent));",
            "_box.SelectionHighlightColorWhenNotFocused = new SolidColorBrush(PaneBrushes.ToColor(palette.Accent));");
        Assert.Contains("SelectionHighlightColorWhenNotFocused", File.ReadAllText(RepoFiles.At("docs", "shell-design.md")), StringComparison.Ordinal);
    }

    // ── §3.6 the SmartTyping hooks ────────────────────────────────────────────────────────────

    [Fact]
    public void TheEditorRoutesEnterAndTheLetterThroughTheHooksOnTheTabKeysUndoPath()
    {
        // smart-typing.md §3.3 "Windows", shell-design.md §3.6: KeyDown for Enter, BeforeTextChanging
        // to decide the letter, the synchronous TextChanging to apply it, PreviewKeyDown for the
        // history chords, the composition and paste announcements — every one of them a call into
        // TypingHooks with the box's own text and selection as the inputs, unconverted. What
        // TypingHooksTests drives through a fake TextBox is what this file wires to the real one.
        Pins("EditorPane.cs", "§3.6",
            "_box.BeforeTextChanging += OnBeforeTextChanging;",
            "_box.TextChanging += OnTextChanging;",
            "_box.PreviewKeyDown += OnPreviewKeyDown;",
            "_box.TextCompositionStarted += (_, _) => _hooks.CompositionStarted();",
            "_box.TextCompositionEnded += (_, _) => _hooks.CompositionEnded();",
            "_box.Paste += (_, _) => AnnouncePaste();",
            "if (e.Key != VirtualKey.Enter) return;",
            "_hooks.Enter(_box.Text, _box.SelectionStart, _box.SelectionLength)",
            "_hooks.BeforeTextChanging(_box.Text, _box.SelectionStart, _box.SelectionLength, e.NewText);",
            "_hooks.TextChanging(_box.Text, _box.SelectionStart, _box.SelectionLength)",
            "TypingHooks.IsHistoryChord((int)e.Key, IsDown(VirtualKey.Control), IsDown(VirtualKey.Menu))",
            "_box.Select(location, length);",
            "_box.SelectedText = replacement;",
            "_box.Select(caret, 0);");

        // Both settings are read through the seam into the hooks, which is where "a function whose
        // setting is off is never called" (§3.1) is decided and tested.
        Pins("EditorPane.cs", "§3.6",
            "public void UseSettings(ISettingsStore? settings)",
            "_hooks.Configure(TypingSettings.Read(_settings));");

        // Shift+Enter is the plain newline (§3.6); Ctrl+Shift+Enter is Zen's, on the root.
        Pins("EditorPane.cs", "§3.6", "if (ShiftIsDown() || IsDown(VirtualKey.Control) || IsDown(VirtualKey.Menu)) return false;");

        // Never `Text =` for an edit — it clears the control's undo history. The one assignment is
        // SetText's external replace, which the design gives that cost to on purpose (§3.1).
        var code = CodeOnly("EditorPane.cs");
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"_box\.Text\s*=[^=]"));
        Assert.DoesNotContain("e.Cancel = true", code, StringComparison.Ordinal);   // the keystroke is never lost
    }

    [Fact]
    public void ASettingsChangeIsReadBackOnTheEditorsOwnThread()
    {
        // §3.6: one store behind every window, so `Changed` arrives on whichever window's thread
        // toggled the switch. Read back there, `TypingHooks.Configure` would write this editor's
        // two bools and clear its tracked capital from a thread that is not the one
        // BeforeTextChanging runs on — underneath a writer mid-word in another window. Both window
        // classes post that same event to their own UI thread; so does the pane, on the queue it
        // already uses for the settle calls.
        var body = Method("EditorPane.cs", "void OnSettingChanged(string key)");
        Assert.Contains("DispatcherQueue.TryEnqueue(ReadTypingSettings)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IsKey(key)) ReadTypingSettings();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePaneHoldsNoTypingStateOfItsOwn()
    {
        // The first cut kept the override, the pending plan and the flags in the pane, and lost the
        // gesture in a place no test off Windows could reach: SetText cleared it on the session's
        // echo of every keystroke. All of it is TypingHooks' now, and the pane must not grow a copy.
        var code = CodeOnly("EditorPane.cs");
        Assert.Contains("readonly TypingHooks _hooks = new();", code, StringComparison.Ordinal);
        foreach (var state in new[] { "CapitalTracker", "CapitalizationPlan", "bool _composing", "bool _pasting", "bool _applying", "bool _replacing", "bool _history", "bool _continueLists", "bool _capitalizeSentences" })
            Assert.False(code.Contains(state, StringComparison.Ordinal), $"EditorPane.cs declares `{state}` — typing state belongs in TypingHooks, where it is tested.");
    }

    [Fact]
    public void SetTextAsksTheHooksBeforeItAssignsSoTheEchoLeavesTheOverrideArmed()
    {
        // The echo of identical text is not a replacement (§3.4): the ExternalText answer gates the
        // assignment, and the assignment runs under the hooks' Replacing flag so its TextChanged is
        // not reported as an edit.
        var body = Method("EditorPane.cs", "public void SetText(string text)");
        var gate = body.IndexOf("if (!_hooks.ExternalText(Text, text)) return;", StringComparison.Ordinal);
        var assign = body.IndexOf("_hooks.Replacing(() => _box.Text = EditorText.ToTextBox(text));", StringComparison.Ordinal);
        Assert.True(gate >= 0, "SetText no longer asks TypingHooks.ExternalText.");
        Assert.True(assign > gate, "SetText assigns before it asks whether the text is the echo.");
        Assert.DoesNotContain("Clear()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetHistoryTellsTheHooksBeforeItClearsTheControlsHistory()
    {
        // §3.4: an external replacement clears both — and the one the text cannot show (another
        // article with the same text, a reload the disk agrees with) reaches the hooks only here.
        var body = Method("EditorPane.cs", "public void ResetHistory()");
        var reset = body.IndexOf("_hooks.Reset();", StringComparison.Ordinal);
        var clear = body.IndexOf("_box.ClearUndoRedoHistory();", StringComparison.Ordinal);
        Assert.True(reset >= 0 && clear > reset, "ResetHistory must tell the hooks (_hooks.Reset()) before it clears the control's history.");
        Assert.DoesNotContain("_box.Text", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// §3.6: Enter is decided in <c>PreviewKeyDown</c>, which tunnels to the box before the control
    /// acts on the key — the same reason the history chords and the deletion keys are announced
    /// there. A multi-line <c>TextBox</c> inserts its own newline for Enter, and an instance
    /// <c>KeyDown</c> handler is not guaranteed to run before that (a control that handles a key
    /// in its own <c>OnKeyDown</c> never raises it to <c>+=</c> handlers at all): the continuation
    /// would then either never happen or land after the control's newline. Tab stays in
    /// <c>KeyDown</c> — the control does not handle Tab, it lets it move focus.
    /// </summary>
    [Fact]
    public void EnterIsDecidedInPreviewKeyDownBeforeTheControlInsertsItsNewline()
    {
        Pins("EditorPane.cs", "§3.6", "_box.PreviewKeyDown += OnPreviewEnter;");
        var enter = Method("EditorPane.cs", "void OnPreviewEnter(object sender, KeyRoutedEventArgs e)");
        Assert.Contains("e.Key != VirtualKey.Enter", enter, StringComparison.Ordinal);
        Assert.Contains("if (TryContinueOnEnter()) e.Handled = true;", enter, StringComparison.Ordinal);
        // KeyDown keeps only the Tab key.
        var keyDown = Method("EditorPane.cs", "void OnKeyDown(object sender, KeyRoutedEventArgs e)");
        Assert.DoesNotContain("VirtualKey.Enter", keyDown, StringComparison.Ordinal);
        Assert.DoesNotContain("TryContinueOnEnter", keyDown, StringComparison.Ordinal);
    }

    [Fact]
    public void BackspaceAndDeleteAreAnnouncedFromPreviewKeyDown()
    {
        // The side of the caret a key removes from is the only way to tell which of two identical
        // units it took (smart-typing.md §3.4 through TextEdit.Between); announced like the history
        // chords, never handled, settled a turn later.
        var preview = Method("EditorPane.cs", "void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)");
        Assert.Contains("TypingHooks.DeletionKey((int)e.Key)", preview, StringComparison.Ordinal);
        Assert.Contains("AnnounceDeletion(direction);", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Handled", preview, StringComparison.Ordinal);
        var announce = Method("EditorPane.cs", "void AnnounceDeletion(DeleteDirection direction)");
        Assert.Contains("_hooks.AnnounceDeletion(direction);", announce, StringComparison.Ordinal);
        Assert.Contains("_ = DispatcherQueue.TryEnqueue(_hooks.SettleDeletion);", announce, StringComparison.Ordinal);
    }

    [Fact]
    public void UndoRedoAndPasteAnnounceThemselvesBeforeTheControlActs()
    {
        // §3.5: a history step is not typing; §3.3: a paste is never capitalized. Each public entry
        // announces first, then reaches the control — and the announcement settles a turn later.
        foreach (var (signature, announce, act) in new[]
        {
            ("public void Undo()", "AnnounceHistory();", "_box.Undo();"),
            ("public void Redo()", "AnnounceHistory();", "_box.Redo();"),
            ("public void Paste()", "AnnouncePaste();", "_box.PasteFromClipboard();"),
        })
        {
            var body = Method("EditorPane.cs", signature);
            var first = body.IndexOf(announce, StringComparison.Ordinal);
            var second = body.IndexOf(act, StringComparison.Ordinal);
            Assert.True(first >= 0 && second > first, $"{signature} must announce ({announce}) before it acts ({act}).");
        }
        Assert.Contains("_ = DispatcherQueue.TryEnqueue(_hooks.SettleHistory);", Method("EditorPane.cs", "void AnnounceHistory()"), StringComparison.Ordinal);
        Assert.Contains("_ = DispatcherQueue.TryEnqueue(_hooks.SettlePaste);", Method("EditorPane.cs", "void AnnouncePaste()"), StringComparison.Ordinal);
        // PreviewKeyDown tunnels before the control undoes; it announces and never handles the key.
        var preview = Method("EditorPane.cs", "void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)");
        Assert.Contains("AnnounceHistory();", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("e.Handled", preview, StringComparison.Ordinal);
    }

    // ── §3.4 the scroll half ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TheScrollerIsFoundByTemplatePartNameWithAFirstScrollViewerFallback()
    {
        // The named lookup is the documented part; the fallback is what keeps the sync alive if the
        // WinUI template ever renames it (§12, risk 1). Both halves have to stay.
        Pins("EditorPane.cs", "§3.4",
            "sv.Name == \"ContentElement\"",
            "Descendant(root, _ => true)",
            "disableAnimation: true");
    }

    // ── §5.4 the Zen capsule ──────────────────────────────────────────────────────────────────

    [Fact]
    public void TheCapsuleCarriesTheThreeSegoeFluentGlyphsSection10Maps()
    {
        // square.and.pencil, eye, arrow.down.right.and.arrow.up.left — §10's icon table.
        Pins("ZenControls.cs", "§5.4, §10",
            "const string WriteGlyph = \"\\uE70F\";",
            "const string ReadGlyph = \"\\uE7B3\";",
            "const string ExitGlyph = \"\\uE73F\";");
    }

    [Fact]
    public void TheCapsuleGeometryIsTheMacs()
    {
        Pins("ZenControls.cs", "§5.4",
            "_row.Spacing = 2;",
            "_capsule.CornerRadius = new CornerRadius(20);",
            "_capsule.Padding = new Thickness(6);",
            "_capsule.Margin = new Thickness(0, 10, 0, 0);",
            "VerticalAlignment = VerticalAlignment.Top;",
            "Rectangle _separator = new() { Width = 1, Height = 14 };",
            "new Thickness(8, 2, 8, 2)",        // the two switches
            "new Thickness(6, 2, 6, 2)");       // exit
    }

    [Fact]
    public void TheCapsuleFadesOverThreeHundredMillisecondsAndStopsHitTestingWhenHidden()
    {
        Pins("ZenControls.cs", "§5.4",
            "new ScalarTransition { Duration = TimeSpan.FromMilliseconds(300) }",
            "_capsule.Opacity = shown ? 1 : 0;",
            "_capsule.IsHitTestVisible = shown;");
    }

    [Fact]
    public void TheCapsuleTintIsAcrylicOverPaperSecondaryAtEightyPercentWithAFallback()
    {
        // Built in code, not a ThemeResource, so tools/xamlcheck can prove it exists.
        Pins("ZenControls.cs", "§5.4",
            "new AcrylicBrush { TintColor = tint, TintOpacity = 0.8, FallbackColor = tint }",
            "palette.PaperSecondary");
    }

    [Fact]
    public void TheCapsulesTooltipsAndTheFindBarsLabelsAreTheMacsWords()
    {
        Assert.Equal("Write", Strings.Zen.Write);
        Assert.Equal("Read", Strings.Zen.Read);
        Assert.Equal("Exit Zen Mode", Strings.Zen.Exit);
        Assert.Equal("Next", Strings.Find.Next);
        Assert.Equal("Previous", Strings.Find.Previous);
        Assert.Equal("Done", Strings.Find.Done);

        Pins("ZenControls.cs", "§5.4", "Strings.Zen.Write", "Strings.Zen.Read", "Strings.Zen.Exit");
        Pins("FindBar.xaml.cs", "§3.5", "Strings.Find.Next", "Strings.Find.Previous", "Strings.Find.Done");
    }

    [Fact]
    public void TheSelectedSwitchIsAccentAndTheOtherIsSecondaryInk()
    {
        Pins("ZenControls.cs", "§5.4",
            "_writeIcon.Foreground = state.ZenReading ? secondary : accent;",
            "_readIcon.Foreground = state.ZenReading ? accent : secondary;");
    }

    // ── §5.5 the footer, §3.5 the find bar ────────────────────────────────────────────────────

    [Fact]
    public void TheFooterIsElevenPointRightAlignedWithTabularFigures()
    {
        // 11 pt × 4/3 = 14.7 epx; NumeralAlignment is an ATTACHED property — TextBlock has none.
        Assert.Equal(14.7, ReadDouble("EditorPane.cs", "public const double FooterEpx = "));
        Pins("FooterBar.xaml.cs", "§5.5",
            "Counts.FontSize = PaneTypography.FooterEpx;",
            "Typography.SetNumeralAlignment(Counts, FontNumeralAlignment.Tabular);",
            "Root.Padding = new Thickness(12, 5, 12, 5);",
            "Strings.Footer(derived.Words, derived.Characters)");
        Assert.Contains("HorizontalAlignment=\"Right\"", File.ReadAllText(RepoFiles.At("src", "Md.App", "Controls", "FooterBar.xaml")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFindBarIsThirteenPointAndAnswersEnterAndEscape()
    {
        // 13 pt × 4/3 = 17.3 epx.
        Assert.Equal(17.3, ReadDouble("EditorPane.cs", "public const double SmallEpx = "));
        Pins("FindBar.xaml.cs", "§3.5",
            "QueryBox.FontSize = PaneTypography.SmallEpx;",
            "case VirtualKey.Enter:",
            "Search(forward: !ShiftIsDown());",
            "case VirtualKey.Escape:");
    }

    [Fact]
    public void TheFindBarsReplaceHalfIsLabelledFromStringsAndWiredInTheCodeBehind()
    {
        // §3.5: the replacement box and its two buttons, like everything else in this control —
        // names in the XAML, words from Md.App.Logic.Strings.Find, handlers here.
        Assert.Equal("Replace", Strings.Find.Replace);
        Assert.Equal("Replace All", Strings.Find.ReplaceAll);
        Assert.Equal("Find", Strings.Find.QueryPlaceholder);
        Assert.Equal("Replace with", Strings.Find.ReplacementPlaceholder);

        Pins("FindBar.xaml.cs", "§3.5",
            "ReplaceBox.FontSize = PaneTypography.SmallEpx;",
            "QueryBox.PlaceholderText = Strings.Find.QueryPlaceholder;",
            "ReplaceBox.PlaceholderText = Strings.Find.ReplacementPlaceholder;",
            "ReplaceButton.Content = Strings.Find.Replace;",
            "ReplaceAllButton.Content = Strings.Find.ReplaceAll;",
            "ReplaceButton.Click += (_, _) => Replace();",
            "ReplaceAllButton.Click += (_, _) => ReplaceAll();",
            "ReplaceRequested?.Invoke(QueryBox.Text, ReplaceBox.Text);",
            "ReplaceAllRequested?.Invoke(QueryBox.Text, ReplaceBox.Text);",
            "public void FocusReplacement()");

        // Enter in the replacement box is Replace, Shift+Enter Replace All, Esc closes — the query
        // box's own answer to the same three keys is pinned above.
        var body = Method("FindBar.xaml.cs", "void OnReplacementKeyDown(object sender, KeyRoutedEventArgs e)");
        Assert.Contains("if (ShiftIsDown()) ReplaceAll();", body, StringComparison.Ordinal);
        Assert.Contains("else Replace();", body, StringComparison.Ordinal);
        Assert.Contains("case VirtualKey.Escape:", body, StringComparison.Ordinal);

        // Neither button fires on an empty query: an empty find is not a whole-document replace.
        foreach (var signature in new[] { "public void Replace()", "public void ReplaceAll()" })
            Assert.Contains("if (QueryBox.Text.Length == 0) return;", Method("FindBar.xaml.cs", signature), StringComparison.Ordinal);

        // The four new parts exist in the XAML, by name and nothing else (§11.2).
        var xaml = XamlWithoutComments("FindBar.xaml");
        foreach (var name in new[] { "x:Name=\"ReplaceBox\"", "x:Name=\"ReplaceButton\"", "x:Name=\"ReplaceAllButton\"" })
            Assert.Contains(name, xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyReplaceKeepsFocusInTheReplacementBox()
    {
        // §3.5: the run of Enters is Replace's, and keeping focus here is what stops the next one
        // reaching the editor. Replace All is a one-shot with no run to preserve, and parking focus
        // in a TextBox after it would hand that box the Ctrl+Z the writer means for the document —
        // Undo is not a root accelerator (§2.4), so the chord belongs to whatever control has focus.
        // Where focus lands after a Replace All is the window's decision; this control must not
        // overrule it a line later.
        Assert.Contains("ReplaceBox.Focus(FocusState.Programmatic);", Method("FindBar.xaml.cs", "public void Replace()"), StringComparison.Ordinal);
        Assert.DoesNotContain("ReplaceBox.Focus", Method("FindBar.xaml.cs", "public void ReplaceAll()"), StringComparison.Ordinal);
    }

    [Fact]
    public void ReplaceGoesInThroughTheSelectionAsOneUndoUnitAndIsNotTyping()
    {
        // §3.5: one Select + SelectedText pair (never `Text =`, which the pin above counts), with
        // the hooks told first that this is an external edit — so the writer's replacement goes in
        // exactly as typed and the control still records exactly one undoable step.
        var body = Method("EditorPane.cs", "public void ReplaceRange(int start, int length, string replacement)");
        var announce = body.IndexOf("_hooks.ExternalEdit(() =>", StringComparison.Ordinal);
        var assign = body.IndexOf("_box.SelectedText = replacement;", StringComparison.Ordinal);
        Assert.True(announce >= 0, "ReplaceRange no longer tells the hooks the edit is external.");
        Assert.True(assign > announce, "ReplaceRange assigns before it announces the external edit.");
        Assert.DoesNotContain("_box.Text =", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ClearUndoRedoHistory", body, StringComparison.Ordinal);   // a replace IS undoable
    }

    // ── §11.2 the XAML lint the design promises ───────────────────────────────────────────────

    [Theory]
    [InlineData("ArticlePanes.xaml")]
    [InlineData("FindBar.xaml")]
    [InlineData("FooterBar.xaml")]
    public void TheXamlIsStructureAndNamesOnly(string file)
    {
        // §11.2: "no x:Bind, no {Binding}, no templates, no ThemeResource lookups the lint cannot
        // prove". Everything dynamic — brushes, faces, labels, handlers — is set from code, so a
        // green xamlcheck really is a green app.
        var xaml = XamlWithoutComments(file);
        foreach (var banned in new[] { "x:Bind", "{Binding", "ThemeResource", "StaticResource", "Click=", "<Style", "ControlTemplate", "DataTemplate", "xmlns:toolkit", "CommunityToolkit" })
            Assert.True(!xaml.Contains(banned, StringComparison.Ordinal), $"{file} contains `{banned}`, which §11.2 forbids.");
    }

    static double ReadDouble(string file, string prefix)
    {
        var source = Source(file);
        var start = source.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{file} no longer declares `{prefix}`.");
        var tail = source[(start + prefix.Length)..];
        var end = tail.IndexOf(';', StringComparison.Ordinal);
        return double.Parse(tail[..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
