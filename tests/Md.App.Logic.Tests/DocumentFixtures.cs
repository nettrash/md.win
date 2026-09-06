using System.Text;
using Md.Core.Export;

namespace Md.App.Logic.Tests;

/// <summary>
/// Bytes the document tests need: the legacy encoding a Windows user's older files are in, and a
/// real <c>.textpack</c> built with Core's own writer so the import path is exercised against an
/// archive md itself could produce.
/// </summary>
static class DocumentFixtures
{
    /// <summary>
    /// Assigned in the static constructor's body, never in a field initializer: C# runs every field
    /// initializer first, so an initializer would call <c>GetEncoding(1251)</c> before the provider
    /// is registered and the whole type would fail to load. (Core's codec carries the same comment.)
    /// </summary>
    public static readonly Encoding Cp1251;

    static DocumentFixtures()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    /// <summary>A <c>.textpack</c> whose nested bundle folder is <paramref name="folderName"/> and whose <c>text.md</c> is <paramref name="text"/>.</summary>
    public static byte[] TextPack(string text, string folderName = "Notes.textbundle") =>
        TextBundle.BundleWrapper(text, []).ToTextPack(folderName);
}
