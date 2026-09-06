// The Book window's sidebar, built in code (shell-design.md §8.3): no ItemTemplate, no
// DataTemplate, nothing tools/xamlcheck cannot see. What the rows ARE is BookSidebarModel's answer;
// this file turns each row into a ListViewItem and hangs the management menu on it.
//
// The Mac's lesson, kept: no per-row tap handler. A gesture recogniser on a row raced the table's
// own mouseDown there and swallowed clicks, so selection is the ListView's job alone and the only
// row that carries a handler is the "New Article…" button, which is not selectable at all.
using Md.App.Controls;
using Md.App.Logic;
using Md.App.Logic.Books;
using Md.App.Logic.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Md.App.Book;

/// <summary>
/// What a sidebar row can ask the window to do. Every one of them ends in
/// <c>BookNavigatorModel</c>; the builder never touches a file.
/// </summary>
/// <param name="OpenInWindow">Open in New Window (also the double-click).</param>
/// <param name="Rename">Rename… — the window prompts, then calls the navigator.</param>
/// <param name="Move">Move Up / Move Down; the int is -1 or +1.</param>
/// <param name="Delete">Delete… — the window confirms first.</param>
/// <param name="NewArticle">The "New Article…" row; the argument is the folder it creates in.</param>
internal sealed record BookSidebarActions(
    Action<string> OpenInWindow,
    Action<string> Rename,
    Action<string, int> Move,
    Action<string> Delete,
    Action<string> NewArticle);

internal sealed class BookSidebarBuilder(BookSidebarActions actions)
{
    // Segoe Fluent Icons for the Mac's SF Symbols (§10): doc.text and plus.
    const string ArticleGlyph = "\uE8A5";
    const string NewArticleGlyph = "\uE710";

    /// <summary>
    /// Every row of <paramref name="book"/> as a <see cref="ListViewItem"/>, in display order. The
    /// <c>Tag</c> of each is its <see cref="BookRow"/>, which is how the window's SelectionChanged
    /// tells an article from a header without looking at the visuals.
    /// </summary>
    public IReadOnlyList<ListViewItem> Build(Md.Core.Book.Book? book, ElementTheme theme)
    {
        var palette = Palette.For(theme == ElementTheme.Dark);
        var ink = PaneBrushes.Get("PaperInkBrush", palette.Ink);
        var secondary = PaneBrushes.Get("PaperInkSecondaryBrush", palette.InkSecondary);
        var items = new List<ListViewItem>();
        foreach (var row in BookSidebarModel.Rows(book))
        {
            items.Add(row.Kind switch
            {
                BookRowKind.ChapterHeader => Header(book, row, ink),
                BookRowKind.NewArticle => NewArticle(row, secondary),
                _ => Article(book, row, ink),
            });
        }
        return items;
    }

    ListViewItem Article(Md.Core.Book.Book? book, BookRow row, Brush ink)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(new FontIcon { Glyph = ArticleGlyph, FontSize = 14, Foreground = ink });
        panel.Children.Add(new TextBlock
        {
            Text = row.Title,
            FontFamily = new FontFamily(PaneTypography.Family),
            FontSize = PaneTypography.SmallEpx,
            Foreground = ink,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return new ListViewItem
        {
            Content = panel,
            Tag = row,
            ContextFlyout = ArticleMenu(book, row.Path!),
        };
    }

    ListViewItem Header(Md.Core.Book.Book? book, BookRow row, Brush ink) =>
        new()
        {
            Content = new TextBlock
            {
                Text = row.Title,
                FontFamily = new FontFamily(PaneTypography.Family),
                FontSize = PaneTypography.SmallEpx,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = ink,
                TextTrimming = TextTrimming.CharacterEllipsis,
            },
            Tag = row,
            // A chapter header is a label, not a destination: the window's SelectionChanged puts the
            // highlight straight back where it was.
            IsTabStop = false,
            ContextFlyout = ManagementMenu(book, row.Path!),
        };

    ListViewItem NewArticle(BookRow row, Brush secondary)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new FontIcon { Glyph = NewArticleGlyph, FontSize = 12, Foreground = secondary });
        content.Children.Add(new TextBlock
        {
            Text = row.Title,
            FontFamily = new FontFamily(PaneTypography.Family),
            FontSize = NewArticleEpx,
            Foreground = secondary,
            VerticalAlignment = VerticalAlignment.Center,
        });

        var folder = row.Folder;
        var button = new Button
        {
            Content = content,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) => actions.NewArticle(folder);

        return new ListViewItem { Content = button, Tag = row, IsTabStop = false };
    }

    /// <summary>An article's menu: the window opener first, then the three management rows (§8.3).</summary>
    MenuFlyout ArticleMenu(Md.Core.Book.Book? book, string path)
    {
        var flyout = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = Strings.Books.OpenInNewWindow };
        open.Click += (_, _) => actions.OpenInWindow(path);
        flyout.Items.Add(open);
        flyout.Items.Add(new MenuFlyoutSeparator());
        AddManagementRows(flyout, book, path);
        return flyout;
    }

    /// <summary>A chapter's menu: the three management rows only — a folder cannot be opened as a document.</summary>
    MenuFlyout ManagementMenu(Md.Core.Book.Book? book, string path)
    {
        var flyout = new MenuFlyout();
        AddManagementRows(flyout, book, path);
        return flyout;
    }

    void AddManagementRows(MenuFlyout flyout, Md.Core.Book.Book? book, string path)
    {
        var commands = BookSidebarModel.CommandsFor(book, path);

        var rename = new MenuFlyoutItem { Text = Strings.Books.RenameItem };
        rename.Click += (_, _) => actions.Rename(path);
        flyout.Items.Add(rename);

        var up = new MenuFlyoutItem { Text = Strings.Books.MoveUp, IsEnabled = commands?.CanMoveUp ?? false };
        up.Click += (_, _) => actions.Move(path, -1);
        flyout.Items.Add(up);

        var down = new MenuFlyoutItem { Text = Strings.Books.MoveDown, IsEnabled = commands?.CanMoveDown ?? false };
        down.Click += (_, _) => actions.Move(path, +1);
        flyout.Items.Add(down);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var delete = new MenuFlyoutItem { Text = Strings.Books.DeleteItem };
        delete.Click += (_, _) => actions.Delete(path);
        flyout.Items.Add(delete);
    }

    /// <summary>12 pt in the Mac's sizes — the one row that is smaller than the article names above it (§10).</summary>
    const double NewArticleEpx = 16;
}
