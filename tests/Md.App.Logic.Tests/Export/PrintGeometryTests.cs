using Md.App.Logic.Export;
using Md.Core.Document;

namespace Md.App.Logic.Tests.Export;

/// <summary>
/// The one piece of arithmetic between the family's shared point table and
/// <c>CoreWebView2PrintSettings</c>, which takes inches — plus the margin, which is a decision this
/// port made and the only number in the export path that no other port would recognise.
/// </summary>
public class PrintGeometryTests
{
    [Fact]
    public void InchesArePointsOverSeventyTwoForEverySizeInTheTable()
    {
        foreach (var size in PageSize.All)
        {
            var geometry = PrintGeometry.For(size);
            Assert.Equal(size.Width / 72.0, geometry.PageWidthIn, 10);
            Assert.Equal(size.Height / 72.0, geometry.PageHeightIn, 10);
        }
    }

    [Fact]
    public void A4AndSixByNineAreTheNumbersTheSelfTestLooksForInTheMediaBox()
    {
        var a4 = PrintGeometry.For(PageSize.A4);
        Assert.Equal(595.2 / 72.0, a4.PageWidthIn, 10);
        Assert.Equal(841.8 / 72.0, a4.PageHeightIn, 10);

        // 6 × 9 inches is exactly that, which is why it is the second size the PDF check uses.
        Assert.Equal(new PrintGeometry(6, 9, 0.5), PrintGeometry.For(PageSize.SixByNine));
    }

    [Fact]
    public void EverySideGetsHalfAnInchAndThatIsTheDefault()
    {
        Assert.Equal(0.5, PrintGeometry.DefaultMarginIn);
        Assert.All(PageSize.All, size => Assert.Equal(0.5, PrintGeometry.For(size).MarginIn));

        // The seam's own default agrees, so a geometry built by hand cannot lose the margin.
        Assert.Equal(0.5, new PrintGeometry(8.5, 11).MarginIn);
    }

    [Fact]
    public void PrintIsAlwaysA4WhateverTheExportPageSizeSays()
    {
        Assert.Equal(PrintGeometry.For(PageSize.A4), PrintGeometry.Paper);
    }

    [Fact]
    public void ANullSizeIsARefusalRatherThanAZeroSizedPage()
    {
        Assert.Throws<ArgumentNullException>(() => PrintGeometry.For(null!));
    }
}
