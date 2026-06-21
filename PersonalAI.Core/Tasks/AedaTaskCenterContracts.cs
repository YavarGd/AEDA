using PersonalAI.Core.Approvals;
using PersonalAI.Core.Modules;

namespace PersonalAI.Core.Tasks;

public enum AedaTaskCenterStatus
{
    Running,
    WaitingForApproval,
    Paused,
    Completed,
    Failed,
    Cancelled,
    NeedsAttention,
    RolledBack,
    Unknown
}

public enum AedaTaskCenterModule
{
    Chat,
    Code,
    Memory,
    Research,
    System,
    Unknown
}

public sealed record AedaTaskStatusBadge(
    AedaTaskCenterStatus Status,
    string Label,
    string SafeReasonCode,
    bool NeedsAttention,
    bool IsTerminal);

public sealed record AedaTaskModuleBadge(
    AedaTaskCenterModule Module,
    string Label,
    AedaModuleId? ModuleId,
    string RouteId);

public sealed record AedaTaskCenterRoute(
    string RouteId,
    AedaModuleId? ModuleId = null,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record AedaTaskArtifactLink(
    string Id,
    string Kind,
    string Label,
    string SafeSummary,
    AedaTaskCenterRoute? Route,
    bool IsAvailable,
    string? SafeUnavailableReason = null);

public sealed record AedaTaskApprovalSummary(
    string Id,
    string Title,
    string SafeScope,
    AedaTaskModuleBadge Module,
    DateTimeOffset RequestedAtUtc,
    AedaTaskCenterRoute? Route,
    string SafeSummary);

public sealed record AedaTaskTimelineItem(
    string Id,
    TaskId TaskId,
    DateTimeOffset TimestampUtc,
    string Title,
    string Summary,
    string? Detail,
    AedaTaskStatusBadge Status,
    AedaTaskModuleBadge Module,
    IReadOnlyList<AedaTaskArtifactLink> Links);

public sealed record AedaTaskActivityGroup(
    string Id,
    string Title,
    IReadOnlyList<AedaTaskTimelineItem> Items);

public sealed record AedaTaskSummary(
    TaskId Id,
    string Title,
    AedaTaskStatusBadge Status,
    AedaTaskModuleBadge Module,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string SafeSummary,
    IReadOnlyList<AedaTaskArtifactLink> ArtifactLinks);

public sealed record AedaTaskFilter(
    AedaTaskCenterModule? Module = null,
    AedaTaskCenterStatus? Status = null,
    int Limit = 25);

public sealed record AedaTaskCenterDashboard(
    IReadOnlyList<AedaTaskSummary> ActiveTasks,
    IReadOnlyList<AedaTaskApprovalSummary> WaitingApprovals,
    IReadOnlyList<AedaTaskSummary> RecentTasks,
    IReadOnlyList<AedaTaskSummary> FailedOrCancelledTasks,
    IReadOnlyDictionary<AedaTaskCenterStatus, int> CountsByStatus,
    IReadOnlyDictionary<AedaTaskCenterModule, int> CountsByModule,
    DateTimeOffset LoadedAtUtc,
    string SafeStatusMessage);

public interface IAedaTaskCenterService
{
    ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(
        AedaTaskFilter? filter = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(
        AedaTaskCenterModule module,
        int limit,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(
        TaskId taskId,
        int limit = 100,
        CancellationToken cancellationToken = default);

    ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(
        TaskId taskId,
        Guid eventId,
        CancellationToken cancellationToken = default);

    ValueTask CancelTaskAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        CancellationToken cancellationToken = default);
}
