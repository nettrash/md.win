using Md.App.Logic.Settings;
using Md.App.Logic.Windows;

namespace Md.App.Logic.Tests;

/// <summary>Sizes, the "WxH" round trip, the cascade and the clamp (shell-design.md §1.3, §9).</summary>
public sealed class WindowPlacementTests
{
    static readonly WindowRect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void TheDefaultsAreTheDesignsAndMatchTheSettingsKeysDefaults()
    {
        Assert.Equal(new WindowSize(900, 640), WindowPlacement.DocumentDefault);
        Assert.Equal(new WindowSize(1000, 700), WindowPlacement.BookDefault);
        Assert.Equal(new WindowSize(480, 320), WindowPlacement.Minimum);
        Assert.Equal(24, WindowPlacement.CascadeStep);

        Assert.Equal(SettingsKeys.DocumentWindowSizeDefault, WindowPlacement.FormatSize(WindowPlacement.DocumentDefault));
        Assert.Equal(SettingsKeys.BookWindowSizeDefault, WindowPlacement.FormatSize(WindowPlacement.BookDefault));
    }

    [Theory]
    [InlineData("900x640", 900, 640)]
    [InlineData("1000x700", 1000, 700)]
    [InlineData("1920x1080", 1920, 1080)]
    public void SizesRoundTripThroughTheStoredSpelling(string stored, int width, int height)
    {
        var size = WindowPlacement.ParseSize(stored, WindowPlacement.DocumentDefault);
        Assert.Equal(new WindowSize(width, height), size);
        Assert.Equal(stored, WindowPlacement.FormatSize(size));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("900")]
    [InlineData("900x")]
    [InlineData("x640")]
    [InlineData("900X640")]      // the codec is ordinal: an upper-case X is not the spelling we write
    [InlineData("900 x 640")]
    [InlineData("-900x640")]
    [InlineData("0x640")]
    [InlineData("nine hundred")]
    [InlineData("900x640x480")]
    public void AnUnreadableStoredSizeFallsBackRatherThanMakingAWindowlessApp(string? stored) =>
        Assert.Equal(WindowPlacement.DocumentDefault, WindowPlacement.ParseSize(stored, WindowPlacement.DocumentDefault));

    [Fact]
    public void AStoredSizeBelowTheMinimumIsRaisedToIt()
    {
        Assert.Equal(WindowPlacement.Minimum, WindowPlacement.ParseSize("100x100", WindowPlacement.DocumentDefault));
    }

    [Fact]
    public void TheFirstWindowOfARunIsCentred()
    {
        var rect = WindowPlacement.Cascade(null, WindowPlacement.DocumentDefault, Screen);
        Assert.Equal(new WindowRect((1920 - 900) / 2, (1080 - 640) / 2, 900, 640), rect);
    }

    [Fact]
    public void EachNextWindowStepsTwentyFourRightAndDown()
    {
        var first = WindowPlacement.Cascade(null, WindowPlacement.DocumentDefault, Screen);
        var second = WindowPlacement.Cascade(first, WindowPlacement.DocumentDefault, Screen);

        Assert.Equal(first.X + 24, second.X);
        Assert.Equal(first.Y + 24, second.Y);
        Assert.Equal(first.Size, second.Size);
    }

    [Fact]
    public void TheCascadeRestartsAtTheBaseWhenTheNextStepWouldLeaveTheWorkArea()
    {
        var almostOff = new WindowRect(1000, 420, 900, 640);           // +24 would cross the right edge
        var next = WindowPlacement.Cascade(almostOff, WindowPlacement.DocumentDefault, Screen);
        Assert.Equal(WindowPlacement.Cascade(null, WindowPlacement.DocumentDefault, Screen), next);
    }

    [Fact]
    public void TheCascadeWrapsOnlyWhenTheStepWouldACTUALLYCrossTheEdge()
    {
        // The boundary itself, both sides of it. A window whose stepped frame ends flush with the
        // work area is still inside it and must NOT restart the cascade — an off-by-one here throws
        // the user back to the centre one window early, every time, on every screen size.
        var flush = new WindowRect(1920 - 900 - 24, 1080 - 640 - 24, 900, 640);
        var stepped = WindowPlacement.Cascade(flush, WindowPlacement.DocumentDefault, Screen);
        Assert.Equal(new WindowRect(1920 - 900, 1080 - 640, 900, 640), stepped);
        Assert.Equal(Screen.Right, stepped.Right);
        Assert.Equal(Screen.Bottom, stepped.Bottom);

        // One pixel further and the step would cross: now it wraps.
        var overByOne = new WindowRect(flush.X + 1, flush.Y, 900, 640);
        Assert.Equal(
            WindowPlacement.Cascade(null, WindowPlacement.DocumentDefault, Screen),
            WindowPlacement.Cascade(overByOne, WindowPlacement.DocumentDefault, Screen));
    }

    [Fact]
    public void ARestoredFrameFromAMonitorThatIsGoneIsBroughtBackOnScreen()
    {
        // Saved on a second monitor to the right that is no longer attached.
        var offscreen = new WindowRect(3200, 200, 900, 640);
        var clamped = WindowPlacement.Clamp(offscreen, Screen);

        Assert.Equal(new WindowRect(1920 - 900, 200, 900, 640), clamped);
    }

    [Fact]
    public void AFrameLargerThanTheWorkAreaShrinksToItAndSitsAtItsOrigin()
    {
        var work = new WindowRect(0, 0, 800, 600);
        Assert.Equal(new WindowRect(0, 0, 800, 600), WindowPlacement.Clamp(new WindowRect(-100, -100, 2000, 1500), work));
    }

    [Fact]
    public void ClampingRespectsAWorkAreaThatDoesNotStartAtTheOrigin()
    {
        // A taskbar on the left, or a second monitor whose origin is negative.
        var work = new WindowRect(-1920, 40, 1920, 1000);
        Assert.Equal(new WindowRect(-1920, 40, 900, 640), WindowPlacement.Clamp(new WindowRect(-4000, -500, 900, 640), work));
        Assert.Equal(new WindowRect(-1920 + 1920 - 900, 40 + 1000 - 640, 900, 640), WindowPlacement.Clamp(new WindowRect(9000, 9000, 900, 640), work));
    }

    [Fact]
    public void ACascadedWindowIsAlwaysInsideTheWorkArea()
    {
        var work = new WindowRect(0, 40, 1280, 960);
        WindowRect? previous = null;
        for (var i = 0; i < 40; i++)
        {
            var rect = WindowPlacement.Cascade(previous, WindowPlacement.DocumentDefault, work);
            Assert.True(rect.X >= work.X && rect.Y >= work.Y && rect.Right <= work.Right && rect.Bottom <= work.Bottom,
                $"window {i} at {rect} left {work}");
            previous = rect;
        }
    }
}
