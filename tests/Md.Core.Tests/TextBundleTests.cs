using System.Globalization;
using System.Text.Json;
using Md.Core.Document;
using Md.Core.Export;
using static Md.Core.Tests.ZipFixtures;

namespace Md.Core.Tests;

/// <summary>
/// The pure pieces of TextBundle / TextPack (macOS mdTests "TextBundle / TextPack" plus
/// the Kotlin-only cases): the info.json shape, reading text.md out of a folder and out
/// of a pack, the export image-ref rewrite, the bundle assembly, and the on-disk
/// containment of asset reads. The picker plumbing is not here.
/// </summary>
public class TextBundleTests
{
    // MARK: info.json

    [Fact]
    public void InfoJsonHasTheTextBundleShape()
    {
        using var document = JsonDocument.Parse(TextBundle.InfoJson);
        var root = document.RootElement;
        Assert.Equal(2, root.GetProperty("version").GetInt32());
        Assert.Equal("net.daringfireball.markdown", root.GetProperty("type").GetString());
        Assert.False(root.GetProperty("transient").GetBoolean());
        // Canonical bytes: stable key order, two-space indent, no trailing newline.
        Assert.Equal("{\n  \"version\": 2,\n  \"type\": \"net.daringfireball.markdown\",\n  \"transient\": false\n}", TextBundle.InfoJson);
        Assert.Equal(81, Utf8(TextBundle.InfoJson).Length);
    }

    // MARK: Import — a .textbundle folder

    [Fact]
    public void ImportReadsTextMarkdownWithEncoding()
    {
        // A `text.markdown` (the spec's other name) in a legacy encoding must decode with
        // the right encoding and round-trip byte-for-byte.
        var cyrillic = Cp1251("Привет");
        var result = TextBundle.TextFromBundle(new Dictionary<string, byte[]> { ["text.markdown"] = cyrillic });
        Assert.NotNull(result);
        Assert.Equal("Привет", result.Text);
        Assert.Equal(TextEncoding.WindowsCP1251, result.Encoding);
        Assert.Equal(cyrillic, PlainTextCodec.Encode(result.Text, result.Encoding).Data);
    }

    [Fact]
    public void ImportPrefersTextMd()
    {
        var bundle = new Dictionary<string, byte[]>
        {
            ["text.markdown"] = Utf8("markdown\n"),
            ["text.md"] = Utf8("md\n"),
        };
        Assert.Equal("md\n", TextBundle.TextFromBundle(bundle)?.Text);
    }

    [Fact]
    public void ImportNilWithoutATextFile()
    {
        Assert.Null(TextBundle.TextFromBundle(new Dictionary<string, byte[]> { ["info.json"] = Utf8("{}") }));
        Assert.Null(TextBundle.TextFromBundle(new Dictionary<string, byte[]>()));
    }

    [Fact]
    public void ImportFallsBackToAnyTextDotFileDeterministically()
    {
        // The spec names the file `text` with an extension matching the declared type;
        // Swift's "any text.*" pick is dictionary order, made ordinal-smallest here.
        var bundle = new Dictionary<string, byte[]>
        {
            ["text.txt"] = Utf8("txt\n"),
            ["info.json"] = Utf8("{}"),
            ["text.mdown"] = Utf8("mdown\n"),
        };
        Assert.Equal("mdown\n", TextBundle.TextFromBundle(bundle)?.Text);
        // `text.markdown` still beats an arbitrary `text.*`.
        bundle["text.markdown"] = Utf8("markdown\n");
        Assert.Equal("markdown\n", TextBundle.TextFromBundle(bundle)?.Text);
    }

    [Fact]
    public void ImportOfAnEmptyTextFileIsEmptyNotNull()
    {
        // FileWrapper hands back empty Data for an empty file, and the codec decodes it.
        var result = TextBundle.TextFromBundle(new Dictionary<string, byte[]> { ["text.md"] = Array.Empty<byte>() });
        Assert.NotNull(result);
        Assert.Equal("", result.Text);
        Assert.Equal(TextEncoding.Utf8, result.Encoding);
    }

    [Fact]
    public void ImportReadsAFolderOnDisk()
    {
        var root = TempFolder();
        try
        {
            var bundle = Path.Combine(root, "Notes.textbundle");
            Directory.CreateDirectory(Path.Combine(bundle, "assets"));
            File.WriteAllBytes(Path.Combine(bundle, "text.md"), Utf8("# From disk\n"));
            File.WriteAllBytes(Path.Combine(bundle, "info.json"), Utf8(TextBundle.InfoJson));
            var result = TextBundle.TextFromBundle(bundle);
            Assert.Equal("# From disk\n", result?.Text);
            Assert.Equal(TextEncoding.Utf8, result?.Encoding);
            // A missing folder, or one without a text file, is nil — not an exception.
            Assert.Null(TextBundle.TextFromBundle(Path.Combine(root, "Missing.textbundle")));
            Directory.CreateDirectory(Path.Combine(root, "Empty.textbundle"));
            Assert.Null(TextBundle.TextFromBundle(Path.Combine(root, "Empty.textbundle")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // MARK: Import — a .textpack

    [Fact]
    public void PackImportReadsStoredZip()
    {
        // A pack wraps a `.textbundle` folder, so text.md is nested under it.
        var zip = Stored(
            ("Doc.textbundle/info.json", Utf8(TextBundle.InfoJson)),
            ("Doc.textbundle/text.md", Utf8("# Packed\n")));
        var result = TextBundle.TextFromPack(zip);
        Assert.Equal("# Packed\n", result?.Text);
        Assert.Equal(TextEncoding.Utf8, result?.Encoding);
    }

    [Fact]
    public void PackImportReadsDeflatedZip()
    {
        var payload = "# Deflated\n\nSome longer body text to compress well.\n";
        Assert.Equal(payload, TextBundle.TextFromPack(Deflated("Doc.textbundle/text.md", Utf8(payload)))?.Text);
    }

    [Fact]
    public void PackImportRejectsNonZip()
    {
        Assert.Null(TextBundle.TextFromPack(Utf8("plain text, not a pack")));
        Assert.Null(TextBundle.TextFromPack(Array.Empty<byte>()));
    }

    [Fact]
    public void PackImportPrefersTextMd()
    {
        // text.md wins even when text.markdown is the earlier entry.
        var zip = Stored(
            ("B.textbundle/text.markdown", Utf8("markdown\n")),
            ("B.textbundle/text.md", Utf8("md\n")));
        Assert.Equal("md\n", TextBundle.TextFromPack(zip)?.Text);
    }

    [Fact]
    public void PackImportDecodesLegacyEncoding()
    {
        // The pack-path counterpart of the folder-import encoding test.
        var result = TextBundle.TextFromPack(Stored(("B.textbundle/text.md", Cp1251("Привет"))));
        Assert.Equal("Привет", result?.Text);
        Assert.Equal(TextEncoding.WindowsCP1251, result?.Encoding);
    }

    [Fact]
    public void PackImportNilWithoutATextFileOrWithAnEmptyOne()
    {
        Assert.Null(TextBundle.TextFromPack(Stored(("B.textbundle/info.json", Utf8("{}")))));
        // Swift's `guard let data = entry?.data, !data.isEmpty` — an empty text entry is
        // nil on the pack path (the folder path decodes it to ""); Kotlin returns "" here.
        Assert.Null(TextBundle.TextFromPack(Stored(("B.textbundle/text.md", Array.Empty<byte>()))));
    }

    [Fact]
    public void PackImportMatchesTheTextFileByItsLastComponentOnly()
    {
        // Any nesting depth, and a `text.*` that is not the whole name does not count.
        Assert.Equal("deep\n", TextBundle.TextFromPack(Stored(("a/b/c/text.md", Utf8("deep\n"))))?.Text);
        Assert.Null(TextBundle.TextFromPack(Stored(("Doc.textbundle/context.md", Utf8("no\n")))));
    }

    [Fact]
    public void LooksLikePackByNameOrZipMagic()
    {
        var zip = Stored(("Doc.textbundle/text.md", Utf8("hi\n")));
        // The 4-byte local-file-header magic identifies an unnamed pack…
        Assert.True(TextBundle.LooksLikePack("whatever", zip));
        // …and so does the extension, whatever the bytes.
        Assert.True(TextBundle.LooksLikePack("Notes.textpack", Utf8("not a zip")));
        Assert.True(TextBundle.LooksLikePack("Notes.TEXTPACK", Utf8("not a zip")));
        // A plain Markdown file — even one whose text starts with the two letters "PK" —
        // is NOT mistaken for a pack (only PK + 03 04 is).
        Assert.False(TextBundle.LooksLikePack("notes.md", Utf8("# Notes\n")));
        Assert.False(TextBundle.LooksLikePack("pk.md", Utf8("PKZIP is a program\n")));
        Assert.False(TextBundle.LooksLikePack("pk.md", new byte[] { 0x50, 0x4B }));
    }

    // MARK: Export — the image-ref rewrite

    [Fact]
    public void ExportCopiesFoundImageAndLeavesTheRestUntouched()
    {
        // A findable local ref is copied to assets/ and rewritten; an unfindable local ref
        // and a remote URL are left exactly as written.
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var source = "![cat](photo.png) ![dog](missing.png) ![web](https://e/x.png)";
        var result = TextBundle.ExportRewriting(source, path => path == "photo.png" ? png : null);
        Assert.Contains("![cat](assets/photo.png)", result.Text, StringComparison.Ordinal);
        Assert.Contains("![dog](missing.png)", result.Text, StringComparison.Ordinal);
        Assert.Contains("![web](https://e/x.png)", result.Text, StringComparison.Ordinal);
        Assert.Equal(new[] { new TextBundle.Asset("photo.png", png) }, result.Assets);
    }

    [Fact]
    public void ExportKeepsImageTitleWhenRewriting()
    {
        // Only the URL is rewritten; a following "title" is preserved — either quote.
        var result = TextBundle.ExportRewriting("![alt](pic.png \"A cat\")", _ => new byte[] { 1 });
        Assert.Contains("![alt](assets/pic.png \"A cat\")", result.Text, StringComparison.Ordinal);
        var single = TextBundle.ExportRewriting("![alt](pic.png 'A cat')", _ => new byte[] { 1 });
        Assert.Equal("![alt](assets/pic.png 'A cat')", single.Text);
        // The separator is any ICU whitespace run, a tab included.
        var tabbed = TextBundle.ExportRewriting("![alt](pic.png\t\"A cat\")", _ => new byte[] { 1 });
        Assert.Equal("![alt](assets/pic.png\t\"A cat\")", tabbed.Text);
    }

    [Fact]
    public void ExportDedupesAssetNamesButReusesOnePathOnce()
    {
        // Two distinct paths sharing a file name get distinct assets; the same path used
        // again reuses its asset (copied once, both refs rewritten).
        var source = "![a](a/logo.png) ![b](b/logo.png) ![c](a/logo.png)";
        var result = TextBundle.ExportRewriting(source, _ => new byte[] { 7 });
        Assert.Equal(new[] { "logo.png", "logo-2.png" }, result.Assets.Select(a => a.Name).ToArray());
        Assert.Contains("![a](assets/logo.png)", result.Text, StringComparison.Ordinal);
        Assert.Contains("![b](assets/logo-2.png)", result.Text, StringComparison.Ordinal);
        Assert.Contains("![c](assets/logo.png)", result.Text, StringComparison.Ordinal);
        // The loader is consulted once per distinct path.
        var calls = new List<string>();
        TextBundle.ExportRewriting(source, path => { calls.Add(path); return new byte[] { 7 }; });
        Assert.Equal(new[] { "a/logo.png", "b/logo.png" }, calls);
    }

    [Fact]
    public void ExportDisambiguatesDotfilesAndExtensionlessNames()
    {
        // The extension split is past the FIRST unit, so a dotfile keeps its whole name as
        // the stem (`.keep` → `.keep-2`, not `-2.keep`), and a name with no dot gets the
        // counter appended. A third collision counts on.
        var dotfiles = TextBundle.ExportRewriting("![a](x/.keep) ![b](y/.keep)", _ => new byte[] { 1 });
        Assert.Equal(new[] { ".keep", ".keep-2" }, dotfiles.Assets.Select(a => a.Name).ToArray());
        var plain = TextBundle.ExportRewriting("![a](x/README) ![b](y/README) ![c](z/README)", _ => new byte[] { 1 });
        Assert.Equal(new[] { "README", "README-2", "README-3" }, plain.Assets.Select(a => a.Name).ToArray());
        var dotted = TextBundle.ExportRewriting("![a](x/a.tar.gz) ![b](y/a.tar.gz)", _ => new byte[] { 1 });
        Assert.Equal(new[] { "a.tar.gz", "a.tar-2.gz" }, dotted.Assets.Select(a => a.Name).ToArray());
    }

    [Fact]
    public void ExportDedupIsCaseInsensitiveAndLocaleIndependent()
    {
        // `Logo.PNG` and `logo.png` collide (the fold is case-insensitive), and the fold is
        // locale-independent — so this holds under a Turkish culture too, where a
        // culture-sensitive lower-case would map I differently.
        WithCulture("tr-TR", () =>
        {
            var result = TextBundle.ExportRewriting("![a](x/Logo.PNG) ![b](y/logo.png) ![c](z/ICON.PNG) ![d](w/icon.png)", _ => new byte[] { 1 });
            Assert.Equal(new[] { "Logo.PNG", "logo-2.png", "ICON.PNG", "icon-2.png" }, result.Assets.Select(a => a.Name).ToArray());
        });
    }

    [Fact]
    public void ExportDestinationStopsAtEveryIcuWhitespace()
    {
        // ICU's \s (measured on macOS): a destination may not contain NBSP, VT or NEL any
        // more than a space, so such a ref never matches and stays exactly as written.
        foreach (var gap in new[] { (char)0x00A0, (char)0x000B, (char)0x0085, (char)0x2003, (char)0x2028, ' ' })
        {
            var source = "![a](pic" + gap + "x.png)";
            var result = TextBundle.ExportRewriting(source, _ => new byte[] { 1 });
            Assert.Equal(source, result.Text);
            Assert.Empty(result.Assets);
        }
        // U+200B and U+FEFF are not whitespace to ICU: the ref matches and is rewritten.
        foreach (var notGap in new[] { (char)0x200B, (char)0xFEFF })
        {
            var result = TextBundle.ExportRewriting("![a](pic" + notGap + "x.png)", _ => new byte[] { 1 });
            Assert.Equal("![a](assets/pic" + notGap + "x.png)", result.Text);
        }
    }

    [Fact]
    public void ExportLeavesTheSourceAloneWhenNothingResolves()
    {
        var source = "# Title\n\n![a](x.png) and `![b](y.png)`\n";
        var result = TextBundle.ExportRewriting(source, _ => null);
        Assert.Equal(source, result.Text);
        Assert.Empty(result.Assets);
        // A ref whose last component is empty (a folder) is skipped even when the loader
        // answers — there is no file name to copy it under.
        var folder = TextBundle.ExportRewriting("![a](dir/)", _ => new byte[] { 1 });
        Assert.Equal("![a](dir/)", folder.Text);
        Assert.Empty(folder.Assets);
    }

    [Fact]
    public void ExportRewritesEveryOccurrenceInDocumentOrder()
    {
        // Several edits in one document: the splice must keep the untouched text between
        // them intact (the Swift applies its UTF-16 ranges back to front).
        var source = "Начало ![a](a.png) середина ![b](sub/b.png \"t\") конец ![a](a.png).";
        var result = TextBundle.ExportRewriting(source, path => Utf8(path));
        Assert.Equal("Начало ![a](assets/a.png) середина ![b](assets/b.png \"t\") конец ![a](assets/a.png).", result.Text);
        Assert.Equal(new[] { new TextBundle.Asset("a.png", Utf8("a.png")), new TextBundle.Asset("b.png", Utf8("sub/b.png")) }, result.Assets);
    }

    [Fact]
    public void LocalRelativeReferenceClassification()
    {
        Assert.True(TextBundle.IsLocalRelativeReference("photo.png"));
        Assert.True(TextBundle.IsLocalRelativeReference("images/photo.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("https://x/y.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("http://x/y.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("data:image/png;base64,AAAA"));
        Assert.False(TextBundle.IsLocalRelativeReference("/abs/photo.png"));
        Assert.False(TextBundle.IsLocalRelativeReference("#anchor"));
        Assert.False(TextBundle.IsLocalRelativeReference(""));
        Assert.False(TextBundle.IsLocalRelativeReference("mailto:me@example.com"));
        Assert.False(TextBundle.IsLocalRelativeReference("file:///x/y.png"));
    }

    [Fact]
    public void ReferenceClassificationIsCodeUnitExactAroundMarks()
    {
        // A relative path carrying a combining mark is still local — the mark is a unit of
        // its own and never manufactures a "://".
        Assert.True(TextBundle.IsLocalRelativeReference("café/photo.png"));
        // A real scheme is still caught when a mark sits elsewhere in the URL.
        Assert.False(TextBundle.IsLocalRelativeReference("https://hóst/x.png"));
        // A ':' wearing a combining mark is NOT the "://" delimiter.
        Assert.True(TextBundle.IsLocalRelativeReference("weird:" + (char)0x0301 + "//name.png"));
    }

    [Fact]
    public void ReferenceClassificationFoldsSchemesAsciiOnly()
    {
        WithCulture("tr-TR", () =>
        {
            Assert.False(TextBundle.IsLocalRelativeReference("DATA:image/png;base64,AAAA"));
            Assert.False(TextBundle.IsLocalRelativeReference("MailTo:me@example.com"));
            // Swift's and Kotlin's full case mapping lower İ to "i" + U+0307, so "MAİLTO:"
            // is not the mailto scheme on either sibling; .NET's ToLowerInvariant would
            // have said otherwise.
            Assert.True(TextBundle.IsLocalRelativeReference("MA" + (char)0x0130 + "LTO:x.png"));
            Assert.True(TextBundle.IsLocalRelativeReference("dat" + (char)0x0130 + ":x.png"));
        });
    }

    // MARK: Export — the bundle assembly

    [Fact]
    public void BundleWrapperHoldsTextInfoAndAssets()
    {
        var assets = new[] { new TextBundle.Asset("p.png", new byte[] { 1, 2 }) };
        var wrapper = TextBundle.BundleWrapper("# Hi\n", assets);
        Assert.Equal(Utf8("# Hi\n"), wrapper.TextBytes);
        Assert.Equal(Utf8(TextBundle.InfoJson), wrapper.InfoJsonBytes);
        Assert.Equal(assets, wrapper.Assets);
        // UTF-8 without a BOM, whatever the document's own encoding was.
        Assert.Equal(new byte[] { 0xD0, 0x9F }, TextBundle.BundleWrapper("П", Array.Empty<TextBundle.Asset>()).TextBytes);
    }

    [Fact]
    public void BundleWrapperWithNoImagesStillCarriesEmptyAssets()
    {
        var wrapper = TextBundle.BundleWrapper("plain\n", Array.Empty<TextBundle.Asset>());
        Assert.Empty(wrapper.Assets);
        var root = TempFolder();
        try
        {
            var target = Path.Combine(root, "Plain.textbundle");
            wrapper.Write(target);
            Assert.True(Directory.Exists(Path.Combine(target, "assets")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(target, "assets")));
            Assert.Equal(Utf8("plain\n"), File.ReadAllBytes(Path.Combine(target, "text.md")));
            Assert.Equal(TextBundle.InfoJson, Utf8(File.ReadAllBytes(Path.Combine(target, "info.json"))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteReplacesAnExistingBundleWhole()
    {
        // FileWrapper's atomic write swaps the whole package: a stale asset from a previous
        // export must not linger beside the new text.
        var root = TempFolder();
        try
        {
            var target = Path.Combine(root, "Doc.textbundle");
            TextBundle.BundleWrapper("v1\n", new[] { new TextBundle.Asset("old.png", new byte[] { 1 }) }).Write(target);
            Assert.True(File.Exists(Path.Combine(target, "assets", "old.png")));
            TextBundle.BundleWrapper("v2\n", new[] { new TextBundle.Asset("new.png", new byte[] { 2 }) }).Write(target);
            Assert.False(File.Exists(Path.Combine(target, "assets", "old.png")));
            Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(target, "assets", "new.png")));
            Assert.Equal("v2\n", Utf8(File.ReadAllBytes(Path.Combine(target, "text.md"))));
            // The folder is readable back as a bundle, and nothing was left in staging.
            Assert.Equal("v2\n", TextBundle.TextFromBundle(target)?.Text);
            Assert.Equal(new[] { "Doc.textbundle" }, Directory.GetFileSystemEntries(root).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void PackEntriesNestUnderTheBundleFolder()
    {
        // Android's bundleEntries: info.json, text.md, then the assets — no stray
        // directory marker when there are real assets…
        var assets = new[] { new TextBundle.Asset("p.png", new byte[] { 1, 2 }) };
        var entries = TextBundle.BundleWrapper("# Hi\n", assets).PackEntries("Doc").ToDictionary(e => e.Name, e => e.Data, StringComparer.Ordinal);
        Assert.Equal("# Hi\n", Utf8(entries["Doc.textbundle/text.md"]));
        Assert.True(entries.ContainsKey("Doc.textbundle/info.json"));
        Assert.Equal(new byte[] { 1, 2 }, entries["Doc.textbundle/assets/p.png"]);
        Assert.False(entries.ContainsKey("Doc.textbundle/assets/"));
        // …and an explicit empty assets/ entry when there are none.
        var empty = TextBundle.BundleWrapper("plain\n", Array.Empty<TextBundle.Asset>()).PackEntries("Doc").Select(e => e.Name).ToArray();
        Assert.Equal(new[] { "Doc.textbundle/info.json", "Doc.textbundle/text.md", "Doc.textbundle/assets/" }, empty);
    }

    [Fact]
    public void PackRoundTripsTextAndAssets()
    {
        // The whole export→import loop through the app's own container: text via the
        // importer, the copied asset byte-exact via the reader.
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A };
        var rewrite = TextBundle.ExportRewriting("![c](photo.png)", _ => png);
        Assert.Equal("![c](assets/photo.png)", rewrite.Text);
        var pack = TextBundle.BundleWrapper(rewrite.Text, rewrite.Assets).ToTextPack("Doc");
        Assert.True(TextBundle.LooksLikePack("anything", pack));
        Assert.Equal(rewrite.Text, TextBundle.TextFromPack(pack)?.Text);
        Assert.Equal(png, Named(ZipReader.Entries(pack), "Doc.textbundle/assets/photo.png")?.Data);
        // A pack with no assets still lists the empty folder for a foreign reader.
        var bare = TextBundle.BundleWrapper("plain\n", Array.Empty<TextBundle.Asset>()).ToTextPack("Doc");
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(bare));
        Assert.Contains("Doc.textbundle/assets/", archive.Entries.Select(e => e.FullName));
    }

    [Fact]
    public void AssetEqualityIsByNameAndBytes()
    {
        Assert.Equal(new TextBundle.Asset("p.png", new byte[] { 1 }), new TextBundle.Asset("p.png", new byte[] { 1 }));
        Assert.NotEqual(new TextBundle.Asset("p.png", new byte[] { 1 }), new TextBundle.Asset("p.png", new byte[] { 2 }));
        Assert.NotEqual(new TextBundle.Asset("p.png", new byte[] { 1 }), new TextBundle.Asset("P.png", new byte[] { 1 }));
    }

    // MARK: Export — reading a referenced image with containment

    [Fact]
    public void ReadAssetFindsAFileBesideTheDocumentAndNothingElse()
    {
        var root = TempFolder();
        try
        {
            var docs = Path.Combine(root, "docs");
            Directory.CreateDirectory(Path.Combine(docs, "images"));
            var document = Path.Combine(docs, "notes.md");
            File.WriteAllBytes(document, Utf8("# n\n"));
            File.WriteAllBytes(Path.Combine(docs, "photo.png"), new byte[] { 1, 2, 3 });
            File.WriteAllBytes(Path.Combine(docs, "images", "deep.png"), new byte[] { 4 });
            File.WriteAllBytes(Path.Combine(root, "outside.png"), new byte[] { 9 });

            Assert.Equal(new byte[] { 1, 2, 3 }, TextBundle.ReadAsset("photo.png", document));
            Assert.Equal(new byte[] { 4 }, TextBundle.ReadAsset("images/deep.png", document));
            Assert.Equal(new byte[] { 1, 2, 3 }, TextBundle.ReadAsset("images/../photo.png", document));
            Assert.Null(TextBundle.ReadAsset("missing.png", document));
            // A folder is not an image.
            Assert.Null(TextBundle.ReadAsset("images", document));
            // Climbing out of the document's folder is "not found", never a read.
            Assert.Null(TextBundle.ReadAsset("../outside.png", document));
            Assert.Null(TextBundle.ReadAsset("images/../../outside.png", document));
            // A rooted path (the Windows drive-letter case the classifier lets through).
            Assert.Null(TextBundle.ReadAsset(Path.Combine(root, "outside.png"), document));
            // An unsaved document has no folder to look beside.
            Assert.Null(TextBundle.ReadAsset("photo.png", null));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadAssetResolvesSymlinksBeforeTheContainmentCheck()
    {
        // A symlink beside the document, named like an image, could otherwise point
        // anywhere on disk and pass the prefix test — both a file link and a folder link.
        var root = TempFolder();
        try
        {
            var docs = Path.Combine(root, "docs");
            var elsewhere = Path.Combine(root, "elsewhere");
            Directory.CreateDirectory(docs);
            Directory.CreateDirectory(elsewhere);
            var document = Path.Combine(docs, "notes.md");
            File.WriteAllBytes(document, Utf8("# n\n"));
            File.WriteAllBytes(Path.Combine(elsewhere, "secret.png"), new byte[] { 0x53 });
            File.WriteAllBytes(Path.Combine(docs, "real.png"), new byte[] { 0x52 });
            try
            {
                File.CreateSymbolicLink(Path.Combine(docs, "link.png"), Path.Combine(elsewhere, "secret.png"));
                Directory.CreateSymbolicLink(Path.Combine(docs, "imgs"), elsewhere);
                File.CreateSymbolicLink(Path.Combine(docs, "same.png"), Path.Combine(docs, "real.png"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return;   // no symlink privilege on this host (Windows without Developer Mode)
            }
            Assert.Null(TextBundle.ReadAsset("link.png", document));
            Assert.Null(TextBundle.ReadAsset("imgs/secret.png", document));
            // A link that stays inside the folder is fine, and is read through.
            Assert.Equal(new byte[] { 0x52 }, TextBundle.ReadAsset("same.png", document));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // MARK: helpers

    private static string TempFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "md-textbundle-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WithCulture(string name, Action body)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(name);
            CultureInfo.CurrentUICulture = new CultureInfo(name);
            body();
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = ui;
        }
    }
}
