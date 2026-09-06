using System.Text;
using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// Adversarial review of the book module against the source of truth. Every expectation
/// here was read off md.macOS — the Swift sources, or a Swift probe run on macOS
/// (2026-09-06: <c>BookLibrary.ordered</c> over 36,000 random pairs,
/// <c>enumerateSubstrings(.byWords)</c> over 490 lines, <c>URL.pathExtension</c> /
/// <c>deletingPathExtension</c> over 62 names, <c>fileExists(atPath:)</c> on a folder) —
/// never off this port's own output. Invisible code points are spelled as escapes so
/// the vectors stay reviewable. Shares the session collection: <c>BookFlushGate</c> is a
/// static event and a Post from a parallel test class would flush these sessions.
/// </summary>
[Collection("BookArticleSession")]
public class BookAdversarialTests
{
    private static readonly Encoding Utf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    // MARK: Ordering — Foundation's localizedStandardCompare, not Kotlin's stand-in

    /// <summary>
    /// Pairs from the 36,000-pair differential on which the previous comparator (the
    /// Kotlin <c>naturalCompare</c> port: per-unit lowercase, ordinal punctuation)
    /// disagreed with Foundation. The sign is <c>BookLibrary.ordered</c>'s: −1 when the
    /// first name lists first. Punctuation collates TAB, space, <c>_ - , ; : ! ? . ' " ( )
    /// …</c> — all before digits, digits before letters — so <c>10_x</c> lists before
    /// <c>10-x</c>, <c>2) x</c> after <c>2_x</c>, and <c>~0</c> before <c>b</c>.
    /// </summary>
    public static TheoryData<string, int, string> FoundationPairs => new()
    {
        { "2) aa.txt", 1, "2_Tables.markdown" },
        { "9223372036854775807. notes", -1, "9223372036854775807)setup.MD" },
        { ")Tables", 1, "-ase" },
        { "0) ecole", 1, "0-01.markdown" },
        { "9223372036854775807) X2.MD", 1, "9223372036854775807-.ab" },
        { "007)The Storm.md", 1, "007-.Intro.markdown" },
        { "007-ecole", -1, "007) 01.txt" },
        { "-)part02.md", 1, "-.rd party" },
        { "2) ase.txt", 1, "2-)Draft.txt" },
        { "2) aesir.md", 1, "2-ase" },
        { "2_ase", -1, "2. Tables" },
        { "2-a-b", 1, "2_Writer-Tools.markdown" },
        { "9223372036854775807) ab.md", 1, "9223372036854775807. setup" },
        { "0-.Tables.MD", 1, "0_10.MD" },
        { "007_writer.tools", -1, "007. Writer-Tools.MD" },
        { ")aa.txt", 1, "-Writer-Tools.markdown" },
        { "0) The Storm.txt", 1, "0_X2.markdown" },
        { "007. Tables.markdown", 1, "007_aa.MD" },
        { "10_Zebra.txt", -1, "10.appendix.markdown" },
        { "2.v1.2", -1, "2)Draft" },
        { "1_a-b", -1, "1-)ase.MD" },
        { "10) setup.markdown", 1, "10. Draft (Copy)" },
        { "10_part02", -1, "10-part02" },
        { "0) Tables", 1, "0-Tables" },
        { "1)", 1, "0001_C" },
        { "~0", -1, "b" },
        { "_1", -1, "&+" },
        { ")A", 1, "_" },
        { ",b0A9", 1, "_'" },
        { ",c1-&Y", -1, "'9#Z 2Y-" },
        { "( _.~", 1, "-" },
        { "~9a&!#z ", -1, "C#0ca " },
        { "-C.A(.C ", -1, "+0!BC,'" },
        { "_z_(", -1, "&1(B" },
        { ", 9", -1, "+Y-xX" },
        { "A..&c+", 1, "~x9 0Y9" },
        { "&y.", 1, "-1-1c" },
        { "(' .a", -1, "#.c1zy.-" },
        { "!x9", 1, "- x0c)01" },
        { "-(_xC", -1, "#0!Y)z" },
        { "Aa", 1, "~~b'+1A(" },
        { "'a)bz1", 1, "_BC9," },
        // Second walk: lower case lists first, fewer leading zeros list first.
        { "x", -1, "X" }, { "a1", -1, "A1" }, { "d", -1, "D" }, { "1-A", -1, "01-a" }, { "a01", -1, "A1" },
    };

    [Theory]
    [MemberData(nameof(FoundationPairs))]
    public void OrderAgreesWithFoundationWhereTheKotlinStandInDidNot(string a, int sign, string b)
    {
        Assert.Equal(sign, Math.Sign(BookOrder.Compare(a, b)));
        Assert.Equal(-sign, Math.Sign(BookOrder.Compare(b, a)));
        Assert.Equal(sign < 0, BookOrder.Ordered(a, b));
        Assert.Equal(sign > 0, BookOrder.Ordered(b, a));
    }

    // MARK: Word count — ICU's root rules, which both siblings run, not bare UAX #29

    /// <summary>
    /// <c>enumerateSubstrings(.byWords)</c> counts on macOS. The colon family is not
    /// MidLetter under ICU's root tailoring (<c>a:b</c> is two words; bare UAX #29 says
    /// one), wide digits are Numeric (fullwidth 123 is one word), Roman numerals and
    /// circled letters are words while circled digits, fractions and superscripts are
    /// not, Thai letters never join Latin or digits, and every control and line
    /// separator breaks.
    /// </summary>
    public static TheoryData<string, int> FoundationWordCounts => new()
    {
        // Colon family out of MidLetter; the other MidLetter / MidNum / MidNumLet code points still join.
        { "a:b", 2 }, { "a：b", 2 }, { "a﹕b", 2 }, { "a·b", 1 }, { "a‧b", 1 }, { "a.b", 1 }, { "1:2", 2 }, { "1;2", 1 }, { "1,2", 1 }, { "1.2", 1 },
        { "1٫2", 1 }, { "1٬2", 1 }, { "1⁄2", 1 },
        // Wide decimal digits are Numeric; other-script digits always were.
        { "１２３", 1 }, { "１２3", 1 }, { "1２", 1 }, { "１a", 1 }, { "１.２", 1 }, { "１．２", 1 }, { "١٢٣", 1 }, { "०१", 1 }, { "Ａ１", 1 },
        // Letter-numbers and alphabetic symbols are words; circled digits, fractions, superscripts are not.
        { "Ⅻ", 1 }, { "ⅫⅢ", 1 }, { "ⅻ", 1 }, { "\U0001F130", 1 }, { "ⓐ", 1 }, { "〇", 1 }, { "ᛮ", 1 }, { "\U00010140", 1 }, { "①", 0 }, { "①②", 0 },
        { "½", 0 }, { "2½", 1 }, { "x²", 1 }, { "₂", 0 },
        // Thai and Lao (dictionary scripts) join themselves, never Latin or digits.
        { "กข", 1 }, { "สวัสดี", 1 }, { "ก1", 2 }, { "1ก", 2 }, { "กa", 2 }, { "ລາວ", 1 },
        // Spaces: only the narrow no-break space (ExtendNumLet) glues digits.
        { "1\u00A0000", 2 }, { "1\u202F000", 1 }, { "1\u2009000", 2 }, { "1\u2007000", 2 }, { "a\u202Fb", 1 },
        // Invisible glue and marks.
        { "a\u200Bb", 2 }, { "a\u200Cb", 1 }, { "a\u2060b", 1 }, { "a\uFEFFb", 1 }, { "a\u00ADb", 1 }, { "a\u034Fb", 1 }, { "\u0301a", 1 },
        { "a\u0301", 1 }, { "\u200D", 0 }, { "\uFE0F", 0 }, { "\u0301", 0 },
        // Apostrophes and quotes.
        { "'tis", 1 }, { "dogs'", 1 }, { "'a'", 1 }, { "a''b", 2 }, { "rock 'n' roll", 3 }, { "rock ’n’ roll", 3 }, { "can't've", 1 },
        { "O'Brien", 1 }, { "l’homme", 1 }, { "ג\"י", 1 }, { "גד\"", 1 }, { "\"גד", 1 }, { "a\"b", 2 }, { "ג׳י", 1 }, { "ג״י", 1 }, { "א_ב", 1 },
        // Katakana and Hangul. (Foundation's Japanese dictionary path also reports a bare "_" or space between
        // Katakana runs as a word token — "カ_カ" counts 3 on macOS; that artifact is not reproduced or pinned.)
        { "カー", 1 }, { "カｰ", 1 }, { "ｶﾀ", 1 }, { "カa", 2 }, { "カ1", 2 }, { "カあ", 2 }, { "가각", 1 }, { "가 각", 2 }, { "가", 1 },
        // Emoji are never words; a keycap digit is.
        { "1\uFE0F\u20E3", 1 }, { "#\uFE0F\u20E3", 0 }, { "\U0001F600", 0 }, { "a\U0001F600b", 2 }, { "\U0001F1FA\U0001F1F8", 0 }, { "©", 0 },
        { "☃\uFE0F", 0 },
        // Symbols and brackets break.
        { "http://x.y/z", 3 }, { "x.y/z", 2 }, { "a(b)c", 3 }, { "a<b>c", 3 }, { "a&b", 2 }, { "a+b", 2 }, { "a=b", 2 }, { "a/b", 2 }, { "a\\b", 2 },
        // Every dash breaks; a lone one is nothing.
        { "e-mail", 2 }, { "a‐b", 2 }, { "a\u2011b", 2 }, { "a–b", 2 }, { "a—b", 2 }, { "a−b", 2 }, { "a－b", 2 }, { "x-", 1 }, { "-x", 1 },
        { "-", 0 }, { "-5", 1 }, { "a - b", 2 },
        // Connectors join.
        { "a_b", 1 }, { "a‿b", 1 }, { "a＿b", 1 }, { "1_2", 1 }, { "_a", 1 }, { "a_", 1 },
        // Astral letters and digits.
        { "\U0001D400\U0001D401", 1 }, { "\U0001D7CE1", 1 }, { "\U00010400", 1 }, { "٣a", 1 }, { "a٣", 1 }, { "ß", 1 }, { "İ", 1 },
        // Controls and line separators all break; CR LF is one break.
        { "a\u0085b", 2 }, { "a\u2028b", 2 }, { "a\u2029b", 2 }, { "a\u000Bb", 2 }, { "a\u000Cb", 2 }, { "a\r\nb", 2 }, { "a\n\rb", 2 },
        { "a\u001Cb", 2 }, { "a\u001Fb", 2 }, { "a\0b", 2 }, { "a\u007Fb", 2 },
        // Plain shapes.
        { "1a1", 1 }, { "a1a", 1 }, { " a ", 1 }, { "  ", 0 }, { "\t", 0 }, { "abc", 1 }, { "a  b", 2 },
    };

    [Theory]
    [MemberData(nameof(FoundationWordCounts))]
    public void WordCountFollowsIcuRootTailoringsNotBareUax29(string text, int expected)
    {
        Assert.Equal(expected, WritingStats.Words(text));
    }

    // MARK: Rescue copy — Foundation's two extension splits, and folders occupying a name

    private sealed class RecordingFileSystem : IArticleFileSystem
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public List<string> Writes { get; } = new();
        /// <summary>A stamp that never moves: whatever is written, the file looks unchanged to the flush.</summary>
        public FileStamp Constant { get; } = new(new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc), 0);

        public bool FileExists(string path) => Files.ContainsKey(path);
        public byte[] ReadAllBytes(string path) => Files.TryGetValue(path, out var data) ? data : throw new FileNotFoundException(path);
        public void WriteAllBytes(string path, byte[] data) { Writes.Add(path); Files[path] = data; }
        public FileStamp? Stamp(string path) => Files.ContainsKey(path) ? Constant : null;
    }

    private static readonly string MemoryRoot = BookPaths.Standardize(Path.Combine(Path.GetTempPath(), "md-adversarial-memory-book"));

    /// <summary>
    /// Swift builds the rescue name from <c>url.deletingPathExtension().lastPathComponent</c>
    /// and <c>url.pathExtension</c> (empty → "md"). Probed on macOS: a stem of nothing
    /// but dots has no extension ("..md" stays whole), a trailing dot is no extension,
    /// CFURL refuses an extension containing a space — U+0020 only, a TAB is accepted —
    /// while <c>deletingPathExtension</c> still strips it, and the extension keeps its
    /// case. <c>BookArticle.Name</c> is the same <c>deletingPathExtension</c>.
    /// </summary>
    [Theory]
    [InlineData("01-Scene.md", "01-Scene", "01-Scene (rescued).md")]
    [InlineData("01-Scene.MD", "01-Scene", "01-Scene (rescued).MD")]
    [InlineData("README", "README", "README (rescued).md")]
    [InlineData("notes.", "notes.", "notes. (rescued).md")]
    [InlineData("01.Scene", "01", "01 (rescued).Scene")]
    [InlineData(".hidden.md", ".hidden", ".hidden (rescued).md")]
    [InlineData("..md", "..md", "..md (rescued).md")]
    [InlineData("...md", "...md", "...md (rescued).md")]
    [InlineData("a..md", "a.", "a. (rescued).md")]
    [InlineData("a.tar.gz", "a.tar", "a.tar (rescued).gz")]
    [InlineData("01-Scene.md.bak", "01-Scene.md", "01-Scene.md (rescued).bak")]
    [InlineData("a.b c", "a", "a (rescued).md")]
    [InlineData("a.md ", "a", "a (rescued).md")]
    [InlineData("a.b\tc", "a", "a (rescued).b\tc")]
    [InlineData("Draft (copy).txt", "Draft (copy)", "Draft (copy) (rescued).txt")]
    public void RescueCopyNamesAndArticleNamesFollowFoundationsSplits(string file, string articleName, string rescueName)
    {
        var path = Path.Combine(MemoryRoot, file);
        Assert.Equal(articleName, new BookArticle(path).Name);

        var fs = new RecordingFileSystem();
        using var session = new BookArticleSession(fs);
        session.OpenBook(MemoryRoot);
        var rescued = session.WriteRescueCopy(path);
        Assert.NotNull(rescued);
        Assert.Equal(rescueName, Path.GetFileName(rescued));
        Assert.Equal(MemoryRoot, Path.GetDirectoryName(rescued));
        Assert.Equal(new[] { rescued }, fs.Writes);
    }

    [Fact]
    public void RescueCopySkipsAFolderSquattingOnItsName()
    {
        // Swift guards with fileExists(atPath:), which is true for a folder (probed);
        // File.Exists alone would try to write into the folder's name and fail the rescue.
        var root = Path.Combine(Path.GetTempPath(), "md-adversarial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var article = Path.Combine(root, "01-Scene.md");
            File.WriteAllBytes(article, Utf8.GetBytes("# Scene\n"));
            Directory.CreateDirectory(Path.Combine(root, "01-Scene (rescued).md"));
            using var session = new BookArticleSession();
            session.OpenBook(root);
            Assert.True(session.Select(article));
            session.Edit("# Mine\n");

            var rescued = session.WriteRescueCopy(article);
            Assert.NotNull(rescued);
            Assert.Equal("01-Scene (rescued 2).md", Path.GetFileName(rescued));
            Assert.Equal("# Mine\n", Utf8.GetString(File.ReadAllBytes(rescued!)));
            Assert.True(Directory.Exists(Path.Combine(root, "01-Scene (rescued).md")));
            Assert.True(session.Dirty);
            session.Select(null);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // MARK: Session — promises read off the Swift state machine

    private sealed class FakeOwnership : IDocumentOwnership
    {
        public HashSet<string> Owned { get; } = new(StringComparer.Ordinal);
        public bool Owns(string path) => Owned.Any(o => BookPaths.Same(o, path));
        public void ShowOwner(string path) { }
    }

    private sealed class ManualAutosave : IAutosaveScheduler
    {
        public Action? Pending { get; private set; }
        public void Schedule(Action flush) => Pending = flush;
        public void Cancel() => Pending = null;
        public void Fire() { var pending = Pending; Pending = null; pending?.Invoke(); }
    }

    [Fact]
    public void AutosaveFlushHandsOffWhenADocumentOpenedTheArticleMidSession()
    {
        // Swift flushNow: `let owner = documentOwning(url)` is read before the write and
        // `if owner != nil { stage = .handoff(url) }` after it — the debounced autosave is
        // the one flush that reaches this without going through recheckOwnership, and it
        // must still step aside so there is never a second writer.
        var root = Path.Combine(Path.GetTempPath(), "md-adversarial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var article = Path.Combine(root, "01-Scene.md");
            File.WriteAllBytes(article, Utf8.GetBytes("# Scene\n"));
            var ownership = new FakeOwnership();
            var autosave = new ManualAutosave();
            using var session = new BookArticleSession(ownership: ownership, autosave: autosave);
            session.OpenBook(root);
            Assert.True(session.Select(article));
            session.Edit("# Typed here, then opened in a window\n");
            Assert.NotNull(autosave.Pending);

            ownership.Owned.Add(article);
            autosave.Fire();

            Assert.Equal(BookStage.Handoff(BookPaths.Standardize(article)), session.Stage);
            Assert.Null(session.EditingPath);
            Assert.False(session.Dirty);
            Assert.Equal("# Typed here, then opened in a window\n", Utf8.GetString(File.ReadAllBytes(article)));
            // And the reclaim on the way back is a fresh load, not a stale buffer.
            ownership.Owned.Clear();
            File.WriteAllBytes(article, Utf8.GetBytes("# Edited in the window\n"));
            session.RecheckOwnership();
            Assert.Equal(BookPaths.Standardize(article), session.EditingPath);
            Assert.Equal("# Edited in the window\n", session.Text);
            session.CloseBook();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ReloadFromDiskNeverWritesTheBufferEvenWhenTheFlushCouldLand()
    {
        // Swift resolveConflictReloading clears dirty BEFORE detaching: "writing the
        // buffer is exactly what must not happen here". The existing conflict tests
        // cannot tell — their stamps have moved, so the detach's flush would be refused
        // anyway. Here a stray Deleted notice (the file was recreated by the time the
        // event arrived, stamp unchanged) conflicts the session while a flush COULD land.
        var fs = new RecordingFileSystem();
        var article = Path.Combine(MemoryRoot, "01-Scene.md");
        fs.Files[article] = Utf8.GetBytes("# Scene\n");
        using var session = new BookArticleSession(fs);
        session.OpenBook(MemoryRoot);
        Assert.True(session.Select(article));
        session.Edit("# Mine, unsaved\n");
        session.FileDeleted();
        Assert.True(session.Conflicted);
        Assert.Equal(BookStageKind.Editing, session.Stage.Kind);

        session.ResolveConflictReloading();

        Assert.Empty(fs.Writes);
        Assert.Equal("# Scene\n", session.Text);
        Assert.Equal("# Scene\n", Utf8.GetString(fs.Files[article]));
        Assert.False(session.Dirty);
        Assert.False(session.Conflicted);
        Assert.Equal(BookStage.Editing(article), session.Stage);
        // Keep My Version is the one path that writes without a stamp check.
        session.Edit("# Mine again\n");
        session.FileDeleted();
        session.ResolveConflictKeepingMine();
        Assert.Equal(new[] { article }, fs.Writes);
        Assert.Equal("# Mine again\n", Utf8.GetString(fs.Files[article]));
        session.CloseBook();
        Assert.Equal(new[] { article }, fs.Writes);
    }

    [Fact]
    public void SelectIsIdempotentAcrossSpellingsOfTheSamePath()
    {
        // Swift compares `current == target` on standardizedFileURLs, so a selection
        // restored as "Book/./01-Scene.md" or "Book/../Book/01-Scene.md" is the article
        // already open: no flush, no reload, the undo stack and the unsaved text survive.
        var root = Path.Combine(Path.GetTempPath(), "md-adversarial-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var article = Path.Combine(root, "01-Scene.md");
            File.WriteAllBytes(article, Utf8.GetBytes("# Scene\n"));
            using var session = new BookArticleSession();
            session.OpenBook(root);
            Assert.True(session.Select(article));
            session.Edit("# Still typing\n");
            var generation = session.UndoGeneration;

            Assert.True(session.Select(Path.Combine(root, ".", "01-Scene.md")));
            Assert.True(session.Select(Path.Combine(root, "..", Path.GetFileName(root), "01-Scene.md")));
            Assert.True(session.Select(article + Path.DirectorySeparatorChar));

            Assert.Equal(generation, session.UndoGeneration);
            Assert.True(session.Dirty);
            Assert.Equal("# Still typing\n", session.Text);
            Assert.Equal("# Scene\n", Utf8.GetString(File.ReadAllBytes(article)));
            Assert.Equal(BookPaths.Standardize(article), session.EditingPath);
            session.CloseBook();
            Assert.Equal("# Still typing\n", Utf8.GetString(File.ReadAllBytes(article)));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
