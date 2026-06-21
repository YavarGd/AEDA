using PersonalAI.Core.Approvals;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Tasks;

public sealed class AedaTaskCenterServiceTests
{
    [Fact]
    public async Task Dashboard_LoadsBoundedListsCountsAndApprovals()
    {
        var running = TaskRun.Create("Running code task", "aeda-code")
            with { Status = TaskRunStatus.Running };
        var waiting = TaskRun.Create("Waiting task", "aeda-code")
            with { Status = TaskRunStatus.WaitingForApproval };
        var failed = TaskRun.Create("Failed research task", "aeda-research")
            with { Status = TaskRunStatus.Failed, SafeErrorCode = "safe_failure" };
        var store = new FakeTaskQueryService
        {
            Running = [running, waiting],
            Recent = [running, waiting, failed],
            ByStatus =
            {
                [TaskRunStatus.Failed] = [failed],
                [TaskRunStatus.Cancelled] = []
            }
        };
        var approvals = new InMemoryApprovalCheckpointStore();
        await approvals.RequestAsync(ApprovalRequest.Create(
            new ApprovalScope(waiting.Id, ApprovalKind.ApproveFutureApply, "patch-apply:workspace:proposal"),
            "Approve patch",
            "Patch apply approval requested."));
        var service = new AedaTaskCenterService(store, new FakeTaskRuntime(), approvals);

        var dashboard = await service.GetDashboardAsync(new AedaTaskFilter(Limit: 2));

        Assert.Equal(2, dashboard.ActiveTasks.Count);
        Assert.Equal(2, dashboard.RecentTasks.Count);
        Assert.Single(dashboard.WaitingApprovals);
        Assert.Single(dashboard.FailedOrCancelledTasks);
        Assert.True(dashboard.CountsByStatus.ContainsKey(AedaTaskCenterStatus.Failed));
        Assert.True(dashboard.CountsByModule.ContainsKey(AedaTaskCenterModule.Code));
        Assert.DoesNotContain("proposal", dashboard.WaitingApprovals[0].Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Timeline_MapsCodeValidationResearchMemoryFailureAndCancellationSafely()
    {
        var task = TaskRun.Create("Mixed task", "chat");
        var events = new[]
        {
            Event(task.Id, TaskEventKind.PatchProposalCreated, "raw { json } stdout secret", TaskExecutionState.Running),
            Event(task.Id, TaskEventKind.PatchApplyCompleted, "Patch apply completed.", TaskExecutionState.Completed),
            Event(task.Id, TaskEventKind.ValidationFailed, "C:\\secret\\stdout should not show", TaskExecutionState.Failed, "validation_failed"),
            Event(task.Id, TaskEventKind.ResearchReportCreated, "Research report created.", TaskExecutionState.Completed),
            Event(task.Id, TaskEventKind.WorkspaceIndexingCompleted, "Workspace indexing completed.", TaskExecutionState.Completed),
            Event(task.Id, TaskEventKind.TaskCancelled, "Task cancelled.", TaskExecutionState.Cancelled)
        };
        var store = new FakeTaskQueryService
        {
            Records = { [task.Id] = new TaskRunRecord(task, events, []) }
        };
        var service = new AedaTaskCenterService(store, new FakeTaskRuntime());

        var groups = await service.GetTimelineAsync(task.Id);
        var items = groups.SelectMany(group => group.Items).ToArray();

        Assert.Contains(items, item => item.Title == "Patch proposal created" && item.Module.Module == AedaTaskCenterModule.Code);
        Assert.Contains(items, item => item.Title == "Validation failed" && item.Status.Status == AedaTaskCenterStatus.Failed);
        Assert.Contains(items, item => item.Title == "Verification report created" && item.Module.Module == AedaTaskCenterModule.Research);
        Assert.Contains(items, item => item.Title == "Workspace indexing completed" && item.Module.Module == AedaTaskCenterModule.Memory);
        Assert.Contains(items, item => item.Status.Status == AedaTaskCenterStatus.Cancelled);
        Assert.DoesNotContain(items, item =>
            item.Summary.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            item.Summary.Contains("C:\\", StringComparison.Ordinal) ||
            item.Summary.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ModuleInference_UsesSourceEventAndFallback()
    {
        Assert.Equal(
            AedaTaskCenterModule.Code,
            AedaTaskCenterService.InferModule(TaskRun.Create("Code", "aeda-code")));
        Assert.Equal(
            AedaTaskCenterModule.Memory,
            AedaTaskCenterService.InferModule(
                TaskRun.Create("Unknown", "unknown"),
                Event(TaskId.NewId(), TaskEventKind.RetrievalCompleted, "Retrieval completed.", TaskExecutionState.Completed)));
        Assert.Equal(
            AedaTaskCenterModule.Research,
            AedaTaskCenterService.InferModule(
                TaskRun.Create("Unknown", "unknown"),
                Event(TaskId.NewId(), TaskEventKind.ResearchEvidenceGathered, "Evidence gathered.", TaskExecutionState.Running)));
        Assert.Equal(
            AedaTaskCenterModule.Chat,
            AedaTaskCenterService.InferModule(TaskRun.Create("Chat", "chat")));
        Assert.Equal(
            AedaTaskCenterModule.Unknown,
            AedaTaskCenterService.InferModule(TaskRun.Create("Unknown", "mystery")));
    }

    [Fact]
    public async Task ArtifactLinks_AreNavigationOnlyAndSafe()
    {
        var task = TaskRun.Create("Proposal task", "aeda-code");
        var proposalEvent = Event(
            task.Id,
            TaskEventKind.PatchProposalCreated,
            "Patch proposal created.",
            TaskExecutionState.Running,
            metadata: TaskEventMetadata.CreateSafe(("proposal_id", "proposal-1")));
        var artifact = TaskArtifact.Create(
            "Patch proposal",
            "proposal",
            safeMetadata: TaskEventMetadata.CreateSafe(("relative_path", "src/App.cs")));
        var store = new FakeTaskQueryService
        {
            Records =
            {
                [task.Id] = new TaskRunRecord(task, [proposalEvent], [artifact])
            }
        };
        var service = new AedaTaskCenterService(store, new FakeTaskRuntime());

        var links = (await service.GetTimelineAsync(task.Id))
            .SelectMany(group => group.Items)
            .SelectMany(item => item.Links)
            .ToArray();

        Assert.NotEmpty(links);
        Assert.All(links, link =>
        {
            Assert.True(link.IsAvailable);
            Assert.NotNull(link.Route);
            Assert.DoesNotContain("C:\\", link.SafeSummary, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task CancelTask_DelegatesToExistingRuntime()
    {
        var runtime = new FakeTaskRuntime();
        var taskId = TaskId.NewId();
        var service = new AedaTaskCenterService(new FakeTaskQueryService(), runtime);

        await service.CancelTaskAsync(taskId, TaskCancellationReason.UserRequested);

        Assert.Equal(taskId, runtime.CancelledTaskId);
        Assert.Equal(TaskCancellationReason.UserRequested, runtime.CancelReason);
    }

    private static TaskEvent Event(
        TaskId taskId,
        TaskEventKind kind,
        string summary,
        TaskExecutionState? state,
        string? errorCode = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        TaskEvent.Create(
            taskId,
            kind,
            summary,
            state,
            safeMetadata: metadata,
            safeErrorCode: errorCode,
            safeErrorMessage: errorCode is null ? null : "Task failed.");

    private sealed class FakeTaskQueryService : ITaskQueryService
    {
        public IReadOnlyList<TaskRun> Running { get; init; } = [];

        public IReadOnlyList<TaskRun> Recent { get; init; } = [];

        public Dictionary<TaskRunStatus, IReadOnlyList<TaskRun>> ByStatus { get; } = [];

        public Dictionary<TaskId, TaskRunRecord> Records { get; } = [];

        public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(Recent.Take(limit).ToArray());

        public ValueTask<TaskRunRecord?> GetTaskRunAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Records.TryGetValue(taskId, out var record) ? record : null);

        public ValueTask<TaskRun?> GetLatestTaskForConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRun?>(null);

        public ValueTask<IReadOnlyList<TaskRun>> GetCurrentlyRunningTasksAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(Running.Take(limit).ToArray());

        public ValueTask<IReadOnlyList<TaskRun>> SearchByStatusAsync(
            TaskRunStatus status,
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(
                ByStatus.TryGetValue(status, out var tasks)
                    ? tasks.Take(limit).ToArray()
                    : []);
    }

    private sealed class FakeTaskRuntime : ITaskRuntime
    {
        public TaskId? CancelledTaskId { get; private set; }

        public TaskCancellationReason? CancelReason { get; private set; }

        public ValueTask<TaskRun> StartTaskAsync(string title, CancellationToken cancellationToken) =>
            ValueTask.FromResult(TaskRun.Create(title));

        public ValueTask<TaskRun> StartTaskAsync(
            string title,
            string source = "unknown",
            Guid? conversationId = null,
            string? model = null,
            string? provider = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(TaskRun.Create(title, source, conversationId, model, provider));

        public ValueTask AppendEventAsync(
            TaskId taskId,
            TaskEventKind kind,
            string summary,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask AttachArtifactAsync(
            TaskId taskId,
            TaskArtifact artifact,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CancelTaskAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            CancellationToken cancellationToken = default)
        {
            CancelledTaskId = taskId;
            CancelReason = reason;
            return ValueTask.CompletedTask;
        }

        public ValueTask FailTaskAsync(
            TaskId taskId,
            string safeErrorCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<TaskRunRecord?> GetTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRunRecord?>(null);
    }
}
