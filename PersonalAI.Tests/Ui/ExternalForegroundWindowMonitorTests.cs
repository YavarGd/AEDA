using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Context;

namespace PersonalAI.Tests.Ui;

[Collection("NativeUiAutomation")]
public sealed class ExternalForegroundWindowMonitorTests
{
    [Fact]
    public async Task RepeatedStartOwnsOneLoopAndDisposalAwaitsActiveTick()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticks = 0;
        var monitor = new ExternalForegroundWindowMonitor(
            new ForegroundWindowTracker(() => PrivacySettings.Default),
            () => 0,
            TimeSpan.FromMilliseconds(1),
            () =>
            {
                Interlocked.Increment(ref ticks);
                started.TrySetResult();
                release.Wait();
            });

        for (var attempt = 0; attempt < 50; attempt++)
        {
            monitor.Start();
        }

        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposal = monitor.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        Assert.Equal(1, Volatile.Read(ref ticks));

        release.Set();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));
        var ticksAfterDispose = Volatile.Read(ref ticks);
        monitor.Start();
        await Task.Delay(20);

        Assert.Equal(ticksAfterDispose, Volatile.Read(ref ticks));
    }
}
