using System.Globalization;
using Md.Core.Export;

namespace Md.Core.Tests;

// The name a save picker is opened with.
//
// Two layers, tested apart. PortableStem is the family rule (macOS
// DocumentExport.sanitized, Android Exporter.sanitized, md.vscode sanitized) and must
// stay byte-identical with those three. Sanitized adds what Windows refuses on top -
// control characters, trailing dots and spaces, the reserved device names - which is
// this port's own layer, and the only part of this file that has no sibling.
//
// The macOS suite has no test of its own for `sanitized` (it is a private helper of
// DocumentExport), so these vectors are read straight off the three implementations
// and off the shapes the export paths compose: "<title>.<ext>" everywhere and
// "<title>-<ordinal+1>.svg" for a diagram.
public class ExportFileNamesTests
{
    private static readonly char[] Forbidden = ['/', '\\', ':', '?', '%', '*', '|', '"', '<', '>'];

    // MARK: - The family rule

    [Fact]
    public void PortableStemReplacesEveryCharacterOfTheFamilySet()
    {
        foreach (var c in Forbidden)
        {
            Assert.Equal("a-b", ExportFileNames.PortableStem("a" + c + "b"));
        }
        Assert.Equal("Report-2026", ExportFileNames.PortableStem("Report/2026"));
        // It is a split-join, so a run of two offending characters becomes TWO dashes,
        // not one - pinned in the md.vscode port and true of all four.
        Assert.Equal("a--b", ExportFileNames.PortableStem("a<>b"));
        Assert.Equal("---", ExportFileNames.PortableStem("///"));
        Assert.Equal("-", ExportFileNames.PortableStem("/"));
        // Characters no port touches, including a line break: the family rule leaves
        // them, because no other platform minds.
        Assert.Equal("a\nb", ExportFileNames.PortableStem("a\nb"));
        Assert.Equal("Q1 (final) #2 & co.", ExportFileNames.PortableStem("Q1 (final) #2 & co."));
    }

    [Fact]
    public void PortableStemTrimsAndFallsBackToDocument()
    {
        Assert.Equal("Draft notes", ExportFileNames.PortableStem("  Draft notes\n"));
        Assert.Equal("Document", ExportFileNames.PortableStem(""));
        Assert.Equal("Document", ExportFileNames.PortableStem("   "));
        Assert.Equal("Document", ExportFileNames.PortableStem("\r\n\t "));
        Assert.Equal("Document", ExportFileNames.Fallback);
    }

    [Fact]
    public void TheTrimIsFoundationsWhitespaceNotDotNets()
    {
        // Foundation's frozen tables still call U+200B a space separator, so macOS
        // trims it and `string.Trim()` would not. A title pasted with zero-width
        // padding must not name the file with an invisible character.
        var zwsp = ((char)0x200B).ToString();
        Assert.Equal("Draft", ExportFileNames.PortableStem(zwsp + "Draft" + zwsp));
        Assert.Equal("Document", ExportFileNames.PortableStem(zwsp));
        // U+180E is NOT in the set (Cf since Unicode 6.3, and Foundation agrees).
        var mvs = ((char)0x180E).ToString();
        Assert.Equal(mvs + "Draft", ExportFileNames.PortableStem(mvs + "Draft"));
        // The newline half of the set is trimmed too (U+0085 NEL, U+2028 LS).
        Assert.Equal("Draft", ExportFileNames.PortableStem(((char)0x0085) + "Draft" + (char)0x2028));
    }

    // MARK: - Composing a file name

    [Fact]
    public void SanitizedComposesTheStemAndTheExtension()
    {
        Assert.Equal("Draft.svg", ExportFileNames.Sanitized("Draft", "svg"));
        // Given with or without its leading dot, and any number of them.
        Assert.Equal("Draft.svg", ExportFileNames.Sanitized("Draft", ".svg"));
        Assert.Equal("Draft.svg", ExportFileNames.Sanitized("Draft", "..svg"));
        // No extension at all: no trailing dot.
        Assert.Equal("Draft", ExportFileNames.Sanitized("Draft", ""));
        Assert.Equal("Draft", ExportFileNames.Sanitized("Draft", "."));
        Assert.Equal("Draft", ExportFileNames.Sanitized("Draft", "   "));
        // The extension gets the same character rule, and can never leave the finished
        // name ending in a dot or a space.
        Assert.Equal("Draft.sv-g", ExportFileNames.Sanitized("Draft", "sv/g"));
        Assert.Equal("Draft.svg", ExportFileNames.Sanitized("Draft", "svg "));
        Assert.Equal("Document.pdf", ExportFileNames.Sanitized("", "pdf"));
        // Every extension the export paths use, on a title that needs cleaning.
        Assert.Equal("Report- Q1-2026.pdf", ExportFileNames.Sanitized("Report: Q1/2026", "pdf"));
        foreach (var extension in (string[])["pdf", "html", "epub", "tex", "svg", "textbundle"])
        {
            Assert.Equal("Draft." + extension, ExportFileNames.Sanitized("Draft", extension));
        }
    }

    [Fact]
    public void SanitizedNamesADiagramExportOneBased()
    {
        // The shape the SVG export composes: "<title>-<ordinal + 1>.svg".
        var ordinal = 2;
        Assert.Equal("Notes- 2026-3.svg",
            ExportFileNames.Sanitized("Notes| 2026-" + (ordinal + 1).ToString(CultureInfo.InvariantCulture), "svg"));
    }

    // MARK: - The Windows-only layer

    [Fact]
    public void SanitizedDropsControlCharactersTheFamilyRuleKeeps()
    {
        // Windows-specific: U+0000-U+001F are illegal in a Win32 file name and the
        // picker throws on them. They become the same dash the family set becomes. A
        // line break in a title is the realistic case, and the family rule keeps it.
        Assert.Equal("a\nb", ExportFileNames.PortableStem("a\nb"));
        Assert.Equal("a-b.svg", ExportFileNames.Sanitized("a\nb", "svg"));
        Assert.Equal("a-b.svg", ExportFileNames.Sanitized("a" + (char)0x0001 + "b", "svg"));
        Assert.Equal("a--b.svg", ExportFileNames.Sanitized("a\r\nb", "svg"));
        // U+007F DEL is legal on Windows and is left alone.
        Assert.Equal("a" + (char)0x007F + "b.svg", ExportFileNames.Sanitized("a" + (char)0x007F + "b", "svg"));
    }

    [Fact]
    public void SanitizedDropsTrailingDots()
    {
        // Windows-specific: a name may not end in a dot. (It may not end in a space
        // either, but the family trim has already taken those off the stem - the guard
        // still covers a hand-written extension.)
        Assert.Equal("Notes.svg", ExportFileNames.Sanitized("Notes...", "svg"));
        Assert.Equal("Document.svg", ExportFileNames.Sanitized("...", "svg"));
        Assert.Equal("Document.svg", ExportFileNames.Sanitized(". . .", "svg"));
        // A leading dot is fine on Windows and is kept: `.keep` is an ordinary name.
        Assert.Equal(".keep.svg", ExportFileNames.Sanitized(".keep", "svg"));
        // Interior dots are untouched.
        Assert.Equal("v1.2.3.svg", ExportFileNames.Sanitized("v1.2.3", "svg"));
    }

    [Fact]
    public void SanitizedEscapesTheReservedDeviceNames()
    {
        // Windows-specific: these are devices with ANY extension, so the match is on
        // the part before the first dot; a dash goes in ahead of that dot, which is
        // enough to make the name ordinary again. The user's own casing survives.
        Assert.Equal("CON-.svg", ExportFileNames.Sanitized("CON", "svg"));
        Assert.Equal("con-.svg", ExportFileNames.Sanitized("con", "svg"));
        Assert.Equal("CoN-.svg", ExportFileNames.Sanitized("CoN", "svg"));
        Assert.Equal("CON-.old.txt", ExportFileNames.Sanitized("CON.old", "txt"));
        Assert.Equal("CON-.svg", ExportFileNames.Sanitized("CON ", "svg"));
        foreach (var device in (string[])["CON", "PRN", "AUX", "NUL"])
        {
            Assert.Equal(device + "-.svg", ExportFileNames.Sanitized(device, "svg"));
        }
        for (var n = 1; n <= 9; n++)
        {
            var digit = n.ToString(CultureInfo.InvariantCulture);
            Assert.Equal("COM" + digit + "-.svg", ExportFileNames.Sanitized("COM" + digit, "svg"));
            Assert.Equal("LPT" + digit + "-.svg", ExportFileNames.Sanitized("LPT" + digit, "svg"));
        }
        // Not devices: two digits, a zero, a longer word, a prefix.
        foreach (var ordinary in (string[])["COM10", "COM0", "LPT0", "CONS", "CONSOLE", "NULL", "AU", "COM"])
        {
            Assert.Equal(ordinary + ".svg", ExportFileNames.Sanitized(ordinary, "svg"));
        }
        // The family rule knows nothing about devices - that layer is this port's.
        Assert.Equal("CON", ExportFileNames.PortableStem("CON"));
    }

    // MARK: - Culture

    [Fact]
    public void SanitizedIsIndependentOfTheCurrentCulture()
    {
        // The device fold is ASCII: under tr-TR an invariant upper-case would turn
        // "i" into a dotted capital, and any culture-sensitive comparison could match
        // more (or less) than the twenty-two names. Run it on its own thread so no
        // other test in the suite sees the culture.
        string? con = null;
        string? title = null;
        var thread = new Thread(() =>
        {
            var turkish = new CultureInfo("tr-TR");
            CultureInfo.CurrentCulture = turkish;
            CultureInfo.CurrentUICulture = turkish;
            con = ExportFileNames.Sanitized("con", "svg");
            title = ExportFileNames.Sanitized("Istanbul I i", "svg");
        });
        thread.Start();
        thread.Join();
        Assert.Equal("con-.svg", con);
        Assert.Equal("Istanbul I i.svg", title);
    }

    [Fact]
    public void TheTitleItselfIsNeverCaseFolded()
    {
        // Only the device comparison folds, and only into a scratch buffer: the name
        // the user sees is their own text.
        var dottedCapitalI = ((char)0x0130).ToString();
        Assert.Equal(dottedCapitalI + "stanbul.svg", ExportFileNames.Sanitized(dottedCapitalI + "stanbul", "svg"));
        Assert.Equal("Grosse Strasse.epub", ExportFileNames.Sanitized("Grosse Strasse", "epub"));
    }
}
