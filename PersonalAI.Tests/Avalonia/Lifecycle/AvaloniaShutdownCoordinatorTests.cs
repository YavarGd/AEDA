using System.Diagnostics;
using Avalonia.Threading;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Avalonia.Lifecycle;

public sealed class AvaloniaShutdownCoordinatorTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1,
        42,
        "notepad",
        "notes.txt - Notepad",
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task MultipleExitRequestsShareOneCleanupAndOneDesktopShutdown()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupCount = 0;
        var shutdownCount = 0;
        var exitCode = -1;
        async ValueTask CleanupAsync()
        {
            Interlocked.Increment(ref cleanupCount);
            await release.Task;
        }

        var coordinator = new AvaloniaShutdownCoordinator(
            CleanupAsync,
            code =>
            {
                Interlocked.Increment(ref shutdownCount);
                exitCode = code;
            });

        var first = coordinator.BeginAsync(7);
        var second = coordinator.BeginAsync(9);

        Assert.Same(first, second);
        Assert.Equal(1, Volatile.Read(ref cleanupCount));
        Assert.Equal(0, Volatile.Read(ref shutdownCount));
        release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal(1, Volatile.Read(ref shutdownCount));
        Assert.Equal(7, exitCode);
    }

    [Fact]
    public async Task DispatcherContinuationRequiredByDisposalRunsBeforeShutdown()
    {
        var continuation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var continuationRan = false;
        var shutdownObservedContinuation = false;
        ValueTask CleanupAsync()
        {
            Dispatcher.UIThread.Post(() =>
            {
                continuationRan = true;
                continuation.TrySetResult();
            });
            return new ValueTask(continuation.Task);
        }

        var coordinator = new AvaloniaShutdownCoordinator(
            CleanupAsync,
            _ => shutdownObservedContinuation = continuationRan);

        var shutdown = coordinator.BeginAsync();
        Assert.False(shutdown.IsCompleted);
        Dispatcher.UIThread.RunJobs();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(continuationRan);
        Assert.True(shutdownObservedContinuation);
    }

    [Fact]
    public async Task ExplicitExitWithNormalUiaCompletesCleanupBeforeShutdown()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) => "selected text");
        var capture = await provider.TryGetSelectedTextAsync(
            Foreground,
            PrivacySettings.Default,
            100,
            CancellationToken.None);
        var shutdownCalled = false;
        var coordinator = new AvaloniaShutdownCoordinator(
            provider.DisposeAsync,
            _ => shutdownCalled = true);

        await coordinator.BeginAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(capture.IsAvailable);
        Assert.True(shutdownCalled);
    }

    [Fact]
    public async Task ExplicitExitWithHungQuarantinedUiaCompletesWithinBound()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            started.TrySetResult();
            release.Wait();
            completed.TrySetResult();
            return "late";
        }, TimeSpan.FromMilliseconds(40));

        try
        {
            var capture = provider.TryGetSelectedTextAsync(
                Foreground,
                PrivacySettings.Default,
                100,
                CancellationToken.None);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.False((await capture).IsAvailable);

            var shutdownCalled = false;
            var coordinator = new AvaloniaShutdownCoordinator(
                provider.DisposeAsync,
                _ => shutdownCalled = true);
            var stopwatch = Stopwatch.StartNew();

            await coordinator.BeginAsync().WaitAsync(TimeSpan.FromSeconds(1));

            Assert.True(shutdownCalled);
            Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        }
        finally
        {
            release.Set();
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public async Task CleanupTimeoutBoundsDesktopShutdown()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shutdownCalled = false;
        var coordinator = new AvaloniaShutdownCoordinator(
            () => new ValueTask(release.Task),
            _ => shutdownCalled = true,
            TimeSpan.FromMilliseconds(50));
        var stopwatch = Stopwatch.StartNew();

        await coordinator.BeginAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.True(shutdownCalled);
        Assert.InRange(stopwatch.Elapsed, TimeSpan.FromMilliseconds(20), TimeSpan.FromSeconds(1));
        release.TrySetResult();
    }
}
