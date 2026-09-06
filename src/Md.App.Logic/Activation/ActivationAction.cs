namespace Md.App.Logic.Activation;

/// <summary>
/// One thing the window manager must do for an activation (shell-design.md §1.2). The router
/// produces an ordered list of these and nothing else — no window is created, no file is read and
/// no registry is consulted while the list is being built, which is what makes every row of the
/// §1.2 table a test.
///
/// Closed: these six are every outcome an activation can have.
/// </summary>
public abstract record ActivationAction
{
    private protected ActivationAction() { }

    /// <summary>Open this file as a document; the window manager turns it into <see cref="Focus"/> when a window already owns it.</summary>
    public sealed record OpenPath(string Path) : ActivationAction;

    /// <summary>Import a <c>.textpack</c> archive as an untitled, read-only document titled after the bundle (§6.5).</summary>
    public sealed record ImportTextPack(string Path) : ActivationAction;

    /// <summary>Import a <c>.textbundle</c> <em>folder</em> the same way (§6.5) — a folder cannot be a file association, so it only ever arrives by drop or by File ▸ Open TextBundle Folder….</summary>
    public sealed record ImportTextBundleFolder(string Path) : ActivationAction;

    /// <summary>The file is already open: activate that window instead of opening a second one (NSDocumentController's rule, §1.2).</summary>
    public sealed record Focus(Guid WindowId) : ActivationAction;

    /// <summary>One new untitled document window — File ▸ New, and what a redirected plain launch means (the Mac's Dock-icon ⌘N).</summary>
    public sealed record OpenUntitled : ActivationAction;

    /// <summary>Reopen the saved documents <c>session.json</c> lists, with their modes, Zen state and placement (§1.6).</summary>
    public sealed record RestoreSession : ActivationAction;
}
