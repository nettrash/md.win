using Md.App.Logic.Windows;

namespace Md.App.Logic.Tests;

/// <summary>
/// Ownership and the Window menu's rows (shell-design.md §8.6, §2.8), including the one place the
/// real registry deliberately differs from <see cref="FakeDocumentRegistry"/>: a session detaching
/// from its file is not a window closing.
/// </summary>
public sealed class WindowRegistryTests
{
    static readonly Guid A = Guid.NewGuid();
    static readonly Guid B = Guid.NewGuid();

    [Fact]
    public void AWindowJoinsTheMenuBeforeItHasAnyFile()
    {
        var registry = new WindowRegistry();
        registry.Add(A, "Untitled");

        Assert.Equal([(A, "Untitled")], registry.Windows);
        Assert.Null(registry.PathOf(A));
    }

    [Fact]
    public void RowsAreInCreationOrder()
    {
        var registry = new WindowRegistry();
        registry.Add(A, "Untitled");
        registry.Add(B, "Untitled 2");
        registry.SetTitle(A, "a");

        Assert.Equal([(A, "a"), (B, "Untitled 2")], registry.Windows);
    }

    [Fact]
    public void OwnershipIsCanonicalPathToWindowAndIsCaseInsensitive()
    {
        var registry = new WindowRegistry();
        registry.Add(A, "a");
        registry.Register(A, @"C:\Docs\a.md");

        Assert.Equal(A, registry.Owning(@"c:\docs\A.MD"));
        Assert.Null(registry.Owning(@"C:\Docs\b.md"));
        Assert.Equal(@"C:\Docs\a.md", registry.PathOf(A));
    }

    [Fact]
    public void AWindowOwnsAtMostOnePathSoASaveAsMovesTheOwnership()
    {
        var registry = new WindowRegistry();
        registry.Add(A, "a");
        registry.Register(A, @"C:\Docs\a.md");
        registry.Register(A, @"C:\Docs\b.md");

        Assert.Null(registry.Owning(@"C:\Docs\a.md"));
        Assert.Equal(A, registry.Owning(@"C:\Docs\b.md"));
    }

    [Fact]
    public void UnregisteringDropsTheFileButKeepsTheWindowInTheMenu()
    {
        // TextFileSession.Detach unregisters; the window is still open and must still be listed.
        var registry = new WindowRegistry();
        registry.Add(A, "a");
        registry.Register(A, @"C:\Docs\a.md");
        registry.Unregister(A);

        Assert.Null(registry.Owning(@"C:\Docs\a.md"));
        Assert.Equal([(A, "a")], registry.Windows);
    }

    [Fact]
    public void RemovingTakesTheRowAndTheOwnershipTogether()
    {
        var registry = new WindowRegistry();
        registry.Add(A, "a");
        registry.Register(A, @"C:\Docs\a.md");
        registry.Remove(A);

        Assert.Null(registry.Owning(@"C:\Docs\a.md"));
        Assert.Empty(registry.Windows);
    }

    [Fact]
    public void FindByPathCanonicalisesFirst()
    {
        var registry = new WindowRegistry();
        var identity = new FakeFileIdentity();
        identity.Alias(@"C:\Docs\LINK.md", @"C:\Docs\a.md");
        registry.Add(A, "a");
        registry.Register(A, identity.Canonical(@"C:\Docs\a.md"));

        Assert.Equal(A, registry.FindByPath(@"C:\Docs\LINK.md", identity));
        Assert.Null(registry.FindByPath(@"C:\Docs\other.md", identity));
    }

    [Fact]
    public void ChangedFiresOnEveryRealMutationAndNeverForAWriteOfWhatIsAlreadyThere()
    {
        var registry = new WindowRegistry();
        var changes = 0;
        registry.Changed += () => changes++;

        registry.Add(A, "a");                       // 1
        registry.Add(A, "a");                       // no change
        registry.SetTitle(A, "a");                  // no change
        registry.SetTitle(A, "a \u2014 Edited");    // 2
        registry.Register(A, @"C:\Docs\a.md");      // 3
        registry.Register(A, @"c:\docs\a.md");      // same file, same window: no change
        registry.Unregister(A);                     // 4
        registry.Unregister(A);                     // nothing left to drop
        registry.Remove(A);                         // 5
        registry.Remove(A);                         // gone already

        Assert.Equal(5, changes);
    }

    [Fact]
    public void ACaseOnlyRenameKeepsOwnershipButNotTheNewSpelling()
    {
        // The trap, and exactly how far it goes. Ownership — the half the book pane and the
        // activation router use — is right for every spelling. PathOf is NOT: .NET keeps the key a
        // case-insensitive dictionary already holds, so it still answers with the pre-rename name.
        // Nothing in Md.App reads PathOf; this pins the limitation so the next caller sees it here
        // rather than in a book that refuses to save an article it owns.
        var registry = new WindowRegistry();
        registry.Add(A, "Notes");
        registry.Register(A, @"C:\Docs\Notes.md");
        registry.Register(A, @"C:\Docs\NOTES.MD");            // Rename… changed only the casing

        Assert.Equal(A, registry.Owning(@"C:\Docs\NOTES.MD"));
        Assert.Equal(A, registry.Owning(@"C:\Docs\Notes.md"));
        Assert.Single(registry.Windows);
        Assert.Equal(@"C:\Docs\Notes.md", registry.PathOf(A));   // the limitation, not the ideal
    }

    [Fact]
    public void ATitleForAWindowThatIsNotOpenIsIgnored()
    {
        var registry = new WindowRegistry();
        registry.SetTitle(A, "ghost");
        Assert.Empty(registry.Windows);
    }
}
