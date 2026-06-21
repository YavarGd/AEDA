using PersonalAI.Core.Approvals;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Coding;

namespace PersonalAI.Tests.Coding;

public sealed class AedaCodeModuleServiceTests
{
    private readonly WorkspaceId _workspaceId = WorkspaceId.NewId();

    [Fact]
    public async Task Service_StartsSessionDelegatesReadOnlyWorkAndBuildsDashboard()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            new WorkspaceAccessPolicy(IsReadOnly: false));
        var reader = new FakeWorkspaceReader(workspace);
        var context = new FakeCodeContextService(_workspaceId);
        var planner = new FakePlanningService();
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal);
        var applyResult = CreateApplyResult(_workspaceId, proposal.Id);
        var apply = new FakeApplyService(applyResult);
        var validationRun = CreateValidationRun(_workspaceId, proposal.Id, applyResult.Id);
        var validation = new FakeValidationRunnerService(validationRun);
        var tasks = new FakeTaskQueryService();
        var service = new AedaCodeModuleService(
            reader,
            context,
            planner,
            proposals,
            apply,
            validation,
            tasks);

        var session = await service.StartSessionAsync(_workspaceId, "Fix tests");
        var summary = await service.GetWorkspaceSummaryAsync(_workspaceId);
        var files = await service.ReadFilesAsync(_workspaceId, ["src/App.cs"]);
        var plan = await service.CreatePlanAsync(
            CodeChangeRequest.Create(_workspaceId, "Fix tests", ["src/App.cs"]),
            files);
        var proposalSummaries = await service.ListProposalSummariesAsync(_workspaceId);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspaceId);
        var dryRun = await service.DryRunApplyAsync(new PatchApplyRequest(proposal.Id, _workspaceId));
        var dashboard = await service.GetDashboardAsync(session.Id);

        Assert.Equal("Code workspace", session.WorkspaceDisplayName);
        Assert.Equal("Fix tests", session.SafeSummary);
        Assert.Equal(1, summary.ImmediateFileCount);
        Assert.Equal(1, summary.ImmediateDirectoryCount);
        Assert.Single(files.Files);
        Assert.Equal("Fix tests", plan.Title);
        Assert.Single(proposalSummaries);
        Assert.Equal(proposal.Id, proposalSummaries[0].ProposalId);
        Assert.Equal(ApprovalKind.ApproveFutureApply, approval.Scope.Kind);
        Assert.Equal(PatchApplyStatus.DryRunPassed, dryRun.Status);
        Assert.Same(session, dashboard.Session);
        Assert.Single(dashboard.Proposals);
        Assert.Single(dashboard.ApplyResults);
        Assert.Single(dashboard.ValidationRuns);
        Assert.Single(dashboard.Timeline);
        Assert.Equal("task", dashboard.Timeline[0].Kind);
    }

    [Fact]
    public async Task Service_RejectsMissingDashboardSession()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId),
            new FakePlanningService(),
            new FakeProposalService(CreateProposal(_workspaceId)),
            new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId())),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetDashboardAsync(AedaCodeSessionId.NewId()));
    }

    private static PatchProposal CreateProposal(WorkspaceId workspaceId)
    {
        var now = DateTimeOffset.UtcNow;
        var file = new PatchProposalFile(
            "src/App.cs",
            PatchProposalFileChangeKind.Modify,
            "old",
            "new",
            "old-hash",
            "new-hash",
            "--- a/src/App.cs\n+++ b/src/App.cs\n",
            []);
        return new PatchProposal(
            PatchProposalId.NewId(),
            workspaceId,
            "Fix tests",
            "Patch summary",
            PatchProposalStatus.ReadyForReview,
            PatchProposalRisk.Low,
            ["small_text_change"],
            [file],
            [],
            new PatchProposalValidationPlan([], []),
            now,
            now);
    }

    private static PatchApplyResult CreateApplyResult(
        WorkspaceId workspaceId,
        PatchProposalId proposalId)
    {
        var now = DateTimeOffset.UtcNow;
        return new PatchApplyResult(
            PatchApplyResultId.NewId(),
            proposalId,
            workspaceId,
            PatchApplyStatus.Applied,
            [new PatchApplyFileResult("src/App.cs", PatchProposalFileChangeKind.Modify, PatchApplyStatus.Applied)],
            [],
            now,
            now);
    }

    private static ValidationRun CreateValidationRun(
        WorkspaceId workspaceId,
        PatchProposalId proposalId,
        PatchApplyResultId? applyResultId)
    {
        var now = DateTimeOffset.UtcNow;
        return new ValidationRun(
            ValidationRunId.NewId(),
            workspaceId,
            "dotnet-build-debug",
            ".",
            ValidationRunStatus.Succeeded,
            proposalId,
            applyResultId,
            null,
            [],
            now,
            now);
    }

    private sealed class FakeWorkspaceReader(WorkspaceDescriptor workspace) : IWorkspaceReader
    {
        public WorkspaceDescriptor GetWorkspace(WorkspaceId workspaceId) => workspace;

        public IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
            WorkspaceId workspaceId,
            string relativePath,
            int maxEntries,
            bool includeHidden,
            CancellationToken cancellationToken = default) =>
            [
                new("src", "src", WorkspaceEntryType.Directory, null, DateTimeOffset.UtcNow, false, string.Empty),
                new("README.md", "README.md", WorkspaceEntryType.File, 4, DateTimeOffset.UtcNow, false, ".md")
            ];

        public WorkspaceTextFile ReadTextFile(
            WorkspaceId workspaceId,
            string relativePath,
            int maxCharacters,
            CancellationToken cancellationToken = default) =>
            new(relativePath, "class App {}", "utf-8", 12, false, false);

        public WorkspaceSearchResult SearchText(
            WorkspaceId workspaceId,
            string query,
            string relativeDirectory,
            string? filePattern,
            bool matchCase,
            int maxResults,
            CancellationToken cancellationToken = default) =>
            new(query, relativeDirectory, [], false, 0, 0);
    }

    private sealed class FakeCodeContextService(WorkspaceId workspaceId) : ICodeContextService
    {
        public Task<CodeContextPack> LoadFilesAsync(
            WorkspaceId workspaceId,
            IReadOnlyList<string> relativePaths,
            int maxFiles = 20,
            int maxCharactersPerFile = 100_000,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new CodeContextPack(
                    workspaceId,
                    [new CodeContextFile(workspaceId, relativePaths[0], "old", "old-hash", "utf-8", 3, false, false)],
                    [],
                    [],
                    false));

        public Task<CodeContextPack> SearchAsync(
            CodeContextSearchRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new CodeContextPack(workspaceId, [], [new CodeContextSearchMatch("src/App.cs", 1, "old")], [], false));
    }

    private sealed class FakePlanningService : ICodeChangePlanningService
    {
        public Task<CodeChangePlan> CreatePlanAsync(
            CodeChangeRequest request,
            CodeContextPack context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new CodeChangePlan(
                    request.UserRequest,
                    "Plan summary",
                    request.RelativePaths,
                    [new CodeChangeStep(1, "Edit file", "Change content", request.RelativePaths)],
                    [],
                    [],
                    new PatchProposalValidationPlan([], []),
                    []));
    }

    private sealed class FakeProposalService(PatchProposal proposal) : IPatchProposalService
    {
        public Task<PatchProposal> CreateProposalAsync(
            PatchProposalCreateRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(proposal);

        public Task<PatchProposal?> GetProposalAsync(
            PatchProposalId proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PatchProposal?>(proposal.Id == proposalId ? proposal : null);

        public Task<IReadOnlyList<PatchProposal>> ListRecentProposalsAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchProposal>>([proposal]);

        public Task<PatchProposal> MarkStatusAsync(
            PatchProposalId proposalId,
            PatchProposalStatus status,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(proposal with { Status = status });

        public Task<ApprovalRequest> RequestApprovalAsync(
            PatchProposalId proposalId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateApproval(ApprovalKind.ApprovePatchProposal, proposalId.ToString()));
    }

    private sealed class FakeApplyService(PatchApplyResult result) : IPatchApplyService
    {
        public Task<PatchApplyPlan> DryRunAsync(
            PatchApplyRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new PatchApplyPlan(
                    request.ProposalId,
                    request.WorkspaceId,
                    PatchApplyStatus.DryRunPassed,
                    [new PatchApplyOperation("src/App.cs", PatchProposalFileChangeKind.Modify, "old-hash", "new-hash")],
                    [],
                    RequiresApproval: true));

        public Task<ApprovalRequest> RequestApplyApprovalAsync(
            PatchProposalId proposalId,
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateApproval(ApprovalKind.ApproveFutureApply, proposalId.ToString()));

        public Task<PatchApplyResult> ApplyAsync(
            PatchApplyRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);

        public Task<PatchApplyResult?> GetApplyResultAsync(
            PatchApplyResultId resultId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<PatchApplyResult?>(result.Id == resultId ? result : null);

        public Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PatchApplyResult>>([result]);

        public Task<PatchRollbackResult> RollbackAsync(
            PatchRollbackRequest request,
            CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(
                new PatchRollbackResult(
                    PatchRollbackResultId.NewId(),
                    request.ApplyResultId,
                    request.WorkspaceId,
                    PatchApplyStatus.RolledBack,
                    [],
                    [],
                    now,
                    now));
        }
    }

    private sealed class FakeValidationRunnerService(ValidationRun run) : IValidationRunnerService
    {
        public Task<ValidationRun> CreateRunAsync(
            ValidationRunRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(run);

        public Task<ValidationRun> DryRunAsync(
            ValidationRunRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(run);

        public Task<ApprovalRequest> RequestApprovalAsync(
            ValidationRunId runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateApproval(ApprovalKind.ValidationRun, runId.ToString()));

        public Task<ValidationRun> ExecuteAsync(
            ValidationRunId runId,
            ApprovalRequest approvalRequest,
            ApprovalDecision approvalDecision,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(run);

        public Task<ValidationRun?> GetRunAsync(
            ValidationRunId runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ValidationRun?>(run.Id == runId ? run : null);

        public Task<IReadOnlyList<ValidationRun>> ListRecentAsync(
            WorkspaceId workspaceId,
            PatchProposalId? proposalId = null,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationRun>>([run]);
    }

    private sealed class FakeTaskQueryService : ITaskQueryService
    {
        private readonly TaskRun _run = TaskRun.Create("Patch proposal created", "aeda-code");

        public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>([_run]);

        public ValueTask<TaskRunRecord?> GetTaskRunAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRunRecord?>(null);

        public ValueTask<TaskRun?> GetLatestTaskForConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRun?>(null);

        public ValueTask<IReadOnlyList<TaskRun>> GetCurrentlyRunningTasksAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>([]);

        public ValueTask<IReadOnlyList<TaskRun>> SearchByStatusAsync(
            TaskRunStatus status,
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>([]);
    }

    private static ApprovalRequest CreateApproval(
        ApprovalKind kind,
        string resourceScope) =>
        ApprovalRequest.Create(
            new ApprovalScope(TaskId.NewId(), kind, resourceScope),
            "Approval",
            "Approval body");
}
