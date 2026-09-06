using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>
/// §5.2's canonical path. Most of it can only be true on Windows — <c>GetFinalPathNameByHandle</c>
/// is the whole point — so those facts are <c>[Fact]</c>s that no-op elsewhere and are exercised on
/// the <c>windows-latest</c> leg of the matrix; the rest holds everywhere.
/// </summary>
public class FileIdentityTests
{
    [Theory]
    [InlineData(@"\\?\C:\Docs\a.md", @"C:\Docs\a.md")]
    [InlineData(@"\\?\UNC\server\share\a.md", @"\\server\share\a.md")]
    [InlineData(@"C:\Docs\a.md", @"C:\Docs\a.md")]
    public void TheExtendedLengthPrefixIsStrippedSoAPickersPathMatches(string final, string expected) =>
        Assert.Equal(expected, FileIdentity.StripPrefix(final));

    [Fact]
    public void ADotSegmentPathAndItsPlainSpellingAreOneIdentity()
    {
        var directory = TempDirectory();
        try
        {
            var file = Path.Combine(directory, "a.md");
            File.WriteAllText(file, "x");

            var plain = FileIdentity.Instance.Canonical(file);
            var roundabout = FileIdentity.Instance.Canonical(Path.Combine(directory, "sub", "..", "a.md"));

            Assert.Equal(plain, roundabout, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AMissingPathStillHasAStableIdentity()
    {
        var missing = Path.Combine(Path.GetTempPath(), "md-win-tests", "never-created.md");
        var canonical = FileIdentity.Instance.Canonical(missing);

        Assert.Equal(canonical, FileIdentity.Instance.Canonical(missing));
        Assert.Equal(FileIdentity.Lexical(missing), canonical);
        Assert.False(canonical.EndsWith(Path.DirectorySeparatorChar));
    }

    [Fact]
    public void AnEmptyPathIsReturnedUnchangedRatherThanThrowing() =>
        Assert.Equal("", FileIdentity.Instance.Canonical(""));

    [Fact]
    public void TwoCaseSpellingsOfOneFileAreOneIdentityOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;                  // NTFS' rule; elsewhere the two are two files
        var directory = TempDirectory();
        try
        {
            var file = Path.Combine(directory, "Chapter.md");
            File.WriteAllText(file, "x");

            Assert.Equal(
                FileIdentity.Instance.Canonical(file),
                FileIdentity.Instance.Canonical(Path.Combine(directory.ToUpperInvariant(), "CHAPTER.MD")),
                StringComparer.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheCanonicalPathIsTheFilesOwnCaseOnWindowsNotTheCallersOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = TempDirectory();
        try
        {
            var file = Path.Combine(directory, "Chapter.md");
            File.WriteAllText(file, "x");

            // GetFinalPathNameByHandle answers with the name the file system holds, so the identity
            // is the true spelling — not whatever the shell, the MRU or a drag-drop handed us.
            Assert.EndsWith("Chapter.md", FileIdentity.Instance.Canonical(Path.Combine(directory, "CHAPTER.MD")), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AFileReachedThroughALinkedFolderHasTheTargetsIdentityOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = TempDirectory();
        try
        {
            var real = Path.Combine(directory, "real");
            Directory.CreateDirectory(real);
            var file = Path.Combine(real, "a.md");
            File.WriteAllText(file, "x");

            var link = Path.Combine(directory, "link");
            try
            {
                Directory.CreateSymbolicLink(link, real);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;                                            // no Developer Mode on this runner; nothing to prove
            }

            Assert.Equal(
                FileIdentity.Instance.Canonical(file),
                FileIdentity.Instance.Canonical(Path.Combine(link, "a.md")),
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "md-win-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
