namespace Md.App.Logic.Commands;

/// <summary>
/// One per window: the handlers behind the ids, the enablement gate, and the double-fire guard.
///
/// The guard is the reason this class exists rather than a dictionary of delegates. WinUI's
/// <c>WebView2</c> forwards accelerator keys to XAML, and microsoft-ui-xaml #6231 documents the
/// <c>KeyboardAccelerator</c> firing <b>twice</b>, roughly 100 ms apart, while the preview has focus —
/// which would open two pickers or print two copies. A second invocation of the same command inside
/// <see cref="DoubleFireWindow"/> is therefore swallowed and reported as *handled*, so nothing else
/// acts on it either. 150 ms is above the observed gap and far below a human repeat.
///
/// No timer: the window is measured against the last invocation that was actually let through, so a
/// held-down chord repeats at 150 ms rather than being locked out for as long as it is held.
/// </summary>
/// <param name="clock">Wall clock; the App passes <c>DispatcherScheduler</c>, tests a <c>FakeClock</c>.</param>
/// <param name="snapshot">Reads the window's current <see cref="ShellSnapshot"/> — called on every invocation, never cached.</param>
public sealed class CommandDispatcher(Seams.IClock clock, Func<ShellSnapshot> snapshot)
{
    /// <summary>The per-command debounce of §2.9.</summary>
    public static readonly TimeSpan DoubleFireWindow = TimeSpan.FromMilliseconds(150);

    readonly Dictionary<CommandId, Action<object?>> _handlers = [];
    readonly Dictionary<CommandId, DateTimeOffset> _lastInvoked = [];

    /// <summary>The window's snapshot as of now.</summary>
    public ShellSnapshot Snapshot => snapshot();

    /// <summary>
    /// Wires a command. Registering the same id twice replaces the handler rather than throwing — a
    /// window that rebuilds its wiring must not have to unwire first.
    /// </summary>
    public void Register(CommandId id, Action<object?> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[id] = handler;
    }

    /// <summary>The argument-free form, for the many commands that take none.</summary>
    public void Register(CommandId id, Action handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        Register(id, _ => handler());
    }

    /// <summary>True when the command is both wired and enabled — what a menu row's <c>IsEnabled</c> reads.</summary>
    public bool CanInvoke(CommandId id) => _handlers.ContainsKey(id) && CommandEnablement.IsEnabled(id, snapshot());

    /// <summary>
    /// Runs the command if it is wired and enabled.
    /// </summary>
    /// <param name="argument">
    /// Which row of a dynamic command: an MRU token (<see cref="CommandId.OpenRecentEntry"/>), an
    /// example file name, an <c>OutlineEntry</c> / <c>NoteEntry</c>, a diagram ordinal, a
    /// <c>PageSize.Id</c>, a window <see cref="Guid"/>. Null for the rest.
    /// </param>
    /// <returns>
    /// What a <c>KeyboardAccelerator.Invoked</c> handler should put in <c>args.Handled</c>: false when
    /// nothing was wired or the command is disabled (the key falls through to the focused control),
    /// true when it ran <b>and</b> when it was swallowed as a double fire.
    /// </returns>
    public bool TryInvoke(CommandId id, object? argument = null)
    {
        if (!_handlers.TryGetValue(id, out var handler)) return false;
        if (!CommandEnablement.IsEnabled(id, snapshot())) return false;

        var now = clock.Now;
        if (_lastInvoked.TryGetValue(id, out var last))
        {
            var since = now - last;
            if (since >= TimeSpan.Zero && since < DoubleFireWindow) return true;
        }

        // Stamped before the handler runs: a command that opens a modal dialog must not let the
        // duplicate through while it is awaited.
        _lastInvoked[id] = now;
        handler(argument);
        return true;
    }

    /// <summary>A menu row's <c>Click</c>: the same path as a chord, minus the caller's interest in the answer.</summary>
    public void Execute(CommandId id, object? argument = null) => TryInvoke(id, argument);

    /// <summary>Forgets the debounce history — for tests, and for a window that has been idle behind a modal.</summary>
    public void ResetDebounce() => _lastInvoked.Clear();
}
