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

public sealed record AedaCodeProposalCreationRequest(
    WorkspaceId WorkspaceId,
    string UserRequest,
    string? OptionalTitle = null);

public enum AedaCodeProposalCreationPhase
{
    Idle,
    PreparingRequest,
    LoadingBoundedContext,
    CallingCodingModel,
    ParsingModelDraft,
    RetryingStructuredDraft,
    ValidatingProposal,
    SavingProposal,
    Succeeded,
    Failed,
    Cancelled
}

public sealed record AedaCodeProposalCreationProgress(
    AedaCodeProposalCreationPhase Phase,
    int? SafeContextFileCount = null,
    bool RetryAttempted = false,
    string? SchemaIssueCode = null,
    string? SafeProviderLabel = null);

public enum AedaCodeProposalCreationFailureReason
{
    WorkspaceMissing,
    WorkspaceUnavailable,
    RequestEmpty,
    RequestTooLong,
    NoSafeContext,
    ProviderUnavailable,
    ProviderRejectedByPolicy,
    ModelTimeout,
    ModelCancelled,
    InvalidModelJson,
    InvalidModelSchema,
    UnsafeFileTarget,
    UnsafePatch,
    ProposalValidationFailed,
    ProposalPersistenceFailed,
    UnknownSafeFailure
}

public sealed record AedaCodeProposalCreationFailure(
    AedaCodeProposalCreationFailureReason Reason,
    string SafeCode,
    string UserMessage,
    string NextStepHint,
    string? SchemaIssueCode = null,
    bool RetryAttempted = false)
{
    public static AedaCodeProposalCreationFailure FromReason(
        AedaCodeProposalCreationFailureReason reason,
        string? schemaIssueCode = null,
        bool retryAttempted = false) =>
        reason switch
        {
            AedaCodeProposalCreationFailureReason.WorkspaceMissing => new(
                reason,
                "workspace_missing",
                "No registered workspace was selected.",
                "Select a registered workspace, then try creating the proposal again."),
            AedaCodeProposalCreationFailureReason.WorkspaceUnavailable => new(
                reason,
                "workspace_unavailable",
                "The selected workspace could not be read safely.",
                "Refresh the workspace list or re-register the workspace."),
            AedaCodeProposalCreationFailureReason.RequestEmpty => new(
                reason,
                "request_empty",
                "Enter a coding request first.",
                "Describe one explicit code change, then create the proposal."),
            AedaCodeProposalCreationFailureReason.RequestTooLong => new(
                reason,
                "request_too_long",
                "The coding request is too long.",
                "Shorten the request to one focused change."),
            AedaCodeProposalCreationFailureReason.NoSafeContext => new(
                reason,
                "no_safe_context",
                "No safe context was available.",
                "Try selecting a more specific workspace or use VS Code to send a file or selection first."),
            AedaCodeProposalCreationFailureReason.ProviderUnavailable => new(
                reason,
                "provider_unavailable",
                "No coding model was available.",
                "Check that qwen2.5-coder:7b or another configured coding model is installed."),
            AedaCodeProposalCreationFailureReason.ProviderRejectedByPolicy => new(
                reason,
                "provider_rejected_by_policy",
                "Provider privacy policy blocked this request.",
                "Use a local coding model or adjust provider privacy settings for workspace context."),
            AedaCodeProposalCreationFailureReason.ModelTimeout => new(
                reason,
                "model_timeout",
                "The coding model did not finish in time.",
                "Try a smaller, more specific request."),
            AedaCodeProposalCreationFailureReason.ModelCancelled => new(
                reason,
                "model_cancelled",
                "Proposal creation was cancelled before the model finished.",
                "Try again when you are ready."),
            AedaCodeProposalCreationFailureReason.InvalidModelJson => new(
                reason,
                "invalid_model_json",
                "The model response was not valid proposal JSON.",
                "Try a smaller, more specific request."),
            AedaCodeProposalCreationFailureReason.InvalidModelSchema => new(
                reason,
                "invalid_model_schema",
                "The model response did not match the proposal schema.",
                "AEDA retried once when possible; try a more explicit request that names the intended change.",
                schemaIssueCode,
                retryAttempted),
            AedaCodeProposalCreationFailureReason.UnsafeFileTarget => new(
                reason,
                "unsafe_file_target",
                "The proposed change targeted a file that was not uniquely available in the bounded context.",
                "Try naming the exact relative path, or make the request more specific if multiple files share that name."),
            AedaCodeProposalCreationFailureReason.UnsafePatch => new(
                reason,
                "unsafe_patch",
                "The proposed patch did not pass safety checks.",
                "Try a smaller change that modifies existing text only."),
            AedaCodeProposalCreationFailureReason.ProposalValidationFailed => new(
                reason,
                "proposal_validation_failed",
                "AEDA rejected the generated diff during proposal validation.",
                "Try a smaller request or include a more specific target file."),
            AedaCodeProposalCreationFailureReason.ProposalPersistenceFailed => new(
                reason,
                "proposal_persistence_failed",
                "The proposal could not be saved safely.",
                "Try again after refreshing AEDA Code."),
            _ => new(
                AedaCodeProposalCreationFailureReason.UnknownSafeFailure,
                "unknown_safe_failure",
                "Proposal creation failed safely.",
                "Try again with a smaller request or a more specific target file.")
        };
}

public sealed class AedaCodeProposalCreationException : InvalidOperationException
{
    public AedaCodeProposalCreationException(
        AedaCodeProposalCreationFailure failure,
        Exception? innerException = null)
        : base(failure.SafeCode, innerException)
    {
        Failure = failure;
    }

    public AedaCodeProposalCreationFailure Failure { get; }
}

public sealed record AedaCodeProposalCreationResult(
    PatchProposal Proposal,
    AedaCodeProposalSummary Summary,
    IReadOnlyList<string> ContextRelativePaths,
    IReadOnlyList<string> SafeNotices);

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

    Task<AedaCodeProposalCreationResult> CreateProposalFromRequestAsync(
        AedaCodeProposalCreationRequest request,
        IProgress<AedaCodeProposalCreationProgress>? progress = null,
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
