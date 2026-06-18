namespace PersonalAI.Core.Tasks;

public interface ITaskQueryService
{
    ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<TaskRunRecord?> GetTaskRunAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default);

    ValueTask<TaskRun?> GetLatestTaskForConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TaskRun>> GetCurrentlyRunningTasksAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<TaskRun>> SearchByStatusAsync(
        TaskRunStatus status,
        int limit,
        CancellationToken cancellationToken = default);
}
