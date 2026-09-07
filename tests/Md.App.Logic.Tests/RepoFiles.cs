namespace Md.App.Logic.Tests;

/// <summary>
/// The checkout, for the tests that pin a repo file against the code: <c>App.xaml</c> and
/// <c>Package.appxmanifest</c> against <c>Palette</c>, the bundled <c>Examples\</c> against
/// <c>Md.Core.Document.ExampleLibrary</c>, and <c>src\Md.App</c>'s own sources against the numbers
/// shell-design.md fixes. Found by walking up from the test assembly to the directory holding
/// <c>md.slnx</c>, so it works from <c>dotnet test</c> on any OS and in CI.
/// </summary>
static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    public static string At(params string[] segments) => Path.Combine(Root, Path.Combine(segments));

    static string FindRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "md.slnx"))) return d.FullName;
        throw new InvalidOperationException($"md.slnx not found above {AppContext.BaseDirectory}");
    }
}
