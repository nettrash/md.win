namespace Md.App.Export;

/// <summary>
/// Where the export path puts a file that is on its way somewhere else: the PDF
/// <c>PrintToPdfAsync</c> writes before its bytes are read back, and the copy the Share sheet is
/// handed for an unsaved document.
/// </summary>
/// <remarks>
/// <c>ApplicationData.Current</c> throws without package identity, so an unpackaged run (the
/// self-test's, among others) falls back to the process temp folder the same way the crash log and
/// the WebView2 user-data folder do.
/// </remarks>
internal static class TemporaryFiles
{
    public static string Folder { get; } = Resolve();

    static string Resolve()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.TemporaryFolder.Path;
        }
        catch (Exception)
        {
            return Path.GetTempPath();
        }
    }

    /// <summary>A fresh name for a file nobody else will ever look at, in the Mac's spelling.</summary>
    public static string Scratch(string extension) =>
        Path.Combine(Folder, "md-" + Guid.NewGuid().ToString("N") + extension);

    /// <summary>Best effort: a temp file that will not delete is the operating system's problem, not the export's.</summary>
    public static void Forget(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
