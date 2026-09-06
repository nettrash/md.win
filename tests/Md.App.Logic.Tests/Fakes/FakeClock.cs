namespace Md.App.Logic.Tests.Fakes;

/// <summary>A clock that moves only when told. Shared by <see cref="FakeScheduler"/> and <see cref="FakeFileSystem"/> when a test hands both the same instance.</summary>
public sealed class FakeClock : IClock
{
    public static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; } = Epoch;

    public void Advance(TimeSpan by) => Now += by;
}
