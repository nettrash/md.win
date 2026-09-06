using System.Text;
using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>§6.3's last-resort save: the names, and the promise that a rescue never overwrites anything.</summary>
public class RescueCopyTests
{
    [Theory]
    [InlineData("01-Scene.md", 1, "01-Scene (rescued).md")]
    [InlineData("01-Scene.md", 2, "01-Scene (rescued 2).md")]
    [InlineData("01-Scene.md", 37, "01-Scene (rescued 37).md")]
    [InlineData(@"C:\Book\01-Scene.md", 1, "01-Scene (rescued).md")]
    [InlineData("notes.markdown", 1, "notes (rescued).markdown")]
    [InlineData("README", 1, "README (rescued).md")]           // no extension: the Mac's ".md" fallback
    [InlineData(".hidden", 1, ".hidden (rescued).md")]         // Foundation: a dotfile has no extension
    [InlineData("a.b c", 1, "a (rescued).md")]                 // an extension with a space is no extension
    public void RescueNamesFollowTheMacsTwoSplits(string fileName, int attempt, string expected) =>
        Assert.Equal(expected, RescueCopy.NameFor(fileName, attempt));

    [Fact]
    public void ThereIsNeverARescued1()
    {
        Assert.DoesNotContain("(rescued 1)", RescueCopy.NameFor("a.md", 1), StringComparison.Ordinal);
        Assert.Contains("(rescued)", RescueCopy.NameFor("a.md", 1), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstFreeNameWinsAndTheOccupantIsUntouched()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\01-Scene.md", "on disk");
        fs.AddFile(@"C:\Book\01-Scene (rescued).md", "someone else's");

        var written = RescueCopy.Write(fs, @"C:\Book\01-Scene.md", Encoding.UTF8.GetBytes("my buffer"));

        Assert.Equal(@"C:\Book\01-Scene (rescued 2).md", written);
        Assert.Equal("my buffer", fs.Text(written!));
        Assert.Equal("someone else's", fs.Text(@"C:\Book\01-Scene (rescued).md"));
        Assert.Equal("on disk", fs.Text(@"C:\Book\01-Scene.md"));
    }

    [Fact]
    public void ADirectorySquattingOnTheNameCountsAsTaken()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\01-Scene.md", "on disk");
        fs.AddDirectory(@"C:\Book\01-Scene (rescued).md");

        Assert.Equal(@"C:\Book\01-Scene (rescued 2).md", RescueCopy.Write(fs, @"C:\Book\01-Scene.md", [1, 2, 3]));
    }

    [Fact]
    public void AFailingWriteGivesUpAtOnceRatherThanTryingAHundredNames()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\01-Scene.md", "on disk");
        fs.NextWriteFailure = new UnauthorizedAccessException("denied");

        Assert.Null(RescueCopy.Write(fs, @"C:\Book\01-Scene.md", [1]));
        Assert.Single(fs.Writes);
    }

    [Fact]
    public void AHundredTakenNamesGiveUp()
    {
        // The literal, not RescueCopy.MaxAttempts: a loop bounded by the constant under test passes
        // for any value of it, and the mutation that lowers the cap to 3 survived that shape.
        Assert.Equal(100, RescueCopy.MaxAttempts);

        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\a.md", "on disk");
        for (var attempt = 1; attempt <= 100; attempt++)
            fs.AddFile(FileNames.Combine(@"C:\Book", RescueCopy.NameFor("a.md", attempt)), "taken");

        Assert.Null(RescueCopy.Write(fs, @"C:\Book\a.md", [1]));
        Assert.Empty(fs.Writes);
    }

    [Fact]
    public void TheHundredthNameIsStillTriedRatherThanGivingUpOneEarly()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Book\a.md", "on disk");
        for (var attempt = 1; attempt < 100; attempt++)
            fs.AddFile(FileNames.Combine(@"C:\Book", RescueCopy.NameFor("a.md", attempt)), "taken");

        Assert.Equal(@"C:\Book\a (rescued 100).md", RescueCopy.Write(fs, @"C:\Book\a.md", [1]));
    }
}
