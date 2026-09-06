using System.Globalization;
using Md.Core.Document;

namespace Md.Core.Tests;

// Port of md.macOS/mdTests/ViewModeTests.swift (all eight cases, names kept) plus the Kotlin-only width
// cases from md.Android's ViewModeTest.kt and the pins for the two places this port had to decide
// something the Swift could not tell it: the Windows path identity and the C# spelling of the trims.
public class ViewModeTests
{
    // MARK: The rule

    [Fact]
    public void OpenRuleTruthTable()
    {
        // A known file opens in exactly the mode it was left in, on any width, even when empty right now.
        foreach (var isWide in new[] { true, false })
        {
            foreach (var remembered in ViewModes.All)
            {
                Assert.Equal(remembered, ViewModeRule.OpenViewMode(remembered, isEmptyDocument: false, hasFileIdentity: true, isWide: isWide));
                Assert.Equal(remembered, ViewModeRule.OpenViewMode(remembered, isEmptyDocument: true, hasFileIdentity: true, isWide: isWide));
            }
        }

        // Nothing to read yet: Edit, whatever the width, with or without a file.
        foreach (var isWide in new[] { true, false })
        {
            Assert.Equal(ViewMode.Edit, ViewModeRule.OpenViewMode(null, isEmptyDocument: true, hasFileIdentity: false, isWide: isWide));
            Assert.Equal(ViewMode.Edit, ViewModeRule.OpenViewMode(null, isEmptyDocument: true, hasFileIdentity: true, isWide: isWide));
        }

        // Unknown with content: Split where Split is on offer, Preview only where it is not.
        Assert.Equal(ViewMode.Split, ViewModeRule.OpenViewMode(null, isEmptyDocument: false, hasFileIdentity: true, isWide: true));
        Assert.Equal(ViewMode.Preview, ViewModeRule.OpenViewMode(null, isEmptyDocument: false, hasFileIdentity: true, isWide: false));
        // hasFileIdentity never changes the answer — an example opened from the menu (content, no file) opens like a document.
        Assert.Equal(ViewMode.Split, ViewModeRule.OpenViewMode(null, isEmptyDocument: false, hasFileIdentity: false, isWide: true));
        Assert.Equal(ViewMode.Preview, ViewModeRule.OpenViewMode(null, isEmptyDocument: false, hasFileIdentity: false, isWide: false));
    }

    [Fact]
    public void RememberedSplitSurvivesANarrowOpen()
    {
        var store = new InMemoryViewModeStore();
        var identity = ViewModeMemory.IdentityFor(Path.Combine(Path.GetTempPath(), "md-viewmode-missing", "split.md"));
        ViewModeMemory.Remember(ViewMode.Split, identity, store);

        var remembered = ViewModeMemory.Lookup(identity, store);
        Assert.NotNull(remembered);
        var opened = ViewModeRule.OpenViewMode(remembered, isEmptyDocument: false, hasFileIdentity: true, isWide: false);
        Assert.Equal(ViewMode.Split, opened);
        Assert.Equal(ViewMode.Edit, ViewModeRule.EffectiveMode(opened, isWide: false));

        // Storing what the narrow window opened with must not downgrade the memory.
        ViewModeMemory.Remember(opened, identity, store);
        Assert.Equal(ViewMode.Split, ViewModeMemory.Lookup(identity, store));
        Assert.Equal(ViewMode.Split, ViewModeRule.EffectiveMode(opened, isWide: true));
    }

    // Kotlin-only: narrowOffersEditAndPreviewOnly, wideOffersAllThree, storedSplitCollapsesToEditWhenNarrow,
    // storedSplitStaysSplitWhenWide, editAndPreviewSurviveBothWidths, coercionRoundTripsWhenWindowWidensAgain.
    [Fact]
    public void WidthRuleOffersSplitOnlyWhereThereIsRoomAndCoercesWithoutDiscarding()
    {
        Assert.Equal(new[] { ViewMode.Edit, ViewMode.Preview }, ViewModeRule.AvailableModes(isWide: false));
        Assert.Equal(new[] { ViewMode.Edit, ViewMode.Split, ViewMode.Preview }, ViewModeRule.AvailableModes(isWide: true));
        Assert.Equal(ViewModes.All, ViewModeRule.AvailableModes(isWide: true));

        Assert.Equal(ViewMode.Edit, ViewModeRule.EffectiveMode(ViewMode.Split, isWide: false));
        Assert.Equal(ViewMode.Split, ViewModeRule.EffectiveMode(ViewMode.Split, isWide: true));
        foreach (var isWide in new[] { true, false })
        {
            Assert.Equal(ViewMode.Edit, ViewModeRule.EffectiveMode(ViewMode.Edit, isWide));
            Assert.Equal(ViewMode.Preview, ViewModeRule.EffectiveMode(ViewMode.Preview, isWide));
        }
        // The same stored value, narrow then wide: Split comes back because the preference was never rewritten.
        const ViewMode stored = ViewMode.Split;
        Assert.Equal(ViewMode.Edit, ViewModeRule.EffectiveMode(stored, isWide: false));
        Assert.Equal(ViewMode.Split, ViewModeRule.EffectiveMode(stored, isWide: true));
    }

    // MARK: Tokens

    [Fact]
    public void ModeTokensAreTheThreeLiterals()
    {
        Assert.Equal("edit", ViewModeMemory.Token(ViewMode.Edit));
        Assert.Equal("split", ViewModeMemory.Token(ViewMode.Split));
        Assert.Equal("preview", ViewModeMemory.Token(ViewMode.Preview));

        foreach (var mode in ViewModes.All)
        {
            var token = ViewModeMemory.Token(mode);
            Assert.Equal(mode, ViewModeMemory.ModeForToken(token));
            Assert.Equal(mode, ViewModeMemory.ModeForToken(token.ToUpperInvariant()));
            // Swift `capitalized`: first letter up, the rest down.
            Assert.Equal(mode, ViewModeMemory.ModeForToken(char.ToUpperInvariant(token[0]) + token[1..]));
        }

        // Whitespace is forgiven, exactly as the Android port's modeFromToken forgives it.
        Assert.Equal(ViewMode.Split, ViewModeMemory.ModeForToken("  Split "));
        // A token that names nothing is null, never fatal.
        Assert.Null(ViewModeMemory.ModeForToken(""));
        Assert.Null(ViewModeMemory.ModeForToken("zen"));
        Assert.Null(ViewModeMemory.ModeForToken("reader"));
    }

    [Fact]
    public void ModeForTokenTrimsFoundationWhitespaceAndFoldsAsciiCaseOnly()
    {
        // Foundation `.whitespaces`: Zs, TAB and U+200B are trimmed; line terminators are not.
        Assert.Equal(ViewMode.Edit, ViewModeMemory.ModeForToken("\tedit\u00A0"));
        Assert.Equal(ViewMode.Edit, ViewModeMemory.ModeForToken("\u200Bedit\u3000"));
        Assert.Equal(ViewMode.Edit, ViewModeMemory.ModeForToken("\u2003edit\u202F"));
        Assert.Null(ViewModeMemory.ModeForToken("edit\r"));
        Assert.Null(ViewModeMemory.ModeForToken("edit\n"));
        Assert.Null(ViewModeMemory.ModeForToken("\u180Eedit"));
        // Swift's lowercased() is a full mapping, so no non-ASCII letter ever spells one of the three words:
        // İ (U+0130) lowercases to i̇ and ı (U+0131) stays ı. .NET's invariant folding would accept both.
        Assert.Null(ViewModeMemory.ModeForToken("ED\u0130T"));
        Assert.Null(ViewModeMemory.ModeForToken("ed\u0131t"));
    }

    [Fact]
    public void RawValueParsesTheThreeSpellingsExactly()
    {
        foreach (var mode in ViewModes.All)
        {
            Assert.Equal(mode, ViewModes.FromRawValue(mode.RawValue()));
        }
        // Mode(rawValue:) is exact: the per-window default takes over for anything else.
        Assert.Null(ViewModes.FromRawValue("Split"));
        Assert.Null(ViewModes.FromRawValue(" split"));
        Assert.Null(ViewModes.FromRawValue(null));
        Assert.Equal(ViewMode.Split, ViewModes.FromRawValue(null) ?? ViewModes.WindowDefault);
        Assert.Equal("md.viewMode", ViewModes.WindowStateKey);
        Assert.Equal(new[] { "Edit", "Split", "Preview" }, ViewModes.All.Select(m => m.Label()));
        Assert.Equal(new[] { "1", "2", "3" }, ViewModes.All.Select(m => m.CommandKey()));
    }

    // MARK: Codec

    [Fact]
    public void CodecRoundTripsAndRejectsAForeignHeader()
    {
        var entries = new[]
        {
            new ViewModeMemory.Entry("53ba23f60734adf1", ViewMode.Preview),
            new ViewModeMemory.Entry("427542472354b900", ViewMode.Split),
            new ViewModeMemory.Entry("0123456789abcdef", ViewMode.Edit),
        };
        var encoded = ViewModeMemory.Encode(entries);
        Assert.Equal("v1\n53ba23f60734adf1 preview\n427542472354b900 split\n0123456789abcdef edit", encoded);
        Assert.Equal(entries, ViewModeMemory.Decode(encoded));
        // An empty memory is still a well-formed one.
        Assert.Equal("v1", ViewModeMemory.Encode([]));
        Assert.Empty(ViewModeMemory.Decode("v1"));

        // Line 0 must read exactly `v1`, or the value is treated as absent.
        Assert.Empty(ViewModeMemory.Decode(""));
        Assert.Empty(ViewModeMemory.Decode(null));
        Assert.Empty(ViewModeMemory.Decode("v2\n53ba23f60734adf1 edit"));
        Assert.Empty(ViewModeMemory.Decode(" v1\n53ba23f60734adf1 edit"));
        Assert.Empty(ViewModeMemory.Decode("53ba23f60734adf1 edit"));
        Assert.Empty(ViewModeMemory.Decode("{\"53ba23f60734adf1\":\"edit\"}"));

        // A malformed line is skipped, never fatal, and a repeated identity keeps only its newest (first) row.
        var survivor = new ViewModeMemory.Entry("53ba23f60734adf1", ViewMode.Edit);
        var malformed = string.Join("\n",
            "v1",
            "not-hex-at-all edit",
            "53ba23f60734adf1extra edit",
            "53BA23F60734ADF1 edit",
            "427542472354b900 zen",
            "427542472354b900",
            "427542472354b900 edit split",
            "",
            "53ba23f60734adf1 edit",
            "53ba23f60734adf1 preview");
        Assert.Equal(new[] { survivor }, ViewModeMemory.Decode(malformed));

        // The mode is parsed BEFORE the identity is marked seen — the exact value the Android suite pins.
        Assert.Equal(
            new[] { new ViewModeMemory.Entry("53ba23f60734adf1", ViewMode.Split) },
            ViewModeMemory.Decode("v1\n53ba23f60734adf1 zen\n53ba23f60734adf1 split"));
    }

    [Fact]
    public void DecodeSplitsLinesOnLfAndFieldsOnSpaceExactlyLikeSwift()
    {
        // Swift's split(separator: " ") drops empty fields: a double space is still two fields …
        Assert.Equal(
            new[] { new ViewModeMemory.Entry("53ba23f60734adf1", ViewMode.Edit) },
            ViewModeMemory.Decode("v1\n53ba23f60734adf1  edit"));
        Assert.Equal(
            new[] { new ViewModeMemory.Entry("53ba23f60734adf1", ViewMode.Edit) },
            ViewModeMemory.Decode("v1\n 53ba23f60734adf1 edit "));
        // … a tab is not a separator on any port …
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf1\tedit"));
        // … a trailing tab on the token is trimmed by mode(forToken:), a tab on the identity is not …
        Assert.Single(ViewModeMemory.Decode("v1\n53ba23f60734adf1 edit\t"));
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf1\t edit"));
        // … and a CRLF-written value reads as absent everywhere (the header line is "v1\r").
        Assert.Empty(ViewModeMemory.Decode("v1\r\n53ba23f60734adf1 edit"));
        // A CRLF past the header is where the ports part: Swift's "\r\n" is one Character, so the line never
        // splits and its three fields are skipped; Kotlin splits on the code unit and trims the "\r" away,
        // reading two entries. The Mac is the source of truth — nothing, not two, and never one.
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf1 edit\r\n427542472354b900 split"));
        // A lone CR is not a separator and not Foundation whitespace; a bare LF still splits.
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf1 edit\r427542472354b900 split"));
        Assert.Equal(2, ViewModeMemory.Decode("v1\n53ba23f60734adf1 edit\n427542472354b900 split").Count);
        // A space fused with a combining mark is one Character that is not " ", so it does not split a line.
        Assert.Empty(ViewModeMemory.Decode("v1\n53ba23f60734adf1 ́edit"));
        // No port writes uppercase, so the decoder does not take it.
        Assert.False(ViewModeMemory.IsIdentity("53BA23F60734ADF1"));
        Assert.False(ViewModeMemory.IsIdentity("53ba23f60734adf"));
        Assert.False(ViewModeMemory.IsIdentity("\uFF15\uFF13ba23f60734adf1"));
        Assert.True(ViewModeMemory.IsIdentity("0123456789abcdef"));
    }

    // MARK: MRU

    [Fact]
    public void TouchedIsMruAndTruncatesAtTwoHundred()
    {
        const string a = "aaaaaaaaaaaaaaaa", b = "bbbbbbbbbbbbbbbb", c = "cccccccccccccccc";
        IReadOnlyList<ViewModeMemory.Entry> list = [];
        list = ViewModeMemory.Touched(list, a, ViewMode.Edit);
        list = ViewModeMemory.Touched(list, b, ViewMode.Split);
        list = ViewModeMemory.Touched(list, c, ViewMode.Preview);
        Assert.Equal(new[] { c, b, a }, list.Select(e => e.Identity));

        // Touching a file already in the list moves it to the front and updates its mode — never a duplicate.
        list = ViewModeMemory.Touched(list, a, ViewMode.Preview);
        Assert.Equal(new[] { a, c, b }, list.Select(e => e.Identity));
        Assert.Equal(3, list.Count);
        Assert.Equal(ViewMode.Preview, list[0].Mode);

        // The cap: exactly 200 survive, and it is the oldest that falls off.
        IReadOnlyList<ViewModeMemory.Entry> big = [];
        for (var index = 0; index < ViewModeMemory.MaxEntries; index++)
        {
            big = ViewModeMemory.Touched(big, Identity(index), ViewMode.Edit);
        }
        Assert.Equal(ViewModeMemory.MaxEntries, big.Count);
        Assert.Equal(Identity(0), big[^1].Identity);

        big = ViewModeMemory.Touched(big, Identity(ViewModeMemory.MaxEntries), ViewMode.Split);
        Assert.Equal(ViewModeMemory.MaxEntries, big.Count);
        Assert.Equal(Identity(ViewModeMemory.MaxEntries), big[0].Identity);
        Assert.Equal(Identity(1), big[^1].Identity);
        Assert.DoesNotContain(big, e => e.Identity == Identity(0));

        // …and the same through the real store, round-tripped as text.
        var store = new InMemoryViewModeStore();
        for (var index = 0; index <= ViewModeMemory.MaxEntries; index++)
        {
            ViewModeMemory.Remember(ViewMode.Edit, Identity(index), store);
        }
        Assert.Equal(ViewModeMemory.MaxEntries, ViewModeMemory.Entries(store).Count);
        Assert.Null(ViewModeMemory.Lookup(Identity(0), store));
        Assert.Equal(ViewMode.Edit, ViewModeMemory.Lookup(Identity(ViewModeMemory.MaxEntries), store));
        // An unknown file stays unknown — that is what makes the open rule's first branch reachable at all.
        Assert.Null(ViewModeMemory.Lookup("ffffffffffffffff", store));
        Assert.Equal("md.viewModeMemory", ViewModeMemory.SettingsKey);
    }

    /// <summary>A distinct, well-formed identity per index: Swift <c>String(format: "%016x", index)</c>.</summary>
    private static string Identity(int index) => index.ToString("x16", CultureInfo.InvariantCulture);

    // MARK: The book exemption

    [Fact]
    public void BookArticleMarkIsClaimedExactlyOnce()
    {
        BookArticleOpens.Reset();
        var article = Path.Combine(Path.GetTempPath(), "md-book-" + Guid.NewGuid().ToString("N"), "ch1.md");
        var plain = Path.Combine(Path.GetTempPath(), "md-plain-" + Guid.NewGuid().ToString("N"), "notes.md");

        Assert.False(BookArticleOpens.ClaimOpen(article));

        BookArticleOpens.Mark(article);
        Assert.False(BookArticleOpens.ClaimOpen(plain));
        Assert.True(BookArticleOpens.ClaimOpen(article));
        Assert.False(BookArticleOpens.ClaimOpen(article));
    }

    [Fact]
    public void BookArticleMarkExpiresAfterTenSeconds()
    {
        var realClock = BookArticleOpens.Now;
        try
        {
            BookArticleOpens.Reset();
            var now = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
            BookArticleOpens.Now = () => now;
            var article = Path.Combine(Path.GetTempPath(), "md-book-" + Guid.NewGuid().ToString("N"), "ch2.md");

            Assert.Equal(TimeSpan.FromSeconds(10), BookArticleOpens.MarkLifetime);
            BookArticleOpens.Mark(article);
            // A mark exactly at the lifetime is gone: `pending.filter { $0.value > cutoff }`.
            now = now.AddSeconds(10);
            Assert.False(BookArticleOpens.ClaimOpen(article));

            BookArticleOpens.Mark(article);
            now = now.AddSeconds(9.999);
            Assert.True(BookArticleOpens.ClaimOpen(article));
        }
        finally
        {
            BookArticleOpens.Now = realClock;
            BookArticleOpens.Reset();
        }
    }

    // MARK: Navigation nudges

    [Fact]
    public void NavigationNudgeIsTransient()
    {
        Assert.Equal(ViewMode.Edit, ViewModeRule.NavigationNudge(ViewMode.Preview, wants: ViewMode.Edit));
        Assert.Equal(ViewMode.Preview, ViewModeRule.NavigationNudge(ViewMode.Edit, wants: ViewMode.Preview));
        Assert.Null(ViewModeRule.NavigationNudge(ViewMode.Edit, wants: ViewMode.Edit));
        Assert.Null(ViewModeRule.NavigationNudge(ViewMode.Preview, wants: ViewMode.Preview));
        Assert.Null(ViewModeRule.NavigationNudge(ViewMode.Split, wants: ViewMode.Edit));
        Assert.Null(ViewModeRule.NavigationNudge(ViewMode.Split, wants: ViewMode.Preview));

        var store = new InMemoryViewModeStore();
        var file = ViewModeMemory.IdentityFor(Path.Combine(Path.GetTempPath(), "md-viewmode-missing", "notes.md"));

        // A file the reader keeps in Preview, opened in a desktop window — always wide, never coerced.
        ViewModeMemory.Remember(ViewMode.Preview, file, store);
        var preferred = ViewModeRule.OpenViewMode(ViewModeMemory.Lookup(file, store), isEmptyDocument: false, hasFileIdentity: true, isWide: true);
        ViewMode? navigation = null;
        ViewMode Displayed() => ViewModeRule.DisplayedMode(preferred, navigation, isWide: true);
        Assert.Equal(ViewMode.Preview, Displayed());

        // Go ▸ Notes: the note has to be visible to be read, and reading it is not a preference.
        if (ViewModeRule.NavigationNudge(Displayed(), ViewMode.Edit) is { } nudge) navigation = nudge;
        Assert.Equal(ViewMode.Edit, Displayed());
        Assert.Equal(ViewMode.Preview, ViewModeMemory.Lookup(file, store));

        // A second note: already nudged, nothing changes — the override is not cleared out from under them.
        if (ViewModeRule.NavigationNudge(Displayed(), ViewMode.Edit) is { } second) navigation = second;
        Assert.Equal(ViewMode.Edit, Displayed());
        Assert.Equal(ViewMode.Preview, ViewModeMemory.Lookup(file, store));

        // A deliberate pick (⌘2): the nudge goes and the raw choice is what is persisted.
        navigation = null;
        preferred = ViewMode.Split;
        ViewModeMemory.Remember(preferred, file, store);
        Assert.Null(navigation);
        Assert.Equal(ViewMode.Split, ViewModeMemory.Lookup(file, store));
        Assert.Equal(ViewMode.Split, Displayed());

        // The narrow half: a raw Split displays as Edit, so a jump into the preview has a pane to bring on
        // screen — and Split survives in the store, where at that width it could never be re-picked.
        Assert.Equal(ViewMode.Edit, ViewModeRule.DisplayedMode(ViewMode.Split, null, isWide: false));
        Assert.Equal(ViewMode.Preview, ViewModeRule.NavigationNudge(ViewMode.Edit, ViewMode.Preview));
        Assert.Equal(ViewMode.Preview, ViewModeRule.DisplayedMode(ViewMode.Split, ViewMode.Preview, isWide: false));
        Assert.Equal(ViewMode.Split, ViewModeMemory.Lookup(file, store));

        // Opening another document drops the override; the Save-As migration deliberately does not.
        navigation = ViewMode.Edit;
        Assert.Equal(ViewMode.Edit, Displayed());
        navigation = null;
        Assert.Equal(ViewMode.Split, Displayed());
    }

    // MARK: Identity

    [Fact]
    public void IdentityMatchesTheSharedShaVectors()
    {
        // The two vectors from the spec — pure ASCII, so they catch a UTF-16-for-UTF-8 slip only together with the é case.
        Assert.Equal("53ba23f60734adf1", ViewModeMemory.Sha256Prefix("file:/tmp/a.md"));
        Assert.Equal("427542472354b900", ViewModeMemory.Sha256Prefix("saf:com.android.externalstorage.documents:primary:Documents/a.md"));
        Assert.Equal(16, ViewModeMemory.Sha256Prefix("file:/tmp/\u00E9.md").Length);
        Assert.NotEqual(ViewModeMemory.Sha256Prefix("file:/tmp/\u00E9.md"), ViewModeMemory.Sha256Prefix("file:/tmp/e.md"));
        // Kotlin's self-check: hashed as UTF-8, not the string's native UTF-16.
        var utf8 = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("saf:com.example:\u00E9\u4E2D"));
        Assert.Equal(Convert.ToHexStringLower(utf8.AsSpan(0, 8)), ViewModeMemory.Sha256Prefix("saf:com.example:\u00E9\u4E2D"));

        // IdentityFor is that hash over "file:" + the canonical path…
        var missing = Path.Combine(Path.GetTempPath(), "md-viewmode-missing-" + Guid.NewGuid().ToString("N"));
        var a = Path.Combine(missing, "a.md");
        var b = Path.Combine(missing, "b.md");
        Assert.True(ViewModeMemory.IsIdentity(ViewModeMemory.IdentityFor(a)));
        Assert.NotEqual(ViewModeMemory.IdentityFor(a), ViewModeMemory.IdentityFor(b));
        Assert.Equal(ViewModeMemory.Sha256Prefix("file:" + ViewModeMemory.CanonicalPath(a)), ViewModeMemory.IdentityFor(a));

        // …and the symlink resolution is load-bearing: the same document under two paths is one entry.
        // The file must exist — resolution is a no-op for a path that names nothing, on Foundation and here.
        var real = Path.Combine(Path.GetTempPath(), "md-viewmode-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(Path.GetTempPath(), "md-viewmode-link-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "a.md"), "# a\n");
        var linked = false;
        try
        {
            try
            {
                Directory.CreateSymbolicLink(link, real);
                linked = true;
            }
            catch (IOException) when (OperatingSystem.IsWindows())
            {
                // ERROR_PRIVILEGE_NOT_HELD: a Windows runner without Developer Mode cannot create symlinks.
                // Everything else in this case still runs; only the two-spellings assertion is skipped.
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            {
            }

            var produced = ViewModeMemory.IdentityFor(Path.Combine(real, "a.md"));
            if (linked)
            {
                Assert.Equal(produced, ViewModeMemory.IdentityFor(Path.Combine(link, "a.md")));
                Assert.Equal(ViewModeMemory.CanonicalPath(Path.Combine(real, "a.md")), ViewModeMemory.CanonicalPath(Path.Combine(link, "a.md")));
            }
            // A well-formed identity by the decoder's own standard, so anything IdentityFor produces round-trips.
            Assert.True(ViewModeMemory.IsIdentity(produced));
            Assert.Equal(
                new[] { new ViewModeMemory.Entry(produced, ViewMode.Split) },
                ViewModeMemory.Decode(ViewModeMemory.Encode([new ViewModeMemory.Entry(produced, ViewMode.Split)])));
        }
        finally
        {
            if (linked) Directory.Delete(link);
            Directory.Delete(real, recursive: true);
        }
    }

    [Fact]
    public void CanonicalPathCollapsesTheSpellingAndFoldsCaseOnlyWhenAsked()
    {
        var tmp = Path.GetTempPath();
        var stem = "md-viewmode-case-" + Guid.NewGuid().ToString("N");
        var plain = Path.Combine(tmp, stem, "MiXeD.md");
        var dotted = Path.Combine(tmp, stem, "Sub", "..", "MiXeD.md");
        var trailing = Path.Combine(tmp, stem, "MiXeD.md") + Path.DirectorySeparatorChar;

        // `..` and a trailing separator vanish (standardizedFileURL), nothing else changes for a path that names nothing.
        Assert.Equal(ViewModeMemory.CanonicalPath(plain, foldCase: false), ViewModeMemory.CanonicalPath(dotted, foldCase: false));
        Assert.Equal(ViewModeMemory.CanonicalPath(plain, foldCase: false), ViewModeMemory.CanonicalPath(trailing, foldCase: false));
        Assert.Equal(Path.GetFullPath(plain), ViewModeMemory.CanonicalPath(plain, foldCase: false));
        Assert.EndsWith("MiXeD.md", ViewModeMemory.CanonicalPath(plain, foldCase: false), StringComparison.Ordinal);

        // The Windows decision: fold case, so C:\Docs\A.md and c:\docs\a.md are one file with one memory.
        var upper = Path.Combine(tmp, stem, "MIXED.MD");
        Assert.Equal(ViewModeMemory.CanonicalPath(plain, foldCase: true), ViewModeMemory.CanonicalPath(upper, foldCase: true));
        Assert.Equal(ViewModeMemory.CanonicalPath(plain, foldCase: false).ToUpperInvariant(), ViewModeMemory.CanonicalPath(plain, foldCase: true));
        Assert.NotEqual(ViewModeMemory.CanonicalPath(plain, foldCase: false), ViewModeMemory.CanonicalPath(upper, foldCase: false));
        // The one-argument form picks the platform rule.
        Assert.Equal(
            ViewModeMemory.CanonicalPath(plain, foldCase: OperatingSystem.IsWindows()),
            ViewModeMemory.CanonicalPath(plain));
        Assert.Equal(
            OperatingSystem.IsWindows(),
            ViewModeMemory.IdentityFor(plain) == ViewModeMemory.IdentityFor(upper));
    }
}
