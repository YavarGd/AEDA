using PersonalAI.Core.Approvals;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Coding;

public readonly record struct ValidationRunId(Guid Value)
{
    public static ValidationRunId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum ValidationRunStatus
{
    Created,
    WaitingForApproval,
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    Rejected,
    Blocked
}

public enum ValidationFailureReason
{
    ApprovalMissing,
    ApprovalDenied,
    CommandNotAllowed,
    WorkspaceNotRegistered,
    WorkingDirectoryOutsideWorkspace,
    Timeout,
    Cancelled,
    ProcessStartFailed,
    OutputLimitExceeded,
    UnsafeArgument,
    UnknownSafeFailure
}

public sealed record ValidationCommandTemplate(
    string Id,
    string DisplayName,
    string Executable,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout,
    string RequiredRelativePath);

public sealed record ValidationCommand(
    string TemplateId,
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectoryLabel,
    TimeSpan Timeout);

public sealed record ValidationRunRequest(
    WorkspaceId WorkspaceId,
    string TemplateId,
    string RelativeWorkingDirectory = ".",
    PatchProposalId? ProposalId = null,
    PatchApplyResultId? ApplyResultId = null,
    ApprovalRequest? ApprovalRequest = null,
    ApprovalDecision? ApprovalDecision = null);

public sealed record ValidationOutputChunk(
    string Text,
    bool IsTruncated);

public sealed record ValidationCommandResult(
    int? ExitCode,
    ValidationRunStatus Status,
    ValidationOutputChunk Stdout,
    ValidationOutputChunk Stderr,
    TimeSpan Duration,
    ValidationFailureReason? FailureReason = null);

public sealed record ValidationRun(
    ValidationRunId Id,
    WorkspaceId WorkspaceId,
    string TemplateId,
    string SafeWorkingDirectoryLabel,
    ValidationRunStatus Status,
    PatchProposalId? ProposalId,
    PatchApplyResultId? ApplyResultId,
    ValidationCommandResult? CommandResult,
    IReadOnlyList<ValidationFailureReason> FailureReasons,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record ControlledProcessRequest(
    string Executable,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    TimeSpan Timeout,
    int MaxOutputCharacters);

public sealed record ControlledProcessResult(
    int? ExitCode,
    bool TimedOut,
    bool Cancelled,
    bool StartFailed,
    string Stdout,
    string Stderr,
    bool StdoutTruncated,
    bool StderrTruncated,
    TimeSpan Duration);

public interface IValidationCommandAllowlist
{
    IReadOnlyList<ValidationCommandTemplate> ListTemplates();

    bool TryCreateCommand(
        ValidationRunRequest request,
        WorkspaceDescriptor workspace,
        out ValidationCommand command,
        out ValidationFailureReason failureReason);
}

public interface IControlledProcessRunner
{
    Task<ControlledProcessResult> RunAsync(
        ControlledProcessRequest request,
        CancellationToken cancellationToken = default);
}

public interface IValidationRunRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(
        ValidationRun run,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        ValidationRun run,
        CancellationToken cancellationToken = default);

    Task<ValidationRun?> GetAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationRun>> ListRecentAsync(
        WorkspaceId workspaceId,
        PatchProposalId? proposalId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

public interface IValidationRunnerService
{
    Task<ValidationRun> CreateRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ValidationRun> DryRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> RequestApprovalAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default);

    Task<ValidationRun> ExecuteAsync(
        ValidationRunId runId,
        ApprovalRequest approvalRequest,
        ApprovalDecision approvalDecision,
        CancellationToken cancellationToken = default);

    Task<ValidationRun?> GetRunAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ValidationRun>> ListRecentAsync(
        WorkspaceId workspaceId,
        PatchProposalId? proposalId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}
