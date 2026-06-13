namespace PersonalAI.Core.Tasks;

public interface ITaskEventStore
{
    ValueTask AppendAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TaskEvent> ReadAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);
}
