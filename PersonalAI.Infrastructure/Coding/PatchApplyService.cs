using System.Text;
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
    private const int MaxBackupBytes = 5 * 1024 * 1024;

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
        var approvalFailure = await ValidateApprovalAsync(request, cancellationToken);
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
        var backups = new List<PatchApplyBackup>();
        var failures = new List<PatchApplyFailureReason>();
        var fileResults = proposal.Files
            .Select(file => new PatchApplyFileResult(
                file.RelativePath,
                file.ChangeKind,
                file.ChangeKind == PatchProposalFileChangeKind.NoOp
                    ? PatchApplyStatus.Applied
                    : PatchApplyStatus.Applying))
            .ToList();

        try
        {
            foreach (var file in proposal.Files.Where(file => file.ChangeKind != PatchProposalFileChangeKind.NoOp))
            {
                cancellationToken.ThrowIfCancellationRequested();
                backups.Add(CreateBackup(resultId, proposal, file, cancellationToken));
                await AppendAsync(TaskEventKind.PatchFileBackupCreated, "Patch file backup created.", cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return await PersistFailureAsync(request, MapFailure(exception.Message), CancellationToken.None);
        }
        catch
        {
            return await PersistFailureAsync(request, PatchApplyFailureReason.BackupFailed, CancellationToken.None);
        }

        var journal = CreateResult(
            resultId,
            request,
            PatchApplyStatus.Applying,
            fileResults,
            []);
        await applyRepository.CreateApplyResultAsync(journal, backups, cancellationToken);

        for (var index = 0; index < proposal.Files.Count; index++)
        {
            var file = proposal.Files[index];
            if (file.ChangeKind == PatchProposalFileChangeKind.NoOp)
            {
                continue;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteProposedContent(proposal.WorkspaceId, file, cancellationToken);
                fileResults[index] = new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Applied);
                await AppendAsync(TaskEventKind.PatchFileApplied, "Patch file applied.", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                failures.Add(PatchApplyFailureReason.Cancelled);
                fileResults[index] = new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Cancelled,
                    PatchApplyFailureReason.Cancelled);
                break;
            }
            catch (InvalidOperationException exception)
            {
                var reason = MapFailure(exception.Message);
                failures.Add(reason);
                fileResults[index] = new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Failed,
                    reason);
                break;
            }
            catch
            {
                failures.Add(PatchApplyFailureReason.WriteFailed);
                fileResults[index] = new PatchApplyFileResult(
                    file.RelativePath,
                    file.ChangeKind,
                    PatchApplyStatus.Failed,
                    PatchApplyFailureReason.WriteFailed);
                break;
            }
        }

        for (var index = 0; index < fileResults.Count; index++)
        {
            if (fileResults[index].Status == PatchApplyStatus.Applying)
            {
                fileResults[index] = fileResults[index] with { Status = PatchApplyStatus.NotStarted };
            }
        }

        var status = failures.Count == 0
            ? PatchApplyStatus.Applied
            : fileResults.Any(file => file.Status == PatchApplyStatus.Applied)
                ? PatchApplyStatus.PartiallyApplied
                : failures.Contains(PatchApplyFailureReason.Cancelled)
                    ? PatchApplyStatus.Cancelled
                    : PatchApplyStatus.Failed;
        var result = journal with
        {
            Status = status,
            Files = fileResults,
            FailureReasons = failures,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await applyRepository.CreateApplyResultAsync(result, [], CancellationToken.None);
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
                    if (backup.OperationKind == PatchProposalFileChangeKind.Add)
                    {
                        var workspace = workspaceReader.GetWorkspace(request.WorkspaceId);
                        if (!ReparseSafePatchWriter.DeleteIfContentHashMatches(
                                workspace.CanonicalRootPath,
                                backup.RelativePath,
                                backup.AppliedContentHash,
                                MaxBackupBytes,
                                cancellationToken))
                        {
                            failures.Add(PatchApplyFailureReason.StaleOriginalContent);
                            files.Add(new PatchApplyFileResult(backup.RelativePath, backup.OperationKind, PatchApplyStatus.RollbackFailed, PatchApplyFailureReason.StaleOriginalContent));
                            continue;
                        }

                        files.Add(new PatchApplyFileResult(backup.RelativePath, backup.OperationKind, PatchApplyStatus.RolledBack));
                        await AppendAsync(TaskEventKind.PatchFileRolledBack, "Patch file rolled back.", cancellationToken);
                        continue;
                    }

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

                    WriteBytes(
                        request.WorkspaceId,
                        backup.RelativePath,
                        backup.OriginalBytes ?? EncodeLegacyBackup(backup),
                        replaceExisting: true,
                        cancellationToken: cancellationToken);
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
        var failedSummary = plan.FailureReasons.Contains(PatchApplyFailureReason.StaleOriginalContent)
            ? "Patch dry run failed: StaleOriginalContent."
            : "Patch dry run failed.";
        await AppendAsync(
            plan.Status == PatchApplyStatus.DryRunPassed ? TaskEventKind.PatchDryRunPassed : TaskEventKind.PatchDryRunFailed,
            plan.Status == PatchApplyStatus.DryRunPassed ? "Patch dry run passed." : failedSummary,
            cancellationToken);
        return plan;
    }

    private async ValueTask<PatchApplyFailureReason?> ValidateApprovalAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ApprovalRequest is null || request.ApprovalDecision is null)
        {
            return PatchApplyFailureReason.ApprovalMissing;
        }

        var expectedScope = CreateScope(request.ProposalId, request.WorkspaceId);
        if (!ScopeMatches(request.ApprovalRequest.Scope, expectedScope) ||
            request.ApprovalDecision.RequestId != request.ApprovalRequest.RequestId)
        {
            return PatchApplyFailureReason.ApprovalMissing;
        }

        if (!request.ApprovalDecision.IsAllowed)
        {
            return PatchApplyFailureReason.ApprovalDenied;
        }

        return await approvalStore.TryConsumeAsync(
            request.ApprovalRequest,
            request.ApprovalDecision,
            cancellationToken)
            ? null
            : PatchApplyFailureReason.ApprovalMissing;
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
        var workspace = workspaceReader.GetWorkspace(proposal.WorkspaceId);
        var originalBytes = ReparseSafePatchWriter.ReadBytes(
            workspace.CanonicalRootPath,
            file.RelativePath,
            MaxBackupBytes,
            cancellationToken);
        if (current.IsTruncated || current.HadDecodingErrors)
        {
            throw new InvalidOperationException("backup_failed");
        }

        if (!string.Equals(DecodeBackup(originalBytes, current.EncodingName), current.Content, StringComparison.Ordinal))
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
            current.EncodingName,
            originalBytes);
    }

    private void WriteProposedContent(WorkspaceId workspaceId, PatchProposalFile file, CancellationToken cancellationToken)
    {
        if (file.ProposedContent is null)
        {
            throw new InvalidOperationException("write_failed");
        }

        WriteContent(
            workspaceId,
            file.RelativePath,
            file.ProposedContent,
            file.ChangeKind != PatchProposalFileChangeKind.Add,
            cancellationToken);
    }

    private void WriteContent(
        WorkspaceId workspaceId,
        string relativePath,
        string content,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        ReparseSafePatchWriter.Write(
            workspace.CanonicalRootPath,
            relativePath,
            content,
            replaceExisting,
            cancellationToken);
    }

    private void WriteBytes(
        WorkspaceId workspaceId,
        string relativePath,
        ReadOnlySpan<byte> content,
        bool replaceExisting,
        CancellationToken cancellationToken)
    {
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        ReparseSafePatchWriter.WriteBytes(
            workspace.CanonicalRootPath,
            relativePath,
            content,
            replaceExisting,
            cancellationToken);
    }

    private static string DecodeBackup(byte[] bytes, string encodingName)
    {
        var text = CreateEncoding(encodingName).GetString(bytes);
        return text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;
    }

    private static byte[] EncodeLegacyBackup(PatchApplyBackup backup)
    {
        var encoding = CreateEncoding(backup.EncodingName);
        var content = encoding.GetBytes(backup.OriginalContent);
        if (string.Equals(backup.EncodingName, "utf-8", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        var preamble = encoding.GetPreamble();
        return [.. preamble, .. content];
    }

    private static Encoding CreateEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" => new UTF8Encoding(false, true),
        "utf-16" => new UnicodeEncoding(false, true, true),
        "utf-16be" => new UnicodeEncoding(true, true, true),
        _ => Encoding.GetEncoding(name, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
    };

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
        new(new TaskId(proposalId.Value), ApprovalKind.ApproveFutureApply, $"patch-apply:{workspaceId}:{proposalId}");

    private static bool ScopeMatches(ApprovalScope actual, ApprovalScope expected) =>
        actual.TaskId == expected.TaskId &&
        actual.Kind == expected.Kind &&
        actual.NormalizedResourceScope == expected.NormalizedResourceScope;

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
