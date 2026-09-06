namespace Md.Core.Tests;

public class SmokeTests
{
    [Fact]
    public void FixturesAreOnDisk()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        Assert.True(Directory.Exists(root), root);
        Assert.Equal(16, Directory.GetFiles(Path.Combine(root, "testdata"), "*.md").Length);
        Assert.Equal(9, Directory.GetFiles(Path.Combine(root, "examples"), "*.md").Length);
    }
}
