using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class AedaCodeModuleService(
    IWorkspaceReader workspaceReader,
    ICodeContextService codeContextService,
    ICodeChangePlanningService planningService,
    IPatchProposalService proposalService,
    IPatchApplyService applyService,
    IValidationRunnerService validationRunnerService,
    IValidationCommandAllowlist validationCommandAllowlist,
    ITaskQueryService? taskQueryService = null) : IAedaCodeModuleService
{
    private readonly object _gate = new();
    private readonly List<AedaCodeSession> _sessions = [];

    public Task<AedaCodeSession> StartSessionAsync(
        WorkspaceId workspaceId,
        string? safeSummary = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        var now = DateTimeOffset.UtcNow;
        var session = new AedaCodeSession(
            AedaCodeSessionId.NewId(),
            workspace.Id,
            workspace.DisplayName,
            CurrentTaskId: null,
            ActiveProposalId: null,
            ActiveApplyResultId: null,
            ActiveValidationRunId: null,
            now,
            now,
            AedaCodeSessionStatus.Active,
            string.IsNullOrWhiteSpace(safeSummary)
                ? "AEDA Code session"
                : safeSummary.Trim());

        lock (_gate)
        {
            _sessions.Insert(0, session);
        }

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AedaCodeSession>>(
                _sessions
                    .OrderByDescending(session => session.UpdatedAtUtc)
                    .Take(Math.Max(0, limit))
                    .ToArray());
        }
    }

    public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        int? fileCount = null;
        int? directoryCount = null;

        try
        {
            var entries = workspaceReader.ListDirectory(
                workspaceId,
                ".",
                maxEntries: 500,
                includeHidden: false,
                cancellationToken);
            fileCount = entries.Count(entry => entry.Type == WorkspaceEntryType.File);
            directoryCount = entries.Count(entry => entry.Type == WorkspaceEntryType.Directory);
        }
        catch (Exception)
        {
            fileCount = null;
            directoryCount = null;
        }

        return Task.FromResult(
            new AedaCodeWorkspaceSummary(
                workspace.Id,
                workspace.DisplayName,
                workspace.Policy.IsReadOnly,
                fileCount,
                directoryCount));
    }

    public Task<CodeContextPack> ReadFilesAsync(
        WorkspaceId workspaceId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken = default) =>
        codeContextService.LoadFilesAsync(workspaceId, relativePaths, cancellationToken: cancellationToken);

    public Task<CodeContextPack> SearchAsync(
        CodeContextSearchRequest request,
        CancellationToken cancellationToken = default) =>
        codeContextService.SearchAsync(request, cancellationToken);

    public Task<CodeChangePlan> CreatePlanAsync(
        CodeChangeRequest request,
        CodeContextPack context,
        CancellationToken cancellationToken = default) =>
        planningService.CreatePlanAsync(request, context, cancellationToken);

    public Task<PatchProposal> CreateProposalAsync(
        PatchProposalCreateRequest request,
        CancellationToken cancellationToken = default) =>
        proposalService.CreateProposalAsync(request, cancellationToken);

    public Task<PatchProposal?> GetProposalAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default) =>
        proposalService.GetProposalAsync(proposalId, cancellationToken);

    public async Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
        WorkspaceId workspaceId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var proposals = await proposalService.ListRecentProposalsAsync(
            Math.Max(limit * 2, limit),
            cancellationToken).ConfigureAwait(false);

        return proposals
            .Where(proposal => proposal.WorkspaceId == workspaceId)
            .OrderByDescending(proposal => proposal.UpdatedAtUtc)
            .Take(Math.Max(0, limit))
            .Select(ToProposalSummary)
            .ToArray();
    }

    public Task<PatchApplyPlan> DryRunApplyAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.DryRunAsync(request, cancellationToken);

    public Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var workspace = workspaceReader.GetWorkspace(workspaceId);
        var templates = validationCommandAllowlist.ListTemplates()
            .Where(template => validationCommandAllowlist.TryCreateCommand(
                new ValidationRunRequest(workspace.Id, template.Id),
                workspace,
                out _,
                out _))
            .OrderBy(template => template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(template => template.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ValidationCommandTemplate>>(templates);
    }

    public Task<ApprovalRequest> RequestApplyApprovalAsync(
        PatchProposalId proposalId,
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default) =>
        applyService.RequestApplyApprovalAsync(proposalId, workspaceId, cancellationToken);

    public Task<PatchApplyResult> ApplyApprovedProposalAsync(
        PatchApplyRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.ApplyAsync(request, cancellationToken);

    public Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId applyResultId,
        CancellationToken cancellationToken = default) =>
        applyService.GetApplyResultAsync(applyResultId, cancellationToken);

    public Task<PatchRollbackResult> RollbackAsync(
        PatchRollbackRequest request,
        CancellationToken cancellationToken = default) =>
        applyService.RollbackAsync(request, cancellationToken);

    public Task<ValidationRun> CreateValidationRunAsync(
        ValidationRunRequest request,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.CreateRunAsync(request, cancellationToken);

    public Task<ApprovalRequest> RequestValidationApprovalAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.RequestApprovalAsync(runId, cancellationToken);

    public Task<ValidationRun> RunApprovedValidationAsync(
        ValidationRunId runId,
        ApprovalRequest approvalRequest,
        ApprovalDecision approvalDecision,
        CancellationToken cancellationToken = default) =>
        validationRunnerService.ExecuteAsync(
            runId,
            approvalRequest,
            approvalDecision,
            cancellationToken);

    public async Task<AedaCodeDashboardModel> GetDashboardAsync(
        AedaCodeSessionId sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = GetSession(sessionId);
        var workspace = await GetWorkspaceSummaryAsync(
            session.WorkspaceId,
            cancellationToken).ConfigureAwait(false);
        var proposals = await ListProposalSummariesAsync(
            session.WorkspaceId,
            limit: 20,
            cancellationToken).ConfigureAwait(false);
        var applyResults = await applyService.ListRecentApplyResultsAsync(
            limit: 50,
            cancellationToken).ConfigureAwait(false);
        var validations = await validationRunnerService.ListRecentAsync(
            session.WorkspaceId,
            limit: 50,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var timeline = await BuildTimelineAsync(cancellationToken).ConfigureAwait(false);

        return new AedaCodeDashboardModel(
            session,
            workspace,
            proposals,
            applyResults
                .Where(result => result.WorkspaceId == session.WorkspaceId)
                .OrderByDescending(result => result.UpdatedAtUtc)
                .Take(20)
                .Select(ToApplySummary)
                .ToArray(),
            validations
                .OrderByDescending(run => run.UpdatedAtUtc)
                .Take(20)
                .Select(ToValidationSummary)
                .ToArray(),
            timeline);
    }

    private AedaCodeSession GetSession(AedaCodeSessionId sessionId)
    {
        lock (_gate)
        {
            return _sessions.FirstOrDefault(session => session.Id == sessionId)
                ?? throw new InvalidOperationException("aeda_code_session_not_found");
        }
    }

    private async Task<IReadOnlyList<AedaCodeTimelineItem>> BuildTimelineAsync(
        CancellationToken cancellationToken)
    {
        if (taskQueryService is null)
        {
            return [];
        }

        var taskRuns = await taskQueryService.ListRecentTaskRunsAsync(
            20,
            cancellationToken).ConfigureAwait(false);

        return taskRuns
            .OrderByDescending(run => run.UpdatedAtUtc)
            .Select(run => new AedaCodeTimelineItem(
                run.UpdatedAtUtc,
                "task",
                $"{run.Title} ({run.Status})"))
            .ToArray();
    }

    private static AedaCodeProposalSummary ToProposalSummary(PatchProposal proposal) =>
        new(
            proposal.Id,
            proposal.Title,
            proposal.Status,
            ToRiskBadge(proposal.Risk, proposal.RiskReasons),
            proposal.Files.Select(file => file.RelativePath).ToArray(),
            proposal.UpdatedAtUtc);

    private static AedaCodeRiskBadge ToRiskBadge(
        PatchProposalRisk risk,
        IReadOnlyList<string> reasons) =>
        new(
            risk,
            risk.ToString(),
            reasons.Count == 0
                ? "risk_not_classified"
                : string.Join(", ", reasons.Take(3)));

    private static AedaCodeApplySummary ToApplySummary(PatchApplyResult result) =>
        new(
            result.Id,
            result.ProposalId,
            result.Status,
            result.Files.Count,
            result.UpdatedAtUtc);

    private static AedaCodeValidationSummary ToValidationSummary(ValidationRun run) =>
        new(
            run.Id,
            run.TemplateId,
            run.Status,
            run.ProposalId,
            run.ApplyResultId,
            run.UpdatedAtUtc);
}
