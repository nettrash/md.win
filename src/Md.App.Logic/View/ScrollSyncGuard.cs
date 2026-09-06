using Md.App.Logic.Seams;

namespace Md.App.Logic.View;

/// <summary>
/// "Is this scroll event our own echo?" for the editor half of the sync (shell-design.md §3.4).
/// AppKit posts the bounds-changed notification synchronously, so the Mac needs nothing but a
/// flag around the programmatic scroll; if WinUI's <c>ScrollViewer.ViewChanged</c> turns out to
/// arrive a turn later, the flag alone would let the echo through and the two panes would fight.
/// The timestamp window is the contingency, the same shape as the 300 ms the preview's injected
/// script already uses.
/// </summary>
public sealed class ScrollSyncGuard
{
    /// <summary>50 ms — long enough for a deferred <c>ViewChanged</c>, far short of a human scroll gesture.</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(50);

    readonly IClock _clock;
    readonly TimeSpan _window;
    DateTimeOffset? _lastApplied;
    int _depth;

    /// <param name="window">
    /// <see cref="TimeSpan.Zero"/> reduces the guard to the plain flag (the Mac's behaviour, and
    /// what the app ships with until a Windows run proves <c>ViewChanged</c> is asynchronous).
    /// </param>
    public ScrollSyncGuard(IClock clock, TimeSpan? window = null)
    {
        _clock = clock;
        _window = window ?? TimeSpan.Zero;
    }

    /// <summary>True while a programmatic scroll is being applied.</summary>
    public bool IsApplying => _depth > 0;

    /// <summary>
    /// Wrap the <c>ChangeView</c> call: <c>using (guard.Applying()) sv.ChangeView(…)</c>. Re-entrant,
    /// so a nested apply cannot clear the flag early.
    /// </summary>
    public IDisposable Applying()
    {
        _depth++;
        _lastApplied = _clock.Now;
        return new Scope(this);
    }

    /// <summary>
    /// True when a <c>ViewChanged</c> arriving now is our own doing: either the apply is still in
    /// flight, or it finished inside the window.
    /// </summary>
    public bool IsEcho
    {
        get
        {
            if (_depth > 0) return true;
            if (_window <= TimeSpan.Zero || _lastApplied is not { } applied) return false;
            return _clock.Now - applied < _window;
        }
    }

    sealed class Scope(ScrollSyncGuard owner) : IDisposable
    {
        bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            owner._depth--;
            owner._lastApplied = owner._clock.Now;
        }
    }
}
