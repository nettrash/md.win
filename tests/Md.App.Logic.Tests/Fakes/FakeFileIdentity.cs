namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IFileIdentity"/> without a file system: the canonical form is the input with
/// forward slashes, unless <see cref="Alias"/> mapped that spelling to another — the way to
/// model "two spellings of one file" (case, junction, 8.3 name) in a test.
/// </summary>
public sealed class FakeFileIdentity : IFileIdentity
{
    readonly Dictionary<string, string> _aliases = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Calls { get; } = [];

    public void Alias(string spelling, string canonical) => _aliases[Normalise(spelling)] = Normalise(canonical);

    public string Canonical(string path)
    {
        Calls.Add(path);
        var n = Normalise(path);
        return _aliases.TryGetValue(n, out var c) ? c : n;
    }

    static string Normalise(string path) => path.Trim().Replace('\\', '/');
}
