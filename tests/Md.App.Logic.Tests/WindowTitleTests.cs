using Md.App.Logic;
using Md.App.Logic.Windows;

namespace Md.App.Logic.Tests;

/// <summary>The title and the "— Edited" proxy (shell-design.md §6.7), and the per-process untitled numbering.</summary>
public sealed class WindowTitleTests
{
    [Fact]
    public void ASavedDocumentIsItsNameAndADirtyOneCarriesTheEmDashEdited()
    {
        Assert.Equal("Notes", WindowTitle.For("Notes", isDirty: false));
        Assert.Equal("Notes — Edited", WindowTitle.For("Notes", isDirty: true));
    }

    [Fact]
    public void TheSuffixIsTheMacsBytes()
    {
        // U+2014 EM DASH with a space either side; the same constant Strings publishes.
        Assert.Equal(" — Edited", WindowTitle.EditedSuffix);
        Assert.Equal(Strings.EditedSuffix, WindowTitle.EditedSuffix);
    }

    [Fact]
    public void AnExampleShowsUntitledEdited()
    {
        // §6.7: an Example opens untitled and dirty.
        Assert.Equal("Untitled — Edited", WindowTitle.For(WindowTitle.Untitled(1), isDirty: true));
    }

    [Theory]
    [InlineData(1, "Untitled")]
    [InlineData(2, "Untitled 2")]
    [InlineData(11, "Untitled 11")]
    public void UntitledWindowsAreNumberedFromTheSecond(int ordinal, string expected) =>
        Assert.Equal(expected, WindowTitle.Untitled(ordinal));

    [Fact]
    public void TheBookWindowIsTheBooksNameOrBookAndAppendsTheArticleBeingEdited()
    {
        Assert.Equal("Book", WindowTitle.ForBook(null, null));
        Assert.Equal("Book", WindowTitle.ForBook("", ""));
        Assert.Equal("My Book", WindowTitle.ForBook("My Book", null));
        Assert.Equal("My Book — Preface", WindowTitle.ForBook("My Book", "Preface"));
    }

    [Fact]
    public void TheLowestFreeUntitledNumberIsHandedOut()
    {
        var names = new UntitledNames();
        Assert.Equal("Untitled", names.TakeName(out var first));
        Assert.Equal("Untitled 2", names.TakeName(out var second));
        Assert.Equal("Untitled 3", names.TakeName(out _));

        names.Release(second);
        Assert.Equal("Untitled 2", names.TakeName(out _));

        names.Release(first);
        Assert.Equal("Untitled", names.TakeName(out _));
    }
}
