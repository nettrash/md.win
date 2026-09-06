using Md.Core.Book;

namespace Md.Core.Tests;

/// <summary>
/// The ordering-prefix grammar (mdTests: testRenamedNameKeepsPrefixAndExtension,
/// testDisplayNameStripsPrefixAndExtension) plus the Kotlin BookOrderTest vectors for
/// the same surface, and the two names where Kotlin contradicts Swift — pinned to the
/// Swift answer so the decision is visible.
/// </summary>
public class BookNamingTests
{
    [Fact]
    public void DisplayNameStripsPrefixAndExtension()
    {
        Assert.Equal("Preface", BookNaming.DisplayName("01-Preface.md"));
        Assert.Equal("Getting Started", BookNaming.DisplayName("02-Getting Started"));
        // An all-number name has nothing else to show — it stays whole.
        Assert.Equal("01", BookNaming.DisplayName("01.md"));
    }

    [Fact]
    public void RenamedNameKeepsPrefixAndExtension()
    {
        Assert.Equal("01-Final.md", BookNaming.RenamedName("01-Draft.md", "Final"));
        // A loose prefix is preserved as written — only a Move normalises it.
        Assert.Equal("2. config.markdown", BookNaming.RenamedName("2. setup.markdown", "config"));
        Assert.Equal("journal.md", BookNaming.RenamedName("notes.md", "journal"));
        Assert.Equal("02-Basics", BookNaming.RenamedName("02-Getting Started", "Basics"));
    }

    [Fact]
    public void KotlinEditableNamesAgree()
    {
        Assert.Equal("The Storm", BookNaming.DisplayName("02-The Storm.md"));
        Assert.Equal("notes", BookNaming.DisplayName("notes.txt"));
        Assert.Equal("chapter.one", BookNaming.DisplayName("01-chapter.one.md"));
        // A chapter folder is no article: its dot is part of the name.
        Assert.Equal("v1.2", BookNaming.DisplayName("v1.2"));
    }

    [Fact]
    public void KotlinRenamesAgree()
    {
        Assert.Equal("02-New.md", BookNaming.RenamedName("02-Old.md", "New"));
        Assert.Equal("ideas.txt", BookNaming.RenamedName("notes.txt", "ideas"));
        Assert.Equal("03-Finale", BookNaming.RenamedName("03-Part", "Finale"));
        Assert.Equal("Ideas", BookNaming.RenamedName("Drafts", "Ideas"));
    }

    [Fact]
    public void ArticleNamesAreTheThreeExtensionsInAnyCase()
    {
        Assert.True(BookNaming.IsArticleName("intro.md"));
        Assert.True(BookNaming.IsArticleName("intro.markdown"));
        Assert.True(BookNaming.IsArticleName("notes.txt"));
        Assert.True(BookNaming.IsArticleName("SHOUTY.MD"));
        Assert.False(BookNaming.IsArticleName("cover.png"));
        Assert.False(BookNaming.IsArticleName("draft.md.bak"));
        Assert.False(BookNaming.IsArticleName("no-extension"));
        // Swift: a dot at position 0 is not an extension separator.
        Assert.False(BookNaming.IsArticleName(".md"));
    }

    [Fact]
    public void SplitExtensionKeepsCaseAndLeavesFolderDotsAlone()
    {
        Assert.Equal(("Notes", "MD"), BookNaming.SplitExtension("Notes.MD"));
        Assert.Equal("Notes-2.MD", BookNaming.RenamedName("Notes.MD", "Notes-2"));
        Assert.Equal(("v1.2", ""), BookNaming.SplitExtension("v1.2"));
        Assert.Equal(("Notes.bak", ""), BookNaming.SplitExtension("Notes.bak"));
        Assert.Equal(("notes.", ""), BookNaming.SplitExtension("notes."));
        Assert.Equal(("", ""), BookNaming.SplitExtension(""));
    }

    [Fact]
    public void SplitPrefixFollowsTheSwiftGrammar()
    {
        Assert.Equal(("01-", "intro"), BookNaming.SplitPrefix("01-intro"));
        Assert.Equal(("2. ", "setup"), BookNaming.SplitPrefix("2. setup"));
        Assert.Equal(("1) ", "intro"), BookNaming.SplitPrefix("1) intro"));
        Assert.Equal(("4 ", "finale"), BookNaming.SplitPrefix("4 finale"));
        Assert.Equal(("01-.) ", "odd"), BookNaming.SplitPrefix("01-.) odd"));
        // No digits, or nothing left after them: no prefix.
        Assert.Equal(("", "appendix"), BookNaming.SplitPrefix("appendix"));
        Assert.Equal(("", "2026"), BookNaming.SplitPrefix("2026"));
        Assert.Equal(("", "01-"), BookNaming.SplitPrefix("01-"));
        Assert.Equal(("", ""), BookNaming.SplitPrefix(""));
        // ASCII digits only — Arabic-Indic three (U+0663) is a letter of the name.
        Assert.Equal(("", "\u0663-arabic"), BookNaming.SplitPrefix("\u0663-arabic"));
    }

    [Fact]
    public void SplitPrefixSidesWithSwiftWhereKotlinDiffers()
    {
        // Swift needs no separator: "3rd party" is numbered 3 with stem "rd party"
        // (Kotlin: unnumbered). Swift's separator set has ")" and not "_": "3_draft"
        // is prefix "3", stem "_draft" (Kotlin: prefix "3_").
        Assert.Equal(("3", "rd party"), BookNaming.SplitPrefix("3rd party"));
        Assert.Equal(("3", "_draft"), BookNaming.SplitPrefix("3_draft"));
        Assert.Equal("rd party", BookNaming.DisplayName("3rd party.md"));
        Assert.Equal("01-rd party.md", RenumberPlan.RenumberedName("3rd party.md", 1));
    }

    [Fact]
    public void DigitCarryingACombiningMarkIsNotAnOrderingDigit()
    {
        // Swift walks graphemes: "1" + U+0301 is one non-ASCII Character, so the
        // digit run is empty; "12" + U+0301 has the run "1".
        Assert.Equal(("", "1\u0301-x"), BookNaming.SplitPrefix("1\u0301-x"));
        Assert.Equal(("1", "2\u0301-x"), BookNaming.SplitPrefix("12\u0301-x"));
        Assert.Null(BookOrder.LeadingNumber("1\u0301-x"));
        Assert.Equal(1L, BookOrder.LeadingNumber("12\u0301-x"));
    }
}
