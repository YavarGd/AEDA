using PersonalAI.Core.Approvals;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Coding;

public readonly record struct AedaCodeSessionId(Guid Value)
{
    public static AedaCodeSessionId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum AedaCodeSessionStatus
{
    Active,
    WaitingForApproval,
    Completed,
    Failed,
    Cancelled
}

public sealed record AedaCodeWorkspaceSummary(
    WorkspaceId WorkspaceId,
    string DisplayName,
    bool IsReadOnly,
    int? ImmediateFileCount = null,
    int? ImmediateDirectoryCount = null);

public sealed record AedaCodeTaskSummary(
    TaskId TaskId,
    string Title,
    string Status,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaCodeArtifactSummary(
    string Kind,
    string Title,
    string SafeReference);

public sealed record AedaCodeSession(
    AedaCodeSessionId Id,
    WorkspaceId WorkspaceId,
    string WorkspaceDisplayName,
    TaskId? CurrentTaskId,
    PatchProposalId? ActiveProposalId,
    PatchApplyResultId? ActiveApplyResultId,
    ValidationRunId? ActiveValidationRunId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    AedaCodeSessionStatus Status,
    string SafeSummary);

public sealed record AedaCodeRiskBadge(
    PatchProposalRisk Risk,
    string Label,
    string SafeReason);

public sealed record AedaCodeApprovalState(
    bool ApprovalRequired,
    string Label,
    string? SafeReasonCode = null);

public sealed record AedaCodeProposalSummary(
    PatchProposalId ProposalId,
    string Title,
    PatchProposalStatus Status,
    AedaCodeRiskBadge Risk,
    IReadOnlyList<string> RelativePaths,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaCodeApplySummary(
    PatchApplyResultId ApplyResultId,
    PatchProposalId ProposalId,
    PatchApplyStatus Status,
    int FileCount,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaCodeValidationSummary(
    ValidationRunId RunId,
    string TemplateId,
    ValidationRunStatus Status,
    PatchProposalId? ProposalId,
    PatchApplyResultId? ApplyResultId,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaCodeTimelineItem(
    DateTimeOffset TimestampUtc,
    string Kind,
    string Summary);

public sealed record AedaCodeDashboardModel(
    AedaCodeSession Session,
    AedaCodeWorkspaceSummary Workspace,
    IReadOnlyList<AedaCodeProposalSummary> Proposals,
    IReadOnlyList<AedaCodeApplySummary> ApplyResults,
    IReadOnlyList<AedaCodeValidationSummary> ValidationRuns,
    IReadOnlyList<AedaCodeTimelineItem> Timeline);

public sealed record ModuleSuggestion(
    bool ShouldSuggest,
    string ModuleId,
    string Message,
    bool AutoLaunch);

public interface IModuleSuggestionService
{
    ModuleSuggestion Suggest(string userText);
}

public interface IAedaCodeModuleService
{
    Task<AedaCodeSession> StartSessionAsync(
        WorkspaceId workspaceId,
        string? safeSummary = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<CodeContextPack> ReadFilesAsync(
        WorkspaceId workspaceId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default);

    Task<CodeContextPack> SearchAsync(
        CodeContextSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<CodeChangePlan> CreatePlanAsync(
        CodeChangeRequest request,
        CodeContextPack context,
        CancellationToken cancellationToken = default);

    Task<PatchProposal> CreateProposalAsync(
        PatchProposalCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PatchProposal?> GetProposalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
        WorkspaceId workspaceId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<PatchApplyPlan> DryRunApplyAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> RequestApplyApprovalAsync(
        PatchProposalId proposalId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<PatchApplyResult> ApplyApprovedProposalAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId applyResultId,
        CancellationToken cancellationToken = default);

    Task<PatchRollbackResult> RollbackAsync(
        PatchRollbackRequest request,
        CancellationToken cancellationToken = default);

    Task<ValidationRun> CreateValidationRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> RequestValidationApprovalAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default);

    Task<ValidationRun> RunApprovedValidationAsync(
        ValidationRunId runId,
        ApprovalRequest approvalRequest,
        ApprovalDecision approvalDecision,
        CancellationToken cancellationToken = default);

    Task<AedaCodeDashboardModel> GetDashboardAsync(
        AedaCodeSessionId sessionId,
        CancellationToken cancellationToken = default);
}
