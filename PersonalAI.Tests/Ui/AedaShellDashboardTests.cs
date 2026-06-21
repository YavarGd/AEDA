using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Infrastructure.Modules;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Ui;

public sealed class AedaShellDashboardTests
{
    [Fact]
    public void NavigationState_DefaultsToChatAndRoutesDeterministically()
    {
        var navigation = new AedaShellNavigationState();
        var code = Module(
            AedaModuleId.Code,
            AedaModuleKind.Code,
            AedaModuleStatus.Available,
            "aeda-code");
        var claw = Module(
            new AedaModuleId("claw"),
            AedaModuleKind.Claw,
            AedaModuleStatus.Unavailable,
            "claw");

        Assert.Equal(AedaShellSection.Chat, navigation.CurrentSection);

        navigation.OpenDashboard();
        Assert.Equal(AedaShellSection.Dashboard, navigation.CurrentSection);

        navigation.OpenChat();
        Assert.Equal(AedaShellSection.Chat, navigation.CurrentSection);

        Assert.True(navigation.TryOpenModule(code));
        Assert.Equal(AedaShellSection.Code, navigation.CurrentSection);
        Assert.Equal(AedaModuleId.Code, navigation.CurrentRoute.ModuleId);
        Assert.Equal("aeda-code", navigation.CurrentRoute.RouteId);

        Assert.False(navigation.TryOpenModule(claw));
        Assert.Equal(AedaShellSection.Code, navigation.CurrentSection);
    }

    [Fact]
    public void Dashboard_LoadsTilesFromRegistryWithSafeAvailability()
    {
        var registry = CreateRegistry();
        var dashboard = new AedaModuleDashboardViewModel(
            registry,
            new FakeTaskQueryService(),
            new WorkspaceRegistry(),
            _ => { });

        var tiles = dashboard.ModuleTiles.ToArray();
        var code = tiles.Single(tile => tile.Kind == AedaModuleKind.Code);
        var deferred = tiles.Where(tile => tile.Kind != AedaModuleKind.Code).ToArray();

        Assert.Equal(
            tiles.OrderBy(tile => tile.Descriptor.SortOrder)
                .Select(tile => tile.DisplayName),
            tiles.Select(tile => tile.DisplayName));
        Assert.True(code.IsEnabled);
        Assert.Equal("Available", code.StatusLabel);
        Assert.NotEmpty(code.CapabilityHints);
        Assert.All(deferred, tile =>
        {
            Assert.False(tile.IsEnabled);
            Assert.Equal("Coming later", tile.StatusLabel);
            Assert.True(tile.HasUnavailableReason);
            Assert.DoesNotContain("C:\\", tile.SafeUnavailableReason);
        });
        Assert.False(deferred[0].OpenCommand.CanExecute(null));
    }

    [Fact]
    public async Task Dashboard_TaskSummariesAreBoundedAndSafe()
    {
        var tasks = new FakeTaskQueryService
        {
            Running =
            [
                TaskRun.Create("Running build", "code"),
                TaskRun.Create("Waiting approval", "code")
                    with { Status = TaskRunStatus.WaitingForApproval }
            ],
            Recent = Enumerable.Range(0, 20)
                .Select(index => TaskRun.Create($"Task {index}", "chat")
                    with
                    {
                        Status = index % 3 == 0
                            ? TaskRunStatus.Failed
                            : TaskRunStatus.Completed,
                        SafeErrorCode = index % 3 == 0
                            ? "safe_failure"
                            : null
                    })
                .ToArray()
        };
        var dashboard = new AedaModuleDashboardViewModel(
            CreateRegistry(),
            tasks,
            new WorkspaceRegistry(),
            _ => { });

        await dashboard.RefreshTaskSummariesAsync();

        Assert.Equal(2, dashboard.ActiveTasks.Count);
        Assert.Equal(AedaModuleDashboardViewModel.RecentTaskLimit, dashboard.RecentTasks.Count);
        Assert.Contains(dashboard.RecentTasks, item => item.SafeSummary.Contains("safe_failure", StringComparison.Ordinal));
        Assert.DoesNotContain(dashboard.RecentTasks, item =>
            item.SafeSummary.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            item.SafeSummary.Contains("C:\\", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AedaCodeSurface_LoadsDescriptorAndBoundedReadModels()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService();
        var viewModel = new AedaCodeModuleViewModel(service, registry)
        {
            Dashboard = CreateDashboard()
        };

        await viewModel.InitializeAsync();

        Assert.Equal("AEDA Code", viewModel.DisplayName);
        Assert.NotEmpty(viewModel.CapabilityBadges);
        Assert.True(viewModel.HasRecentSessions);
        Assert.Equal(6, viewModel.ProposalSummaries.Count);
        Assert.Equal(6, viewModel.ValidationSummaries.Count);
        Assert.Equal(6, viewModel.ApplySummaries.Count);
        Assert.Equal(6, viewModel.TimelineSummaries.Count);
        Assert.DoesNotContain(viewModel.ProposalSummaries, item =>
            item.RelativePaths.Any(path => Path.IsPathRooted(path)));
        Assert.DoesNotContain(viewModel.TimelineSummaries, item =>
            item.Summary.Contains("stdout", StringComparison.OrdinalIgnoreCase) ||
            item.Summary.Contains("class Secret", StringComparison.Ordinal));
    }

    private static IAedaModuleRegistry CreateRegistry()
    {
        var capabilities = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasCodeContextRead: true,
            hasCodeChangePlanning: true,
            hasPatchProposal: true,
            hasAedaModules: true,
            hasAedaCodeModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true,
            hasCodeTaskTimeline: true);

        return new AedaModuleRegistry(
            [
                AedaCodeModuleDescriptorFactory.Create(capabilities),
                .. AedaDeferredModuleDescriptorFactory.CreateAll()
            ]);
    }

    private static AedaModuleDescriptor Module(
        AedaModuleId id,
        AedaModuleKind kind,
        AedaModuleStatus status,
        string route) =>
        new(
            id,
            kind,
            kind.ToString(),
            "Description",
            "\uE943",
            status,
            [],
            new AedaModuleRoute(route),
            status == AedaModuleStatus.Unavailable
                ? "module_deferred"
                : null,
            10);

    private static AedaCodeDashboardModel CreateDashboard()
    {
        var workspaceId = WorkspaceId.NewId();
        var session = new AedaCodeSession(
            AedaCodeSessionId.NewId(),
            workspaceId,
            "Workspace",
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            AedaCodeSessionStatus.Active,
            "Safe session");

        return new AedaCodeDashboardModel(
            session,
            new AedaCodeWorkspaceSummary(workspaceId, "Workspace", true, 2, 1),
            Enumerable.Range(0, 12)
                .Select(index => new AedaCodeProposalSummary(
                    PatchProposalId.NewId(),
                    $"Proposal {index}",
                    PatchProposalStatus.ReadyForReview,
                    new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "small_text_change"),
                    [$"src/File{index}.cs"],
                    DateTimeOffset.UtcNow))
                .ToArray(),
            Enumerable.Range(0, 12)
                .Select(_ => new AedaCodeApplySummary(
                    PatchApplyResultId.NewId(),
                    PatchProposalId.NewId(),
                    PatchApplyStatus.DryRunPassed,
                    1,
                    DateTimeOffset.UtcNow))
                .ToArray(),
            Enumerable.Range(0, 12)
                .Select(_ => new AedaCodeValidationSummary(
                    ValidationRunId.NewId(),
                    "dotnet-build-debug",
                    ValidationRunStatus.Created,
                    null,
                    null,
                    DateTimeOffset.UtcNow))
                .ToArray(),
            Enumerable.Range(0, 12)
                .Select(index => new AedaCodeTimelineItem(
                    DateTimeOffset.UtcNow,
                    "task",
                    $"Task {index} completed"))
                .ToArray());
    }

    private sealed class FakeTaskQueryService : ITaskQueryService
    {
        public IReadOnlyList<TaskRun> Recent { get; init; } = [];

        public IReadOnlyList<TaskRun> Running { get; init; } = [];

        public ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(Recent.Take(limit).ToArray());

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
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(Running.Take(limit).ToArray());

        public ValueTask<IReadOnlyList<TaskRun>> SearchByStatusAsync(
            TaskRunStatus status,
            int limit,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<TaskRun>>(
                Recent.Where(task => task.Status == status).Take(limit).ToArray());
    }

    private sealed class FakeAedaCodeModuleService : IAedaCodeModuleService
    {
        public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
            int limit = 20,
            CancellationToken cancellationToken = default)
        {
            var session = CreateDashboard().Session;
            return Task.FromResult<IReadOnlyList<AedaCodeSession>>([session]);
        }

        public Task<AedaCodeSession> StartSessionAsync(
            WorkspaceId workspaceId,
            string? safeSummary = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeContextPack> ReadFilesAsync(
            WorkspaceId workspaceId,
            IReadOnlyList<string> relativePaths,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeContextPack> SearchAsync(
            CodeContextSearchRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeChangePlan> CreatePlanAsync(
            CodeChangeRequest request,
            CodeContextPack context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchProposal> CreateProposalAsync(
            PatchProposalCreateRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchProposal?> GetProposalAsync(
            PatchProposalId proposalId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
            WorkspaceId workspaceId,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchApplyPlan> DryRunApplyAsync(
            PatchApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApprovalRequest> RequestApplyApprovalAsync(
            PatchProposalId proposalId,
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchApplyResult> ApplyApprovedProposalAsync(
            PatchApplyRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchRollbackResult> RollbackAsync(
            PatchRollbackRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ValidationRun> CreateValidationRunAsync(
            ValidationRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApprovalRequest> RequestValidationApprovalAsync(
            ValidationRunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ValidationRun> RunApprovedValidationAsync(
            ValidationRunId runId,
            ApprovalRequest approvalRequest,
            ApprovalDecision approvalDecision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AedaCodeDashboardModel> GetDashboardAsync(
            AedaCodeSessionId sessionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDashboard());
    }
}
