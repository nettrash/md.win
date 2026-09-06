namespace Md.App.Logic.Tests.Fakes;

/// <summary><see cref="IShare"/> that records what was shared; <see cref="Failure"/> makes every call throw it.</summary>
public sealed class FakeShare : IShare
{
    public List<(string Path, string Title)> Shared { get; } = [];
    public Exception? Failure { get; set; }

    public Task ShareFileAsync(string path, string title)
    {
        if (Failure is { } f) return Task.FromException(f);
        Shared.Add((path, title));
        return Task.CompletedTask;
    }
}
