using Md.App.Logic.Documents;
using Md.App.Logic.Seams;
using Md.Core.Export;

namespace Md.App.Logic.Activation;

/// <summary>
/// shell-design.md §1.2's table as code: an <see cref="ActivationDescription"/> in, an ordered list
/// of <see cref="ActivationAction"/>s out. Pure — <see cref="Route"/> reads no disk, creates no
/// window and asks no registry, so every row of the table is a unit test.
///
/// The "a path already open activates that window instead of opening a second one" rule is the
/// second half, <see cref="Resolve"/>: it needs the live registry, so it is a separate call the
/// window manager makes with the batch <see cref="Route"/> produced.
/// </summary>
public static class ActivationRouter
{
    /// <summary>The <c>.textbundle</c> folder suffix (Core's constant), matched ordinal-ignore-case.</summary>
    static string BundleSuffix => TextBundle.BundleExtension;

    /// <summary>The <c>.textpack</c> archive suffix (Core's constant).</summary>
    static string PackSuffix => TextBundle.PackExtension;

    /// <summary>
    /// The §1.2 table. File activations classify each item; every other kind — Launch, Protocol,
    /// StartupTask, whatever the platform adds — routes as a launch, which is the table's last row.
    /// </summary>
    public static IReadOnlyList<ActivationAction> Route(ActivationDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);

        if (description.Kind == ActivationKind.File)
        {
            var actions = new List<ActivationAction>(description.Items.Count);
            foreach (var item in description.Items)
            {
                if (Classify(item) is { } action) actions.Add(action);
            }
            // Every item was a folder we do not open (§1.2: "other folders ignored"). A redirect that
            // asked for nothing does nothing; the first activation still has to produce a window, or
            // the process would pump with none.
            if (actions.Count > 0) return actions;
            return description.IsFirst ? Launch(description) : [];
        }

        return Launch(description);
    }

    /// <summary>
    /// One item of a file activation, or one command-line path: a <c>.textbundle</c> folder
    /// imports, any other folder is ignored, a <c>.textpack</c> imports, anything else opens as a
    /// document (§1.2, §6.5).
    ///
    /// <see cref="ActivationItem.IsFolder"/> is the shell's own answer and it decides first, exactly
    /// as §1.2's File row reads it ("a <c>StorageFolder</c> whose name ends in <c>.textbundle</c>"):
    /// a bundle is a folder, so a plain FILE that happens to be called <c>notes.textbundle</c> is a
    /// document and opens as one — feeding it to the folder importer could only ever fail.
    /// </summary>
    public static ActivationAction? Classify(ActivationItem item)
    {
        var path = item.Path;
        if (string.IsNullOrWhiteSpace(path)) return null;

        // The name, not the whole path: a document inside C:\Books\My.textbundle\ is still a document.
        var name = FileNames.NameOf(path);
        if (item.IsFolder)
        {
            return name.EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase)
                ? new ActivationAction.ImportTextBundleFolder(path)
                : null;                                                        // §1.2: other folders ignored
        }
        if (name.EndsWith(PackSuffix, StringComparison.OrdinalIgnoreCase)) return new ActivationAction.ImportTextPack(path);
        return new ActivationAction.OpenPath(path);
    }

    /// <summary>
    /// The same question where nobody has told us what the path is — a command line, which is a
    /// string and not a <c>StorageItem</c>. The name is then the only evidence there is, so a
    /// <c>.textbundle</c> is taken at its word.
    /// </summary>
    static ActivationItem FromCommandLine(string path) =>
        FileNames.NameOf(path).EndsWith(BundleSuffix, StringComparison.OrdinalIgnoreCase)
            ? ActivationItem.Folder(path)
            : ActivationItem.File(path);

    /// <summary>
    /// Turn "open this file" into "activate the window that already has it" where one does, and drop
    /// a file named twice in one activation (a multi-select can repeat a path through two spellings;
    /// two windows on one file would give it two writers). Imports are untitled documents and are
    /// never owned, so the same <c>.textpack</c> twice really does give two windows — the Mac's
    /// behaviour for the same Example opened twice.
    /// </summary>
    public static IReadOnlyList<ActivationAction> Resolve(
        IEnumerable<ActivationAction> actions,
        IDocumentRegistry registry,
        IFileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(identity);

        var resolved = new List<ActivationAction>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenWindows = new HashSet<Guid>();

        foreach (var action in actions)
        {
            if (action is not ActivationAction.OpenPath open)
            {
                resolved.Add(action);
                continue;
            }

            var canonical = identity.Canonical(open.Path);
            if (registry.Owning(canonical) is { } owner)
            {
                if (seenWindows.Add(owner)) resolved.Add(new ActivationAction.Focus(owner));
                continue;
            }
            if (seenPaths.Add(canonical)) resolved.Add(open);
        }

        return resolved;
    }

    static IReadOnlyList<ActivationAction> Launch(ActivationDescription description)
    {
        var paths = description.ArgumentPaths;
        if (paths.Count > 0)
        {
            var actions = new List<ActivationAction>(paths.Count);
            foreach (var path in paths)
            {
                // Classified, not blindly opened: a .textpack on a command line is the same import a
                // double-click gives (§6.5). A folder cannot be told from a file without a disk, so a
                // command-line path is a file unless its name says .textbundle.
                if (Classify(FromCommandLine(path)) is { } action) actions.Add(action);
            }
            if (actions.Count > 0) return actions;
        }

        // A redirect with nothing to open is "the user asked for md again": a new untitled window,
        // which is what clicking the Dock icon gives on the Mac (⌘N). Only the first activation of
        // the process may bring the last session back.
        if (!description.IsFirst) return [new ActivationAction.OpenUntitled()];
        return description.HasRestorableSession
            ? [new ActivationAction.RestoreSession()]
            : [new ActivationAction.OpenUntitled()];
    }
}
