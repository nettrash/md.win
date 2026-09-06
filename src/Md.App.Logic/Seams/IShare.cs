namespace Md.App.Logic.Seams;

/// <summary>
/// The Windows Share sheet for one file (§7.8). App: <c>ShareBridge</c> via
/// <c>IDataTransferManagerInterop</c>; tests: <c>FakeShare</c>.
/// FROZEN — shell-final.md §13.2.
/// </summary>
public interface IShare
{
    Task ShareFileAsync(string path, string title);
}
