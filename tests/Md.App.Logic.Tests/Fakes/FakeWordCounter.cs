namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IWordCounter"/>: by default the number of whitespace-separated runs that contain a
/// letter or digit (enough for scheduler and footer tests; the real counters have their own
/// vectors); <see cref="Override"/> scripts any other answer. Calls are recorded.
/// </summary>
public sealed class FakeWordCounter : IWordCounter
{
    public List<string> Calls { get; } = [];
    public Func<string, int>? Override { get; set; }

    public int Count(string text)
    {
        Calls.Add(text);
        if (Override is { } f) return f(text);
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit));
    }
}
