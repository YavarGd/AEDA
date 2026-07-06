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

public sealed record AedaCodeContextFileCandidate(
    WorkspaceId WorkspaceId,
    string RelativePath,
    string FileName,
    string ContainingFolder,
    string Extension,
    string Language,
    string SizeLabel,
    long? SizeBytes,
    bool IsReadable,
    bool IsAlreadySelected,
    string? SafeReason);

public sealed record AedaCodeSelectedContextFile(
    string RelativePath,
    string FileName,
    string ContainingFolder,
    string Extension,
    string SizeLabel,
    long? SizeBytes,
    bool IsReadable,
    bool IsTruncated,
    int ApproximateCharacters,
    string? SafeReason);

public sealed record AedaCodeContextSearchRequest(
    WorkspaceId WorkspaceId,
    string Query,
    IReadOnlyList<string>? SelectedRelativePaths = null,
    int MaxResults = 50);

public sealed record AedaCodeContextSearchResult(
    WorkspaceId WorkspaceId,
    IReadOnlyList<AedaCodeContextFileCandidate> Candidates,
    bool IsTruncated,
    IReadOnlyList<string> SkippedSafeReasons);

public sealed record AedaCodeTargetSnippetCandidate(
    string Id,
    string RelativePath,
    string DisplayName,
    string SignaturePreview,
    int StartLine,
    int LineCount,
    int ApproximateCharacters,
    bool AlreadyHasXmlDocumentation,
    string SafePreview);

public sealed record AedaCodeTargetSnippetRequest(
    WorkspaceId WorkspaceId,
    IReadOnlyList<string> SelectedRelativePaths);

public sealed record AedaCodeSelectedTargetSnippet(
    string Id,
    string RelativePath);

public sealed record AedaCodeProposalContextSelection(
    IReadOnlyList<string> RelativePaths,
    AedaCodeSelectedTargetSnippet? SelectedTargetSnippet = null);

public sealed record AedaCodeProposalCreationRequest(
    WorkspaceId WorkspaceId,
    string UserRequest,
    string? OptionalTitle = null,
    AedaCodeProposalContextSelection? ContextSelection = null);

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
    SelectedContextUnavailable,
    SelectedContextTooLarge,
    PartialProposedContent,
    UnsafeLargeDeletion,
    InvalidFileShape,
    TargetTextNotFound,
    AmbiguousTextReplacement,
    SelectedTargetStale,
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
                "Select one or more files or make your request more specific."),
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
                "The coding model took too long.",
                "No files were changed. Try a smaller selected file or a more specific request."),
            AedaCodeProposalCreationFailureReason.ModelCancelled => new(
                reason,
                "model_cancelled",
                "Proposal creation was cancelled before the model finished.",
                "Try again when you are ready."),
            AedaCodeProposalCreationFailureReason.InvalidModelJson => new(
                reason,
                "invalid_model_json",
                "The model did not return valid JSON.",
                "Retry usually helps. No files were changed."),
            AedaCodeProposalCreationFailureReason.InvalidModelSchema => new(
                reason,
                "invalid_model_schema",
                "The model returned JSON, but it did not match AEDA's safe proposal format.",
                "No files were changed. Retry or make the request more explicit.",
                schemaIssueCode,
                retryAttempted),
            AedaCodeProposalCreationFailureReason.UnsafeFileTarget => new(
                reason,
                "unsafe_file_target",
                "The proposed change targeted a file that was not uniquely available in the bounded context.",
                "Select the target file in context or use its exact relative path."),
            AedaCodeProposalCreationFailureReason.SelectedContextUnavailable => new(
                reason,
                "selected_context_unavailable",
                "One selected file is no longer available.",
                "Remove it or refresh context, then try again."),
            AedaCodeProposalCreationFailureReason.SelectedContextTooLarge => new(
                reason,
                "selected_context_too_large",
                "Selected files exceed the context budget.",
                "Remove files or choose smaller ones."),
            AedaCodeProposalCreationFailureReason.PartialProposedContent => new(
                reason,
                "partial_proposed_content",
                "AEDA blocked a partial proposal.",
                "The model returned only a small snippet instead of a safe file edit. No files were changed."),
            AedaCodeProposalCreationFailureReason.UnsafeLargeDeletion => new(
                reason,
                "unsafe_large_deletion",
                "AEDA blocked a proposal that looked like a large destructive deletion.",
                "No files were changed. Try a more specific request or select a smaller file."),
            AedaCodeProposalCreationFailureReason.InvalidFileShape => new(
                reason,
                "invalid_file_shape",
                "AEDA blocked this proposal because the generated content did not match the target file type.",
                "No files were changed. Try again with the exact selected source file."),
            AedaCodeProposalCreationFailureReason.TargetTextNotFound => new(
                reason,
                "target_text_not_found",
                "The model picked text that no longer matches the file.",
                "No files were changed. Refresh context or choose the method again."),
            AedaCodeProposalCreationFailureReason.AmbiguousTextReplacement => new(
                reason,
                "ambiguous_text_replacement",
                "AEDA found the model's target text more than once.",
                "No files were changed. Try naming a more specific helper method."),
            AedaCodeProposalCreationFailureReason.SelectedTargetStale => new(
                reason,
                "selected_target_stale",
                "The selected method changed or could not be revalidated.",
                "Refresh context and choose the method again."),
            AedaCodeProposalCreationFailureReason.UnsafePatch => new(
                reason,
                "unsafe_patch",
                "The model tried to change content outside AEDA's safe proposal rules.",
                "AEDA blocked it. No files were changed."),
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

    Task<AedaCodeContextSearchResult> SearchContextFilesAsync(
        AedaCodeContextSearchRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaCodeTargetSnippetCandidate>> ListTargetSnippetCandidatesAsync(
        AedaCodeTargetSnippetRequest request,
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
