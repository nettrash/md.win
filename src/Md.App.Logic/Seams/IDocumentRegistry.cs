namespace Md.App.Logic.Seams;

/// <summary>
/// Which window owns which file (canonical path, <c>OrdinalIgnoreCase</c>), for the
/// "already open → activate that window" rule and the book's hand-off (§8.6). App:
/// <c>WindowRegistry</c> (Md.App.Logic.Windows, WP3); tests: <c>FakeDocumentRegistry</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IDocumentRegistry
{
    Guid? Owning(string canonicalPath);
    void Register(Guid windowId, string canonicalPath);
    void Unregister(Guid windowId);

    /// <summary>Every open window, for the Window menu — document titles plus "Book".</summary>
    IReadOnlyList<(Guid Id, string Title)> Windows { get; }

    event Action Changed;
}
