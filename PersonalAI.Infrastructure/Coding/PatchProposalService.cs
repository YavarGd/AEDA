using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class PatchProposalService(
    IPatchProposalRepository repository,
    IUnifiedDiffBuilder diffBuilder,
    IPatchRiskClassifier riskClassifier,
    IValidationPlanService validationPlanService,
    IWorkspaceReader workspaceReader,
    IApprovalCheckpointStore? approvalStore = null,
    ITaskRuntime? taskRuntime = null) : IPatchProposalService
{
    private const int MaxFiles = 20;

    public async Task<PatchProposal> CreateProposalAsync(
        PatchProposalCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = workspaceReader.GetWorkspace(request.WorkspaceId);
        await AppendAsync(TaskEventKind.CodeChangeRequested, "Code change requested.", cancellationToken);

        if (request.FileEdits.Count == 0 || request.FileEdits.Count > MaxFiles)
        {
            throw new InvalidOperationException("patch_file_count_invalid");
        }

        var canonicalEdits = request.FileEdits
            .Select(edit => CaptureWorkspaceBaseline(request.WorkspaceId, edit, cancellationToken))
            .ToArray();
        var files = canonicalEdits
            .Select(edit => diffBuilder.BuildFileDiff(edit))
            .ToArray();
        var (risk, reasons) = riskClassifier.Classify(files);
        await AppendAsync(TaskEventKind.PatchProposalRiskClassified, "Patch proposal risk classified.", cancellationToken);
        var validationPlan = request.ValidationPlan ?? validationPlanService.CreatePlan(files);
        await AppendAsync(TaskEventKind.ValidationPlanCreated, "Validation plan created.", cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var proposal = new PatchProposal(
            PatchProposalId.NewId(),
            request.WorkspaceId,
            Bound(request.Title, 120),
            Bound(request.Summary, 500),
            risk == PatchProposalRisk.Blocked
                ? PatchProposalStatus.Failed
                : PatchProposalStatus.ReadyForReview,
            risk,
            reasons,
            files,
            request.Sources,
            validationPlan,
            now,
            now);

        await repository.CreateAsync(proposal, cancellationToken);
        await AppendAsync(TaskEventKind.PatchProposalCreated, "Patch proposal created.", cancellationToken);
        await AppendAsync(TaskEventKind.PatchProposalValidated, "Patch proposal validated.", cancellationToken);
        return proposal;
    }

    public Task<PatchProposal?> GetProposalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(proposalId, cancellationToken);

    public Task<IReadOnlyList<PatchProposal>> ListRecentProposalsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        repository.ListRecentAsync(limit, cancellationToken);

    public async Task<PatchProposal> MarkStatusAsync(
        PatchProposalId proposalId,
        PatchProposalStatus status,
        CancellationToken cancellationToken = default)
    {
        await repository.UpdateStatusAsync(proposalId, status, cancellationToken);
        var proposal = await repository.GetAsync(proposalId, cancellationToken) ??
            throw new InvalidOperationException("patch_proposal_not_found");
        await AppendAsync(
            status == PatchProposalStatus.Rejected
                ? TaskEventKind.PatchProposalRejected
                : TaskEventKind.PatchProposalValidated,
            "Patch proposal status updated.",
            cancellationToken);
        return proposal;
    }

    public async Task<ApprovalRequest> RequestApprovalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default)
    {
        var proposal = await repository.GetAsync(proposalId, cancellationToken) ??
            throw new InvalidOperationException("patch_proposal_not_found");
        var request = ApprovalRequest.Create(
            new ApprovalScope(
                TaskId.NewId(),
                ApprovalKind.ApproveFutureApply,
                $"patch-proposal:{proposal.WorkspaceId}:{proposal.Id}"),
            proposal.Title,
            $"{proposal.Summary} Risk: {proposal.Risk}. Files: {proposal.Files.Count}.");

        if (approvalStore is not null)
        {
            await approvalStore.RequestAsync(request, cancellationToken);
        }

        await repository.UpdateStatusAsync(
            proposalId,
            PatchProposalStatus.ApprovalRequested,
            cancellationToken);
        await AppendAsync(TaskEventKind.PatchProposalApprovalRequested, "Patch proposal approval requested.", cancellationToken);
        return request;
    }

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

    private static string Bound(string value, int maxLength)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? "Patch proposal" : value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private PatchProposalFileEdit CaptureWorkspaceBaseline(
        WorkspaceId workspaceId,
        PatchProposalFileEdit edit,
        CancellationToken cancellationToken)
    {
        if (edit.ChangeKind == PatchProposalFileChangeKind.Add)
        {
            return edit with
            {
                OriginalContent = string.Empty,
                RelativePath = edit.RelativePath.Replace('\\', '/')
            };
        }

        var current = PatchFileBaseline.ReadCurrentText(
            workspaceReader,
            workspaceId,
            edit.RelativePath,
            cancellationToken);
        return edit with
        {
            RelativePath = current.RelativePath.Replace('\\', '/'),
            OriginalContent = current.Content
        };
    }
}
