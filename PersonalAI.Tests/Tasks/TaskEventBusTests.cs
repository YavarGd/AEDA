using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Tasks;

public sealed class TaskEventBusTests
{
    [Fact]
    public async Task SubscribeAsync_ReceivesEventsInPublishOrder()
    {
        var bus = new TaskEventBus();
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedTask = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 3);

        await Task.Yield();
        await bus.PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.TaskStarted,
            "started"));
        await bus.PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolStarted,
            "tool started"));
        await bus.PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.ToolCompleted,
            "tool completed"));

        var received = await receivedTask;

        Assert.Collection(
            received,
            first => Assert.Equal(TaskEventKind.TaskStarted, first.Kind),
            second => Assert.Equal(TaskEventKind.ToolStarted, second.Kind),
            third => Assert.Equal(TaskEventKind.ToolCompleted, third.Kind));
    }

    [Fact]
    public async Task SubscribeAsync_FiltersByTaskId()
    {
        var bus = new TaskEventBus();
        var wanted = TaskId.NewId();
        var other = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receivedTask = CollectAsync(bus.SubscribeAsync(wanted, cancellation.Token), 1);

        await Task.Yield();
        await bus.PublishAsync(TaskEvent.Create(
            other,
            TaskEventKind.TaskStarted,
            "other"));
        await bus.PublishAsync(TaskEvent.Create(
            wanted,
            TaskEventKind.TaskStarted,
            "wanted"));

        var received = await receivedTask;

        var item = Assert.Single(received);
        Assert.Equal(wanted, item.TaskId);
        Assert.Equal("wanted", item.Summary);
    }

    [Fact]
    public async Task PublishAsync_DoesNotBlockOnSlowSubscriber()
    {
        var bus = new TaskEventBus(new TaskEventBusOptions(SubscriberBufferCapacity: 1));
        await using var enumerator = bus.SubscribeAsync().GetAsyncEnumerator();
        var taskId = TaskId.NewId();
        var firstMove = enumerator.MoveNextAsync().AsTask();

        await Task.Yield();
        await bus.PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.TaskStarted,
            "first"));
        Assert.True(await firstMove.WaitAsync(TimeSpan.FromSeconds(5)));

        var publishTasks = Enumerable.Range(0, 100)
            .Select(index => bus.PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.TaskStatusChanged,
                $"event {index}")).AsTask())
            .ToArray();

        await Task.WhenAll(publishTasks).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledSubscriber_IsRemoved()
    {
        var bus = new TaskEventBus();
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = bus.SubscribeAsync(cancellation.Token)
            .GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, bus.ActiveSubscriberCount);

        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => move);

        Assert.Equal(0, bus.ActiveSubscriberCount);
    }

    [Fact]
    public async Task DisposedSubscriber_IsRemoved()
    {
        var bus = new TaskEventBus();
        var enumerator = bus.SubscribeAsync().GetAsyncEnumerator();
        var move = enumerator.MoveNextAsync().AsTask();

        Assert.Equal(1, bus.ActiveSubscriberCount);

        await enumerator.DisposeAsync();

        Assert.Equal(0, bus.ActiveSubscriberCount);
        await Assert.ThrowsAsync<OperationCanceledException>(() => move);
    }

    [Fact]
    public async Task MultipleSubscribers_AreIndependent()
    {
        var bus = new TaskEventBus();
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var first = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 1);
        var second = CollectAsync(bus.SubscribeAsync(taskId, cancellation.Token), 1);

        await Task.Yield();
        await bus.PublishAsync(TaskEvent.Create(
            taskId,
            TaskEventKind.TaskStarted,
            "started"));

        Assert.Single(await first);
        Assert.Single(await second);
    }

    [Fact]
    public async Task ConcurrentSubscribeUnsubscribePublish_DoesNotThrow()
    {
        var bus = new TaskEventBus();
        var taskId = TaskId.NewId();
        var operations = Enumerable.Range(0, 40).Select(async index =>
        {
            using var cancellation = new CancellationTokenSource();
            await using var enumerator = bus.SubscribeAsync(taskId, cancellation.Token)
                .GetAsyncEnumerator();
            var move = enumerator.MoveNextAsync().AsTask();
            await bus.PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.TaskStatusChanged,
                $"event {index}"));
            cancellation.Cancel();
            try
            {
                await move;
            }
            catch (OperationCanceledException)
            {
            }
        });

        await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ConcurrentPublishers_AllSubscribersObserveSameRetainedOrder()
    {
        var eventCount = 200;
        var bus = new TaskEventBus(new TaskEventBusOptions(eventCount + 8));
        var taskId = TaskId.NewId();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var allSubscriber = CollectAsync(
            bus.SubscribeAsync(cancellation.Token),
            eventCount);
        var secondAllSubscriber = CollectAsync(
            bus.SubscribeAsync(cancellation.Token),
            eventCount);
        var taskSubscriber = CollectAsync(
            bus.SubscribeAsync(taskId, cancellation.Token),
            eventCount);

        await WaitForSubscribersAsync(bus, expectedCount: 3);

        var publishTasks = Enumerable.Range(0, eventCount)
            .Select(index => Task.Run(() => bus.PublishAsync(TaskEvent.Create(
                taskId,
                TaskEventKind.TaskStatusChanged,
                $"event {index}",
                safeMetadata: TaskEventMetadata.CreateSafe(
                    ("sequence", index.ToString())))).AsTask()))
            .ToArray();

        await Task.WhenAll(publishTasks).WaitAsync(TimeSpan.FromSeconds(10));

        var first = await allSubscriber;
        var second = await secondAllSubscriber;
        var filtered = await taskSubscriber;

        Assert.Equal(
            first.Select(GetSequence),
            second.Select(GetSequence));
        Assert.Equal(
            first.Select(GetSequence),
            filtered.Select(GetSequence));
    }

    [Fact]
    public void TaskEventMetadata_TruncatesOversizedValuesAndKeepsSafeMetadata()
    {
        var metadata = TaskEventMetadata.CreateSafe(
            ("phase", new string('a', TaskEventMetadata.MaxMetadataValueLength + 10)));

        Assert.Equal(TaskEventMetadata.MaxMetadataValueLength, metadata["phase"].Length);
    }

    [Fact]
    public void TaskEventMetadata_RedactsSecretLikeValues()
    {
        var taskEvent = TaskEvent.Create(
            TaskId.NewId(),
            TaskEventKind.ToolFailed,
            "token=super-secret",
            safeMetadata: TaskEventMetadata.CreateSafe(("status", "api_key=secret")));

        Assert.DoesNotContain("super-secret", taskEvent.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", taskEvent.SafeMetadata!["status"], StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<TaskEvent>> CollectAsync(
        IAsyncEnumerable<TaskEvent> events,
        int count)
    {
        var received = new List<TaskEvent>();

        await foreach (var taskEvent in events)
        {
            received.Add(taskEvent);

            if (received.Count == count)
            {
                return received;
            }
        }

        return received;
    }

    private static async Task WaitForSubscribersAsync(
        TaskEventBus bus,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        while (bus.ActiveSubscriberCount < expectedCount)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
    }

    private static int GetSequence(TaskEvent taskEvent) =>
        int.Parse(taskEvent.SafeMetadata!["sequence"]);
}
