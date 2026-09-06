using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>
/// §6.4's name rules and the path arithmetic the whole package rests on. The path tests matter more
/// than they look: <c>System.IO.Path</c> splits on the host's separators, so on the Linux leg of the
/// CI matrix every Windows path a test feeds the session would be one long file name — these pin the
/// OS-independent split that keeps the suite honest wherever it runs.
/// </summary>
public class FileNamesTests
{
    [Theory]
    [InlineData("Chapter One")]
    [InlineData("notes.md")]
    [InlineData(".hidden")]
    [InlineData("a b")]
    [InlineData("CONSOLE")]              // only the exact device names are reserved
    [InlineData("COM10")]                // COM1..COM9 are; COM10 is an ordinary name
    public void AValidNameIsAccepted(string name) => Assert.True(FileNames.Validate(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a*b")]
    [InlineData("a?b")]
    [InlineData("a\"b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a|b")]
    [InlineData("trailing.")]
    [InlineData("trailing ")]
    [InlineData("CON")]
    [InlineData("con.md")]               // reserved with an extension too
    [InlineData("LPT9")]
    [InlineData("nul")]
    [InlineData("a\tb")]                 // control characters
    public void AnInvalidNameIsRejected(string? name) => Assert.False(FileNames.Validate(name));

    [Fact]
    public void EveryDocumentedInvalidCharacterIsInTheMessageTheDialogShows()
    {
        // The message is fixed verbatim; if the rule and the wording ever drift apart, this fails.
        foreach (var c in FileNames.InvalidCharacters)
            Assert.Contains(c.ToString(), Strings.Documents.InvalidNameMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a/b:c", "a-b-c")]
    [InlineData("trailing. ", "trailing")]
    [InlineData("CON", "_CON")]
    [InlineData("", "Document")]
    [InlineData("   ", "Document")]
    [InlineData("...", "Document")]
    [InlineData("Notes", "Notes")]
    public void ForWindowsMakesAnyTitleSafeToHandAPicker(string input, string expected) =>
        Assert.Equal(expected, FileNames.ForWindows(input));

    [Theory]
    [InlineData("Scene.md", "Act One", "Act One.md")]
    [InlineData("Scene.markdown", "Act One", "Act One.markdown")]
    [InlineData("README", "Notes", "Notes")]              // no extension to keep
    [InlineData("notes.", "Other", "Other")]              // Foundation: a trailing dot is not an extension
    [InlineData(".hidden", "Other", "Other")]
    public void RenameKeepsTheExtension(string original, string stem, string expected) =>
        Assert.Equal(expected, FileNames.WithExtensionOf(original, stem));

    [Theory]
    [InlineData(@"C:\Docs\a.md", "a.md", @"C:\Docs")]
    [InlineData("C:/Docs/a.md", "a.md", "C:/Docs")]
    [InlineData(@"C:\a.md", "a.md", @"C:\")]
    [InlineData("/tmp/a.md", "a.md", "/tmp")]
    [InlineData("/a.md", "a.md", "/")]
    [InlineData("a.md", "a.md", "")]
    [InlineData(@"C:\Docs\", "Docs", @"C:\")]
    [InlineData(@"\\server\share\a.md", "a.md", @"\\server\share")]
    public void NameAndDirectorySplitOnBothSeparatorsOnEveryOs(string path, string name, string directory)
    {
        Assert.Equal(name, FileNames.NameOf(path));
        Assert.Equal(directory, FileNames.DirectoryOf(path));
    }

    [Theory]
    [InlineData(@"C:\Docs\01-Scene.md", "01-Scene", ".md")]
    [InlineData(@"C:\Docs\README", "README", "")]
    [InlineData(@"C:\Docs\notes.", "notes.", "")]         // Foundation keeps the whole name
    [InlineData(@"C:\Docs\.hidden", ".hidden", "")]
    [InlineData(@"C:\Docs\a.b c", "a", "")]               // Foundation's two splits disagree: the stem drops it, the extension is empty
    public void StemAndExtensionFollowFoundationsTwoDisagreeingSplits(string path, string stem, string extension)
    {
        Assert.Equal(stem, FileNames.StemOf(path));
        Assert.Equal(extension, FileNames.ExtensionOf(path));
    }

    [Theory]
    [InlineData(@"C:\Docs", "a.md", @"C:\Docs\a.md")]
    [InlineData(@"C:\", "a.md", @"C:\a.md")]
    [InlineData("/tmp", "a.md", "/tmp/a.md")]
    [InlineData("", "a.md", "a.md")]
    public void CombineKeepsTheSeparatorTheDirectoryAlreadyUses(string directory, string name, string expected) =>
        Assert.Equal(expected, FileNames.Combine(directory, name));

    [Theory]
    [InlineData(@"C:\Docs\..\Docs\.\a.md", @"C:\Docs\a.md")]
    [InlineData(@"C:\Docs\sub\..\..\a.md", @"C:\a.md")]
    [InlineData(@"C:\..\..\a.md", @"C:\a.md")]            // ".." at the root is the root
    [InlineData("/tmp/./a.md", "/tmp/a.md")]
    [InlineData(@"C:\Docs\", @"C:\Docs")]
    [InlineData("relative/path", "relative/path")]        // no working directory is invented
    public void StandardizeCollapsesDotSegmentsWithoutAskingTheHost(string path, string expected) =>
        Assert.Equal(expected, FileNames.Standardize(path));

    [Theory]
    [InlineData(@"C:\Docs\a.md", @"c:\docs\A.MD", true)]
    [InlineData(@"C:\Docs\a.md", "C:/Docs/a.md", true)]
    [InlineData(@"C:\Docs\sub\..\a.md", @"C:\Docs\a.md", true)]
    [InlineData(@"C:\Docs\a.md", @"C:\Docs2\a.md", false)]
    [InlineData(@"C:\Docs\a.md", @"C:\Docs\a.md\b", false)]
    public void SamePathIsWindowsRuleEverywhere(string a, string b, bool same) =>
        Assert.Equal(same, FileNames.SamePath(a, b));

    [Theory]
    [InlineData(@"C:\x", true)]
    [InlineData("C:/x", true)]
    [InlineData(@"\x", true)]
    [InlineData("/x", true)]
    [InlineData(@"\\server\share", true)]
    [InlineData("photo.png", false)]
    [InlineData("images/photo.png", false)]
    [InlineData("C:photo.png", false)]                    // drive-relative, not rooted
    public void IsRootedCatchesEveryWayAReferenceEscapesRelativity(string reference, bool rooted) =>
        Assert.Equal(rooted, FileNames.IsRooted(reference));

    [Theory]
    [InlineData(@"C:\Docs", "photo.png", @"C:\Docs\photo.png")]
    [InlineData(@"C:\Docs", "images/photo.png", @"C:\Docs\images\photo.png")]
    [InlineData(@"C:\Docs", "./photo.png", @"C:\Docs\photo.png")]
    [InlineData(@"C:\Docs\sub", "../photo.png", @"C:\Docs\photo.png")]
    [InlineData("/tmp/docs", "photo.png", "/tmp/docs/photo.png")]
    public void ResolveRelativeJoinsAndCollapses(string directory, string relative, string expected) =>
        Assert.Equal(expected, FileNames.ResolveRelative(directory, relative));

    [Theory]
    [InlineData(@"C:\Docs", @"C:\Windows\x.png")]         // rooted
    [InlineData(@"C:\Docs", @"\x.png")]
    [InlineData(@"C:\Docs", "../../../x.png")]            // climbs past the root
    [InlineData(@"C:\Docs", "")]
    public void ResolveRelativeRefusesAnythingThatIsNotBesideTheDocument(string directory, string relative) =>
        Assert.Null(FileNames.ResolveRelative(directory, relative));
}
