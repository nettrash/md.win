using Md.App.Logic.Activation;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-design.md §1.2's routing table, row by row, plus the ownership resolution §1.2's prose
/// adds ("a path already open activates that window instead of opening a second one").
/// </summary>
public sealed class ActivationRouterTests
{
    static IReadOnlyList<ActivationAction> Route(ActivationDescription description) => ActivationRouter.Route(description);

    // ── the table: File ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void AFileActivationOpensOneDocumentPerFileInTheOrderTheShellGaveThem()
    {
        var actions = Route(ActivationDescription.ForFiles(
            [ActivationItem.File(@"C:\Docs\a.md"), ActivationItem.File(@"C:\Docs\b.markdown")], isFirst: true));

        Assert.Collection(actions,
            a => Assert.Equal(@"C:\Docs\a.md", Assert.IsType<ActivationAction.OpenPath>(a).Path),
            a => Assert.Equal(@"C:\Docs\b.markdown", Assert.IsType<ActivationAction.OpenPath>(a).Path));
    }

    [Fact]
    public void ATextPackFileImportsRatherThanOpens()
    {
        var actions = Route(ActivationDescription.ForFiles([ActivationItem.File(@"C:\Docs\notes.textpack")], isFirst: true));
        Assert.Equal(@"C:\Docs\notes.textpack", Assert.IsType<ActivationAction.ImportTextPack>(Assert.Single(actions)).Path);
    }

    [Fact]
    public void ATextBundleFolderImportsAndEveryOtherFolderIsIgnored()
    {
        var actions = Route(ActivationDescription.ForFiles(
            [ActivationItem.Folder(@"C:\Docs\Notes.textbundle"), ActivationItem.Folder(@"C:\Docs\Pictures"), ActivationItem.File(@"C:\Docs\a.md")],
            isFirst: true));

        Assert.Collection(actions,
            a => Assert.Equal(@"C:\Docs\Notes.textbundle", Assert.IsType<ActivationAction.ImportTextBundleFolder>(a).Path),
            a => Assert.Equal(@"C:\Docs\a.md", Assert.IsType<ActivationAction.OpenPath>(a).Path));
    }

    [Fact]
    public void TheBundleSuffixIsMatchedCaseInsensitivelyAndOnlyOnTheName()
    {
        // A document that merely lives inside a bundle folder is still a document.
        var actions = Route(ActivationDescription.ForFiles(
            [ActivationItem.Folder(@"C:\Docs\NOTES.TEXTBUNDLE"), ActivationItem.File(@"C:\Docs\Notes.textbundle\text.md")],
            isFirst: true));

        Assert.IsType<ActivationAction.ImportTextBundleFolder>(actions[0]);
        Assert.IsType<ActivationAction.OpenPath>(actions[1]);
    }

    [Fact]
    public void AFileActivationOfNothingButIgnoredFoldersStillGivesTheFirstLaunchAWindow()
    {
        var first = Route(ActivationDescription.ForFiles([ActivationItem.Folder(@"C:\Docs\Pictures")], isFirst: true));
        Assert.IsType<ActivationAction.OpenUntitled>(Assert.Single(first));

        // A redirect that asked for nothing does nothing: the running instance already has windows.
        var redirected = Route(ActivationDescription.ForFiles([ActivationItem.Folder(@"C:\Docs\Pictures")], isFirst: false));
        Assert.Empty(redirected);
    }

    // ── the table: Launch with arguments ──────────────────────────────────────────────────────

    [Fact]
    public void LaunchWithArgumentsOpensOneDocumentPerArgument()
    {
        var actions = Route(ActivationDescription.ForLaunch(@"a.md ""C:\My Docs\b.md""", isFirst: true));

        Assert.Collection(actions,
            a => Assert.Equal("a.md", Assert.IsType<ActivationAction.OpenPath>(a).Path),
            a => Assert.Equal(@"C:\My Docs\b.md", Assert.IsType<ActivationAction.OpenPath>(a).Path));
    }

    [Fact]
    public void ATextPackOnTheCommandLineImportsLikeADoubleClick()
    {
        var actions = Route(ActivationDescription.ForLaunch(@"C:\Docs\notes.textpack", isFirst: true));
        Assert.IsType<ActivationAction.ImportTextPack>(Assert.Single(actions));
    }

    [Fact]
    public void TheExecutableIsNotADocument()
    {
        // An unpackaged run can see its own image path first (the App SDK sample splits Arguments
        // into several tokens, and the docs point .NET apps at Environment.GetCommandLineArgs).
        var actions = Route(ActivationDescription.ForLaunch(@"""C:\Program Files\md\md.exe"" a.md", isFirst: true));
        Assert.Equal("a.md", Assert.IsType<ActivationAction.OpenPath>(Assert.Single(actions)).Path);
    }

    [Fact]
    public void ACommandLineCarryingASwitchIsNotADocumentList()
    {
        // --selftest <outDir> (§11.4): the operand is an output directory, and the App takes that
        // mode over before routing. Opening it as a document would be a bug with a visible window.
        Assert.IsType<ActivationAction.OpenUntitled>(
            Assert.Single(Route(ActivationDescription.ForLaunch(@"""C:\md\md.exe"" --selftest C:\out", isFirst: true))));
        Assert.IsType<ActivationAction.RestoreSession>(
            Assert.Single(Route(ActivationDescription.ForLaunch("--selftest", isFirst: true, hasRestorableSession: true))));
    }

    // ── the table: plain Launch ───────────────────────────────────────────────────────────────

    [Fact]
    public void APlainFirstLaunchRestoresTheSessionWhenThereIsOne()
    {
        var actions = Route(ActivationDescription.ForLaunch(null, isFirst: true, hasRestorableSession: true));
        Assert.IsType<ActivationAction.RestoreSession>(Assert.Single(actions));
    }

    [Fact]
    public void APlainFirstLaunchWithNothingToRestoreOpensOneUntitledWindow()
    {
        var actions = Route(ActivationDescription.ForLaunch("", isFirst: true, hasRestorableSession: false));
        Assert.IsType<ActivationAction.OpenUntitled>(Assert.Single(actions));
    }

    [Fact]
    public void APlainRedirectedLaunchAlwaysOpensAnUntitledWindowAndNeverRestores()
    {
        // What clicking the Dock icon gives on the Mac: a new document, not the old session back.
        var actions = Route(ActivationDescription.ForLaunch(null, isFirst: false, hasRestorableSession: true));
        Assert.IsType<ActivationAction.OpenUntitled>(Assert.Single(actions));
    }

    [Theory]
    [InlineData(ActivationKind.Protocol)]
    [InlineData(ActivationKind.StartupTask)]
    [InlineData(ActivationKind.Other)]
    public void EveryOtherKindIsTreatedAsALaunch(ActivationKind kind)
    {
        Assert.IsType<ActivationAction.OpenUntitled>(Assert.Single(Route(ActivationDescription.ForOther(kind, isFirst: true))));
        Assert.IsType<ActivationAction.RestoreSession>(
            Assert.Single(Route(ActivationDescription.ForOther(kind, isFirst: true, hasRestorableSession: true))));
    }

    // ── command-line splitting ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a.md", new[] { "a.md" })]
    [InlineData("a.md \t b.md", new[] { "a.md", "b.md" })]
    [InlineData(@"C:\Docs\a.md", new[] { @"C:\Docs\a.md" })]                        // backslashes not before a quote are literal
    [InlineData(@"""C:\My Docs\a.md""", new[] { @"C:\My Docs\a.md" })]              // a quoted path keeps its spaces
    [InlineData(@"""C:\My Docs\\"" b.md", new[] { @"C:\My Docs\", "b.md" })]        // \\ before the quote = one backslash, quote closes
    [InlineData(@"""a""""b""", new[] { @"a""b" })]                                  // "" inside quotes is one literal quote
    public void ArgumentsAreSplitTheWayWindowsSplitsThem(string? commandLine, string[] expected) =>
        Assert.Equal(expected, ActivationDescription.SplitArguments(commandLine));

    [Fact]
    public void ATrailingBackslashBeforeAClosingQuoteEscapesItTheWayCommandLineToArgvDoes()
    {
        // The classic Windows trap, reproduced deliberately: `"C:\My Docs\" b.md` is ONE argument
        // `C:\My Docs" b.md`, because the lone backslash escaped the quote and the run stayed open.
        // A caller that means the folder has to write `"C:\My Docs\\"`. Splitting on spaces would
        // give the "nicer" answer and disagree with the shell that produced the string.
        Assert.Equal([@"C:\My Docs"" b.md"], ActivationDescription.SplitArguments(@"""C:\My Docs\"" b.md"));
    }

    // ── ownership resolution ──────────────────────────────────────────────────────────────────

    [Fact]
    public void APathAlreadyOpenBecomesAFocusOfThatWindow()
    {
        var registry = new FakeDocumentRegistry();
        var identity = new FakeFileIdentity();
        var owner = Guid.NewGuid();
        registry.Register(owner, identity.Canonical(@"C:\Docs\a.md"));

        var resolved = ActivationRouter.Resolve(
            [new ActivationAction.OpenPath(@"c:\docs\A.MD"), new ActivationAction.OpenPath(@"C:\Docs\b.md")],
            registry, identity);

        Assert.Collection(resolved,
            a => Assert.Equal(owner, Assert.IsType<ActivationAction.Focus>(a).WindowId),
            a => Assert.Equal(@"C:\Docs\b.md", Assert.IsType<ActivationAction.OpenPath>(a).Path));
    }

    [Fact]
    public void TwoSpellingsOfOneUnopenedFileInOneActivationGiveOneWindow()
    {
        var identity = new FakeFileIdentity();
        identity.Alias(@"C:\Docs\LINK.md", @"C:\Docs\a.md");

        var resolved = ActivationRouter.Resolve(
            [new ActivationAction.OpenPath(@"C:\Docs\a.md"), new ActivationAction.OpenPath(@"C:\Docs\LINK.md")],
            new FakeDocumentRegistry(), identity);

        Assert.Equal(@"C:\Docs\a.md", Assert.IsType<ActivationAction.OpenPath>(Assert.Single(resolved)).Path);
    }

    [Fact]
    public void OneWindowIsFocusedOnceEvenWhenTwoOfItsSpellingsArrive()
    {
        var registry = new FakeDocumentRegistry();
        var identity = new FakeFileIdentity();
        identity.Alias(@"C:\Docs\LINK.md", @"C:\Docs\a.md");
        var owner = Guid.NewGuid();
        registry.Register(owner, identity.Canonical(@"C:\Docs\a.md"));

        var resolved = ActivationRouter.Resolve(
            [new ActivationAction.OpenPath(@"C:\Docs\a.md"), new ActivationAction.OpenPath(@"C:\Docs\LINK.md")],
            registry, identity);

        Assert.Equal(owner, Assert.IsType<ActivationAction.Focus>(Assert.Single(resolved)).WindowId);
    }

    [Fact]
    public void ImportsAndTheTwoWindowActionsPassThroughUntouched()
    {
        // The same .textpack twice really does give two windows: an import is untitled, so nothing
        // owns it — the Mac's behaviour for the same Example opened twice.
        ActivationAction[] batch =
        [
            new ActivationAction.ImportTextPack(@"C:\a.textpack"),
            new ActivationAction.ImportTextPack(@"C:\a.textpack"),
            new ActivationAction.ImportTextBundleFolder(@"C:\b.textbundle"),
            new ActivationAction.OpenUntitled(),
            new ActivationAction.RestoreSession(),
        ];

        Assert.Equal(batch, ActivationRouter.Resolve(batch, new FakeDocumentRegistry(), new FakeFileIdentity()));
    }

    // ── adversarial: the traps the porting reports name ────────────────────────────────────────

    [Fact]
    public void ASecondInstanceDoubleClickingAFileThisOneAlreadyHasOpensNoWindow()
    {
        // The single-instance case end to end (§1.1, §1.2): the second process redirects, this one
        // is handed a File activation with isFirst:false, and the whole answer must be "come to the
        // front" — never a second window on the same file, and never an untitled one either.
        var registry = new FakeDocumentRegistry();
        var identity = new FakeFileIdentity();
        var owner = Guid.NewGuid();
        registry.Register(owner, identity.Canonical(@"C:\Docs\a.md"));

        var routed = Route(ActivationDescription.ForFiles([ActivationItem.File(@"C:\DOCS\A.MD")], isFirst: false));
        var resolved = ActivationRouter.Resolve(routed, registry, identity);

        Assert.Equal(owner, Assert.IsType<ActivationAction.Focus>(Assert.Single(resolved)).WindowId);
    }

    [Fact]
    public void AFileWhoseWindowClosedWhileItsSaveWasInFlightOpensAFreshWindow()
    {
        // The window is gone but the write may still be draining (§1.4 bounds it at 10 s). What
        // must NOT happen is a Focus of a window id that no longer exists: the manager would find
        // nothing, and §1.2's "activate that window instead" would silently open nothing at all.
        var registry = new FakeDocumentRegistry();
        var identity = new FakeFileIdentity();
        var closed = Guid.NewGuid();
        registry.Register(closed, identity.Canonical(@"C:\Docs\a.md"));
        registry.Unregister(closed);                       // Window.Closed → cleanup (§1.4 route 3)

        var resolved = ActivationRouter.Resolve([new ActivationAction.OpenPath(@"C:\Docs\a.md")], registry, identity);

        Assert.Equal(@"C:\Docs\a.md", Assert.IsType<ActivationAction.OpenPath>(Assert.Single(resolved)).Path);
    }

    [Fact]
    public void ACaseOnlyRenameIsStillTheSameOpenDocument()
    {
        // Rename… on Windows may change nothing but the casing. FileIdentity.Canonical returns the
        // on-disk spelling (§5.2), so the registry key and the activation can disagree by a letter —
        // and NTFS would then let two windows write the same bytes.
        var registry = new FakeDocumentRegistry();
        var identity = new FakeFileIdentity();
        var owner = Guid.NewGuid();
        registry.Register(owner, identity.Canonical(@"C:\Docs\Notes.md"));

        foreach (var spelling in new[] { @"C:\Docs\NOTES.MD", @"c:\docs\notes.md", @"C:\Docs\Notes.md" })
        {
            var resolved = ActivationRouter.Resolve([new ActivationAction.OpenPath(spelling)], registry, identity);
            Assert.Equal(owner, Assert.IsType<ActivationAction.Focus>(Assert.Single(resolved)).WindowId);
        }
    }

    [Fact]
    public void AFileNamedLikeABundleIsADocumentWhenTheShellSaysItIsAFile()
    {
        // §1.2's File row is explicit that the bundle import is for "a StorageFolder whose name ends
        // in .textbundle". A .textbundle that is a FILE is not a bundle at all — sending it to
        // TextFromBundleFolder could only ever produce "The TextPack could not be read."
        var actions = Route(ActivationDescription.ForFiles(
            [ActivationItem.File(@"C:\Docs\notes.textbundle"), ActivationItem.Folder(@"C:\Docs\real.textbundle")],
            isFirst: true));

        Assert.Collection(actions,
            a => Assert.Equal(@"C:\Docs\notes.textbundle", Assert.IsType<ActivationAction.OpenPath>(a).Path),
            a => Assert.Equal(@"C:\Docs\real.textbundle", Assert.IsType<ActivationAction.ImportTextBundleFolder>(a).Path));

        // A command line has no shell to ask, so there the name is still taken at its word.
        Assert.IsType<ActivationAction.ImportTextBundleFolder>(
            Assert.Single(Route(ActivationDescription.ForLaunch("\"C:\\Docs\\real.textbundle\"", isFirst: true))));
    }
}
