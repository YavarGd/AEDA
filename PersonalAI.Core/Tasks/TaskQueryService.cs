namespace PersonalAI.Core.Tasks;

public sealed class TaskQueryService(ITaskEventStore store) : ITaskQueryService
{
    public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        store.ListRecentTaskRunsAsync(limit, cancellationToken);

    public ValueTask<TaskRunRecord?> GetTaskRunAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        store.GetTaskRunAsync(taskId, cancellationToken);

    public ValueTask<TaskRun?> GetLatestTaskForConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default) =>
        store.GetLatestTaskRunForConversationAsync(conversationId, cancellationToken);

    public async ValueTask<IReadOnlyList<TaskRun>> GetCurrentlyRunningTasksAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var running = await store.ListTaskRunsByStatusAsync(
            TaskRunStatus.Running,
            limit,
            cancellationToken);
        var waiting = await store.ListTaskRunsByStatusAsync(
            TaskRunStatus.WaitingForApproval,
            limit,
            cancellationToken);
        return running
            .Concat(waiting)
            .OrderByDescending(task => task.UpdatedAtUtc)
            .ThenByDescending(task => task.CreatedAtUtc)
            .Take(Math.Clamp(limit, 1, 200))
            .ToArray();
    }

    public ValueTask<IReadOnlyList<TaskRun>> SearchByStatusAsync(
        TaskRunStatus status,
        int limit,
        CancellationToken cancellationToken = default) =>
        store.ListTaskRunsByStatusAsync(status, limit, cancellationToken);
}
