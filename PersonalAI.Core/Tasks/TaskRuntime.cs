using System.Collections.Concurrent;

namespace PersonalAI.Core.Tasks;

public sealed class TaskRuntime : ITaskRuntime
{
    private static readonly TaskRunStatus[] TerminalStatuses =
    [
        TaskRunStatus.Cancelled,
        TaskRunStatus.Completed,
        TaskRunStatus.Failed
    ];

    private readonly ITaskEventStore _store;
    private readonly ITaskEventBus _eventBus;
    private readonly ConcurrentDictionary<TaskId, TaskRunStatus> _knownStatuses = [];

    public TaskRuntime(ITaskEventStore store, ITaskEventBus eventBus)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
    }

    public async ValueTask<TaskRun> StartTaskAsync(
        string title,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var taskRun = TaskRun.Create(title);
        await _store.CreateTaskRunAsync(taskRun, cancellationToken);
        _knownStatuses[taskRun.Id] = TaskRunStatus.Created;
        await AppendAndPublishAsync(
            TaskEvent.Create(
                taskRun.Id,
                TaskEventKind.TaskCreated,
                "Task created.",
                TaskExecutionState.Created),
            cancellationToken);
        await TransitionAsync(
            taskRun.Id,
            TaskRunStatus.Running,
            TaskEventKind.TaskStarted,
            "Task started.",
            cancellationToken);
        return taskRun with
        {
            Status = TaskRunStatus.Running,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public ValueTask AppendEventAsync(
        TaskId taskId,
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken = default) =>
        AppendAndPublishAsync(
            TaskEvent.Create(taskId, kind, summary, MapState(kind)),
            cancellationToken);

    public async ValueTask AttachArtifactAsync(
        TaskId taskId,
        TaskArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(artifact);
        await _store.AppendArtifactAsync(taskId, artifact, cancellationToken);
        await AppendAndPublishAsync(
            TaskEvent.Create(
                taskId,
                TaskEventKind.ArtifactCreated,
                $"Artifact created: {artifact.Name}.",
                TaskExecutionState.Running,
                safeMetadata: artifact.SafeMetadata),
            cancellationToken);
    }

    public ValueTask CompleteTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(
            taskId,
            TaskRunStatus.Completed,
            TaskEventKind.TaskCompleted,
            "Task completed.",
            cancellationToken);

    public async ValueTask CancelTaskAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        CancellationToken cancellationToken = default)
    {
        if (IsTerminal(taskId))
        {
            return;
        }

        await _store.UpdateTaskRunStatusAsync(
            taskId,
            TaskRunStatus.Cancelling,
            DateTimeOffset.UtcNow,
            cancellationToken: cancellationToken);
        _knownStatuses[taskId] = TaskRunStatus.Cancelling;
        await _store.MarkCancelledAsync(
            taskId,
            reason,
            DateTimeOffset.UtcNow,
            cancellationToken);
        _knownStatuses[taskId] = TaskRunStatus.Cancelled;
        await AppendAndPublishAsync(
            TaskEvent.Create(
                taskId,
                TaskEventKind.TaskCancelled,
                "Task cancelled.",
                TaskExecutionState.Cancelled,
                safeMetadata: TaskEventMetadata.CreateSafe(("reason", reason.ToString()))),
            cancellationToken);
    }

    public async ValueTask FailTaskAsync(
        TaskId taskId,
        string safeErrorCode,
        CancellationToken cancellationToken = default)
    {
        if (IsTerminal(taskId))
        {
            return;
        }

        await _store.MarkFailedAsync(
            taskId,
            TaskEventMetadata.SanitizeErrorCode(safeErrorCode) ?? "task_failed",
            DateTimeOffset.UtcNow,
            cancellationToken);
        _knownStatuses[taskId] = TaskRunStatus.Failed;
        await AppendAndPublishAsync(
            TaskEvent.Create(
                taskId,
                TaskEventKind.TaskFailed,
                "Task failed.",
                TaskExecutionState.Failed,
                safeErrorCode: safeErrorCode,
                safeErrorMessage: "Task failed."),
            cancellationToken);
    }

    public ValueTask<TaskRunRecord?> GetTaskAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default) =>
        _store.GetTaskRunAsync(taskId, cancellationToken);

    private async ValueTask TransitionAsync(
        TaskId taskId,
        TaskRunStatus status,
        TaskEventKind eventKind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (IsTerminal(taskId))
        {
            return;
        }

        await _store.UpdateTaskRunStatusAsync(
            taskId,
            status,
            DateTimeOffset.UtcNow,
            cancellationToken: cancellationToken);
        _knownStatuses[taskId] = status;
        await AppendAndPublishAsync(
            TaskEvent.Create(taskId, eventKind, summary, MapStatus(status)),
            cancellationToken);
    }

    private async ValueTask AppendAndPublishAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _store.AppendAsync(taskEvent, cancellationToken);
        await _eventBus.PublishAsync(taskEvent, cancellationToken);
    }

    private bool IsTerminal(TaskId taskId) =>
        _knownStatuses.TryGetValue(taskId, out var status) &&
        TerminalStatuses.Contains(status);

    private static TaskExecutionState? MapState(TaskEventKind kind) =>
        kind switch
        {
            TaskEventKind.ApprovalRequested => TaskExecutionState.WaitingForApproval,
            TaskEventKind.TaskPaused => TaskExecutionState.Paused,
            TaskEventKind.TaskCancelled => TaskExecutionState.Cancelled,
            TaskEventKind.TaskFailed => TaskExecutionState.Failed,
            TaskEventKind.TaskCompleted => TaskExecutionState.Completed,
            _ => TaskExecutionState.Running
        };

    private static TaskExecutionState MapStatus(TaskRunStatus status) =>
        status switch
        {
            TaskRunStatus.Created => TaskExecutionState.Created,
            TaskRunStatus.WaitingForApproval => TaskExecutionState.WaitingForApproval,
            TaskRunStatus.Paused => TaskExecutionState.Paused,
            TaskRunStatus.Cancelling => TaskExecutionState.Cancelling,
            TaskRunStatus.Cancelled => TaskExecutionState.Cancelled,
            TaskRunStatus.Completed => TaskExecutionState.Completed,
            TaskRunStatus.Failed => TaskExecutionState.Failed,
            _ => TaskExecutionState.Running
        };
}
