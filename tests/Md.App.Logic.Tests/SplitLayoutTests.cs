using Md.App.Logic.View;
using Md.Core.Document;

namespace Md.App.Logic.Tests;

/// <summary>shell-design.md §5.3 — the mode picks the layout and Split picks on width alone at 640 epx.</summary>
public sealed class SplitLayoutTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(639.9)]
    [InlineData(1400)]
    public void EditIsAlwaysEditorOnly(double width) =>
        Assert.Equal(PaneLayout.EditorOnly, SplitLayout.Arrange(ViewMode.Edit, width));

    [Theory]
    [InlineData(0)]
    [InlineData(639.9)]
    [InlineData(1400)]
    public void PreviewIsAlwaysPreviewOnly(double width) =>
        Assert.Equal(PaneLayout.PreviewOnly, SplitLayout.Arrange(ViewMode.Preview, width));

    [Theory]
    [InlineData(639.9, PaneLayout.Stacked)]
    [InlineData(640.0, PaneLayout.SideBySide)]
    [InlineData(640.1, PaneLayout.SideBySide)]
    [InlineData(0, PaneLayout.Stacked)]
    public void SplitTurnsSideBySideAtSixHundredAndForty(double width, PaneLayout expected) =>
        Assert.Equal(expected, SplitLayout.Arrange(ViewMode.Split, width));

    [Fact]
    public void AWidthThatIsNotYetKnownStacks()
    {
        Assert.Equal(PaneLayout.Stacked, SplitLayout.Arrange(ViewMode.Split, double.NaN));
        Assert.Equal(PaneLayout.Stacked, SplitLayout.Arrange(ViewMode.Split, -1));
    }

    [Fact]
    public void OnlyTheCollapsedPaneIsHidden()
    {
        Assert.True(SplitLayout.ShowsEditor(PaneLayout.EditorOnly));
        Assert.False(SplitLayout.ShowsPreview(PaneLayout.EditorOnly));
        Assert.False(SplitLayout.ShowsEditor(PaneLayout.PreviewOnly));
        Assert.True(SplitLayout.ShowsPreview(PaneLayout.PreviewOnly));

        foreach (var split in new[] { PaneLayout.SideBySide, PaneLayout.Stacked })
        {
            Assert.True(SplitLayout.ShowsEditor(split));
            Assert.True(SplitLayout.ShowsPreview(split));
        }
    }

    [Fact]
    public void TheDividerIsDrawnOnlyBetweenTwoVisiblePanes()
    {
        Assert.False(SplitLayout.ShowsDivider(PaneLayout.EditorOnly));
        Assert.False(SplitLayout.ShowsDivider(PaneLayout.PreviewOnly));
        Assert.True(SplitLayout.ShowsDivider(PaneLayout.SideBySide));
        Assert.True(SplitLayout.ShowsDivider(PaneLayout.Stacked));
    }
}
