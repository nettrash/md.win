using System.Text;
using Md.App.Logic.Documents;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>§6.1, §6.3 and §6.5: what one path turns into, and which alert each failure earns.</summary>
public class DocumentLoaderTests
{
    [Fact]
    public void APlainFileIsEditedInPlaceUnderItsOwnStem()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\Chapter One.md", "# Title\r\nBody\r\n");

        var load = DocumentLoader.Load(@"C:\Docs\Chapter One.md", fs);

        Assert.Equal(LoadFailure.None, load.Failure);
        Assert.Null(load.AlertTitle);
        var document = load.Document!;
        Assert.Equal(DocumentKind.PlainText, document.Kind);
        Assert.Equal("# Title\nBody\n", document.Text);
        Assert.Equal(NewLine.CrLf, document.Dressing.NewLine);
        Assert.Equal("Chapter One", document.Title);
    }

    [Fact]
    public void ALegacyEncodedFileKeepsItsEncoding()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\legacy.md", DocumentFixtures.Cp1251.GetBytes("Привет, мир!"));

        var document = DocumentLoader.Load(@"C:\Docs\legacy.md", fs).Document!;

        Assert.Equal("Привет, мир!", document.Text);
        Assert.Equal(TextEncoding.WindowsCP1251, document.Dressing.Encoding);
    }

    [Fact]
    public void AMissingFileIsCouldNotOpenWithTheSystemsOwnMessage()
    {
        var load = DocumentLoader.Load(@"C:\Docs\gone.md", new FakeFileSystem());

        Assert.Null(load.Document);
        Assert.Equal(LoadFailure.Unreadable, load.Failure);
        Assert.Equal(Strings.Documents.CouldNotOpen, load.AlertTitle);
        Assert.NotEqual("", load.ErrorText);
    }

    [Fact]
    public void ATextPackImportsAsAnUntitledDocumentTitledAfterTheBundle()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Downloads\Notes.textpack", DocumentFixtures.TextPack("# From a pack\n"));

        var load = DocumentLoader.Load(@"C:\Downloads\Notes.textpack", fs);

        Assert.Equal(LoadFailure.None, load.Failure);
        Assert.Equal(DocumentKind.TextPack, load.Document!.Kind);
        Assert.Equal("# From a pack\n", load.Document.Text);
        Assert.Equal("Notes", load.Document.Title);
    }

    [Fact]
    public void ZipBytesUnderAMarkdownNameAreStillReadAsAPackNotAsText()
    {
        // Core's sniff: the full PK\x03\x04 magic, never the two letters. Feeding zip bytes to the
        // text decoder is what Latin-1 would happily "succeed" at.
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Downloads\mislabelled.md", DocumentFixtures.TextPack("# Inside\n"));

        Assert.Equal(DocumentKind.TextPack, DocumentLoader.Load(@"C:\Downloads\mislabelled.md", fs).Document!.Kind);
    }

    [Fact]
    public void ProseThatBeginsWithTheLettersPkIsOrdinaryText()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\pk.md", "PK is a person, not an archive.\n");

        Assert.Equal(DocumentKind.PlainText, DocumentLoader.Load(@"C:\Docs\pk.md", fs).Document!.Kind);
    }

    [Fact]
    public void ACorruptPackEarnsTheTextPackAlertAndIsNeverDecodedAsText()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Downloads\broken.textpack", [0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF]);

        var load = DocumentLoader.Load(@"C:\Downloads\broken.textpack", fs);

        Assert.Null(load.Document);
        Assert.Equal(LoadFailure.BundleUnreadable, load.Failure);
        Assert.Equal(Strings.Documents.TextPackUnreadable, load.AlertTitle);
    }

    [Fact]
    public void ATextBundleFolderImportsItsTextFile()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\Trip.textbundle\text.md", "# Trip\n");
        fs.AddFile(@"C:\Docs\Trip.textbundle\info.json", "{}");
        fs.AddFile(@"C:\Docs\Trip.textbundle\assets\photo.png", "PNG");

        var load = DocumentLoader.Load(@"C:\Docs\Trip.textbundle", fs);

        Assert.Equal(DocumentKind.TextBundleFolder, load.Document!.Kind);
        Assert.Equal("# Trip\n", load.Document.Text);
        Assert.Equal("Trip", load.Document.Title);
    }

    [Fact]
    public void ABundleFolderFallsBackFromTextMdToTextMarkdownToAnyTextFile()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\A.textbundle\text.markdown", "markdown wins over text.txt");
        fs.AddFile(@"C:\Docs\A.textbundle\text.txt", "not this one");
        Assert.Equal("markdown wins over text.txt", DocumentLoader.Load(@"C:\Docs\A.textbundle", fs).Document!.Text);

        var only = new FakeFileSystem();
        only.AddFile(@"C:\Docs\B.textbundle\text.txt", "the only one");
        Assert.Equal("the only one", DocumentLoader.Load(@"C:\Docs\B.textbundle", only).Document!.Text);
    }

    [Fact]
    public void ABundleFolderWithNoTextFileEarnsTheBundleAlert()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\Empty.textbundle\info.json", "{}");

        var load = DocumentLoader.Load(@"C:\Docs\Empty.textbundle", fs);

        Assert.Null(load.Document);
        Assert.Equal(LoadFailure.BundleUnreadable, load.Failure);
    }

    [Fact]
    public void AFileNamedTextbundleThatIsNotAFolderIsJustAFile()
    {
        var fs = new FakeFileSystem();
        fs.AddFile(@"C:\Docs\odd.textbundle", "# Actually text\n");

        Assert.Equal(DocumentKind.PlainText, DocumentLoader.Load(@"C:\Docs\odd.textbundle", fs).Document!.Kind);
    }

    [Fact]
    public void ABundleFolderIsListedForItsTextFileOnlyNeverForItsAssets()
    {
        var fs = new CountingFileSystem();
        fs.AddFile(@"C:\Docs\Big.textbundle\text.md", "# Big\n");
        // Both shapes of neighbour a real bundle has: a top-level sibling of text.md — every
        // bundle carries info.json, and a cover image is common — and the assets folder. Only the
        // second is filtered out by "is it a file"; without the "text." prefix test the first is
        // inflated into memory, which is the whole reason the prefix is there.
        fs.AddFile(@"C:\Docs\Big.textbundle\info.json", Md.Core.Export.TextBundle.InfoJson);
        fs.AddFile(@"C:\Docs\Big.textbundle\cover.png", new string('c', 4096));
        fs.AddFile(@"C:\Docs\Big.textbundle\assets\huge.bin", new string('x', 4096));

        DocumentLoader.Load(@"C:\Docs\Big.textbundle", fs);

        Assert.Equal([@"C:/Docs/Big.textbundle/text.md"], fs.Reads);
    }

    [Theory]
    [InlineData(@"C:\Docs\Notes.mkd", "Notes")]
    [InlineData(@"C:\Docs\Notes.mdtxt", "Notes")]
    [InlineData(@"C:\Docs\Notes.mkdown", "Notes")]
    [InlineData(@"C:\Diagrams\Login Flow.iuml", "Login Flow")]
    [InlineData(@"C:\Diagrams\Login Flow.pu", "Login Flow")]
    [InlineData(@"C:\Docs\readme.text", "readme")]
    public void AnAliasExtensionIsAnOrdinaryFileEditedInPlaceUnderItsOwnStem(string path, string title)
    {
        // Every spelling the family associates is a plain file to the loader: read, decoded, edited
        // in place under its own name, never mistaken for a bundle. And the alias is in the Open
        // filter, so File ▸ Open… finds the same file a double-click delivers.
        var fs = new FakeFileSystem();
        fs.AddFile(path, "@startuml\r\nA -> B\r\n@enduml\r\n");

        var load = DocumentLoader.Load(path, fs);

        Assert.Equal(LoadFailure.None, load.Failure);
        var document = load.Document!;
        Assert.Equal(DocumentKind.PlainText, document.Kind);
        Assert.Equal(path, document.Path);
        Assert.Equal("@startuml\nA -> B\n@enduml\n", document.Text);
        Assert.Equal(NewLine.CrLf, document.Dressing.NewLine);
        Assert.Equal(title, document.Title);
        Assert.Contains(FileNames.ExtensionOf(path), DocumentLoader.OpenExtensions);
    }

    [Fact]
    public void ThePickerSetsAreTheMacsListsInTheMacsOrder()
    {
        Assert.Equal(
            [".md", ".markdown", ".mdown", ".markdn", ".mdtext", ".mdtxt", ".mkd", ".mkdn", ".mdwn", ".mkdown"],
            DocumentLoader.MarkdownExtensions);
        Assert.Equal([".txt", ".text"], DocumentLoader.PlainTextExtensions);
        Assert.Equal([".puml", ".plantuml", ".iuml", ".pu"], DocumentLoader.PlantUmlExtensions);
        Assert.Equal([".gv"], DocumentLoader.GraphvizExtensions);
        Assert.Equal(
            [
                ".md", ".markdown", ".mdown", ".markdn", ".mdtext", ".mdtxt", ".mkd", ".mkdn", ".mdwn", ".mkdown",
                ".txt", ".text", ".puml", ".plantuml", ".iuml", ".pu", ".gv", ".textpack",
            ],
            DocumentLoader.OpenExtensions);
        Assert.Equal(
            [Strings.Exports.MarkdownDocument, Strings.Exports.PlainText, Strings.Exports.PlantUmlDiagram, Strings.Exports.GraphvizDotGraph],
            DocumentLoader.SaveChoices.Select(c => c.Label));
        Assert.Equal(
            [
                ".md", ".markdown", ".mdown", ".markdn", ".mdtext", ".mdtxt", ".mkd", ".mkdn", ".mdwn", ".mkdown",
                ".txt", ".text", ".puml", ".plantuml", ".iuml", ".pu", ".gv",
            ],
            DocumentLoader.SaveChoices.SelectMany(c => c.Extensions));
        // Never a bundle type: saving one back would drop its assets.
        Assert.DoesNotContain(DocumentLoader.SaveChoices.SelectMany(c => c.Extensions), e => e is ".textpack" or ".textbundle");
    }

    [Theory]
    [InlineData(@"C:\Docs\a.md", ".md")]
    [InlineData(@"C:\Docs\a.mkd", ".mkd")]
    [InlineData(@"C:\Docs\a.puml", ".puml")]
    [InlineData(@"C:\Docs\a.iuml", ".iuml")]
    [InlineData(@"C:\Docs\a.text", ".text")]
    [InlineData(@"C:\Docs\README", ".md")]
    [InlineData(null, ".md")]
    public void TheSavePickerDefaultsToTheDocumentsOwnExtension(string? path, string expected) =>
        Assert.Equal(expected, DocumentLoader.DefaultExtensionFor(path));

    sealed class CountingFileSystem : IFileSystem
    {
        readonly FakeFileSystem inner = new();
        public List<string> Reads { get; } = [];
        public void AddFile(string path, string text) => inner.AddFile(path, text);
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public byte[] ReadAllBytes(string path) { Reads.Add(path); return inner.ReadAllBytes(path); }
        public void WriteAllBytesInPlace(string path, ReadOnlySpan<byte> bytes) => inner.WriteAllBytesInPlace(path, bytes);
        public FileStamp? Stamp(string path) => inner.Stamp(path);
        public void Move(string from, string to) => inner.Move(from, to);
        public void Delete(string path) => inner.Delete(path);
        public IEnumerable<string> EnumerateEntries(string directory) => inner.EnumerateEntries(directory);
    }
}
