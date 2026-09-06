using Md.App.Logic.Export;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The measurement the EPUB pairs its images by. The two rules here are the difference between a
/// correct book and one where every figure after the first is on the wrong page.
/// </summary>
public class RichRectsTests
{
    [Fact]
    public void FiveNumberRowsBecomeRectsInTheOrderThePageGaveThem()
    {
        var rects = RichRects.Parse("[[10,20,30,40,1],[50,60,70,80,0]]");

        Assert.Equal(
            [new RichRect(new RectD(10, 20, 30, 40), true), new RichRect(new RectD(50, 60, 70, 80), false)],
            rects);
    }

    [Fact]
    public void ADegenerateRectIsKeptBecauseDroppingItShiftsEveryLaterImage()
    {
        var rects = RichRects.Parse("[[0,0,0,0,0],[1,2,3,4,1]]");

        Assert.Equal(2, rects.Count);
        Assert.Equal(new RectD(0, 0, 0, 0), rects[0].Rect);
    }

    [Fact]
    public void ARowThatIsNotFiveNumbersIsDroppedRatherThanGuessedAt()
    {
        // The Mac's `count != 5` guard: four columns is iOS's shape, six is nobody's.
        Assert.Empty(RichRects.Parse("[[1,2,3,4]]"));
        Assert.Empty(RichRects.Parse("[[1,2,3,4,5,6]]"));
        Assert.Empty(RichRects.Parse("[[1,2,3,4,\"1\"]]"));
        Assert.Empty(RichRects.Parse("[[1,2,3,4,null]]"));
        Assert.Empty(RichRects.Parse("[\"nonsense\"]"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]                 // the page went away, or the script threw
    [InlineData("{\"els\":[]}")]         // Android's shape, which this port does not use
    [InlineData("[[1,2,3,")]             // truncated
    public void AnythingThatIsNotAnArrayOfRowsIsNoMeasurementAtAll(string? json)
    {
        Assert.Empty(RichRects.Parse(json));
    }

    [Fact]
    public void ANonFiniteNumberIsNotAMeasurement()
    {
        // JSON has no NaN literal, but a page can still answer with something unusable.
        Assert.Empty(RichRects.Parse("[[1,2,3,4,1e400]]"));
    }

    [Fact]
    public void TheFifthColumnIsWhetherItIsAFormula()
    {
        var rects = RichRects.Parse("[[0,0,1,1,1],[0,0,1,1,0]]");

        Assert.True(rects[0].IsMath);
        Assert.False(rects[1].IsMath);
    }
}
