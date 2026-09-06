using Md.App.Logic.Books;

namespace Md.App.Logic.Tests.Books;

/// <summary>
/// A real book folder in a temp directory: articles are files, chapters are subfolders. The book
/// tests run against a disk on purpose — the navigator's operations are renames, deletes and
/// two-phase reorders, and a fake file system would pin the fake's behaviour, not Windows'.
/// </summary>
public sealed class BookFixture : IDisposable
{
    public BookFixture(string? name = null)
    {
        Root = Path.Combine(Path.GetTempPath(), "md-win-book-" + (name ?? Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public string At(params string[] parts) => Path.Combine([Root, .. parts]);

    /// <summary>Write an article; intermediate chapter folders are created.</summary>
    public string Article(string relative, string text = "# Scene\n")
    {
        var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    public string Chapter(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>The names of everything directly in <paramref name="folder"/>, sorted ordinally — what a rename or a reorder is checked against.</summary>
    public IReadOnlyList<string> Names(string? folder = null) =>
        new DirectoryInfo(folder ?? Root).EnumerateFileSystemInfos()
            .Select(info => info.Name)
            .Where(name => !name.StartsWith('.'))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// <see cref="IBookArticleSession"/> without a file system: it records what it was asked to do and
/// can be told to refuse. "Refuse" is the state that matters — a failed flush must abort a selection
/// change and a whole managed operation, and that cannot be provoked through a disk in a test.
/// </summary>
public sealed class FakeArticleSession : IBookArticleSession
{
    public string? EditingPath { get; private set; }

    /// <summary>While true every <see cref="Select"/> and <see cref="HandOffForExternalOpen"/> fails, as an unsaveable article does.</summary>
    public bool FlushFails { get; set; }

    /// <summary>Every path the session was asked to hold, in order; null is a deselect.</summary>
    public List<string?> Selected { get; } = [];

    public int HandOffs { get; private set; }

    /// <summary>Every <see cref="Detach"/> and whether it was asked to report a failure.</summary>
    public List<bool> Detaches { get; } = [];

    public void Detach(bool reportFailure)
    {
        Detaches.Add(reportFailure);
        EditingPath = null;
    }

    public bool Select(string? path)
    {
        if (FlushFails) return false;
        Selected.Add(path);
        EditingPath = path;
        return true;
    }

    public bool HandOffForExternalOpen()
    {
        HandOffs++;
        if (FlushFails) return false;
        EditingPath = null;
        return true;
    }
}
