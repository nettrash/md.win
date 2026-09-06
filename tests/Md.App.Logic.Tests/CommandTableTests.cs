using Md.App.Logic.Commands;
using Md.Core.Markdown;

namespace Md.App.Logic.Tests;

/// <summary>
/// The invariants of the declarative command surface (shell-final.md §2): one row per command, one
/// owner per chord, the Mac's wording to the character, and the seven menus with the dividers §2
/// draws. Everything the WinUI builder does is a walk over what is asserted here.
/// </summary>
public class CommandTableTests
{
    // ── The table itself ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryCommandIdAppearsExactlyOnce()
    {
        var ids = CommandTable.All.Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(Enum.GetValues<CommandId>().OrderBy(i => i), ids.OrderBy(i => i));
    }

    [Fact]
    public void NoChordIsClaimedTwice()
    {
        var chords = CommandTable.All.Where(s => s.Chord is not null).Select(s => s.Chord!.Value).ToList();
        var duplicates = chords.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => g.Key.DisplayText).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void EveryRowHasAPathExceptTheAcceleratorOnlyOne()
    {
        foreach (var spec in CommandTable.All)
        {
            if (spec.Kind == CommandKind.Accelerator) Assert.Null(spec.Path);
            else Assert.NotNull(spec.Path);
        }
        Assert.Equal<CommandId>([CommandId.Escape], CommandTable.All.Where(s => s.Kind == CommandKind.Accelerator).Select(s => s.Id));
    }

    [Fact]
    public void EveryRowSitsInOneOfTheSevenMenus()
    {
        foreach (var spec in CommandTable.All.Where(s => s.Path is not null))
            Assert.Contains(spec.Path!.Menu, CommandTable.MenuTitles);
    }

    [Fact]
    public void ForThrowsForNothingBecauseTheTableIsComplete()
    {
        foreach (var id in Enum.GetValues<CommandId>()) Assert.Equal(id, CommandTable.For(id).Id);
    }

    // ── Titles, verbatim ──────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every title, transcribed from §2 independently of the table. The ellipsis is U+2026 (written
    /// as an escape here too): three ASCII dots in either file would fail this.
    /// </summary>
    public static TheoryData<CommandId, string> Titles => new()
    {
        { CommandId.New, "New" },
        { CommandId.Open, "Open…" },
        { CommandId.OpenRecentEntry, "Open Recent" },
        { CommandId.ClearRecent, "Clear Menu" },
        { CommandId.OpenTextBundleFolder, "Open TextBundle Folder…" },
        { CommandId.Example, "Examples" },
        { CommandId.ExampleBook, "Example Book…" },
        { CommandId.Close, "Close" },
        { CommandId.Save, "Save" },
        { CommandId.SaveAs, "Save As…" },
        { CommandId.Duplicate, "Duplicate" },
        { CommandId.Rename, "Rename…" },
        { CommandId.MoveTo, "Move To…" },
        { CommandId.RevertToSaved, "Revert to Saved" },
        { CommandId.Print, "Print…" },
        { CommandId.ShareSource, "Source…" },
        { CommandId.ShareRenderedPdf, "Rendered PDF…" },
        { CommandId.ExportPdf, "PDF…" },
        { CommandId.ExportHtml, "HTML…" },
        { CommandId.ExportEpub, "EPUB…" },
        { CommandId.ExportLaTeX, "LaTeX…" },
        { CommandId.ExportTextBundle, "TextBundle…" },
        { CommandId.ExportDiagramSvg, "Diagram as SVG" },
        { CommandId.PdfPageSize, "PDF Page Size" },
        { CommandId.Exit, "Exit" },
        { CommandId.Undo, "Undo" },
        { CommandId.Redo, "Redo" },
        { CommandId.Cut, "Cut" },
        { CommandId.Copy, "Copy" },
        { CommandId.Paste, "Paste" },
        { CommandId.Delete, "Delete" },
        { CommandId.SelectAll, "Select All" },
        { CommandId.Find, "Find…" },
        { CommandId.FindNext, "Find Next" },
        { CommandId.FindPrevious, "Find Previous" },
        { CommandId.UseSelectionForFind, "Use Selection for Find" },
        { CommandId.ViewEdit, "Edit" },
        { CommandId.ViewSplit, "Split" },
        { CommandId.ViewPreview, "Preview" },
        { CommandId.ZenMode, "Zen Mode" },
        { CommandId.ShowSidebar, "Show Sidebar" },
        { CommandId.FullScreen, "Enter Full Screen" },
        { CommandId.NewBook, "New Book…" },
        { CommandId.OpenBook, "Open Book…" },
        { CommandId.ShowBook, "Show Book" },
        { CommandId.CloseBook, "Close Book" },
        { CommandId.ShareBookPdf, "Share Book as PDF" },
        { CommandId.PrintBook, "Print Book…" },
        { CommandId.ExportBookPdf, "PDF…" },
        { CommandId.ExportBookEpub, "EPUB…" },
        { CommandId.ExportBookLaTeX, "LaTeX…" },
        { CommandId.PreviousArticle, "Previous Article" },
        { CommandId.NextArticle, "Next Article" },
        { CommandId.Contents, "Contents" },
        { CommandId.Notes, "Notes" },
        { CommandId.Minimize, "Minimize" },
        { CommandId.Zoom, "Zoom" },
        { CommandId.ActivateWindow, "Window List" },
        { CommandId.Help, "md Help" },
        { CommandId.PrivacyPolicy, "Privacy Policy" },
        { CommandId.About, "About md" },
        { CommandId.Escape, "Escape" },
    };

    [Theory]
    [MemberData(nameof(Titles))]
    public void TitleIsTheMacsWordVerbatim(CommandId id, string expected) => Assert.Equal(expected, CommandTable.For(id).Title);

    [Fact]
    public void EveryTitleWithAnEllipsisIsSpelledInSection2OfTheDesign()
    {
        // The design is the source of truth for the wording; this catches an ASCII "..." creeping
        // into either file, which no eye catches in a menu.
        var design = File.ReadAllText(RepoFiles.At("docs", "shell-design.md"));
        var start = design.IndexOf("## 2. The command surface", StringComparison.Ordinal);
        var end = design.IndexOf("## 3. The editor", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "shell-design.md §2 not found");
        var section = design[start..end];

        foreach (var spec in CommandTable.All.Where(s => s.Title.Contains('…', StringComparison.Ordinal)))
            Assert.Contains(spec.Title, section, StringComparison.Ordinal);
    }

    [Fact]
    public void ExitFullScreenIsTheOtherHalfOfTheFullScreenRow()
    {
        var windowed = ShellSnapshot.Empty;
        Assert.Equal("Enter Full Screen", CommandTable.DisplayTitle(CommandId.FullScreen, windowed));
        Assert.Equal("Exit Full Screen", CommandTable.DisplayTitle(CommandId.FullScreen, windowed with { IsFullScreen = true }));
        // Everything else ignores the snapshot.
        Assert.Equal("Save", CommandTable.DisplayTitle(CommandId.Save, windowed with { IsFullScreen = true }));
    }

    // ── Chords ────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<CommandId, string> Chords => new()
    {
        { CommandId.New, "Ctrl+N" },
        { CommandId.Open, "Ctrl+O" },
        { CommandId.Close, "Ctrl+W" },
        { CommandId.Save, "Ctrl+S" },
        { CommandId.SaveAs, "Ctrl+Shift+S" },
        { CommandId.Print, "Ctrl+P" },
        { CommandId.Undo, "Ctrl+Z" },
        { CommandId.Redo, "Ctrl+Y" },
        { CommandId.Cut, "Ctrl+X" },
        { CommandId.Copy, "Ctrl+C" },
        { CommandId.Paste, "Ctrl+V" },
        { CommandId.Delete, "Del" },
        { CommandId.SelectAll, "Ctrl+A" },
        { CommandId.Find, "Ctrl+F" },
        { CommandId.FindNext, "F3" },
        { CommandId.FindPrevious, "Shift+F3" },
        { CommandId.UseSelectionForFind, "Ctrl+E" },
        { CommandId.ViewEdit, "Ctrl+1" },
        { CommandId.ViewSplit, "Ctrl+2" },
        { CommandId.ViewPreview, "Ctrl+3" },
        { CommandId.ZenMode, "Ctrl+Shift+Enter" },
        { CommandId.FullScreen, "F11" },
        { CommandId.ShowBook, "Ctrl+Shift+B" },
        { CommandId.PreviousArticle, "Ctrl+Alt+Up" },
        { CommandId.NextArticle, "Ctrl+Alt+Down" },
        { CommandId.Help, "F1" },
        { CommandId.Escape, "Esc" },
    };

    [Theory]
    [MemberData(nameof(Chords))]
    public void ChordReadsTheWindowsWay(CommandId id, string expected)
    {
        var chord = CommandTable.For(id).Chord;
        Assert.NotNull(chord);
        Assert.Equal(expected, chord!.Value.DisplayText);
    }

    [Fact]
    public void OnlyTheseTwentySevenRowsCarryAChord()
    {
        var withChords = CommandTable.All.Where(s => s.Chord is not null).Select(s => s.Id).ToHashSet();
        Assert.Equal(Chords.Select(row => (CommandId)row[0]!).OrderBy(i => i), withChords.OrderBy(i => i));
    }

    [Fact]
    public void TheEditorsOwnKeysAreShownButNeverRegisteredOnTheRoot()
    {
        // §2.4: Ctrl+Z/Y/X/C/V/A and Del stay with the focused control — the WebView2 must keep
        // Ctrl+C on a preview selection, and Del must delete a character, not a selection's worth.
        CommandId[] controlOwned = [CommandId.Undo, CommandId.Redo, CommandId.Cut, CommandId.Copy, CommandId.Paste, CommandId.Delete, CommandId.SelectAll];
        foreach (var id in controlOwned)
        {
            var spec = CommandTable.For(id);
            Assert.NotNull(spec.Chord);
            Assert.False(spec.RootAccelerator);
        }
        Assert.Equal(controlOwned.OrderBy(i => i), CommandTable.All.Where(s => !s.RootAccelerator).Select(s => s.Id).OrderBy(i => i));
        Assert.DoesNotContain(CommandTable.RootAccelerators, s => controlOwned.Contains(s.Id));
        Assert.Equal(20, CommandTable.RootAccelerators.Count);
    }

    [Theory]
    [InlineData(VirtualKeys.Escape, "Esc")]
    [InlineData(VirtualKeys.Delete, "Del")]
    [InlineData(VirtualKeys.Enter, "Enter")]
    [InlineData(VirtualKeys.Up, "Up")]
    [InlineData(VirtualKeys.Down, "Down")]
    [InlineData(VirtualKeys.F1, "F1")]
    [InlineData(VirtualKeys.F11, "F11")]
    [InlineData(VirtualKeys.Number1, "1")]
    [InlineData(VirtualKeys.Z, "Z")]
    public void KeyNamesAreTheMenuSpellings(int virtualKey, string expected) => Assert.Equal(expected, Chord.KeyName(virtualKey));

    [Fact]
    public void ModifiersReadCtrlThenAltThenShift() =>
        Assert.Equal("Ctrl+Alt+Shift+F3", new Chord(VirtualKeys.F3, KeyModifiers.Ctrl | KeyModifiers.Alt | KeyModifiers.Shift).DisplayText);

    // ── The bar ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheBarIsSevenMenusInTheMacsOrderWithoutTheAppMenu()
    {
        Assert.Equal<string>(["File", "Edit", "View", "Book", "Go", "Window", "Help"], CommandTable.MenuTitles);
        Assert.Equal(CommandTable.MenuTitles, CommandTable.Menus.Select(m => m.Title).ToList());
        Assert.All(CommandTable.Menus, m => Assert.Null(m.Command));
    }

    static MenuNode Menu(string title) => CommandTable.Menus.Single(m => m.Title == title);

    static MenuNode Child(MenuNode parent, string title) => parent.Children.Single(c => c.Title == title);

    /// <summary>Rows in order, a lone "—" standing in for each separator §2 draws.</summary>
    static IReadOnlyList<string> Rows(MenuNode menu) =>
        [.. menu.Children.SelectMany(c => c.SeparatorBefore ? new[] { "—", c.Title } : new[] { c.Title })];

    [Fact]
    public void FileReadsAsSection22DoesDividersIncluded() =>
        Assert.Equal<string>(
        [
            "New", "Open…", "Open Recent", "Open TextBundle Folder…", "Examples",
            "—", "Close", "Save", "Save As…", "Duplicate", "Rename…", "Move To…", "Revert to Saved",
            "—", "Print…",
            "—", "Share", "Export",
            "—", "Exit",
        ], Rows(Menu("File")));

    [Fact]
    public void FilesSubmenusHoldWhatSection22Says()
    {
        Assert.Equal<string>(["Open Recent", "—", "Clear Menu"], Rows(Child(Menu("File"), "Open Recent")));
        Assert.Equal<string>(["Examples", "—", "Example Book…"], Rows(Child(Menu("File"), "Examples")));
        Assert.Equal<string>(["Source…", "Rendered PDF…"], Rows(Child(Menu("File"), "Share")));
        Assert.Equal<string>(
        [
            "PDF…", "HTML…", "EPUB…", "LaTeX…", "TextBundle…",
            "—", "Diagram as SVG",
            "—", "PDF Page Size",
        ], Rows(Child(Menu("File"), "Export")));
    }

    [Fact]
    public void EditReadsAsSection24Does() =>
        Assert.Equal<string>(
        [
            "Undo", "Redo",
            "—", "Cut", "Copy", "Paste", "Delete", "Select All",
            "—", "Find…", "Find Next", "Find Previous", "Use Selection for Find",
        ], Rows(Menu("Edit")));

    [Fact]
    public void ViewReadsAsSection25Does() =>
        Assert.Equal<string>(["Edit", "Split", "Preview", "Zen Mode", "—", "Show Sidebar", "Enter Full Screen"], Rows(Menu("View")));

    [Fact]
    public void BookReadsAsSection26Does()
    {
        Assert.Equal<string>(
        [
            "New Book…", "Open Book…", "Show Book", "Close Book",
            "—", "Share Book as PDF", "Print Book…", "Export Book",
        ], Rows(Menu("Book")));
        Assert.Equal<string>(["PDF…", "EPUB…", "LaTeX…"], Rows(Child(Menu("Book"), "Export Book")));
    }

    [Fact]
    public void GoReadsAsSection27Does()
    {
        Assert.Equal<string>(["Previous Article", "Next Article", "—", "Contents", "Notes"], Rows(Menu("Go")));
        Assert.Equal(CommandId.Contents, Child(Menu("Go"), "Contents").Children.Single().Command!.Id);
        Assert.Equal(CommandId.Notes, Child(Menu("Go"), "Notes").Children.Single().Command!.Id);
    }

    [Fact]
    public void WindowAndHelpReadAsSection28Does()
    {
        Assert.Equal<string>(["Minimize", "Zoom", "—", "Window List"], Rows(Menu("Window")));
        Assert.Equal<string>(["md Help", "Privacy Policy", "—", "About md"], Rows(Menu("Help")));
    }

    [Fact]
    public void EscapeIsInNoMenu() =>
        Assert.DoesNotContain(All(CommandTable.Menus), n => n.Command?.Id == CommandId.Escape);

    [Fact]
    public void EveryRowOfTheTableReachesTheBarExactlyOnce()
    {
        var placed = All(CommandTable.Menus).Where(n => n.Command is not null).Select(n => n.Command!.Id).ToList();
        Assert.Equal(placed.Count, placed.Distinct().Count());
        Assert.Equal(CommandTable.All.Count - 1, placed.Count);   // every row but Escape
    }

    static IEnumerable<MenuNode> All(IEnumerable<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in All(node.Children)) yield return child;
        }
    }

    [Fact]
    public void MenuPathKnowsItsDepthLeafAndAncestry()
    {
        var nested = new MenuPath("File", "Export", "PDF Page Size");
        Assert.Equal(3, nested.Depth);
        Assert.Equal("PDF Page Size", nested.Leaf);
        Assert.True(nested.IsUnder(new MenuPath("File")));
        Assert.True(nested.IsUnder(new MenuPath("File", "Export")));
        Assert.False(nested.IsUnder(new MenuPath("File", "Share")));
        Assert.False(nested.IsUnder(nested));
        Assert.Equal(new MenuPath("File", "Export"), nested.Truncate(2));
        Assert.Equal("File ▸ Export ▸ PDF Page Size", nested.ToString());
    }

    // ── The row titles the builder cannot invent ──────────────────────────────────────────────

    [Theory]
    [InlineData(1, "Title")]
    [InlineData(2, "  Title")]
    [InlineData(3, "    Title")]
    [InlineData(0, "Title")]
    [InlineData(-1, "Title")]
    public void ContentsRowsIndentTwoSpacesPerHeadingLevelBelowOne(int level, string expected) =>
        Assert.Equal(expected, CommandTable.ContentsRowTitle(new OutlineEntry(level, "Title", "title", 0)));

    [Fact]
    public void ThePageSizeRadiosShareOneGroup() => Assert.Equal("PdfPageSize", CommandTable.PdfPageSizeGroupName);

    [Fact]
    public void OnlyTheDesignsWindowsOnlyRowsAreMarkedSo() =>
        Assert.Equal(
            new[]
            {
                CommandId.OpenTextBundleFolder, CommandId.Exit, CommandId.Find, CommandId.FindNext,
                CommandId.FindPrevious, CommandId.UseSelectionForFind, CommandId.Minimize,
                CommandId.Zoom, CommandId.ActivateWindow, CommandId.Escape,
            }.OrderBy(i => i),
            CommandTable.All.Where(s => s.WindowsOnly).Select(s => s.Id).OrderBy(i => i));
}
