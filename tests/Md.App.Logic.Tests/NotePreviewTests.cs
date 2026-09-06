using Md.App.Logic;
using Md.App.Logic.Text;

namespace Md.App.Logic.Tests;

/// <summary>
/// shell-design.md §5.6 — the Go ▸ Notes label. Four steps, each of which the Swift does in a way a
/// naive C# port gets wrong: the first line is the first NON-EMPTY one, a line of spaces still
/// counts as that line, whitespace collapses on Unicode White_Space, and the 50 is grapheme
/// clusters, not UTF-16 units. Every non-ASCII character is written as an escape so an editor or a
/// normalising tool cannot quietly change what is being pinned.
/// </summary>
public sealed class NotePreviewTests
{
    const string Acute = "\u0301";                             // COMBINING ACUTE ACCENT
    const string EAcute = "e\u0301";                           // one cluster, two UTF-16 units
    const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";  // one cluster, eleven units
    const string Ellipsis = "\u2026";

    [Theory]
    [InlineData("")]
    [InlineData("\n\n\n")]
    [InlineData("   ")]
    [InlineData("   \n fix me")]                              // a line of spaces IS the first line; collapsing it empties it
    [InlineData("\u2003\u00A0")]
    public void ANoteWithNothingOnItsFirstLineIsTheEmptyNote(string text) =>
        Assert.Equal("(empty note)", NotePreview.Of(text));

    [Fact]
    public void TheEmptyNoteLiteralComesFromStrings()
    {
        Assert.Equal(Strings.EmptyNote, NotePreview.Of(""));
        Assert.Equal(Ellipsis, Strings.Ellipsis);
    }

    [Fact]
    public void LeadingBlankLinesAreSkipped() => Assert.Equal("fix me", NotePreview.Of("\n\n\nfix me\nand more"));

    [Fact]
    public void OnlyTheFirstLineIsUsed() => Assert.Equal("one", NotePreview.Of("one\ntwo\nthree"));

    /// <summary>Character.isNewline: the seven Foundation terminators, and CRLF as one of them.</summary>
    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\u000B")]
    [InlineData("\u000C")]
    [InlineData("\u0085")]
    [InlineData("\u2028")]
    [InlineData("\u2029")]
    public void EveryLineTerminatorSwiftKnowsEndsTheFirstLine(string terminator) =>
        Assert.Equal("one", NotePreview.Of("one" + terminator + "two"));

    [Fact]
    public void CarriageReturnLineFeedIsOneSeparatorAndNotTwo() =>
        Assert.Equal("one", NotePreview.Of("\r\n\r\none\r\ntwo"));

    [Fact]
    public void WhitespaceRunsCollapseToOneSpaceAndTheEndsLoseTheirs() =>
        Assert.Equal("a b c", NotePreview.Of("  a \t  b c   "));

    [Fact]
    public void CollapsingUsesUnicodeWhiteSpaceAndNotJustSpaceAndTab() =>
        Assert.Equal("a b", NotePreview.Of("a\u2003\u00A0b"));

    [Fact]
    public void FiftyTextElementsAreKeptWhole()
    {
        var fifty = new string('a', 50);

        Assert.Equal(fifty, NotePreview.Of(fifty));
        Assert.Equal(fifty + Ellipsis, NotePreview.Of(fifty + "b"));
    }

    [Fact]
    public void TheCutIsTrimmedBeforeTheEllipsisIsAdded()
    {
        // Element 50 is the space between the two words, so the cut ends in one.
        var text = new string('a', 49) + " bcd";

        Assert.Equal(new string('a', 49) + Ellipsis, NotePreview.Of(text));
    }

    [Fact]
    public void CombiningMarksCountAsOneElementEach()
    {
        var text = string.Concat(Enumerable.Repeat(EAcute, 60));

        var preview = NotePreview.Of(text);

        Assert.Equal(string.Concat(Enumerable.Repeat(EAcute, 50)) + Ellipsis, preview);
        Assert.Equal(101, preview.Length);                      // 50 x 2 units plus the ellipsis
    }

    [Fact]
    public void EmojiCountAsOneElementEach()
    {
        var text = string.Concat(Enumerable.Repeat(Family, 60));

        var preview = NotePreview.Of(text);

        Assert.Equal(string.Concat(Enumerable.Repeat(Family, 50)) + Ellipsis, preview);
        Assert.Equal(551, preview.Length);                      // 50 x 11 units plus the ellipsis
    }

    [Fact]
    public void ASpaceWearingACombiningMarkIsNotASeparator()
    {
        // Swift splits on Characters, so a cluster whose base is a space is not whitespace; Md.Core's
        // own view-mode codec splits the same way for the same reason.
        var text = "a " + Acute + "b";

        Assert.Equal(text, NotePreview.Of(text));
    }
}
