namespace Md.Core.Tests;

/// <summary>
/// The shared corpus on disk: <c>Fixtures/**</c> is copied beside the test assembly, so a
/// relative path such as <c>testdata/headings.md</c> or <c>golden/testdata/headings.html</c>
/// resolves under <see cref="AppContext.BaseDirectory"/>. Files are read as UTF-8 and
/// returned byte-for-byte — no newline normalisation, no BOM stripping beyond what the
/// UTF-8 decoder does — because the goldens are compared ordinally.
/// </summary>
public static class Fixtures
{
    public static string Path(string relativePath) =>
        System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);

    public static string Read(string relativePath) => File.ReadAllText(Path(relativePath));
}
