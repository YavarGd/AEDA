using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Tasks;

public sealed class TaskRuntimeTests
{
    [Fact]
    public async Task StartCompleteCancelAndFail_AreDurableTerminalTransitions()
    {
        var store = new MemoryTaskEventStore();
        var bus = new TaskEventBus();
        var runtime = new TaskRuntime(store, bus);

        var run = await runtime.StartTaskAsync("Answer workspace question");
        await runtime.AttachArtifactAsync(run.Id, TaskArtifact.Create("answer", "text"));
        await runtime.CompleteTaskAsync(run.Id);
        await runtime.FailTaskAsync(run.Id, "should_not_duplicate");
        await runtime.CancelTaskAsync(run.Id, TaskCancellationReason.UserRequested);

        var loaded = await runtime.GetTaskAsync(run.Id);

        Assert.NotNull(loaded);
        Assert.Equal(TaskRunStatus.Completed, loaded.TaskRun.Status);
        Assert.Single(loaded.Artifacts);
        Assert.Equal(1, loaded.Events.Count(item => item.Kind == TaskEventKind.TaskCompleted));
        Assert.DoesNotContain(loaded.Events, item => item.Kind == TaskEventKind.TaskFailed);
        Assert.DoesNotContain(loaded.Events, item => item.Kind == TaskEventKind.TaskCancelled);
    }

    [Fact]
    public async Task CancellationDuringStart_PropagatesAndCreatesNoRun()
    {
        var runtime = new TaskRuntime(new MemoryTaskEventStore(), new TaskEventBus());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await runtime.StartTaskAsync("cancel", cancellation.Token));
    }

    [Fact]
    public async Task FailTask_NormalizesSafeCode()
    {
        var store = new MemoryTaskEventStore();
        var runtime = new TaskRuntime(store, new TaskEventBus());
        var run = await runtime.StartTaskAsync("fail");

        await runtime.FailTaskAsync(run.Id, "tool_failed");

        var loaded = await runtime.GetTaskAsync(run.Id);
        Assert.NotNull(loaded);
        Assert.Equal(TaskRunStatus.Failed, loaded.TaskRun.Status);
        Assert.Equal("tool_failed", loaded.TaskRun.SafeErrorCode);
        Assert.Contains(loaded.Events, item => item.Kind == TaskEventKind.TaskFailed);
    }

    private sealed class MemoryTaskEventStore : ITaskEventStore
    {
        private readonly Dictionary<TaskId, TaskRun> _runs = [];
        private readonly Dictionary<TaskId, List<TaskEvent>> _events = [];
        private readonly Dictionary<TaskId, List<TaskArtifact>> _artifacts = [];

        public ValueTask CreateTaskRunAsync(
            TaskRun taskRun,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _runs[taskRun.Id] = taskRun;
            return ValueTask.CompletedTask;
        }

        public ValueTask UpdateTaskRunStatusAsync(
            TaskId taskId,
            TaskRunStatus status,
            DateTimeOffset updatedAtUtc,
            string? safeErrorCode = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _runs[taskId] = _runs[taskId] with
            {
                Status = status,
                UpdatedAtUtc = updatedAtUtc,
                SafeErrorCode = safeErrorCode
            };
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendAsync(
            TaskEvent taskEvent,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_events.TryGetValue(taskEvent.TaskId, out var events))
            {
                events = [];
                _events[taskEvent.TaskId] = events;
            }

            events.Add(taskEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendArtifactAsync(
            TaskId taskId,
            TaskArtifact artifact,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_artifacts.TryGetValue(taskId, out var artifacts))
            {
                artifacts = [];
                _artifacts[taskId] = artifacts;
            }

            artifacts.Add(artifact);
            return ValueTask.CompletedTask;
        }

        public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(_runs.Values.ToArray());

        public ValueTask<IReadOnlyList<TaskRun>> ListTaskRunsByStatusAsync(
            TaskRunStatus status,
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(
                _runs.Values.Where(run => run.Status == status).ToArray());

        public ValueTask<TaskRun?> GetLatestTaskRunForConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(
                _runs.Values
                    .Where(run => run.ConversationId == conversationId)
                    .OrderByDescending(run => run.UpdatedAtUtc)
                    .FirstOrDefault());

        public ValueTask<TaskRunRecord?> GetTaskRunAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default)
        {
            if (!_runs.TryGetValue(taskId, out var run))
            {
                return ValueTask.FromResult<TaskRunRecord?>(null);
            }

            return ValueTask.FromResult<TaskRunRecord?>(
                new TaskRunRecord(
                    run,
                    _events.GetValueOrDefault(taskId) ?? [],
                    _artifacts.GetValueOrDefault(taskId) ?? []));
        }

        public ValueTask MarkCancelledAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            DateTimeOffset cancelledAtUtc,
            CancellationToken cancellationToken = default) =>
            UpdateTaskRunStatusAsync(
                taskId,
                TaskRunStatus.Cancelled,
                cancelledAtUtc,
                reason.ToString().ToLowerInvariant(),
                cancellationToken);

        public ValueTask MarkFailedAsync(
            TaskId taskId,
            string safeErrorCode,
            DateTimeOffset failedAtUtc,
            CancellationToken cancellationToken = default) =>
            UpdateTaskRunStatusAsync(
                taskId,
                TaskRunStatus.Failed,
                failedAtUtc,
                safeErrorCode,
                cancellationToken);

        public async IAsyncEnumerable<TaskEvent> ReadAsync(
            TaskId taskId,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var taskEvent in _events.GetValueOrDefault(taskId) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return taskEvent;
                await Task.Yield();
            }
        }
    }
}
