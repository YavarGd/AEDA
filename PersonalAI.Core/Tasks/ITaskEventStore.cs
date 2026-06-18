namespace PersonalAI.Core.Tasks;

public interface ITaskEventStore
{
    ValueTask CreateTaskRunAsync(TaskRun taskRun, CancellationToken cancellationToken = default);

    ValueTask UpdateTaskRunStatusAsync(
        TaskId taskId,
        TaskRunStatus status,
        DateTimeOffset updatedAtUtc,
        string? safeErrorCode = null,
        CancellationToken cancellationToken = default);

    ValueTask AppendAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default);

    ValueTask AppendArtifactAsync(
        TaskId taskId,
        TaskArtifact artifact,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<TaskRunRecord?> GetTaskRunAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);

    ValueTask MarkCancelledAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken = default);

    ValueTask MarkFailedAsync(
        TaskId taskId,
        string safeErrorCode,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<TaskEvent> ReadAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);
}
