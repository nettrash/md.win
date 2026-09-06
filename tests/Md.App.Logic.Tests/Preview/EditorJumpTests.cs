using Md.App.Logic.Preview;

namespace Md.App.Logic.Tests.Preview;

/// <summary>
/// The one-shot rule both halves of a Contents / Notes pick obey (§3.3, §5.6): the request is the
/// identity, not the destination, so the same heading or note can be picked twice in a row — and a
/// pane rebuilt by Split → Edit → Split never replays the last one.
/// </summary>
public class EditorJumpTests
{
    [Fact]
    public void AJumpIsClaimedOnce()
    {
        var tracker = new EditorJumpTracker();
        var jump = new EditorJump(Guid.NewGuid(), 12);

        Assert.True(tracker.Claim(jump));
        Assert.False(tracker.Claim(jump));
        Assert.False(tracker.Claim(new EditorJump(jump.Id, 99)));   // the id is the identity
    }

    [Fact]
    public void TheSameLineTwiceIsTwoJumps()
    {
        var tracker = new EditorJumpTracker();

        Assert.True(tracker.Claim(new EditorJump(Guid.NewGuid(), 12)));
        Assert.True(tracker.Claim(new EditorJump(Guid.NewGuid(), 12)));
    }

    [Fact]
    public void NoRequestIsNeverAJump()
    {
        var tracker = new EditorJumpTracker();
        Assert.False(tracker.Claim(null));
    }

    [Fact]
    public void ARebuiltTrackerStartsClean()
    {
        // The counterpart of the coordinator's dedupe: a request the owner has not cleared is
        // performed again by a fresh pane, which is why the owner clears it in OnJumpHandled.
        var jump = new EditorJump(Guid.NewGuid(), 3);
        Assert.True(new EditorJumpTracker().Claim(jump));
        Assert.True(new EditorJumpTracker().Claim(jump));
    }
}
