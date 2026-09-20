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
    public async Task ImmediateCancellationContinuation_ObservesReplacementAsCurrent()
    {
        var queue = new ChatConversationLoadQueue();

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var firstStarted = new TaskCompletionSource();
            var firstCancelled = new TaskCompletionSource();
            var staleAbandoned = false;
            var currentAbandoned = false;

            var first = queue.Enqueue(
                async token =>
                {
                    using var registration = token.Register(firstCancelled.SetResult);
                    firstStarted.SetResult();
                    await firstCancelled.Task;
                    token.ThrowIfCancellationRequested();
                },
                () => staleAbandoned = true);

            await firstStarted.Task;

            var second = queue.Enqueue(
                _ => Task.CompletedTask,
                () => currentAbandoned = true);

            await first;
            await second;

            Assert.True(firstCancelled.Task.IsCompleted);
            Assert.False(staleAbandoned, "the cancelling request must observe its replacement as current");
            Assert.False(currentAbandoned);
        }
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

    // G05 — deterministic supersession-race evidence. Each schedule below forces a
    // specific ordering of ownership transition versus cancellation observation against
    // the real ChatConversationLoadQueue; none relies on Task.Delay/Thread.Sleep/timing.

    [Fact]
    public async Task SynchronousCancellationUnderExplicitUiSynchronizationContext_DoesNotPublishStaleFallback()
    {
        // Schedule B: an explicit, manually-drained SynchronizationContext (standing in for
        // the UI dispatcher) lets this test force first's cancellation continuation to be
        // genuinely deferred (queued rather than inlined - the BCL inlines an await
        // continuation whenever SynchronizationContext.Current at completion time is
        // reference-equal to the one captured at the await, so the context is restored
        // before triggering cancellation) so the test can choose exactly when that
        // continuation is allowed to run relative to the replacement's ownership
        // transition, which has already happened synchronously inside Enqueue by the time
        // anything here is drained.
        var context = new QueuingSynchronizationContext();
        var previousContext = SynchronizationContext.Current;
        var queue = new ChatConversationLoadQueue();
        var gate = new TaskCompletionSource();
        var staleAbandoned = false;
        var currentAbandoned = false;
        var secondRan = false;
        var firstStarted = false;
        Task first;

        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            first = queue.Enqueue(
                async token =>
                {
                    using var registration = token.Register(() => gate.SetResult());
                    firstStarted = true;
                    await gate.Task;
                    token.ThrowIfCancellationRequested();
                },
                () => staleAbandoned = true);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Assert.True(firstStarted);
        Assert.Equal(0, context.PendingCount);

        var second = queue.Enqueue(
            _ =>
            {
                secondRan = true;
                return Task.CompletedTask;
            },
            () => currentAbandoned = true);

        // Enqueue(second) already published second's identity as current before cancelling
        // first. Cancelling first only POSTED its continuation to this context instead of
        // running it inline. Ownership has therefore already moved before anything queued
        // here gets a chance to run.
        Assert.True(
            context.PendingCount > 0,
            "cancelling first must have posted its continuation rather than running it inline");

        while (context.RunNext())
        {
            Assert.False(
                staleAbandoned,
                "first must never observe itself as current at any point while draining");
        }

        Assert.False(staleAbandoned);

        await second;
        await first;

        Assert.True(secondRan, "second must still run to completion");
        Assert.False(currentAbandoned);
    }

    [Fact]
    public async Task ThreadPoolScheduledCancellation_ObservesReplacementAsCurrent()
    {
        // Schedule C: force the cancellation continuation onto a genuine ThreadPool worker
        // thread (RunContinuationsAsynchronously), so the invariant is proven under real
        // cross-thread handoff rather than the fully-inline execution the ambient
        // (no-context) default would otherwise produce.
        var queue = new ChatConversationLoadQueue();
        var startingThread = Environment.CurrentManagedThreadId;

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstStarted = new TaskCompletionSource();
            var continuationThread = -1;
            var staleAbandoned = false;
            var currentAbandoned = false;

            var first = queue.Enqueue(
                async token =>
                {
                    using var registration = token.Register(() => gate.SetResult());
                    firstStarted.SetResult();
                    await gate.Task;
                    continuationThread = Environment.CurrentManagedThreadId;
                    token.ThrowIfCancellationRequested();
                },
                () => staleAbandoned = true);

            await firstStarted.Task;

            var second = queue.Enqueue(_ => Task.CompletedTask, () => currentAbandoned = true);

            await first;
            await second;

            Assert.NotEqual(-1, continuationThread);
            Assert.NotEqual(startingThread, continuationThread);
            Assert.False(staleAbandoned, "the replacement must be observed as current on the thread pool too");
            Assert.False(currentAbandoned);
        }
    }

    [Fact]
    public async Task CompletionCancellationCollision_CancellationAlreadyRequestedBeforeCompletion_DoesNotPublish()
    {
        // Schedule D (cancellation wins): mirrors ChatConversationOpener.OpenAndConfirmAsync
        // exactly - await the real underlying operation, then check the token right before
        // deciding to publish. The underlying operation is driven by a gate independent of
        // the CancellationTokenSource, so completion and cancellation become observable at
        // the same boundary without one causing the other.
        var queue = new ChatConversationLoadQueue();
        var operationGate = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var staleAbandoned = false;
        var publishedStale = false;

        var first = queue.Enqueue(
            async token =>
            {
                firstStarted.SetResult();
                await operationGate.Task;
                token.ThrowIfCancellationRequested();
                publishedStale = true;
            },
            () => staleAbandoned = true);

        await firstStarted.Task;

        // Supersede first before its underlying operation completes.
        var second = queue.Enqueue(_ => Task.CompletedTask, () => { });

        // The operation "completes" only after cancellation was already requested.
        operationGate.SetResult();

        await first;
        await second;

        Assert.False(publishedStale, "a load must not publish once its token has been cancelled");
        Assert.False(staleAbandoned, "the superseded request must not touch visible state either");
    }

    [Fact]
    public async Task CompletionCancellationCollision_CompletionBeforeSupersession_PublishesNormally()
    {
        // Schedule D (completion wins): the same shape, but the underlying operation
        // finishes and the token check passes while first is still the only request, so
        // publishing is legitimate - this is the control proving the fix does not
        // over-suppress an unsuperseded request.
        var queue = new ChatConversationLoadQueue();
        var operationGate = new TaskCompletionSource();
        var firstStarted = new TaskCompletionSource();
        var published = false;

        var first = queue.Enqueue(
            async token =>
            {
                firstStarted.SetResult();
                await operationGate.Task;
                token.ThrowIfCancellationRequested();
                published = true;
            },
            () => { });

        await firstStarted.Task;
        operationGate.SetResult();
        await first;

        // Only now does a replacement arrive, after first already published.
        var second = queue.Enqueue(_ => Task.CompletedTask, () => { });
        await second;

        Assert.True(published, "an unsuperseded request must still be able to publish normally");
    }

    [Fact]
    public async Task SupersededRequest_FullyUnwindsIncludingFallbackBeforeReplacementStarts()
    {
        // Schedule E: proves "B completes while A unwinds" cannot happen, because B's own
        // RunAsync awaits A's entire predecessor task - including A's own unwind - before
        // B's load ever starts. First is genuinely superseded here, so its visible fallback
        // correctly never runs (the primary invariant); "first-unwound" instead marks the
        // moment first's own load has finished unwinding, independent of that gating, so the
        // ordering itself can be proven.
        var queue = new ChatConversationLoadQueue();
        var order = new List<string>();
        var firstStarted = new TaskCompletionSource();

        var first = queue.Enqueue(
            async token =>
            {
                order.Add("first-start");
                firstStarted.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    order.Add("first-unwound");
                }
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

        await first;
        await second;

        Assert.Equal(
            new[] { "first-start", "first-unwound", "second-start" },
            order);
        Assert.DoesNotContain("first-abandoned", order);
        Assert.DoesNotContain("second-abandoned", order);
    }

    [Fact]
    public async Task Invalidate_SynchronousCancellationCascade_DoesNotRunTheFallback()
    {
        // Schedule F (New Chat invalidation): the same synchronous-cascade pressure as
        // ImmediateCancellationContinuation_ObservesReplacementAsCurrent, but through
        // Invalidate instead of a replacement Enqueue, proving the identical
        // publish-null-before-cancel ordering closes the race on the New Chat path too.
        var queue = new ChatConversationLoadQueue();

        for (var iteration = 0; iteration < 10; iteration++)
        {
            var firstStarted = new TaskCompletionSource();
            var abandoned = false;

            var first = queue.Enqueue(
                async token =>
                {
                    using var registration = token.Register(() => { });
                    firstStarted.SetResult();
                    var cancelled = new TaskCompletionSource();
                    using var registration2 = token.Register(() => cancelled.SetResult());
                    await cancelled.Task;
                    token.ThrowIfCancellationRequested();
                },
                () => abandoned = true);

            await firstStarted.Task;

            queue.Invalidate();

            await first;

            Assert.False(abandoned, "New Chat invalidation must stay silent even under a synchronous cascade");
        }
    }

    [Fact]
    public async Task RapidTripleSupersession_OnlyTheFinalRequestPublishes()
    {
        // Schedule G: A is in flight when B and C both arrive before A has unwound, so B is
        // itself superseded before it ever becomes current. Only C may own final visible
        // state; neither A nor B may run their fallback.
        var queue = new ChatConversationLoadQueue();
        var firstStarted = new TaskCompletionSource();
        var order = new List<string>();

        var first = queue.Enqueue(
            async token =>
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
            () => order.Add("first-abandoned"));

        await firstStarted.Task;

        var second = queue.Enqueue(
            _ => Task.CompletedTask,
            () => order.Add("second-abandoned"));
        var third = queue.Enqueue(
            _ =>
            {
                order.Add("third-ran");
                return Task.CompletedTask;
            },
            () => order.Add("third-abandoned"));

        await first;
        await second;
        await third;

        Assert.DoesNotContain("first-abandoned", order);
        Assert.DoesNotContain("second-abandoned", order);
        Assert.Contains("third-ran", order);
        Assert.DoesNotContain("third-abandoned", order);
    }

    /// <summary>
    /// A manually-drained stand-in for a UI dispatcher's SynchronizationContext: posted
    /// callbacks are queued rather than run, so a test can choose exactly when a captured
    /// continuation is allowed to execute relative to other synchronous code.
    /// </summary>
    private sealed class QueuingSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int PendingCount => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public override void Send(SendOrPostCallback d, object? state) => d(state);

        /// <summary>Runs exactly one queued continuation, if any. Returns whether one ran.</summary>
        public bool RunNext()
        {
            if (_queue.Count == 0)
            {
                return false;
            }

            var (callback, state) = _queue.Dequeue();
            callback(state);
            return true;
        }
    }
}
