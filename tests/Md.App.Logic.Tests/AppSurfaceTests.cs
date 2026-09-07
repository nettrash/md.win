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
