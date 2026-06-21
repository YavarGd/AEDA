using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class ValidationRunnerService(
    IValidationRunRepository repository,
    IValidationCommandAllowlist allowlist,
    IControlledProcessRunner processRunner,
    IWorkspaceReader workspaceReader,
    IApprovalCheckpointStore approvalStore,
    ITaskRuntime? taskRuntime = null) : IValidationRunnerService
{
    private const int MaxOutputCharacters = 20_000;

    public async Task<ValidationRun> CreateRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var run = CreateInitialRun(request, ValidationRunStatus.Created, []);
        await repository.CreateAsync(run, cancellationToken);
        await AppendAsync(TaskEventKind.ValidationRunCreated, "Validation run created.", cancellationToken);
        return run;
    }

    public async Task<ValidationRun> DryRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default)
    {
        var (status, failures, _) = ValidateRequest(request);
        return CreateInitialRun(
            request,
            status == ValidationRunStatus.Created ? ValidationRunStatus.Created : status,
            failures);
    }

    public async Task<ApprovalRequest> RequestApprovalAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(runId, cancellationToken) ??
            throw new InvalidOperationException("validation_run_not_found");
        var approval = ApprovalRequest.Create(
            CreateScope(run.Id, run.WorkspaceId),
            "Approve validation run",
            $"Run validation template '{run.TemplateId}' in workspace.");
        await approvalStore.RequestAsync(approval, cancellationToken);
        await repository.UpdateAsync(run with
        {
            Status = ValidationRunStatus.WaitingForApproval,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        }, cancellationToken);
        await AppendAsync(TaskEventKind.ValidationApprovalRequested, "Validation approval requested.", cancellationToken);
        return approval;
    }

    public async Task<ValidationRun> ExecuteAsync(
        ValidationRunId runId,
        ApprovalRequest approvalRequest,
        ApprovalDecision approvalDecision,
        CancellationToken cancellationToken = default)
    {
        var run = await repository.GetAsync(runId, cancellationToken) ??
            throw new InvalidOperationException("validation_run_not_found");
        var approvalFailure = ValidateApproval(run, approvalRequest, approvalDecision);
        if (approvalFailure is not null)
        {
            var rejected = run with
            {
                Status = approvalFailure == ValidationFailureReason.ApprovalDenied
                    ? ValidationRunStatus.Rejected
                    : ValidationRunStatus.Blocked,
                FailureReasons = [approvalFailure.Value],
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await repository.UpdateAsync(rejected, cancellationToken);
            await AppendAsync(
                rejected.Status == ValidationRunStatus.Rejected ? TaskEventKind.ValidationApprovalDenied : TaskEventKind.ValidationFailed,
                "Validation approval rejected.",
                cancellationToken);
            return rejected;
        }

        var request = new ValidationRunRequest(
            run.WorkspaceId,
            run.TemplateId,
            run.SafeWorkingDirectoryLabel,
            run.ProposalId,
            run.ApplyResultId,
            approvalRequest,
            approvalDecision);
        var (status, failures, command) = ValidateRequest(request);
        if (status == ValidationRunStatus.Blocked || command is null)
        {
            var blocked = run with
            {
                Status = ValidationRunStatus.Blocked,
                FailureReasons = failures,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            await repository.UpdateAsync(blocked, cancellationToken);
            await AppendAsync(TaskEventKind.ValidationFailed, "Validation blocked.", cancellationToken);
            return blocked;
        }

        await AppendAsync(TaskEventKind.ValidationApprovalGranted, "Validation approval granted.", cancellationToken);
        var running = run with
        {
            Status = ValidationRunStatus.Running,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await repository.UpdateAsync(running, cancellationToken);
        await AppendAsync(TaskEventKind.ValidationStarted, "Validation started.", cancellationToken);

        var workspace = workspaceReader.GetWorkspace(run.WorkspaceId);
        var processResult = await processRunner.RunAsync(
            new ControlledProcessRequest(
                command.Executable,
                command.Arguments,
                Path.GetFullPath(Path.Combine(workspace.CanonicalRootPath, command.WorkingDirectoryLabel)),
                command.Timeout,
                MaxOutputCharacters),
            cancellationToken);
        var result = CreateCommandResult(processResult);
        var completed = running with
        {
            Status = result.Status,
            CommandResult = result,
            FailureReasons = result.FailureReason is null ? [] : [result.FailureReason.Value],
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await repository.UpdateAsync(completed, cancellationToken);
        await AppendAsync(TaskEventKind.ValidationOutputCaptured, "Validation output captured.", cancellationToken);
        await AppendAsync(MapEvent(completed.Status), MapSummary(completed.Status), cancellationToken);
        return completed;
    }

    public Task<ValidationRun?> GetRunAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(runId, cancellationToken);

    public Task<IReadOnlyList<ValidationRun>> ListRecentAsync(
        WorkspaceId workspaceId,
        PatchProposalId? proposalId = null,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        repository.ListRecentAsync(workspaceId, proposalId, limit, cancellationToken);

    private (ValidationRunStatus Status, IReadOnlyList<ValidationFailureReason> Failures, ValidationCommand? Command)
        ValidateRequest(ValidationRunRequest request)
    {
        WorkspaceDescriptor workspace;
        try
        {
            workspace = workspaceReader.GetWorkspace(request.WorkspaceId);
        }
        catch (WorkspaceAccessException)
        {
            return (ValidationRunStatus.Blocked, [ValidationFailureReason.WorkspaceNotRegistered], null);
        }

        return allowlist.TryCreateCommand(request, workspace, out var command, out var failure)
            ? (ValidationRunStatus.Created, [], command)
            : (ValidationRunStatus.Blocked, [failure], null);
    }

    private static ValidationCommandResult CreateCommandResult(
        ControlledProcessResult result)
    {
        var stdout = ValidationOutputSanitizer.Sanitize(result.Stdout, MaxOutputCharacters);
        var stderr = ValidationOutputSanitizer.Sanitize(result.Stderr, MaxOutputCharacters);
        result = result with
        {
            Stdout = stdout.Text,
            Stderr = stderr.Text,
            StdoutTruncated = result.StdoutTruncated || stdout.Truncated,
            StderrTruncated = result.StderrTruncated || stderr.Truncated
        };

        if (result.Cancelled)
        {
            return ToResult(ValidationRunStatus.Cancelled, ValidationFailureReason.Cancelled, result);
        }

        if (result.TimedOut)
        {
            return ToResult(ValidationRunStatus.TimedOut, ValidationFailureReason.Timeout, result);
        }

        if (result.StartFailed)
        {
            return ToResult(ValidationRunStatus.Failed, ValidationFailureReason.ProcessStartFailed, result);
        }

        if (result.StdoutTruncated || result.StderrTruncated)
        {
            return ToResult(ValidationRunStatus.Failed, ValidationFailureReason.OutputLimitExceeded, result);
        }

        return ToResult(
            result.ExitCode == 0 ? ValidationRunStatus.Succeeded : ValidationRunStatus.Failed,
            result.ExitCode == 0 ? null : ValidationFailureReason.UnknownSafeFailure,
            result);
    }

    private static ValidationCommandResult ToResult(
        ValidationRunStatus status,
        ValidationFailureReason? reason,
        ControlledProcessResult result) =>
        new(
            result.ExitCode,
            status,
            new ValidationOutputChunk(result.Stdout, result.StdoutTruncated),
            new ValidationOutputChunk(result.Stderr, result.StderrTruncated),
            result.Duration,
            reason);

    private static ValidationFailureReason? ValidateApproval(
        ValidationRun run,
        ApprovalRequest request,
        ApprovalDecision decision)
    {
        if (request.Scope.NormalizedResourceScope !=
            CreateScope(run.Id, run.WorkspaceId).NormalizedResourceScope)
        {
            return ValidationFailureReason.ApprovalMissing;
        }

        return decision.IsAllowed ? null : ValidationFailureReason.ApprovalDenied;
    }

    private static ValidationRun CreateInitialRun(
        ValidationRunRequest request,
        ValidationRunStatus status,
        IReadOnlyList<ValidationFailureReason> failures)
    {
        var now = DateTimeOffset.UtcNow;
        return new ValidationRun(
            ValidationRunId.NewId(),
            request.WorkspaceId,
            request.TemplateId,
            request.RelativeWorkingDirectory,
            status,
            request.ProposalId,
            request.ApplyResultId,
            null,
            failures,
            now,
            now);
    }

    private static ApprovalScope CreateScope(ValidationRunId runId, WorkspaceId workspaceId) =>
        new(TaskId.NewId(), ApprovalKind.ValidationRun, $"validation-run:{workspaceId}:{runId}");

    private static TaskEventKind MapEvent(ValidationRunStatus status) =>
        status switch
        {
            ValidationRunStatus.Succeeded => TaskEventKind.ValidationSucceeded,
            ValidationRunStatus.TimedOut => TaskEventKind.ValidationTimedOut,
            ValidationRunStatus.Cancelled => TaskEventKind.ValidationCancelled,
            _ => TaskEventKind.ValidationFailed
        };

    private static string MapSummary(ValidationRunStatus status) =>
        status switch
        {
            ValidationRunStatus.Succeeded => "Validation succeeded.",
            ValidationRunStatus.TimedOut => "Validation timed out.",
            ValidationRunStatus.Cancelled => "Validation cancelled.",
            _ => "Validation failed."
        };

    private async ValueTask AppendAsync(
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (taskRuntime is null)
        {
            return;
        }

        try
        {
            await taskRuntime.AppendEventAsync(TaskId.NewId(), kind, summary, cancellationToken);
        }
        catch
        {
        }
    }
}
