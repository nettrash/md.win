// The MenuBar of shell-final.md §2.9: controls built from Md.App.Logic's CommandTable, and nothing
// else. Every decision — which row, which title, which chord, which divider, enabled or not, ticked
// or not — is answered by the table and by CommandEnablement, and is tested off Windows; this file
// only creates the WinUI objects and keeps the dynamic rows in step with the snapshot.
//
// The dynamic submenus (Open Recent, Examples, Contents, Notes, Diagram as SVG, PDF Page Size, the
// Window list) are rebuilt on a snapshot change rather than when the menu opens: MenuFlyoutSubItem
// declares only Icon, Items and Text, and MenuBarItem only Items and Title — neither exposes a
// flyout or an Opening event (§2.9). The rebuild is ≤ 60 items and only runs when the snapshot
// actually differs, which ShellSnapshot's structural equality decides.
using Md.App.Logic.Commands;
using Md.Core.Document;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Md.App.Menus;

/// <summary>
/// What the menu rows need that a <see cref="ShellSnapshot"/> does not carry: the bundled examples
/// (a process constant, read once from <c>&lt;install&gt;\Examples</c>) and the note-preview function
/// (<c>Md.App.Logic.Text.NotePreview.Of</c>, §5.6) that titles a Go ▸ Notes row.
/// </summary>
internal sealed record MenuBarSources(IReadOnlyList<Example> Examples, Func<string, string> NotePreview);

internal sealed class MenuBarBuilder(CommandDispatcher dispatcher, MenuBarSources sources)
{
    // A run of rows one dynamic command owns inside a host's item list. Start moves when a region
    // earlier in the same host grows or shrinks; Following is the divider that must disappear with
    // an empty run (Open Recent with no entries would otherwise open with a leading line).
    sealed class Region(CommandId id, IList<MenuFlyoutItemBase> host, int start)
    {
        public CommandId Id => id;
        public IList<MenuFlyoutItemBase> Host => host;
        public int Start { get; set; } = start;
        public int Count { get; set; }
        public MenuFlyoutSeparator? Following { get; set; }
    }

    readonly Dictionary<CommandId, MenuFlyoutItem> _items = [];
    readonly Dictionary<CommandId, ToggleMenuFlyoutItem> _toggles = [];
    readonly List<(MenuPath Path, MenuFlyoutSubItem Item)> _submenus = [];
    readonly List<Region> _regions = [];
    ShellSnapshot? _last;

    /// <summary>The seven menus, in bar order, with every static row in place and the dynamic runs still empty.</summary>
    public MenuBar Build()
    {
        var bar = new MenuBar();
        foreach (var menu in CommandTable.Menus)
        {
            var root = new MenuBarItem { Title = menu.Title };
            Fill(root.Items, menu.Children);
            bar.Items.Add(root);
        }

        // A region is created before the rows after it exist, so its divider is found afterwards.
        foreach (var region in _regions)
        {
            if (region.Start < region.Host.Count && region.Host[region.Start] is MenuFlyoutSeparator separator)
                region.Following = separator;
        }

        return bar;
    }

    /// <summary>
    /// Applies a snapshot: enabled, ticked, the two titles that change, and a rebuild of every
    /// dynamic run whose source moved. Cheap to call on every publish — an unchanged snapshot returns
    /// at the first line.
    /// </summary>
    public void Refresh(ShellSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_last is not null && _last.Equals(snapshot)) return;
        var previous = _last;
        _last = snapshot;

        foreach (var (id, item) in _items)
        {
            item.IsEnabled = CommandEnablement.IsEnabled(id, snapshot);
            item.Text = CommandTable.DisplayTitle(id, snapshot);
        }

        foreach (var (id, toggle) in _toggles)
        {
            toggle.IsEnabled = CommandEnablement.IsEnabled(id, snapshot);
            toggle.IsChecked = CommandEnablement.IsChecked(id, snapshot);
        }

        foreach (var (path, item) in _submenus) item.IsEnabled = CommandEnablement.IsSubmenuEnabled(path, snapshot);

        foreach (var region in _regions)
        {
            if (NeedsRebuild(region.Id, previous, snapshot)) Rebuild(region, snapshot);
            else RetickRows(region, snapshot);
        }
    }

    // ── Building ──────────────────────────────────────────────────────────────────────────────

    void Fill(IList<MenuFlyoutItemBase> host, IReadOnlyList<MenuNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.SeparatorBefore) host.Add(new MenuFlyoutSeparator());

            if (node.Command is null)
            {
                var submenu = new MenuFlyoutSubItem { Text = node.Title };
                host.Add(submenu);
                _submenus.Add((node.Path, submenu));
                Fill(submenu.Items, node.Children);
                continue;
            }

            var spec = node.Command;
            switch (spec.Kind)
            {
                case CommandKind.Item:
                {
                    var item = new MenuFlyoutItem { Text = spec.Title };
                    Wire(item, spec);
                    _items[spec.Id] = item;
                    host.Add(item);
                    break;
                }

                case CommandKind.Toggle:
                {
                    // The tick is set from the snapshot, never by the click: the Mac's setter ignores
                    // the Bool and calls select(mode) (§2.5).
                    var toggle = new ToggleMenuFlyoutItem { Text = spec.Title };
                    Wire(toggle, spec);
                    _toggles[spec.Id] = toggle;
                    host.Add(toggle);
                    break;
                }

                case CommandKind.DynamicItems:
                case CommandKind.DynamicToggles:
                case CommandKind.DynamicRadios:
                    _regions.Add(new Region(spec.Id, host, host.Count));
                    break;

                case CommandKind.Accelerator:
                    break;   // no row: AcceleratorInstaller owns it

                default:
                    throw new InvalidOperationException($"Unhandled command kind {spec.Kind}");
            }
        }
    }

    void Wire(MenuFlyoutItem item, CommandSpec spec)
    {
        var id = spec.Id;
        if (spec.Chord is { } chord) item.KeyboardAcceleratorTextOverride = chord.DisplayText;
        item.Click += (_, _) => dispatcher.Execute(id);
    }

    // ── The dynamic runs ──────────────────────────────────────────────────────────────────────

    static bool NeedsRebuild(CommandId id, ShellSnapshot? previous, ShellSnapshot snapshot)
    {
        if (previous is null) return true;
        return id switch
        {
            CommandId.OpenRecentEntry => !previous.RecentEntries.SequenceEqual(snapshot.RecentEntries),
            CommandId.Contents => !previous.Outline.SequenceEqual(snapshot.Outline),
            CommandId.Notes => !previous.Notes.SequenceEqual(snapshot.Notes),
            CommandId.ExportDiagramSvg => !previous.Diagrams.SequenceEqual(snapshot.Diagrams),
            CommandId.ActivateWindow => !previous.WindowTitles.SequenceEqual(snapshot.WindowTitles),
            // The seven page sizes never change; only which one is ticked does.
            CommandId.Example or CommandId.PdfPageSize => false,
            _ => true,
        };
    }

    void Rebuild(Region region, ShellSnapshot snapshot)
    {
        for (var i = 0; i < region.Count; i++) region.Host.RemoveAt(region.Start);

        var rows = Rows(region.Id, snapshot);
        var index = region.Start;
        foreach (var row in rows) region.Host.Insert(index++, row);

        var delta = rows.Count - region.Count;
        region.Count = rows.Count;
        if (region.Following is not null) region.Following.Visibility = rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // No host holds two runs today; keeping the arithmetic honest costs nothing and stops the
        // second one from silently landing in the wrong place if one is ever added.
        if (delta != 0)
        {
            foreach (var other in _regions)
            {
                if (!ReferenceEquals(other, region) && ReferenceEquals(other.Host, region.Host) && other.Start > region.Start)
                    other.Start += delta;
            }
        }
    }

    void RetickRows(Region region, ShellSnapshot snapshot)
    {
        if (region.Id is not (CommandId.PdfPageSize or CommandId.ActivateWindow)) return;
        for (var i = 0; i < region.Count; i++)
        {
            var ticked = CommandEnablement.IsRowChecked(region.Id, RowArgument(region.Id, i, snapshot), snapshot);
            switch (region.Host[region.Start + i])
            {
                // RadioMenuFlyoutItem derives from MenuFlyoutItem, NOT from ToggleMenuFlyoutItem, so a
                // single `is ToggleMenuFlyoutItem` test would skip every page-size radio and leave the
                // tick wherever the last click in *this* window's group put it. §2.2 wants the tick to
                // read md.pdfPageSize, which another window may have changed. Exactly one row is ticked,
                // and checking one clears its group, so the order of this loop does not matter.
                case RadioMenuFlyoutItem radio:
                    radio.IsChecked = ticked;
                    break;
                case ToggleMenuFlyoutItem toggle:
                    toggle.IsChecked = ticked;
                    break;
                default:
                    break;
            }
        }
    }

    static object? RowArgument(CommandId id, int index, ShellSnapshot snapshot) => id switch
    {
        CommandId.PdfPageSize => PageSize.All[index].Id,
        CommandId.ActivateWindow => snapshot.WindowTitles[index].Id,
        _ => null,
    };

    IReadOnlyList<MenuFlyoutItemBase> Rows(CommandId id, ShellSnapshot snapshot)
    {
        var rows = new List<MenuFlyoutItemBase>();
        switch (id)
        {
            case CommandId.OpenRecentEntry:
                // File name in the row, folder in the tooltip (§2.2) — two documents can share a name.
                foreach (var entry in snapshot.RecentEntries)
                {
                    var item = Row(id, entry.Name, entry.Token);
                    ToolTipService.SetToolTip(item, entry.Folder);
                    rows.Add(item);
                }
                break;

            case CommandId.Example:
                foreach (var example in sources.Examples) rows.Add(Row(id, example.Name, example.FileName));
                break;

            case CommandId.Contents:
                foreach (var entry in snapshot.Outline) rows.Add(Row(id, CommandTable.ContentsRowTitle(entry), entry));
                break;

            case CommandId.Notes:
                foreach (var note in snapshot.Notes) rows.Add(Row(id, sources.NotePreview(note.Text), note));
                break;

            case CommandId.ExportDiagramSvg:
                // The ordinal is the identity; the pipeline re-resolves the diagram from the source.
                foreach (var diagram in snapshot.Diagrams) rows.Add(Row(id, diagram.MenuTitle, diagram.Ordinal));
                break;

            case CommandId.PdfPageSize:
                foreach (var size in PageSize.All)
                {
                    var radio = new RadioMenuFlyoutItem
                    {
                        Text = size.Label,
                        GroupName = CommandTable.PdfPageSizeGroupName,
                        IsChecked = CommandEnablement.IsRowChecked(id, size.Id, snapshot),
                    };
                    var argument = size.Id;
                    radio.Click += (_, _) => dispatcher.Execute(id, argument);
                    rows.Add(radio);
                }
                break;

            case CommandId.ActivateWindow:
                foreach (var (windowId, title, isThis) in snapshot.WindowTitles)
                {
                    var toggle = new ToggleMenuFlyoutItem { Text = title, IsChecked = isThis };
                    toggle.Click += (_, _) => dispatcher.Execute(id, windowId);
                    rows.Add(toggle);
                }
                break;

            default:
                throw new InvalidOperationException($"{id} has no dynamic rows");
        }
        return rows;
    }

    MenuFlyoutItem Row(CommandId id, string text, object argument)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => dispatcher.Execute(id, argument);
        return item;
    }
}
