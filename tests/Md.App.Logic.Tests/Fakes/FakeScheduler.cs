namespace Md.App.Logic.Tests.Fakes;

/// <summary>
/// <see cref="IScheduler"/> with manual time. <see cref="Advance"/> fires every timer whose due time
/// falls inside the window, earliest first (registration order breaks ties), moving the clock to
/// each due time before running it — so a timer that a callback schedules is fired in the same call
/// when it also falls due, as a real dispatcher timer would. Posted actions run only on
/// <see cref="RunPosted"/>, FIFO, including ones posted while draining.
/// </summary>
public sealed class FakeScheduler : IScheduler
{
    sealed class Timer(long seq, DateTimeOffset due, Action action) : IDisposable
    {
        public long Seq => seq;
        public DateTimeOffset Due => due;
        public Action Action => action;
        public bool Cancelled { get; private set; }
        public void Dispose() => Cancelled = true;
    }

    readonly List<Timer> _timers = [];
    readonly Queue<Action> _posted = new();
    long _seq;

    public FakeScheduler() : this(new FakeClock()) { }
    public FakeScheduler(FakeClock clock) => Clock = clock;

    public FakeClock Clock { get; }
    public DateTimeOffset Now => Clock.Now;

    public int PendingTimers => _timers.Count(t => !t.Cancelled);
    public int PendingPosts => _posted.Count;

    /// <summary>Delays every live timer still waits for, relative to now — for "the debounce was restarted" assertions.</summary>
    public IReadOnlyList<TimeSpan> PendingDelays =>
        _timers.Where(t => !t.Cancelled).OrderBy(t => t.Due).ThenBy(t => t.Seq).Select(t => t.Due - Now).ToList();

    public IDisposable After(TimeSpan delay, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var timer = new Timer(_seq++, Now + (delay < TimeSpan.Zero ? TimeSpan.Zero : delay), action);
        _timers.Add(timer);
        return timer;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _posted.Enqueue(action);
    }

    /// <summary>Move the clock forward, firing due timers on the way.</summary>
    public void Advance(TimeSpan by)
    {
        if (by < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(by));
        var target = Now + by;
        while (true)
        {
            var next = _timers.Where(t => !t.Cancelled && t.Due <= target).OrderBy(t => t.Due).ThenBy(t => t.Seq).FirstOrDefault();
            if (next is null) break;
            _timers.Remove(next);
            if (next.Due > Now) Clock.Now = next.Due;
            next.Action();
        }
        _timers.RemoveAll(t => t.Cancelled);
        Clock.Now = target;
    }

    /// <summary>Fire everything that is due right now (a zero-length advance).</summary>
    public void RunDue() => Advance(TimeSpan.Zero);

    /// <summary>Drain the posted queue, FIFO; actions posted while draining run in the same call.</summary>
    public int RunPosted()
    {
        var ran = 0;
        while (_posted.Count > 0)
        {
            if (++ran > 10_000) throw new InvalidOperationException("Post loop: an action keeps re-posting itself");
            _posted.Dequeue()();
        }
        return ran;
    }
}
