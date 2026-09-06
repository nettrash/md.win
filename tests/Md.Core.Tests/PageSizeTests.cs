using Md.Core.Document;

namespace Md.Core.Tests;

// Port of the three PageSize cases in md.macOS/mdTests/mdTests.swift plus the exact seven paddings the
// Kotlin suite pins (the Swift pins A4 and Letter and "strictly smaller" for the four small pages).
public class PageSizeTests
{
    [Fact]
    public void PageSizeTableHasTheAgreedPointDimensions()
    {
        static (double, double) Dims(string id)
        {
            var size = PageSize.Named(id);
            return (size.Width, size.Height);
        }

        Assert.Equal((595.2, 841.8), Dims("a4"));
        Assert.Equal((419.5, 595.3), Dims("a5"));
        Assert.Equal((612, 792), Dims("letter"));
        Assert.Equal((612, 1008), Dims("legal"));
        Assert.Equal((432, 648), Dims("6x9"));
        Assert.Equal((360, 576), Dims("5x8"));
        Assert.Equal((396, 612), Dims("5.5x8.5"));

        // The offered list is exactly these seven, A4 first — the default.
        Assert.Equal(new[] { "a4", "a5", "letter", "legal", "6x9", "5x8", "5.5x8.5" }, PageSize.All.Select(s => s.Id));
        Assert.Equal(PageSize.A4, PageSize.All[0]);
        Assert.Same(PageSize.A4, PageSize.All[0]);

        // Labels are the menu titles — the imperial ones carry a real × (U+00D7) and a straight inch mark.
        Assert.Equal("A4", PageSize.A4.Label);
        Assert.Equal("US Letter", PageSize.UsLetter.Label);
        Assert.Equal("US Legal", PageSize.UsLegal.Label);
        Assert.Equal("6 × 9\"", PageSize.SixByNine.Label);
        Assert.Equal("5 × 8\"", PageSize.FiveByEight.Label);
        Assert.Equal("5.5 × 8.5\"", PageSize.Digest.Label);
    }

    [Fact]
    public void PageSizePreferenceRoundTripsAndDefaultsToA4()
    {
        foreach (var size in PageSize.All)
        {
            Assert.Equal(size, PageSize.Named(size.Id));
            Assert.Same(size, PageSize.Named(size.Id));
        }
        // An empty (first launch), absent or unknown (stale / future-version) key falls back to A4 — never null.
        Assert.Equal(PageSize.A4, PageSize.Named(""));
        Assert.Equal(PageSize.A4, PageSize.Named(null));
        Assert.Equal(PageSize.A4, PageSize.Named("tabloid"));
        Assert.Equal(PageSize.A4, PageSize.Named("a6"));
        // Ids are lower-case; the wrong case is unknown (Kotlin pins this).
        Assert.Equal(PageSize.A4, PageSize.Named("A4"));
        Assert.Equal("a4", PageSize.DefaultId);
        Assert.Equal("md.pdfPageSize", PageSize.PreferenceKey);
    }

    [Fact]
    public void A4MarginIsUnchangedAndSmallerPagesScaleTheMarginDown()
    {
        // A4 reproduces the historical body margin to the pixel, so an A4 export carries the exact CSS it always did.
        Assert.Equal("48px 56px", PageSize.A4.CssPadding);

        // Every smaller page gets a strictly smaller margin on both axes.
        foreach (var size in new[] { PageSize.A5, PageSize.SixByNine, PageSize.FiveByEight, PageSize.Digest })
        {
            var parts = size.CssPadding.Split(' ');
            Assert.Equal(2, parts.Length);
            var vertical = int.Parse(parts[0][..^2], System.Globalization.CultureInfo.InvariantCulture);
            var horizontal = int.Parse(parts[1][..^2], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(vertical < 48, size.Id + " vertical margin should shrink");
            Assert.True(horizontal < 56, size.Id + " horizontal margin should shrink");
            Assert.True(vertical > 0);
            Assert.True(horizontal > 0);
        }
        // Letter is wider than A4, so its horizontal margin grows — proportional per axis, not a blanket cap.
        Assert.Equal("45px 58px", PageSize.UsLetter.CssPadding);

        // The exact seven, as the Kotlin suite pins them.
        Assert.Equal("34px 39px", PageSize.A5.CssPadding);
        Assert.Equal("57px 58px", PageSize.UsLegal.CssPadding);
        Assert.Equal("37px 41px", PageSize.SixByNine.CssPadding);
        Assert.Equal("33px 34px", PageSize.FiveByEight.CssPadding);
        Assert.Equal("35px 37px", PageSize.Digest.CssPadding);
    }
}
