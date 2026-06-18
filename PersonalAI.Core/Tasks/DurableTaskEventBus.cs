namespace PersonalAI.Core.Tasks;

public sealed class DurableTaskEventBus(
    ITaskEventBus inner,
    ITaskEventStore store) : ITaskEventBus
{
    public async ValueTask PublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);

        try
        {
            await store.AppendAsync(taskEvent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Task history must never break chat/tool execution.
        }

        await inner.PublishAsync(taskEvent, cancellationToken);
    }

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        CancellationToken cancellationToken = default) =>
        inner.SubscribeAsync(cancellationToken);

    public IAsyncEnumerable<TaskEvent> SubscribeAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        inner.SubscribeAsync(taskId, cancellationToken);
}
