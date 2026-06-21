using PersonalAI.Core.Approvals;
using PersonalAI.Core.Modules;

namespace PersonalAI.Core.Tasks;

public sealed class AedaTaskCenterService(
    ITaskQueryService taskQueryService,
    ITaskRuntime taskRuntime,
    IApprovalCheckpointQueryStore? approvalQueryStore = null) : IAedaTaskCenterService
{
    public const int DefaultActiveLimit = 8;
    public const int DefaultApprovalLimit = 8;
    public const int DefaultRecentLimit = 20;
    public const int DefaultFailedLimit = 8;
    public const int MaxDashboardLimit = 50;
    public const int MaxTimelineLimit = 150;

    public async ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(
        AedaTaskFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var limit = BoundLimit(filter?.Limit ?? DefaultRecentLimit, MaxDashboardLimit);
        var active = await ListActiveTasksAsync(DefaultActiveLimit, cancellationToken)
            .ConfigureAwait(false);
        var approvals = await ListWaitingApprovalsAsync(DefaultApprovalLimit, cancellationToken)
            .ConfigureAwait(false);
        var recent = await ListRecentTasksAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        var failed = await ListFailedOrCancelledTasksAsync(DefaultFailedLimit, cancellationToken)
            .ConfigureAwait(false);
        var filteredRecent = ApplyFilter(recent, filter).ToArray();
        return new AedaTaskCenterDashboard(
            ApplyFilter(active, filter).ToArray(),
            approvals,
            filteredRecent,
            ApplyFilter(failed, filter).ToArray(),
            CountByStatus(recent.Concat(active).Concat(failed)),
            CountByModule(recent.Concat(active).Concat(failed)),
            DateTimeOffset.UtcNow,
            "Task Center loaded.");
    }

    public async ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var taskRuns = await taskQueryService.GetCurrentlyRunningTasksAsync(
            BoundLimit(limit, MaxDashboardLimit),
            cancellationToken).ConfigureAwait(false);
        return taskRuns.Select(ToSummary).ToArray();
    }

    public async ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (approvalQueryStore is null)
        {
            return [];
        }

        var approvals = await approvalQueryStore.ListPendingAsync(
            BoundLimit(limit, MaxDashboardLimit),
            cancellationToken).ConfigureAwait(false);
        return approvals
            .Select(checkpoint => ToApprovalSummary(checkpoint.Request))
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var taskRuns = await taskQueryService.ListRecentTaskRunsAsync(
            BoundLimit(limit, MaxDashboardLimit),
            cancellationToken).ConfigureAwait(false);
        return taskRuns.Select(ToSummary).ToArray();
    }

    public async ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        var bounded = BoundLimit(limit, MaxDashboardLimit);
        var failed = await taskQueryService.SearchByStatusAsync(
            TaskRunStatus.Failed,
            bounded,
            cancellationToken).ConfigureAwait(false);
        var cancelled = await taskQueryService.SearchByStatusAsync(
            TaskRunStatus.Cancelled,
            bounded,
            cancellationToken).ConfigureAwait(false);
        return failed
            .Concat(cancelled)
            .OrderByDescending(task => task.UpdatedAtUtc)
            .ThenByDescending(task => task.CreatedAtUtc)
            .Take(bounded)
            .Select(ToSummary)
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(
        AedaTaskCenterModule module,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var recent = await ListRecentTasksAsync(limit, cancellationToken)
            .ConfigureAwait(false);
        return recent
            .Where(task => task.Module.Module == module)
            .Take(BoundLimit(limit, MaxDashboardLimit))
            .ToArray();
    }

    public async ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(
        TaskId taskId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var record = await taskQueryService.GetTaskRunAsync(taskId, cancellationToken)
            .ConfigureAwait(false);
        if (record is null)
        {
            return [];
        }

        var taskModule = InferModule(record.TaskRun);
        var items = record.Events
            .OrderByDescending(taskEvent => taskEvent.TimestampUtc)
            .ThenBy(taskEvent => taskEvent.EventId)
            .Take(BoundLimit(limit, MaxTimelineLimit))
            .Select(taskEvent => ToTimelineItem(record.TaskRun, taskEvent, taskModule))
            .ToArray();
        var artifacts = record.Artifacts
            .OrderByDescending(artifact => artifact.CreatedAtUtc)
            .ThenBy(artifact => artifact.Id)
            .Take(20)
            .Select(artifact => ToArtifactTimelineItem(record.TaskRun, artifact, taskModule))
            .ToArray();
        var combined = items.Concat(artifacts)
            .OrderByDescending(item => item.TimestampUtc)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();
        return combined
            .GroupBy(item => GetPhase(item.Status.Status))
            .Select(group => new AedaTaskActivityGroup(
                group.Key.ToLowerInvariant().Replace(' ', '-'),
                group.Key,
                group.ToArray()))
            .ToArray();
    }

    public async ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(
        TaskId taskId,
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        var record = await taskQueryService.GetTaskRunAsync(taskId, cancellationToken)
            .ConfigureAwait(false);
        var taskEvent = record?.Events.FirstOrDefault(item => item.EventId == eventId);
        return record is null || taskEvent is null
            ? null
            : ToTimelineItem(record.TaskRun, taskEvent, InferModule(record.TaskRun));
    }

    public ValueTask CancelTaskAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        CancellationToken cancellationToken = default) =>
        taskRuntime.CancelTaskAsync(taskId, reason, cancellationToken);

    public static AedaTaskSummary ToSummary(TaskRun taskRun)
    {
        var module = InferModule(taskRun);
        var status = ToStatusBadge(taskRun.Status);
        return new AedaTaskSummary(
            taskRun.Id,
            Bound(taskRun.Title, 120, "Untitled task"),
            status,
            ToModuleBadge(module),
            taskRun.CreatedAtUtc.ToUniversalTime(),
            taskRun.UpdatedAtUtc.ToUniversalTime(),
            taskRun.SafeErrorCode is null
                ? $"{status.Label} from {ToModuleBadge(module).Label}"
                : $"{status.Label}: {Bound(taskRun.SafeErrorCode, 64, "task_failed")}",
            []);
    }

    public static AedaTaskTimelineItem ToTimelineItem(
        TaskRun taskRun,
        TaskEvent taskEvent,
        AedaTaskCenterModule? taskModule = null)
    {
        var module = InferModule(taskRun, taskEvent, taskModule);
        var status = ToStatusBadge(taskEvent.State, taskEvent.Kind);
        var title = FriendlyTitle(taskEvent.Kind);
        var summary = FriendlySummary(taskEvent);
        return new AedaTaskTimelineItem(
            taskEvent.EventId.ToString("N"),
            taskRun.Id,
            taskEvent.TimestampUtc.ToUniversalTime(),
            title,
            summary,
            SafeDetail(taskEvent),
            status,
            ToModuleBadge(module),
            CreateEventLinks(taskEvent, module));
    }

    public static AedaTaskCenterModule InferModule(
        TaskRun taskRun,
        TaskEvent? taskEvent = null,
        AedaTaskCenterModule? fallback = null)
    {
        AedaTaskCenterModule module;
        if (taskEvent is not null)
        {
            if (taskEvent.SafeMetadata is not null)
            {
                foreach (var key in new[] { "module", "source", "kind" })
                {
                    if (taskEvent.SafeMetadata.TryGetValue(key, out var value) &&
                        TryInferModule(value, out module))
                    {
                        return module;
                    }
                }
            }

            var eventModule = taskEvent.Kind switch
            {
                TaskEventKind.CodeChangeRequested or
                TaskEventKind.CodeContextLoaded or
                TaskEventKind.CodeChangePlanCreated or
                TaskEventKind.PatchProposalCreated or
                TaskEventKind.PatchProposalValidated or
                TaskEventKind.PatchProposalRiskClassified or
                TaskEventKind.PatchProposalApprovalRequested or
                TaskEventKind.PatchProposalApproved or
                TaskEventKind.PatchProposalRejected or
                TaskEventKind.PatchDryRunStarted or
                TaskEventKind.PatchDryRunPassed or
                TaskEventKind.PatchDryRunFailed or
                TaskEventKind.PatchApplyApprovalRequested or
                TaskEventKind.PatchApplyStarted or
                TaskEventKind.PatchFileBackupCreated or
                TaskEventKind.PatchFileApplied or
                TaskEventKind.PatchApplyCompleted or
                TaskEventKind.PatchApplyFailed or
                TaskEventKind.PatchApplyCancelled or
                TaskEventKind.PatchRollbackStarted or
                TaskEventKind.PatchFileRolledBack or
                TaskEventKind.PatchRollbackCompleted or
                TaskEventKind.PatchRollbackFailed or
                TaskEventKind.ValidationRunCreated or
                TaskEventKind.ValidationApprovalRequested or
                TaskEventKind.ValidationApprovalGranted or
                TaskEventKind.ValidationApprovalDenied or
                TaskEventKind.ValidationStarted or
                TaskEventKind.ValidationOutputCaptured or
                TaskEventKind.ValidationSucceeded or
                TaskEventKind.ValidationFailed or
                TaskEventKind.ValidationTimedOut or
                TaskEventKind.ValidationCancelled => AedaTaskCenterModule.Code,
                TaskEventKind.WorkspaceIndexingStarted or
                TaskEventKind.WorkspaceFileQueued or
                TaskEventKind.WorkspaceFileSkipped or
                TaskEventKind.WorkspaceFileIndexed or
                TaskEventKind.WorkspaceIndexingCompleted or
                TaskEventKind.WorkspaceIndexingFailed or
                TaskEventKind.WorkspaceIndexingCancelled or
                TaskEventKind.RetrievalStarted or
                TaskEventKind.RetrievalCompleted or
                TaskEventKind.RetrievalFailed => AedaTaskCenterModule.Memory,
                TaskEventKind.ResearchVerificationRequested or
                TaskEventKind.ResearchClaimsExtracted or
                TaskEventKind.ResearchEvidenceGathered or
                TaskEventKind.ResearchReportCreated or
                TaskEventKind.ResearchVerificationCancelled or
                TaskEventKind.ResearchVerificationFailed => AedaTaskCenterModule.Research,
                TaskEventKind.ProviderSelected or
                TaskEventKind.ProviderUnavailable or
                TaskEventKind.RoutingPolicyDenied or
                TaskEventKind.ContextFiltered or
                TaskEventKind.ProviderRequestStarted or
                TaskEventKind.ProviderRequestCompleted or
                TaskEventKind.ProviderRequestFailed or
                TaskEventKind.MessageEmitted => AedaTaskCenterModule.Chat,
                _ => AedaTaskCenterModule.Unknown
            };

            if (eventModule != AedaTaskCenterModule.Unknown)
            {
                return eventModule;
            }
        }

        var source = (taskRun.Source ?? string.Empty).Trim();
        if (TryInferModule(source, out module))
        {
            return module;
        }

        return fallback ?? AedaTaskCenterModule.Unknown;
    }

    private static AedaTaskApprovalSummary ToApprovalSummary(ApprovalRequest request)
    {
        var module = InferModule(request.Scope.Kind, request.Scope.ResourceScope);
        return new AedaTaskApprovalSummary(
            request.RequestId.ToString("N"),
            FriendlyApprovalTitle(request.Scope.Kind),
            SafeScope(request.Scope),
            ToModuleBadge(module),
            request.RequestedAtUtc.ToUniversalTime(),
            CreateApprovalRoute(request.Scope, module),
            Bound(request.Body, 180, "Approval needed."));
    }

    private static AedaTaskTimelineItem ToArtifactTimelineItem(
        TaskRun taskRun,
        TaskArtifact artifact,
        AedaTaskCenterModule module)
    {
        var link = ToArtifactLink(artifact, module);
        return new AedaTaskTimelineItem(
            artifact.Id.ToString("N"),
            taskRun.Id,
            artifact.CreatedAtUtc.ToUniversalTime(),
            "Artifact available",
            link.SafeSummary,
            null,
            new AedaTaskStatusBadge(
                AedaTaskCenterStatus.Completed,
                "Available",
                "artifact_available",
                NeedsAttention: false,
                IsTerminal: false),
            ToModuleBadge(module),
            [link]);
    }

    private static AedaTaskArtifactLink ToArtifactLink(
        TaskArtifact artifact,
        AedaTaskCenterModule module)
    {
        var label = Bound(artifact.Name, 100, "Task artifact");
        return new AedaTaskArtifactLink(
            artifact.Id.ToString("N"),
            Bound(artifact.Kind, 64, "artifact"),
            label,
            artifact.SafeUri is null
                ? $"{label} is recorded for this task."
                : $"{label} can be opened from its related module.",
            artifact.SafeUri is null
                ? CreateModuleRoute(module)
                : new AedaTaskCenterRoute(
                    ToModuleBadge(module).RouteId,
                    ToModuleBadge(module).ModuleId,
                    new Dictionary<string, string>
                    {
                        ["artifactId"] = artifact.Id.ToString("N"),
                        ["artifactKind"] = Bound(artifact.Kind, 64, "artifact")
                    }),
            IsAvailable: true);
    }

    private static IReadOnlyList<AedaTaskArtifactLink> CreateEventLinks(
        TaskEvent taskEvent,
        AedaTaskCenterModule module)
    {
        var links = new List<AedaTaskArtifactLink>();
        AddLinkFromMetadata(
            links,
            taskEvent,
            "proposal_id",
            "Code proposal",
            "Patch proposal",
            module);
        AddLinkFromMetadata(
            links,
            taskEvent,
            "apply_result_id",
            "Patch apply result",
            "Patch apply result",
            module);
        AddLinkFromMetadata(
            links,
            taskEvent,
            "validation_run_id",
            "Validation run",
            "Validation run",
            module);
        AddLinkFromMetadata(
            links,
            taskEvent,
            "research_report_id",
            "Research report",
            "Verification report",
            module);
        AddLinkFromMetadata(
            links,
            taskEvent,
            "memory_id",
            "Memory record",
            "Memory record",
            module);
        return links;
    }

    private static void AddLinkFromMetadata(
        List<AedaTaskArtifactLink> links,
        TaskEvent taskEvent,
        string key,
        string kind,
        string label,
        AedaTaskCenterModule module)
    {
        if (taskEvent.SafeMetadata is null ||
            !taskEvent.SafeMetadata.TryGetValue(key, out var value) ||
            string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        links.Add(new AedaTaskArtifactLink(
            $"{key}:{Bound(value, 80, "unknown")}",
            kind,
            label,
            $"{label} is linked from this event.",
            new AedaTaskCenterRoute(
                ToModuleBadge(module).RouteId,
                ToModuleBadge(module).ModuleId,
                new Dictionary<string, string> { [key] = Bound(value, 120, "unknown") }),
            IsAvailable: true));
    }

    private static AedaTaskStatusBadge ToStatusBadge(TaskRunStatus status) =>
        status switch
        {
            TaskRunStatus.Running => Badge(AedaTaskCenterStatus.Running, "Running", "task_running"),
            TaskRunStatus.WaitingForApproval => Badge(AedaTaskCenterStatus.WaitingForApproval, "Waiting for approval", "approval_needed", true),
            TaskRunStatus.Paused => Badge(AedaTaskCenterStatus.Paused, "Paused", "task_paused"),
            TaskRunStatus.Completed => Badge(AedaTaskCenterStatus.Completed, "Completed", "task_completed", false, true),
            TaskRunStatus.Failed => Badge(AedaTaskCenterStatus.Failed, "Failed", "task_failed", true, true),
            TaskRunStatus.Cancelling => Badge(AedaTaskCenterStatus.Cancelled, "Cancelling", "task_cancelling"),
            TaskRunStatus.Cancelled => Badge(AedaTaskCenterStatus.Cancelled, "Cancelled", "task_cancelled", false, true),
            _ => Badge(AedaTaskCenterStatus.Unknown, "Unknown", "task_status_unknown")
        };

    private static AedaTaskStatusBadge ToStatusBadge(
        TaskExecutionState? state,
        TaskEventKind kind)
    {
        if (kind is TaskEventKind.PatchRollbackCompleted)
        {
            return Badge(AedaTaskCenterStatus.RolledBack, "Rolled back", "task_rolled_back", false, true);
        }

        return state switch
        {
            TaskExecutionState.Running => Badge(AedaTaskCenterStatus.Running, "Running", "task_running"),
            TaskExecutionState.WaitingForApproval => Badge(AedaTaskCenterStatus.WaitingForApproval, "Waiting for approval", "approval_needed", true),
            TaskExecutionState.Paused => Badge(AedaTaskCenterStatus.Paused, "Paused", "task_paused"),
            TaskExecutionState.Completed => Badge(AedaTaskCenterStatus.Completed, "Completed", "task_completed", false, true),
            TaskExecutionState.Failed => Badge(AedaTaskCenterStatus.Failed, "Failed", "task_failed", true, true),
            TaskExecutionState.Cancelled or TaskExecutionState.Cancelling => Badge(AedaTaskCenterStatus.Cancelled, "Cancelled", "task_cancelled", false, true),
            _ => kind switch
            {
                TaskEventKind.ApprovalRequested or
                TaskEventKind.PatchProposalApprovalRequested or
                TaskEventKind.PatchApplyApprovalRequested or
                TaskEventKind.ValidationApprovalRequested or
                TaskEventKind.RemoteApprovalRequired => Badge(AedaTaskCenterStatus.WaitingForApproval, "Waiting for approval", "approval_needed", true),
                TaskEventKind.TaskFailed or
                TaskEventKind.ToolFailed or
                TaskEventKind.ValidationFailed or
                TaskEventKind.PatchApplyFailed or
                TaskEventKind.PatchDryRunFailed or
                TaskEventKind.PatchRollbackFailed or
                TaskEventKind.ResearchVerificationFailed or
                TaskEventKind.WorkspaceIndexingFailed or
                TaskEventKind.RetrievalFailed => Badge(AedaTaskCenterStatus.Failed, "Failed", "task_failed", true, true),
                TaskEventKind.TaskCancelled or
                TaskEventKind.ToolCancelled or
                TaskEventKind.ValidationCancelled or
                TaskEventKind.PatchApplyCancelled or
                TaskEventKind.ResearchVerificationCancelled or
                TaskEventKind.WorkspaceIndexingCancelled => Badge(AedaTaskCenterStatus.Cancelled, "Cancelled", "task_cancelled", false, true),
                TaskEventKind.TaskCompleted or
                TaskEventKind.ValidationSucceeded or
                TaskEventKind.PatchApplyCompleted or
                TaskEventKind.ResearchReportCreated or
                TaskEventKind.WorkspaceIndexingCompleted => Badge(AedaTaskCenterStatus.Completed, "Completed", "task_completed", false, true),
                _ => Badge(AedaTaskCenterStatus.Running, "Activity", "task_activity")
            }
        };
    }

    private static AedaTaskStatusBadge Badge(
        AedaTaskCenterStatus status,
        string label,
        string reason,
        bool needsAttention = false,
        bool isTerminal = false) =>
        new(status, label, reason, needsAttention, isTerminal);

    private static AedaTaskModuleBadge ToModuleBadge(AedaTaskCenterModule module) =>
        module switch
        {
            AedaTaskCenterModule.Chat => new(module, "Chat", AedaModuleId.Chat, "chat"),
            AedaTaskCenterModule.Code => new(module, "Code", AedaModuleId.Code, "aeda-code"),
            AedaTaskCenterModule.Memory => new(module, "Memory", AedaModuleId.Memory, "aeda-memory"),
            AedaTaskCenterModule.Research => new(module, "Research", AedaModuleId.Research, "aeda-research"),
            AedaTaskCenterModule.System => new(module, "System", null, "system"),
            _ => new(module, "Unknown", null, "task-center")
        };

    private static bool TryInferModule(string value, out AedaTaskCenterModule module)
    {
        var normalized = value.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal);
        module = normalized switch
        {
            "chat" or "main-chat" or "conversation" => AedaTaskCenterModule.Chat,
            "code" or "aeda-code" or "coding" => AedaTaskCenterModule.Code,
            "memory" or "aeda-memory" or "rag" or "workspace-indexing" => AedaTaskCenterModule.Memory,
            "research" or "aeda-research" or "verification" => AedaTaskCenterModule.Research,
            "system" or "runtime" => AedaTaskCenterModule.System,
            _ => AedaTaskCenterModule.Unknown
        };
        return module != AedaTaskCenterModule.Unknown;
    }

    private static AedaTaskCenterModule InferModule(
        ApprovalKind kind,
        string resourceScope)
    {
        if (TryInferModule(resourceScope, out var module))
        {
            return module;
        }

        return kind switch
        {
            ApprovalKind.ApprovePatchProposal or
            ApprovalKind.ApproveFutureApply or
            ApprovalKind.ValidationRun => AedaTaskCenterModule.Code,
            ApprovalKind.MemoryWrite => AedaTaskCenterModule.Memory,
            _ => AedaTaskCenterModule.Unknown
        };
    }

    private static string FriendlyTitle(TaskEventKind kind) =>
        kind switch
        {
            TaskEventKind.TaskCreated => "Task created",
            TaskEventKind.TaskStarted => "Task started",
            TaskEventKind.StepStarted => "Step started",
            TaskEventKind.PermissionRequested => "Permission requested",
            TaskEventKind.PermissionGranted => "Permission granted",
            TaskEventKind.PermissionDenied => "Permission denied",
            TaskEventKind.ToolRequested => "Tool requested",
            TaskEventKind.ToolStarted => "Tool started",
            TaskEventKind.ToolCompleted => "Tool completed",
            TaskEventKind.ToolCancelled => "Tool cancelled",
            TaskEventKind.ToolTimedOut => "Tool timed out",
            TaskEventKind.ToolFailed => "Tool failed",
            TaskEventKind.WorkspaceIndexingStarted => "Workspace indexing started",
            TaskEventKind.WorkspaceFileQueued => "Workspace file queued",
            TaskEventKind.WorkspaceFileSkipped => "Workspace file skipped",
            TaskEventKind.WorkspaceFileIndexed => "Workspace file indexed",
            TaskEventKind.WorkspaceIndexingCompleted => "Workspace indexing completed",
            TaskEventKind.WorkspaceIndexingFailed => "Workspace indexing failed",
            TaskEventKind.WorkspaceIndexingCancelled => "Workspace indexing cancelled",
            TaskEventKind.RetrievalStarted => "Retrieval started",
            TaskEventKind.RetrievalCompleted => "Retrieval completed",
            TaskEventKind.RetrievalFailed => "Retrieval failed",
            TaskEventKind.PatchProposalCreated => "Patch proposal created",
            TaskEventKind.PatchProposalValidated => "Patch proposal validated",
            TaskEventKind.PatchProposalRiskClassified => "Patch risk classified",
            TaskEventKind.PatchProposalApprovalRequested => "Waiting for proposal approval",
            TaskEventKind.PatchApplyApprovalRequested => "Waiting for patch approval",
            TaskEventKind.PatchApplyStarted => "Patch apply started",
            TaskEventKind.PatchApplyCompleted => "Patch applied",
            TaskEventKind.PatchApplyFailed => "Patch apply failed",
            TaskEventKind.PatchApplyCancelled => "Patch apply cancelled",
            TaskEventKind.PatchRollbackStarted => "Rollback started",
            TaskEventKind.PatchRollbackCompleted => "Rollback completed",
            TaskEventKind.PatchRollbackFailed => "Rollback failed",
            TaskEventKind.ValidationPlanCreated => "Validation plan created",
            TaskEventKind.ValidationRunCreated => "Validation run created",
            TaskEventKind.ValidationApprovalRequested => "Waiting for validation approval",
            TaskEventKind.ValidationStarted => "Validation started",
            TaskEventKind.ValidationOutputCaptured => "Validation output captured",
            TaskEventKind.ValidationSucceeded => "Validation succeeded",
            TaskEventKind.ValidationFailed => "Validation failed",
            TaskEventKind.ValidationTimedOut => "Validation timed out",
            TaskEventKind.ValidationCancelled => "Validation cancelled",
            TaskEventKind.ResearchVerificationRequested => "Research verification requested",
            TaskEventKind.ResearchClaimsExtracted => "Research claims extracted",
            TaskEventKind.ResearchEvidenceGathered => "Evidence gathered",
            TaskEventKind.ResearchReportCreated => "Verification report created",
            TaskEventKind.ResearchVerificationCancelled => "Research verification cancelled",
            TaskEventKind.ResearchVerificationFailed => "Research verification failed",
            TaskEventKind.ApprovalRequested => "Approval requested",
            TaskEventKind.ApprovalGranted => "Approval granted",
            TaskEventKind.ApprovalDenied => "Approval denied",
            TaskEventKind.ArtifactCreated => "Artifact created",
            TaskEventKind.TaskPaused => "Task paused",
            TaskEventKind.TaskResumed => "Task resumed",
            TaskEventKind.TaskCompleted => "Task completed",
            TaskEventKind.TaskCancelled => "Task cancelled",
            TaskEventKind.TaskFailed => "Task failed",
            _ => SplitWords(kind.ToString())
        };

    private static string FriendlySummary(TaskEvent taskEvent)
    {
        if (!string.IsNullOrWhiteSpace(taskEvent.SafeErrorCode))
        {
            return $"Safe reason: {Bound(taskEvent.SafeErrorCode, 64, "task_event")}";
        }

        var summary = Bound(taskEvent.Summary, 180, FriendlyTitle(taskEvent.Kind));
        if (summary.Contains('{', StringComparison.Ordinal) ||
            summary.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            summary.Contains("stderr", StringComparison.OrdinalIgnoreCase))
        {
            return FriendlyTitle(taskEvent.Kind);
        }

        return summary;
    }

    private static string? SafeDetail(TaskEvent taskEvent)
    {
        if (taskEvent.ProgressPercent is not null)
        {
            return taskEvent.ProgressLabel is null
                ? $"{taskEvent.ProgressPercent}%"
                : $"{taskEvent.ProgressPercent}% - {Bound(taskEvent.ProgressLabel, 80, "progress")}";
        }

        if (taskEvent.SafeMetadata is null || taskEvent.SafeMetadata.Count == 0)
        {
            return taskEvent.SafeErrorMessage is null
                ? null
                : Bound(taskEvent.SafeErrorMessage, 160, "Task event failed.");
        }

        var pairs = taskEvent.SafeMetadata
            .Where(pair => IsDisplayableMetadata(pair.Key, pair.Value))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(4)
            .Select(pair => $"{SplitWords(pair.Key)}: {Bound(pair.Value, 80, "value")}")
            .ToArray();
        return pairs.Length == 0 ? null : string.Join(" | ", pairs);
    }

    private static bool IsDisplayableMetadata(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = key.ToLowerInvariant();
        if (normalized.Contains("content", StringComparison.Ordinal) ||
            normalized.Contains("prompt", StringComparison.Ordinal) ||
            normalized.Contains("output", StringComparison.Ordinal) ||
            normalized.Contains("secret", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal))
        {
            return false;
        }

        return !Path.IsPathRooted(value);
    }

    private static string FriendlyApprovalTitle(ApprovalKind kind) =>
        kind switch
        {
            ApprovalKind.ApprovePatchProposal => "Patch proposal approval needed",
            ApprovalKind.ApproveFutureApply => "Patch apply approval needed",
            ApprovalKind.ValidationRun => "Validation run approval needed",
            ApprovalKind.MemoryWrite => "Memory write approval needed",
            _ => "Approval needed"
        };

    private static string SafeScope(ApprovalScope scope)
    {
        var resource = scope.ResourceScope.Trim();
        if (resource.Contains(':', StringComparison.Ordinal))
        {
            resource = resource[..resource.IndexOf(':', StringComparison.Ordinal)];
        }

        return $"{SplitWords(scope.Kind.ToString())}: {Bound(resource, 60, "resource")}";
    }

    private static AedaTaskCenterRoute? CreateApprovalRoute(
        ApprovalScope scope,
        AedaTaskCenterModule module) =>
        new(
            ToModuleBadge(module).RouteId,
            ToModuleBadge(module).ModuleId,
            new Dictionary<string, string>
            {
                ["approvalKind"] = scope.Kind.ToString(),
                ["taskId"] = scope.TaskId.ToString()
            });

    private static AedaTaskCenterRoute? CreateModuleRoute(AedaTaskCenterModule module)
    {
        var badge = ToModuleBadge(module);
        return badge.RouteId == "task-center"
            ? null
            : new AedaTaskCenterRoute(badge.RouteId, badge.ModuleId);
    }

    private static IReadOnlyDictionary<AedaTaskCenterStatus, int> CountByStatus(
        IEnumerable<AedaTaskSummary> tasks) =>
        tasks.GroupBy(task => task.Status.Status)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

    private static IReadOnlyDictionary<AedaTaskCenterModule, int> CountByModule(
        IEnumerable<AedaTaskSummary> tasks) =>
        tasks.GroupBy(task => task.Module.Module)
            .OrderBy(group => group.Key)
            .ToDictionary(group => group.Key, group => group.Count());

    private static IEnumerable<AedaTaskSummary> ApplyFilter(
        IEnumerable<AedaTaskSummary> tasks,
        AedaTaskFilter? filter)
    {
        var query = tasks;
        if (filter?.Module is not null)
        {
            query = query.Where(task => task.Module.Module == filter.Module.Value);
        }

        if (filter?.Status is not null)
        {
            query = query.Where(task => task.Status.Status == filter.Status.Value);
        }

        return query.Take(BoundLimit(filter?.Limit ?? MaxDashboardLimit, MaxDashboardLimit));
    }

    private static string GetPhase(AedaTaskCenterStatus status) =>
        status switch
        {
            AedaTaskCenterStatus.WaitingForApproval or AedaTaskCenterStatus.NeedsAttention => "Needs Attention",
            AedaTaskCenterStatus.Failed or AedaTaskCenterStatus.Cancelled => "Problems",
            AedaTaskCenterStatus.Completed or AedaTaskCenterStatus.RolledBack => "Completed",
            _ => "Activity"
        };

    private static int BoundLimit(int limit, int max) => Math.Clamp(limit, 1, max);

    private static string Bound(string? value, int maxLength, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        text = text.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        return text.Length <= maxLength ? text : text[..maxLength];
    }

    private static string SplitWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current is '_' or '-')
            {
                result.Add(' ');
                continue;
            }

            if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1]))
            {
                result.Add(' ');
            }

            result.Add(current);
        }

        return new string(result.ToArray());
    }
}
