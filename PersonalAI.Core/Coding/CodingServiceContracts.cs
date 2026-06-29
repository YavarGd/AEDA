using PersonalAI.Core.Approvals;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Coding;

public interface ICodeContextService
{
    Task<CodeContextPack> LoadFilesAsync(
        WorkspaceId workspaceId,
        IReadOnlyList<string> relativePaths,
        int maxFiles = 20,
        int maxCharactersPerFile = 100_000,
        CancellationToken cancellationToken = default);

    Task<CodeContextPack> SearchAsync(
        CodeContextSearchRequest request,
        CancellationToken cancellationToken = default);
}

public interface ICodeChangePlanningService
{
    Task<CodeChangePlan> CreatePlanAsync(
        CodeChangeRequest request,
        CodeContextPack context,
        CancellationToken cancellationToken = default);
}

public sealed record CodeProposalDraftRequest(
    CodeChangeRequest ChangeRequest,
    CodeContextPack Context,
    string? OptionalTitle = null);

public sealed record CodeProposalDraft(
    string Title,
    string Summary,
    IReadOnlyList<PatchProposalFileEdit> FileEdits,
    IReadOnlyList<string> SafeNotices);

public interface ICodeProposalDraftService
{
    Task<CodeProposalDraft> CreateDraftAsync(
        CodeProposalDraftRequest request,
        CancellationToken cancellationToken = default);
}

public interface IUnifiedDiffBuilder
{
    PatchProposalFile BuildFileDiff(
        PatchProposalFileEdit edit,
        int maxDiffCharacters = 200_000);
}

public interface IPatchRiskClassifier
{
    (PatchProposalRisk Risk, IReadOnlyList<string> Reasons) Classify(
        IReadOnlyList<PatchProposalFile> files);
}

public interface IValidationPlanService
{
    PatchProposalValidationPlan CreatePlan(
        IReadOnlyList<PatchProposalFile> files);
}

public interface IPatchProposalRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task CreateAsync(
        PatchProposal proposal,
        CancellationToken cancellationToken = default);

    Task<PatchProposal?> GetAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchProposal>> ListRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task UpdateStatusAsync(
        PatchProposalId proposalId,
        PatchProposalStatus status,
        CancellationToken cancellationToken = default);
}

public interface IPatchProposalService
{
    Task<PatchProposal> CreateProposalAsync(
        PatchProposalCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<PatchProposal?> GetProposalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PatchProposal>> ListRecentProposalsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<PatchProposal> MarkStatusAsync(
        PatchProposalId proposalId,
        PatchProposalStatus status,
        CancellationToken cancellationToken = default);

    Task<ApprovalRequest> RequestApprovalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default);
}
