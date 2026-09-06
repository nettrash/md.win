namespace Md.App.Logic.Seams;

/// <summary>
/// The canonical spelling of a path, so two spellings of one file hash to one
/// <c>md.viewModeMemory</c> identity and one registry key (§5.2). Logic: <c>FileIdentity</c>
/// (GetFinalPathNameByHandle, Windows-only at run time, WP2); tests: <c>FakeFileIdentity</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IFileIdentity
{
    string Canonical(string path);
}
