using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class PatchApplyValidator(
    IPatchProposalRepository proposalRepository,
    IWorkspaceReader workspaceReader,
    IWorkspacePathResolver pathResolver) : IPatchApplyValidator
{
    private const int MaxFileCharacters = 500_000;
    private const int MaxPatchCharacters = 500_000;

    public async Task<PatchApplyPlan> DryRunAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var proposal = await proposalRepository.GetAsync(
            request.ProposalId,
            cancellationToken);
        if (proposal is null)
        {
            return Failed(request, PatchApplyFailureReason.ProposalNotFound);
        }

        if (proposal.WorkspaceId != request.WorkspaceId)
        {
            return Failed(request, PatchApplyFailureReason.WorkspaceNotRegistered);
        }

        try
        {
            _ = workspaceReader.GetWorkspace(request.WorkspaceId);
        }
        catch (WorkspaceAccessException)
        {
            return Failed(request, PatchApplyFailureReason.WorkspaceNotRegistered);
        }

        if (proposal.Status is not (
                PatchProposalStatus.ReadyForReview or
                PatchProposalStatus.ApprovalRequested or
                PatchProposalStatus.ApprovedForApply))
        {
            return Failed(request, PatchApplyFailureReason.ProposalNotReady);
        }

        if (proposal.Risk == PatchProposalRisk.Blocked)
        {
            return Failed(request, PatchApplyFailureReason.ProposalNotReady);
        }

        var failures = new List<PatchApplyFailureReason>();
        var operations = new List<PatchApplyOperation>();

        foreach (var file in proposal.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.UnifiedDiff.Length > MaxPatchCharacters)
            {
                failures.Add(PatchApplyFailureReason.LargeFileRejected);
                continue;
            }

            if (file.ChangeKind == PatchProposalFileChangeKind.Delete)
            {
                failures.Add(PatchApplyFailureReason.DeleteNotAllowed);
                continue;
            }

            if (ContainsBinary(file.OriginalContent) || ContainsBinary(file.ProposedContent))
            {
                failures.Add(PatchApplyFailureReason.BinaryUnsupported);
                continue;
            }

            if (file.ProposedContent is not null &&
                CodeContextService.ComputeHash(file.ProposedContent) != file.ProposedContentHash)
            {
                failures.Add(PatchApplyFailureReason.HashMismatch);
                continue;
            }

            if (file.ChangeKind is PatchProposalFileChangeKind.Modify or PatchProposalFileChangeKind.NoOp)
            {
                try
                {
                    _ = pathResolver.Resolve(
                        request.WorkspaceId,
                        file.RelativePath,
                        WorkspacePathKind.File);
                    var current = workspaceReader.ReadTextFile(
                        request.WorkspaceId,
                        file.RelativePath,
                        MaxFileCharacters,
                        cancellationToken);
                    if (current.IsTruncated || current.HadDecodingErrors)
                    {
                        failures.Add(PatchApplyFailureReason.LargeFileRejected);
                        continue;
                    }

                    var currentHash = CodeContextService.ComputeHash(current.Content);
                    if (currentHash != file.OriginalContentHash)
                    {
                        failures.Add(PatchApplyFailureReason.StaleOriginalContent);
                        continue;
                    }
                }
                catch (WorkspaceAccessException exception) when (
                    exception.SafeErrorCode is "path_outside_workspace" or "invalid_relative_path" or "reparse_point_rejected")
                {
                    failures.Add(PatchApplyFailureReason.PathOutsideWorkspace);
                    continue;
                }
                catch (WorkspaceAccessException)
                {
                    failures.Add(PatchApplyFailureReason.StaleOriginalContent);
                    continue;
                }
            }
            else if (file.ChangeKind == PatchProposalFileChangeKind.Add)
            {
                try
                {
                    _ = pathResolver.Resolve(
                        request.WorkspaceId,
                        ".",
                        WorkspacePathKind.Directory);
                    RejectUnsafeRelativePath(file.RelativePath);
                }
                catch (WorkspaceAccessException)
                {
                    failures.Add(PatchApplyFailureReason.PathOutsideWorkspace);
                    continue;
                }
                catch (InvalidOperationException)
                {
                    failures.Add(PatchApplyFailureReason.PathOutsideWorkspace);
                    continue;
                }
            }

            operations.Add(new PatchApplyOperation(
                file.RelativePath,
                file.ChangeKind,
                file.OriginalContentHash,
                file.ProposedContentHash));
        }

        if (failures.Count > 0)
        {
            return new PatchApplyPlan(
                request.ProposalId,
                request.WorkspaceId,
                PatchApplyStatus.DryRunFailed,
                operations,
                failures.Distinct().ToArray(),
                RequiresApproval: true);
        }

        return new PatchApplyPlan(
            request.ProposalId,
            request.WorkspaceId,
            PatchApplyStatus.DryRunPassed,
            operations,
            [],
            RequiresApproval: true);
    }

    internal static void RejectUnsafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Contains("..", StringComparison.Ordinal) ||
            relativePath.Contains('\\') ||
            relativePath.Contains('\0'))
        {
            throw new InvalidOperationException("unsafe_patch_path");
        }
    }

    private static PatchApplyPlan Failed(
        PatchApplyRequest request,
        PatchApplyFailureReason reason) =>
        new(
            request.ProposalId,
            request.WorkspaceId,
            PatchApplyStatus.DryRunFailed,
            [],
            [reason],
            RequiresApproval: true);

    private static bool ContainsBinary(string? value) =>
        value?.Contains('\0') == true;
}
