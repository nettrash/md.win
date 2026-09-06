namespace Md.App.Logic.Seams;

/// <summary>
/// Wall clock for the debounces (the 150 ms command double-fire guard, the 250 ms derived-text
/// tick). App: <c>DateTimeOffset.UtcNow</c> behind <c>DispatcherScheduler</c>; tests: <c>FakeClock</c>.
/// FROZEN — shell-final.md §13.2; every work package codes against this shape.
/// </summary>
public interface IClock
{
    DateTimeOffset Now { get; }
}
