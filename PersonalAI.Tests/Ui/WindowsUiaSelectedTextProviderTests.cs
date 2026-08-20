using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Windows;

namespace PersonalAI.Tests.Ui;

[Collection("NativeUiAutomation")]
public sealed class WindowsUiaSelectedTextProviderTests
{
    private static readonly ActiveWindowReference Foreground = new(
        1, 42, "notepad", "notes.txt - Notepad", DateTimeOffset.UtcNow);

    [Fact]
    public async Task ValidSelectionIsTrustedAndBounded()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) => "selected text");

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 8, CancellationToken.None);

        Assert.True(result.IsAvailable);
        Assert.True(result.IsTrustedForImmediateSubmission);
        Assert.Equal("selected", result.Text);
        Assert.Equal("notepad", result.ApplicationIdentity);
    }

    [Fact]
    public async Task EmptyOrUnsupportedSelectionFallsBackSafely()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) => null);

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.False(result.IsTrustedForImmediateSubmission);
        Assert.Null(result.Text);
    }

    [Theory]
    [InlineData("1Password", "Vault")]
    [InlineData("msedge", "InPrivate - Microsoft Edge")]
    public async Task SensitiveApplicationsAreBlockedBeforeAutomation(
        string processName,
        string title)
    {
        var invoked = false;
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            invoked = true;
            return "secret";
        });

        var result = await provider.TryGetSelectedTextAsync(
            Foreground with { ProcessName = processName, WindowTitle = title },
            PrivacySettings.Default,
            100,
            CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.False(invoked);
    }

    [Fact]
    public async Task SlowAutomationTimesOutToFallback()
    {
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            Thread.Sleep(1_000);
            return "late";
        });

        var result = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(result.IsAvailable);
        Assert.Contains("timed out", result.SafeFailureReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HungAutomationIsQuarantinedUntilItCompletes()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        await using var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            var invocation = Interlocked.Increment(ref invocations);
            started.TrySetResult();
            if (invocation == 1)
            {
                release.Wait();
                completed.TrySetResult();
                return "late";
            }

            return "fresh";
        }, TimeSpan.FromMilliseconds(30));

        var first = provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False((await first).IsAvailable);

        for (var request = 0; request < 55; request++)
        {
            var result = await provider.TryGetSelectedTextAsync(
                Foreground, PrivacySettings.Default, 100, CancellationToken.None);
            Assert.False(result.IsAvailable);
        }

        Assert.Equal(1, Volatile.Read(ref invocations));
        release.Set();
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var recovered = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);
        Assert.True(recovered.IsAvailable);
        Assert.Equal("fresh", recovered.Text);
        Assert.Equal(2, invocations);
    }

    [Fact]
    public async Task CancellationBeforeStartingDoesNotInvokeAutomation()
    {
        var invocations = 0;
        await using var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            Interlocked.Increment(ref invocations);
            return "selection";
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            provider.TryGetSelectedTextAsync(
                Foreground, PrivacySettings.Default, 100, cancellation.Token));

        Assert.Equal(0, invocations);
    }

    [Fact]
    public async Task CancellationWhileAwaitingDoesNotStartReplacementWorker()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        await using var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            Interlocked.Increment(ref invocations);
            started.TrySetResult();
            release.Wait();
            return "late";
        }, TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var capture = provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);
        var next = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(next.IsAvailable);
        Assert.Equal(1, invocations);
        release.Set();
    }

    [Fact]
    public async Task DisposalStopsNewWorkAndDoesNotWaitForeverForNativeCall()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var invocations = 0;
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            Interlocked.Increment(ref invocations);
            started.TrySetResult();
            release.Wait();
            return "late";
        }, TimeSpan.FromMilliseconds(30));
        var capture = provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        await provider.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
        var afterDispose = await provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);

        Assert.False(afterDispose.IsAvailable);
        Assert.Equal(1, invocations);
        release.Set();
        await capture;
    }

    [Fact]
    public async Task DisposalAwaitsNormallyCompletingOwnedOperation()
    {
        using var release = new ManualResetEventSlim();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new WindowsUiaSelectedTextProvider((_, _) =>
        {
            started.TrySetResult();
            release.Wait();
            return "selection";
        }, TimeSpan.FromSeconds(2));
        var capture = provider.TryGetSelectedTextAsync(
            Foreground, PrivacySettings.Default, 100, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disposal = provider.DisposeAsync().AsTask();
        Assert.False(disposal.IsCompleted);
        release.Set();
        await disposal.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True((await capture).IsAvailable);
    }
}
