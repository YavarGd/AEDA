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
}
