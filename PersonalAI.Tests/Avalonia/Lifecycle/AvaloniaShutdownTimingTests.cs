using System.Diagnostics;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Avalonia.Lifecycle;

[Collection(TimingSensitiveCollection.Name)]
public sealed class AvaloniaShutdownTimingTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1,
        42,
        "notepad",
        "notes.txt - Notepad",
        DateTimeOffset.UtcNow);

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
}
