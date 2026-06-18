namespace PersonalAI.Core.Tasks;

public interface ITaskRuntime
{
    ValueTask<TaskRun> StartTaskAsync(
        string title,
        CancellationToken cancellationToken);

    ValueTask<TaskRun> StartTaskAsync(
        string title,
        string source = "unknown",
        Guid? conversationId = null,
        string? model = null,
        string? provider = null,
        CancellationToken cancellationToken = default);

    ValueTask AppendEventAsync(
        TaskId taskId,
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken = default);

    ValueTask AttachArtifactAsync(
        TaskId taskId,
        TaskArtifact artifact,
        CancellationToken cancellationToken = default);

    ValueTask CompleteTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);

    ValueTask CancelTaskAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        CancellationToken cancellationToken = default);

    ValueTask FailTaskAsync(
        TaskId taskId,
        string safeErrorCode,
        CancellationToken cancellationToken = default);

    ValueTask<TaskRunRecord?> GetTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);
}
