namespace PersonalAI.Core.Tasks;

public interface ITaskEventBus
{
    /// <summary>
    /// Publishes an event to current subscribers without waiting for slow readers.
    /// Implementations should preserve publish order for events accepted by each subscriber.
    /// </summary>
    ValueTask PublishAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to all future events. Slow subscribers may drop old events according to
    /// the bus back-pressure policy instead of blocking publishers.
    /// </summary>
    IAsyncEnumerable<TaskEvent> SubscribeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to future events for one task id.
    /// </summary>
    IAsyncEnumerable<TaskEvent> SubscribeAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);
}
