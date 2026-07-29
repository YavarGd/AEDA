using PersonalAI.Desktop.Avalonia.Views.Chat;

namespace PersonalAI.Tests.Avalonia.Views;

public sealed class AvaloniaChatConversationLoadQueueTests
{
    [Fact]
    public async Task SecondRequest_DoesNotStartUntilFirstHasFinished()
    {
        var queue = new ChatConversationLoadQueue();
        var order = new List<string>();
        var firstStarted = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        var first = queue.Enqueue(
            async _ =>
            {
                order.Add("first-start");
                firstStarted.SetResult();
                await releaseFirst.Task;
                order.Add("first-end");
            },
            () => order.Add("first-abandoned"));

        await firstStarted.Task;

        var second = queue.Enqueue(
            _ =>
            {
                order.Add("second-start");
                return Task.CompletedTask;
            },
            () => order.Add("second-abandoned"));

        // The second request must still be waiting behind the first.
        Assert.DoesNotContain("second-start", order);

        releaseFirst.SetResult();
        await first;
        await second;

        // First cancels (it was in flight when the second arrived) but the second only
        // ever runs after the first has fully unwound.
        Assert.Equal("second-start", order[^1]);
        Assert.True(
            order.IndexOf("second-start") > order.IndexOf("first-start"),
            "the newer request must publish after the older one");
    }

    [Fact]
    public async Task EnqueueingNewRequest_CancelsTheOneInFlight()
    {
        var queue = new ChatConversationLoadQueue();
        var firstStarted = new TaskCompletionSource();
        var firstObservedCancellation = false;
        var firstAbandoned = false;

        var first = queue.Enqueue(
            async token =>
            {
                firstStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                catch (OperationCanceledException)
                {
                    firstObservedCancellation = true;
                    throw;
                }
            },
            () => firstAbandoned = true);

        await firstStarted.Task;

        var second = queue.Enqueue(_ => Task.CompletedTask, () => { });

        await first;
        await second;

        Assert.True(firstObservedCancellation, "the in-flight load must receive cancellation");
        Assert.False(
            firstAbandoned,
            "being superseded is not a reason to touch visible state; the newer request owns it");
    }

    [Fact]
    public async Task OrdinaryFailure_IsContainedAndReported()
    {
        var queue = new ChatConversationLoadQueue();
        var abandoned = false;

        // The returned task must never fault; nothing may reach the dispatcher.
        await queue.Enqueue(
            _ => throw new InvalidOperationException("load failed"),
            () => abandoned = true);

        Assert.True(abandoned);
    }

    [Fact]
    public async Task SupersededRequest_DoesNotRunTheVisibleFallback()
    {
        var queue = new ChatConversationLoadQueue();
        var firstStarted = new TaskCompletionSource();
        var staleAbandoned = false;
        var currentAbandoned = false;

        var first = queue.Enqueue(
            async token =>
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            () => staleAbandoned = true);

        await firstStarted.Task;

        // Supersede it; the stale request must stay silent so it cannot resync the
        // display back to the older selection.
        var second = queue.Enqueue(
            _ => Task.CompletedTask,
            () => currentAbandoned = true);

        await first;
        await second;

        Assert.False(staleAbandoned, "a superseded request must not touch visible state");
        Assert.False(currentAbandoned);
    }

    [Fact]
    public async Task CurrentRequest_RunsTheVisibleFallbackOnFailure()
    {
        var queue = new ChatConversationLoadQueue();
        var abandoned = false;

        await queue.Enqueue(
            _ => throw new InvalidOperationException(),
            () => abandoned = true);

        Assert.True(abandoned, "the current request must resync when it fails");
    }

    [Fact]
    public async Task Invalidate_CancelsSilentlyWithoutRunningTheFallback()
    {
        var queue = new ChatConversationLoadQueue();
        var started = new TaskCompletionSource();
        var abandoned = false;

        var pending = queue.Enqueue(
            async token =>
            {
                started.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            () => abandoned = true);

        await started.Task;
        queue.Invalidate();
        await pending;

        Assert.False(abandoned, "an invalidated intent handles its own visible state");
    }

    [Fact]
    public async Task Invalidate_CannotStopALoadThatIgnoresItsToken()
    {
        // A load whose reads already finished still publishes, so Invalidate alone is not
        // a barrier. This is precisely why replacement actions wait for the chain to drain.
        var queue = new ChatConversationLoadQueue();
        var started = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var published = false;

        var pending = queue.Enqueue(
            async _ =>
            {
                started.SetResult();
                await release.Task;
                published = true;
            },
            () => { });

        await started.Task;
        queue.Invalidate();

        Assert.False(pending.IsCompleted, "the queue task stays pending while the load runs");
        Assert.False(published);

        release.SetResult();
        await pending;

        Assert.True(
            published,
            "the cancellation-insensitive load still published despite Invalidate");
    }

    [Fact]
    public async Task FailedRequest_DoesNotBlockTheNextOne()
    {
        var queue = new ChatConversationLoadQueue();
        var ran = false;

        await queue.Enqueue(_ => throw new InvalidOperationException(), () => { });
        await queue.Enqueue(
            _ =>
            {
                ran = true;
                return Task.CompletedTask;
            },
            () => { });

        Assert.True(ran);
    }
}
