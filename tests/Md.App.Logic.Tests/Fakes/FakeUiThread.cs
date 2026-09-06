namespace Md.App.Logic.Tests.Fakes;

/// <summary><see cref="IUiThread"/> that runs posts inline (the default) or queues them for <see cref="RunPosted"/>.</summary>
public sealed class FakeUiThread : IUiThread
{
    readonly Queue<Action> _posted = new();

    public bool RunInline { get; set; } = true;
    public bool IsCurrent { get; set; } = true;
    public int PostCount { get; private set; }
    public int PendingPosts => _posted.Count;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        PostCount++;
        if (RunInline) action();
        else _posted.Enqueue(action);
    }

    public int RunPosted()
    {
        var ran = 0;
        while (_posted.Count > 0) { _posted.Dequeue()(); ran++; }
        return ran;
    }
}
