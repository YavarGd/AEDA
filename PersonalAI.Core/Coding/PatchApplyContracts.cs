using PersonalAI.Core.Approvals;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Coding;

public readonly record struct PatchApplyResultId(Guid Value)
{
    public static PatchApplyResultId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public readonly record struct PatchRollbackResultId(Guid Value)
{
    public static PatchRollbackResultId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum PatchApplyStatus
{
    NotStarted,
    DryRunPassed,
    DryRunFailed,
    WaitingForApproval,
    Applying,
    Applied,
    PartiallyApplied,
    Failed,
    Cancelled,
    RolledBack,
    RollbackFailed
}

public enum PatchApplyFailureReason
{
    ApprovalMissing,
    ApprovalDenied,
    ProposalNotFound,
    ProposalNotReady,
    WorkspaceNotRegistered,
    PathOutsideWorkspace,
    StaleOriginalContent,
    HashMismatch,
    BinaryUnsupported,
    LargeFileRejected,
    UnsafeLargeDeletion,
    DeleteNotAllowed,
    WriteFailed,
    BackupFailed,
    Cancelled,
    UnknownSafeFailure
}

public enum PatchApplyMode
{
    DryRun,
    Apply
}

public sealed record PatchApplyRequest(
    PatchProposalId ProposalId,
    WorkspaceId WorkspaceId,
    ApprovalRequest? ApprovalRequest = null,
    ApprovalDecision? ApprovalDecision = null);

public sealed record PatchRollbackRequest(
    PatchApplyResultId ApplyResultId,
    WorkspaceId WorkspaceId);

public sealed record PatchApplyOperation(
    string RelativePath,
    PatchProposalFileChangeKind ChangeKind,
    string OriginalContentHash,
    string ProposedContentHash);

public sealed record PatchApplyBackup(
    PatchApplyResultId ApplyResultId,
    PatchProposalId ProposalId,
    WorkspaceId WorkspaceId,
    string RelativePath,
    string OriginalContent,
    string OriginalContentHash,
    string AppliedContentHash,
    DateTimeOffset CreatedAtUtc,
    PatchProposalFileChangeKind OperationKind,
    string EncodingName = "utf-8");

public sealed record PatchApplyPlan(
    PatchProposalId ProposalId,
    WorkspaceId WorkspaceId,
    PatchApplyStatus Status,
    IReadOnlyList<PatchApplyOperation> Operations,
    IReadOnlyList<PatchApplyFailureReason> FailureReasons,
    bool RequiresApproval);

public sealed record PatchApplyFileResult(
    string RelativePath,
    PatchProposalFileChangeKind ChangeKind,
    PatchApplyStatus Status,
    PatchApplyFailureReason? FailureReason = null);

public sealed record PatchApplyResult(
    PatchApplyResultId Id,
    PatchProposalId ProposalId,
    WorkspaceId WorkspaceId,
    PatchApplyStatus Status,
    IReadOnlyList<PatchApplyFileResult> Files,
    IReadOnlyList<PatchApplyFailureReason> FailureReasons,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record PatchRollbackResult(
    PatchRollbackResultId Id,
    PatchApplyResultId ApplyResultId,
    WorkspaceId WorkspaceId,
    PatchApplyStatus Status,
    IReadOnlyList<PatchApplyFileResult> Files,
    IReadOnlyList<PatchApplyFailureReason> FailureReasons,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public interface IPatchApplyValidator
{
    Task<PatchApplyPlan> DryRunAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default);
}

public interface IPatchApplyRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateApplyResultAsync(
        PatchApplyResult result,
        IReadOnlyList<PatchApplyBackup> backups,
        CancellationToken cancellationToken = default);

    Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchApplyBackup>> ListBackupsAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default);

    Task CreateRollbackResultAsync(
        PatchRollbackResult result,
        CancellationToken cancellationToken = default);

    Task<PatchRollbackResult?> GetRollbackResultAsync(
        PatchRollbackResultId resultId,
        CancellationToken cancellationToken = default);
}

public interface IPatchApplyService
{
    Task<PatchApplyPlan> DryRunAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> RequestApplyApprovalAsync(
        PatchProposalId proposalId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<PatchApplyResult> ApplyAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default);

    Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<PatchRollbackResult> RollbackAsync(
        PatchRollbackRequest request,
        CancellationToken cancellationToken = default);
}
