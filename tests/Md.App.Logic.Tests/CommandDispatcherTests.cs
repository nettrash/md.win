using Md.App.Logic.Commands;

namespace Md.App.Logic.Tests;

/// <summary>
/// The dispatcher's three jobs (§2.9): run what is wired, refuse what is disabled so the chord falls
/// through to the focused control, and swallow the WebView2 double fire — microsoft-ui-xaml #6231,
/// two <c>Invoked</c>s about 100 ms apart while the preview has focus.
/// </summary>
public class CommandDispatcherTests
{
    readonly FakeScheduler _scheduler = new();
    ShellSnapshot _snapshot = ShellSnapshot.Empty with { HasDocument = true };

    CommandDispatcher NewDispatcher() => new(_scheduler.Clock, () => _snapshot);

    [Fact]
    public void RunsAWiredEnabledCommandAndReportsItHandled()
    {
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        Assert.True(dispatcher.TryInvoke(CommandId.Save));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void TwoInvocationsTenMillisecondsApartRunOnce()
    {
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        Assert.True(dispatcher.TryInvoke(CommandId.Save));
        _scheduler.Advance(TimeSpan.FromMilliseconds(10));
        // Handled, so nothing else acts on the duplicate either — that is the whole point.
        Assert.True(dispatcher.TryInvoke(CommandId.Save));

        Assert.Equal(1, runs);
    }

    [Fact]
    public void TwoInvocationsTwoHundredMillisecondsApartRunTwice()
    {
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        dispatcher.TryInvoke(CommandId.Save);
        _scheduler.Advance(TimeSpan.FromMilliseconds(200));
        dispatcher.TryInvoke(CommandId.Save);

        Assert.Equal(2, runs);
    }

    [Fact]
    public void TheWindowIsExactlyOneHundredAndFiftyMilliseconds()
    {
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        dispatcher.TryInvoke(CommandId.Save);
        _scheduler.Advance(TimeSpan.FromMilliseconds(149));
        dispatcher.TryInvoke(CommandId.Save);
        Assert.Equal(1, runs);

        _scheduler.Advance(TimeSpan.FromMilliseconds(1));
        dispatcher.TryInvoke(CommandId.Save);
        Assert.Equal(2, runs);
        Assert.Equal(TimeSpan.FromMilliseconds(150), CommandDispatcher.DoubleFireWindow);
    }

    [Fact]
    public void TheWindowIsPerCommandNotGlobal()
    {
        var saves = 0;
        var prints = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => saves++);
        dispatcher.Register(CommandId.Print, () => prints++);

        dispatcher.TryInvoke(CommandId.Save);
        _scheduler.Advance(TimeSpan.FromMilliseconds(10));
        dispatcher.TryInvoke(CommandId.Print);

        Assert.Equal(1, saves);
        Assert.Equal(1, prints);
    }

    [Fact]
    public void ASuppressedDuplicateDoesNotExtendTheWindow()
    {
        // Held-down keys repeat; the guard measures from the last invocation that ran, so a chord
        // held for a second still repeats rather than locking out.
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        dispatcher.TryInvoke(CommandId.Save);                       // t=0, runs
        _scheduler.Advance(TimeSpan.FromMilliseconds(100));
        dispatcher.TryInvoke(CommandId.Save);                       // t=100, swallowed
        _scheduler.Advance(TimeSpan.FromMilliseconds(60));
        dispatcher.TryInvoke(CommandId.Save);                       // t=160, 160 ms since the last run

        Assert.Equal(2, runs);
    }

    [Fact]
    public void AnUnwiredCommandIsNotHandledSoTheKeyReachesTheControl()
    {
        var dispatcher = NewDispatcher();
        Assert.False(dispatcher.TryInvoke(CommandId.Save));
        Assert.False(dispatcher.CanInvoke(CommandId.Save));
    }

    [Fact]
    public void ADisabledCommandIsNotHandledAndNeverRuns()
    {
        var runs = 0;
        _snapshot = ShellSnapshot.Empty;                            // no document
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        Assert.False(dispatcher.CanInvoke(CommandId.Save));
        Assert.False(dispatcher.TryInvoke(CommandId.Save));
        Assert.Equal(0, runs);
    }

    [Fact]
    public void TheSnapshotIsReadOnEveryInvocationNeverCached()
    {
        var runs = 0;
        _snapshot = ShellSnapshot.Empty;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);
        Assert.False(dispatcher.TryInvoke(CommandId.Save));

        _snapshot = ShellSnapshot.Empty with { HasDocument = true };
        Assert.True(dispatcher.TryInvoke(CommandId.Save));
        Assert.Equal(1, runs);
    }

    [Fact]
    public void ADisabledInvocationLeavesTheGuardAloneSoTheNextEnabledOneRunsImmediately()
    {
        var runs = 0;
        _snapshot = ShellSnapshot.Empty;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        dispatcher.TryInvoke(CommandId.Save);                       // refused
        _snapshot = ShellSnapshot.Empty with { HasDocument = true };
        Assert.True(dispatcher.TryInvoke(CommandId.Save));          // same instant, but nothing ran before
        Assert.Equal(1, runs);
    }

    [Fact]
    public void TheArgumentReachesTheHandlerUnchanged()
    {
        object? seen = null;
        _snapshot = ShellSnapshot.Empty with { RecentEntries = [new RecentEntry("tok", "notes.md", "folder")] };
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.OpenRecentEntry, argument => seen = argument);

        Assert.True(dispatcher.TryInvoke(CommandId.OpenRecentEntry, "tok"));
        Assert.Equal("tok", seen);
    }

    [Fact]
    public void RegisteringTwiceReplacesTheHandler()
    {
        var first = 0;
        var second = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => first++);
        dispatcher.Register(CommandId.Save, () => second++);

        dispatcher.TryInvoke(CommandId.Save);

        Assert.Equal(0, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public void ExecuteIsTryInvokeForAMenuClickAndIsDebouncedTheSameWay()
    {
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () => runs++);

        dispatcher.Execute(CommandId.Save);
        dispatcher.Execute(CommandId.Save);
        Assert.Equal(1, runs);

        dispatcher.ResetDebounce();
        dispatcher.Execute(CommandId.Save);
        Assert.Equal(2, runs);
    }

    [Fact]
    public void TheGuardIsStampedBeforeTheHandlerRunsSoAReentrantFireIsSwallowed()
    {
        // A command that opens a modal dialog pumps messages; the duplicate accelerator can arrive
        // while the handler is still on the stack.
        var runs = 0;
        var dispatcher = NewDispatcher();
        dispatcher.Register(CommandId.Save, () =>
        {
            runs++;
            if (runs < 5) dispatcher.TryInvoke(CommandId.Save);
        });

        dispatcher.TryInvoke(CommandId.Save);

        Assert.Equal(1, runs);
    }

    [Fact]
    public void TheDispatcherPublishesTheWindowsSnapshotForTheMenuBuilder()
    {
        var dispatcher = NewDispatcher();
        Assert.Equal(_snapshot, dispatcher.Snapshot);
        _snapshot = _snapshot with { IsDirty = true };
        Assert.Equal(_snapshot, dispatcher.Snapshot);
    }

    [Fact]
    public void EveryRootAcceleratorCanBeWiredAndInvoked()
    {
        // What AcceleratorInstaller does on the window root, once: nothing in the table is missing a
        // handler slot, and no chord is registered for a command the focused control owns.
        _snapshot = ShellSnapshot.Empty with
        {
            HasDocument = true, IsSaved = true, IsDirty = true, EditorVisible = true, CanUndo = true, CanRedo = true,
            HasBook = true, HasSelection = true, HasFindQuery = true, ZenActive = true, IsBookWindow = false,
        };
        var dispatcher = NewDispatcher();
        var seen = new List<CommandId>();
        foreach (var spec in CommandTable.RootAccelerators)
        {
            var id = spec.Id;
            dispatcher.Register(id, () => seen.Add(id));
        }

        foreach (var spec in CommandTable.RootAccelerators) dispatcher.TryInvoke(spec.Id);

        Assert.DoesNotContain(CommandTable.RootAccelerators, s => !s.RootAccelerator);
        Assert.Contains(CommandId.Find, seen);
        Assert.Contains(CommandId.Escape, seen);
        Assert.DoesNotContain(CommandId.Copy, seen);
    }
}
