namespace Md.App.Logic.Tests.Export;

/// <summary>
/// Removes the ambient <see cref="SynchronizationContext"/> for the length of a test, and puts it
/// back afterwards.
/// </summary>
/// <remarks>
/// The export path is asynchronous but never concurrent: everything runs on the UI thread and every
/// wait goes through <c>IScheduler</c>, so a test that advances a fake scheduler should see the flow
/// resume before the next line. That only holds when a completion source's continuation is allowed
/// to run inline, and the runtime refuses to inline whenever the current synchronization context is
/// anything but the default one — which under xUnit it always is. Without this the tests would be
/// timing races dressed up as assertions.
///
/// It must be constructed <b>inside the test method</b>: xUnit installs its own context after the
/// test class is built, so a field initialiser would run too early and be overwritten.
/// </remarks>
sealed class NoSyncContext : IDisposable
{
    readonly SynchronizationContext? _outer = SynchronizationContext.Current;

    public NoSyncContext() => SynchronizationContext.SetSynchronizationContext(null);

    public void Dispose() => SynchronizationContext.SetSynchronizationContext(_outer);
}
