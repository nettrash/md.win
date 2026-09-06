using System.Text;
using Md.App.Logic.Documents;

namespace Md.App.Logic.Tests;

/// <summary>
/// §7.7's containment rule. Export ▸ TextBundle… is the one feature that reads files the writer
/// never picked, so these are the tests that decide whether an <c>![](..\..\secrets.txt)</c> in a
/// downloaded document copies something out of the user's machine.
/// </summary>
public class AssetReaderTests
{
    static (FakeFileSystem Fs, FakeFileIdentity Identity) World()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\note.md", "# Note");
        fs.AddFile(@"C:\Docs\photo.png", "PNG");
        fs.AddFile(@"C:\Docs\images\logo.png", "LOGO");
        fs.AddFile(@"C:\Secrets\passwords.txt", "SECRET");
        return (fs, new FakeFileIdentity());
    }

    static string? Read(FakeFileSystem fs, FakeFileIdentity id, string reference, string? documentPath = @"C:\Docs\note.md")
    {
        var bytes = AssetReader.Beside(documentPath, fs, id)(reference);
        return bytes is null ? null : Encoding.UTF8.GetString(bytes);
    }

    [Theory]
    [InlineData("photo.png", "PNG")]
    [InlineData("./photo.png", "PNG")]
    [InlineData("images/logo.png", "LOGO")]
    [InlineData(@"images\logo.png", "LOGO")]
    [InlineData("images/../photo.png", "PNG")]
    public void AnImageBesideTheDocumentIsRead(string reference, string expected)
    {
        var (fs, id) = World();
        Assert.Equal(expected, Read(fs, id, reference));
    }

    [Theory]
    [InlineData(@"..\Secrets\passwords.txt")]
    [InlineData("../Secrets/passwords.txt")]
    [InlineData(@"C:\Secrets\passwords.txt")]
    [InlineData(@"\Secrets\passwords.txt")]
    [InlineData("/Secrets/passwords.txt")]
    [InlineData(@"\\server\share\x.png")]
    [InlineData("../../../../etc/passwd")]
    [InlineData("")]
    public void NothingOutsideTheDocumentsOwnFolderIsEverRead(string reference)
    {
        var (fs, id) = World();
        Assert.Null(Read(fs, id, reference));
    }

    [Fact]
    public void AMissingFileIsSimplyNotFound()
    {
        var (fs, id) = World();
        Assert.Null(Read(fs, id, "absent.png"));
    }

    [Fact]
    public void AnUnsavedDocumentResolvesNothingSoItExportsWithEmptyAssets()
    {
        var (fs, id) = World();
        Assert.Null(Read(fs, id, "photo.png", documentPath: null));
        Assert.Null(Read(fs, id, "photo.png", documentPath: ""));
        Assert.Empty(id.Calls);                              // and nothing is even canonicalised
    }

    [Fact]
    public void ALinkPointingOutOfTheFolderIsRefusedEvenThoughTheNameIsInside()
    {
        var (fs, id) = World();
        // A junction beside the document whose real target is elsewhere: the name passes the
        // lexical test, the canonical path does not.
        fs.AddFile(@"C:\Docs\link.png", "VIA LINK");
        id.Alias(@"C:\Docs\link.png", @"C:\Secrets\real.png");

        Assert.Null(Read(fs, id, "link.png"));
    }

    [Fact]
    public void AFolderWhoseNameMerelyStartsWithTheDocumentsFolderIsOutside()
    {
        // "C:\Docs2" starts with "C:\Docs" as a string; it is not inside it.
        Assert.False(AssetReader.IsStrictlyInside(@"C:\Docs2\x.png", @"C:\Docs"));
        Assert.True(AssetReader.IsStrictlyInside(@"C:\Docs\x.png", @"C:\Docs"));
        Assert.True(AssetReader.IsStrictlyInside(@"C:/Docs/sub/x.png", @"C:\Docs"));      // separators are equal
        Assert.True(AssetReader.IsStrictlyInside(@"C:\DOCS\x.png", @"c:\docs"));          // Windows' case rule
        Assert.False(AssetReader.IsStrictlyInside(@"C:\Docs", @"C:\Docs"));               // strictly inside
    }

    [Fact]
    public void AnUnreadableFileIsTreatedAsNotFoundSoTheReferenceStaysAsWritten()
    {
        var (fs, id) = World();
        var reader = AssetReader.Beside(@"C:\Docs\note.md", new ThrowingFileSystem(fs), id);
        Assert.Null(reader("photo.png"));
    }

    sealed class ThrowingFileSystem(FakeFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public byte[] ReadAllBytes(string path) => throw new UnauthorizedAccessException("locked");
        public void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes) => inner.WriteAllBytesInPlace(path, bytes);
        public FileStamp? Stamp(string path) => inner.Stamp(path);
        public void Move(string from, string to) => inner.Move(from, to);
        public void Delete(string path) => inner.Delete(path);
        public IEnumerable<string> EnumerateEntries(string directory) => inner.EnumerateEntries(directory);
    }
}
