using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class PatchApplyService(
    IPatchProposalRepository proposalRepository,
    IPatchApplyRepository applyRepository,
    IPatchApplyValidator validator,
    IWorkspaceReader workspaceReader,
    IApprovalCheckpointStore approvalStore,
    ITaskRuntime? taskRuntime = null) : IPatchApplyService
{
    private const int MaxBackupCharacters = 500_000;

    public Task<PatchApplyPlan> DryRunAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default) =>
        DryRunWithEventsAsync(request, cancellationToken);

    public async Task<ApprovalRequest> RequestApplyApprovalAsync(
        PatchProposalId proposalId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await proposalRepository.GetAsync(proposalId, cancellationToken) ??
            throw new InvalidOperationException("patch_proposal_not_found");
        var request = ApprovalRequest.Create(
            CreateScope(proposalId, workspaceId),
            proposal.Title,
            $"{proposal.Summary} Risk: {proposal.Risk}. Files: {proposal.Files.Count}.");
        await approvalStore.RequestAsync(request, cancellationToken);
        await proposalRepository.UpdateStatusAsync(
            proposalId,
            PatchProposalStatus.ApprovalRequested,
            cancellationToken);
        await AppendAsync(TaskEventKind.PatchApplyApprovalRequested, "Patch apply approval requested.", cancellationToken);
        return request;
    }

    public async Task<PatchApplyResult> ApplyAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var approvalFailure = ValidateApproval(request);
        if (approvalFailure is not null)
        {
            return await PersistFailureAsync(request, approvalFailure.Value, cancellationToken);
        }

        var plan = await DryRunWithEventsAsync(request, cancellationToken);
        if (plan.Status != PatchApplyStatus.DryRunPassed)
        {
            return await PersistFailureAsync(request, plan.FailureReasons, cancellationToken);
        }

        var proposal = await proposalRepository.GetAsync(request.ProposalId, cancellationToken);
        if (proposal is null)
        {
            return await PersistFailureAsync(request, PatchApplyFailureReason.ProposalNotFound, cancellationToken);
        }

        await AppendAsync(TaskEventKind.PatchApplyStarted, "Patch apply started.", cancellationToken);
        var resultId = PatchApplyResultId.NewId();
        var fileResults = new List<PatchApplyFileResult>();
        var backups = new List<PatchApplyBackup>();
        var failures = new List<PatchApplyFailureReason>();

        foreach (var file in proposal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.ChangeKind == PatchProposalFileChangeKind.NoOp)
            {
                fileResults.Add(new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Applied));
                continue;
            }

            try
            {
                var backup = CreateBackup(resultId, proposal, file, cancellationToken);
                backups.Add(backup);
                await AppendAsync(TaskEventKind.PatchFileBackupCreated, "Patch file backup created.", cancellationToken);
                WriteProposedContent(proposal.WorkspaceId, file, cancellationToken);
                fileResults.Add(new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Applied));
                await AppendAsync(TaskEventKind.PatchFileApplied, "Patch file applied.", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                failures.Add(PatchApplyFailureReason.Cancelled);
                fileResults.Add(new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Cancelled,
                    PatchApplyFailureReason.Cancelled));
                break;
            }
            catch (InvalidOperationException exception)
            {
                var reason = MapFailure(exception.Message);
                failures.Add(reason);
                fileResults.Add(new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Failed,
                    reason));
                break;
            }
            catch
            {
                failures.Add(PatchApplyFailureReason.WriteFailed);
                fileResults.Add(new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Failed,
                    PatchApplyFailureReason.WriteFailed));
                break;
            }
        }

        var status = failures.Count == 0
            ? PatchApplyStatus.Applied
            : fileResults.Any(file => file.Status == PatchApplyStatus.Applied)
                ? PatchApplyStatus.PartiallyApplied
                : PatchApplyStatus.Failed;
        var result = CreateResult(resultId, request, status, fileResults, failures);
        await applyRepository.CreateApplyResultAsync(result, backups, cancellationToken);
        if (status == PatchApplyStatus.Applied)
        {
            await proposalRepository.UpdateStatusAsync(request.ProposalId, PatchProposalStatus.Applied, cancellationToken);
            await AppendAsync(TaskEventKind.PatchApplyCompleted, "Patch apply completed.", cancellationToken);
        }
        else
        {
            await AppendAsync(TaskEventKind.PatchApplyFailed, "Patch apply failed.", cancellationToken);
        }

        return result;
    }

    public Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default) =>
        applyRepository.GetApplyResultAsync(resultId, cancellationToken);

    public Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        applyRepository.ListRecentApplyResultsAsync(limit, cancellationToken);

    public async Task<PatchRollbackResult> RollbackAsync(
        PatchRollbackRequest request,
        CancellationToken cancellationToken = default)
    {
        await AppendAsync(TaskEventKind.PatchRollbackStarted, "Patch rollback started.", cancellationToken);
        var applyResult = await applyRepository.GetApplyResultAsync(request.ApplyResultId, cancellationToken);
        var backups = await applyRepository.ListBackupsAsync(request.ApplyResultId, cancellationToken);
        var files = new List<PatchApplyFileResult>();
        var failures = new List<PatchApplyFailureReason>();

        if (applyResult is null || backups.Count == 0 || applyResult.WorkspaceId != request.WorkspaceId)
        {
            failures.Add(PatchApplyFailureReason.UnknownSafeFailure);
        }
        else
        {
            foreach (var backup in backups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var current = workspaceReader.ReadTextFile(
                        request.WorkspaceId,
                        backup.RelativePath,
                        MaxBackupCharacters,
                        cancellationToken);
                    if (CodeContextService.ComputeHash(current.Content) != backup.AppliedContentHash)
                    {
                        failures.Add(PatchApplyFailureReason.StaleOriginalContent);
                        files.Add(new PatchApplyFileResult(backup.RelativePath, backup.OperationKind, PatchApplyStatus.RollbackFailed, PatchApplyFailureReason.StaleOriginalContent));
                        continue;
                    }

                    WriteContent(request.WorkspaceId, backup.RelativePath, backup.OriginalContent, cancellationToken);
                    files.Add(new PatchApplyFileResult(backup.RelativePath, backup.OperationKind, PatchApplyStatus.RolledBack));
                    await AppendAsync(TaskEventKind.PatchFileRolledBack, "Patch file rolled back.", cancellationToken);
                }
                catch
                {
                    failures.Add(PatchApplyFailureReason.WriteFailed);
                    files.Add(new PatchApplyFileResult(backup.RelativePath, backup.OperationKind, PatchApplyStatus.RollbackFailed, PatchApplyFailureReason.WriteFailed));
                }
            }
        }

        var now = DateTimeOffset.UtcNow;
        var result = new PatchRollbackResult(
            PatchRollbackResultId.NewId(),
            request.ApplyResultId,
            request.WorkspaceId,
            failures.Count == 0 ? PatchApplyStatus.RolledBack : PatchApplyStatus.RollbackFailed,
            files,
            failures,
            now,
            now);
        await applyRepository.CreateRollbackResultAsync(result, cancellationToken);
        if (result.Status == PatchApplyStatus.RolledBack && applyResult is not null)
        {
            await proposalRepository.UpdateStatusAsync(
                applyResult.ProposalId,
                PatchProposalStatus.RolledBack,
                cancellationToken);
        }

        await AppendAsync(
            result.Status == PatchApplyStatus.RolledBack ? TaskEventKind.PatchRollbackCompleted : TaskEventKind.PatchRollbackFailed,
            result.Status == PatchApplyStatus.RolledBack ? "Patch rollback completed." : "Patch rollback failed.",
            cancellationToken);
        return result;
    }

    private async Task<PatchApplyPlan> DryRunWithEventsAsync(PatchApplyRequest request, CancellationToken cancellationToken)
    {
        await AppendAsync(TaskEventKind.PatchDryRunStarted, "Patch dry run started.", cancellationToken);
        var plan = await validator.DryRunAsync(request, cancellationToken);
        await AppendAsync(
            plan.Status == PatchApplyStatus.DryRunPassed ? TaskEventKind.PatchDryRunPassed : TaskEventKind.PatchDryRunFailed,
            plan.Status == PatchApplyStatus.DryRunPassed ? "Patch dry run passed." : "Patch dry run failed.",
            cancellationToken);
        return plan;
    }

    private PatchApplyFailureReason? ValidateApproval(PatchApplyRequest request)
    {
        if (request.ApprovalRequest is null || request.ApprovalDecision is null)
        {
            return PatchApplyFailureReason.ApprovalMissing;
        }

        if (request.ApprovalRequest.Scope.NormalizedResourceScope != CreateScope(request.ProposalId, request.WorkspaceId).NormalizedResourceScope)
        {
            return PatchApplyFailureReason.ApprovalMissing;
        }

        return request.ApprovalDecision.IsAllowed
            ? null
            : PatchApplyFailureReason.ApprovalDenied;
    }

    private PatchApplyBackup CreateBackup(
        PatchApplyResultId resultId,
        PatchProposal proposal,
        PatchProposalFile file,
        CancellationToken cancellationToken)
    {
        if (file.ChangeKind == PatchProposalFileChangeKind.Add)
        {
            return new PatchApplyBackup(resultId, proposal.Id, proposal.WorkspaceId, file.RelativePath, string.Empty, CodeContextService.ComputeHash(string.Empty), file.ProposedContentHash, DateTimeOffset.UtcNow, file.ChangeKind);
        }

        var current = workspaceReader.ReadTextFile(proposal.WorkspaceId, file.RelativePath, MaxBackupCharacters, cancellationToken);
        if (current.IsTruncated || current.HadDecodingErrors)
        {
            throw new InvalidOperationException("backup_failed");
        }

        return new PatchApplyBackup(
            resultId,
            proposal.Id,
            proposal.WorkspaceId,
            file.RelativePath,
            current.Content,
            CodeContextService.ComputeHash(current.Content),
            file.ProposedContentHash,
            DateTimeOffset.UtcNow,
            file.ChangeKind,
            current.EncodingName);
    }

    private void WriteProposedContent(WorkspaceId workspaceId, PatchProposalFile file, CancellationToken cancellationToken)
    {
        if (file.ProposedContent is null)
        {
            throw new InvalidOperationException("write_failed");
        }

        WriteContent(workspaceId, file.RelativePath, file.ProposedContent, cancellationToken);
    }

    private void WriteContent(WorkspaceId workspaceId, string relativePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PatchApplyValidator.RejectUnsafeRelativePath(relativePath);
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        var fullPath = Path.GetFullPath(Path.Combine(workspace.CanonicalRootPath, relativePath));
        EnsureInside(workspace.CanonicalRootPath, fullPath);
        var directory = Path.GetDirectoryName(fullPath) ?? workspace.CanonicalRootPath;
        Directory.CreateDirectory(directory);
        EnsureInside(workspace.CanonicalRootPath, directory);
        var tempPath = Path.Combine(Path.GetTempPath(), "PersonalAI", "patch-apply", Guid.NewGuid().ToString("N") + ".tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        File.WriteAllText(tempPath, content);
        if (CodeContextService.ComputeHash(File.ReadAllText(tempPath)) != CodeContextService.ComputeHash(content))
        {
            throw new InvalidOperationException("hash_mismatch");
        }

        if (File.Exists(fullPath))
        {
            File.Copy(tempPath, fullPath, overwrite: true);
            File.Delete(tempPath);
        }
        else
        {
            File.Move(tempPath, fullPath);
        }
    }

    private static void EnsureInside(string root, string fullPath)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedFull = Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(normalizedRoot, normalizedFull, comparison))
        {
            return;
        }

        if (!normalizedFull.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException("path_outside_workspace");
        }
    }

    private async Task<PatchApplyResult> PersistFailureAsync(PatchApplyRequest request, PatchApplyFailureReason reason, CancellationToken cancellationToken) =>
        await PersistFailureAsync(request, [reason], cancellationToken);

    private async Task<PatchApplyResult> PersistFailureAsync(PatchApplyRequest request, IReadOnlyList<PatchApplyFailureReason> reasons, CancellationToken cancellationToken)
    {
        var result = CreateResult(PatchApplyResultId.NewId(), request, PatchApplyStatus.Failed, [], reasons);
        await applyRepository.CreateApplyResultAsync(result, [], cancellationToken);
        await AppendAsync(TaskEventKind.PatchApplyFailed, "Patch apply failed.", cancellationToken);
        return result;
    }

    private static PatchApplyResult CreateResult(
        PatchApplyResultId id,
        PatchApplyRequest request,
        PatchApplyStatus status,
        IReadOnlyList<PatchApplyFileResult> files,
        IReadOnlyList<PatchApplyFailureReason> failures)
    {
        var now = DateTimeOffset.UtcNow;
        return new PatchApplyResult(id, request.ProposalId, request.WorkspaceId, status, files, failures, now, now);
    }

    private static PatchApplyFailureReason MapFailure(string message) =>
        message switch
        {
            "backup_failed" => PatchApplyFailureReason.BackupFailed,
            "hash_mismatch" => PatchApplyFailureReason.HashMismatch,
            "path_outside_workspace" => PatchApplyFailureReason.PathOutsideWorkspace,
            _ => PatchApplyFailureReason.WriteFailed
        };

    private static ApprovalScope CreateScope(PatchProposalId proposalId, WorkspaceId workspaceId) =>
        new(TaskId.NewId(), ApprovalKind.ApproveFutureApply, $"patch-apply:{workspaceId}:{proposalId}");

    private async ValueTask AppendAsync(TaskEventKind kind, string summary, CancellationToken cancellationToken)
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
