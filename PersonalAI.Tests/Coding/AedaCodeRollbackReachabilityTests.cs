using Microsoft.Data.Sqlite;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.Presentation.ViewModels;
using PersonalAI.Infrastructure.Coding;
using PersonalAI.Infrastructure.Modules;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Coding;

/// <summary>
/// Exercises M02-03 rollback reachability against real, restartable persistence
/// (SQLite-backed apply/proposal repositories) instead of in-memory fakes, so
/// "app restart" is simulated by discarding every in-memory object and building
/// brand-new repository/service/view-model instances against the same database
/// files - matching how AEDA Code actually persists apply results and backups.
/// </summary>
public sealed class AedaCodeRollbackReachabilityTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.AedaCodeRollbackReachability.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly string _proposalsDb;
    private readonly string _applyDb;
    private readonly WorkspaceId _workspaceId = WorkspaceId.NewId();

    public AedaCodeRollbackReachabilityTests()
    {
        Directory.CreateDirectory(_root);
        _proposalsDb = Path.Combine(_root, "proposals.db");
        _applyDb = Path.Combine(_root, "apply.db");
    }

    [Fact]
    public async Task ModifyApply_SurvivesRestart_IsSelectableAndRollsBackSuccessfully()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var applyResultId = await ApplyProposalAsync(proposal.Id);

        // Simulate an application restart: discard every in-memory object and
        // build a fresh registry, repositories, service, and view model against
        // the same database and workspace root.
        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);

        var historyItem = Assert.Single(viewModel.ApplyResults);
        Assert.Equal(applyResultId, historyItem.ApplyResultId);

        await viewModel.SelectApplyResultAsync(historyItem);

        Assert.True(viewModel.HasRollbackAvailable);
        Assert.True(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));

        await viewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);

        Assert.Contains("Rollback completed", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task AddOnlyApply_SurvivesRestart_IsSelectableAndRollsBackByDeletingFile()
    {
        var proposal = await SaveProposalAsync([
            Edit("src/Added.cs", null, "created\n", PatchProposalFileChangeKind.Add)
        ]);
        var applyResultId = await ApplyProposalAsync(proposal.Id);
        Assert.True(File.Exists(PathFor("src/Added.cs")));

        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        Assert.Equal(applyResultId, historyItem.ApplyResultId);

        await viewModel.SelectApplyResultAsync(historyItem);

        Assert.True(viewModel.HasRollbackAvailable);
        Assert.True(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));

        await viewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);

        Assert.Contains("Rollback completed", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(PathFor("src/Added.cs")));
    }

    [Fact]
    public async Task ModifyChangedAfterApply_RollbackIsOfferedButServiceRefusesAndPreservesFile()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposal.Id);
        Write("src/App.cs", "mutated after apply\n");

        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        await viewModel.SelectApplyResultAsync(historyItem);

        Assert.True(viewModel.HasRollbackAvailable);

        await viewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);

        Assert.Contains("safe blockers", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("mutated after apply\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task AddChangedAfterApply_RollbackIsOfferedButServiceRefusesAndPreservesFile()
    {
        var proposal = await SaveProposalAsync([
            Edit("src/Added.cs", null, "created\n", PatchProposalFileChangeKind.Add)
        ]);
        await ApplyProposalAsync(proposal.Id);
        Write("src/Added.cs", "mutated after apply\n");

        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        await viewModel.SelectApplyResultAsync(historyItem);

        Assert.True(viewModel.HasRollbackAvailable);

        await viewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);

        Assert.Contains("safe blockers", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(PathFor("src/Added.cs")));
        Assert.Equal("mutated after apply\n", Read("src/Added.cs"));
    }

    [Fact]
    public async Task ApplyResultFromAnotherWorkspace_CannotBeSelectedOrRolledBackThroughCurrentWorkspace()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var applyResultId = await ApplyProposalAsync(proposal.Id);

        var otherRoot = Path.Combine(_root, "other-workspace");
        Directory.CreateDirectory(otherRoot);
        var otherWorkspaceId = WorkspaceId.NewId();
        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo");
        registry.Register(otherWorkspaceId, otherRoot, "OtherRepo");

        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);
        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(
            viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == otherWorkspaceId));
        await viewModel.StartSessionCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.ApplyResults);

        var foreignItem = AedaCodeApplyItem.From(new AedaCodeApplySummary(
            applyResultId,
            proposal.Id,
            PatchApplyStatus.Applied,
            1,
            DateTimeOffset.UtcNow));

        await viewModel.SelectApplyResultAsync(foreignItem);

        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
        Assert.Contains("no longer available", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("new\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task MissingPersistedApplyResult_FailsSafelyWithoutCrashing()
    {
        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);

        var missingItem = AedaCodeApplyItem.From(new AedaCodeApplySummary(
            PatchApplyResultId.NewId(),
            PatchProposalId.NewId(),
            PatchApplyStatus.Applied,
            1,
            DateTimeOffset.UtcNow));

        await viewModel.SelectApplyResultAsync(missingItem);

        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
        Assert.Contains("no longer available", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkspaceSwitch_ClearsRehydratedApplyResultReachability()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposal.Id);

        var otherRoot = Path.Combine(_root, "other-workspace-2");
        Directory.CreateDirectory(otherRoot);
        var otherWorkspaceId = WorkspaceId.NewId();
        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo");
        registry.Register(otherWorkspaceId, otherRoot, "OtherRepo");

        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        await viewModel.SelectApplyResultAsync(historyItem);
        Assert.True(viewModel.HasRollbackAvailable);

        await viewModel.SelectWorkspaceAsync(
            viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == otherWorkspaceId));

        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.CanShowRollback);
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));

        // The previous workspace's Apply-history entries must not remain
        // visible/announced under the newly selected workspace.
        Assert.Empty(viewModel.ApplyResults);
        Assert.DoesNotContain(viewModel.ApplyResults, item => item.ApplyResultId == historyItem.ApplyResultId);
    }

    [Fact]
    public async Task WorkspaceSwitch_ViaTwoWayBindingSettingSelectedWorkspaceFirst_StillClearsOldApplyHistory()
    {
        // Reproduces the real Avalonia ordering: the ComboBox's two-way
        // binding assigns SelectedWorkspace directly, BEFORE the
        // SelectWorkspaceAsync command runs - unlike every other test in this
        // file, which only ever invokes the command. A fix that detects the
        // transition by comparing SelectedWorkspace against the command's
        // own argument is blind to this ordering, because by the time the
        // command runs the two are already equal.
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposal.Id);

        var otherRoot = Path.Combine(_root, "other-workspace-binding-order");
        Directory.CreateDirectory(otherRoot);
        var otherWorkspaceId = WorkspaceId.NewId();
        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo");
        registry.Register(otherWorkspaceId, otherRoot, "OtherRepo");

        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        await viewModel.SelectApplyResultAsync(historyItem);
        Assert.NotEmpty(viewModel.ApplyResults);
        Assert.True(viewModel.HasRollbackAvailable);

        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == otherWorkspaceId);

        // Step 1: simulate the two-way binding assigning the property FIRST,
        // with no command involved yet.
        viewModel.SelectedWorkspace = workspaceB;

        // Step 2: assert synchronously, before the command ever runs, that
        // the old workspace's Apply-history/rollback state is already gone.
        Assert.Empty(viewModel.ApplyResults);
        Assert.DoesNotContain(viewModel.ApplyResults, item => item.ApplyResultId == historyItem.ApplyResultId);
        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.CanShowRollback);

        // Step 3: now the command runs, exactly as it would after the
        // binding has already changed the property.
        await viewModel.SelectWorkspaceAsync(workspaceB);

        // Step 4: the old workspace's entries must still be absent, and no
        // stale rollback state may have returned.
        Assert.Empty(viewModel.ApplyResults);
        Assert.DoesNotContain(viewModel.ApplyResults, item => item.ApplyResultId == historyItem.ApplyResultId);
        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.CanShowRollback);
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task WorkspaceSwitch_ToWorkspaceWithItsOwnHistory_ShowsOnlyThatWorkspacesApplyResults()
    {
        Write("src/App.cs", "old\n");
        var proposalA = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var applyResultIdA = await ApplyProposalAsync(proposalA.Id);

        var workspaceBRoot = Path.Combine(_root, "workspace-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();
        Write("src/Other.cs", "b-old\n", workspaceBRoot);
        var proposalB = await SaveProposalAsync(
            [Edit("src/Other.cs", "b-old\n", "b-new\n")], workspaceBId);
        var applyResultIdB = await ApplyProposalAsync(proposalB.Id, workspaceBId, workspaceBRoot);

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var viewModel = await OpenViewModelWithSessionAsync(registry);
        Assert.Single(viewModel.ApplyResults, item => item.ApplyResultId == applyResultIdA);

        // Give Workspace B its own active session so switching to it can
        // reload its persisted history through the existing dashboard path.
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId));
        await viewModel.StartSessionCommand.ExecuteAsync(null);

        Assert.Single(viewModel.ApplyResults, item => item.ApplyResultId == applyResultIdB);
        Assert.DoesNotContain(viewModel.ApplyResults, item => item.ApplyResultId == applyResultIdA);
    }

    [Fact]
    public async Task WorkspaceSwitch_BackAndForthBetweenActiveSessions_ReloadsCorrectlyWithoutDuplicates()
    {
        Write("src/App.cs", "old\n");
        var proposalA = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var applyResultIdA = await ApplyProposalAsync(proposalA.Id);

        var workspaceBRoot = Path.Combine(_root, "workspace-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();
        Write("src/Other.cs", "b-old\n", workspaceBRoot);
        var proposalB = await SaveProposalAsync(
            [Edit("src/Other.cs", "b-old\n", "b-new\n")], workspaceBId);
        var applyResultIdB = await ApplyProposalAsync(proposalB.Id, workspaceBId, workspaceBRoot);

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var viewModel = await OpenViewModelWithSessionAsync(registry);
        Assert.Single(viewModel.ApplyResults, item => item.ApplyResultId == applyResultIdA);

        // Switch to B without starting a session there: A's own session
        // (from OpenViewModelWithSessionAsync) remains the view model's only
        // active session, so B correctly shows no history of its own yet.
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId));
        Assert.Empty(viewModel.ApplyResults);

        // Switch back to A: its session is still the active one, so the
        // matching-session reload path must bring A's history back with no
        // duplicates and no B entries (B has none loaded to begin with).
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId));

        var current = viewModel.ApplyResults.ToArray();
        Assert.Single(current, item => item.ApplyResultId == applyResultIdA);
        Assert.DoesNotContain(current, item => item.ApplyResultId == applyResultIdB);
        Assert.Equal(current.Length, current.Select(item => item.ApplyResultId).Distinct().Count());
    }

    [Fact]
    public async Task WorkspaceSwitch_ToNullWorkspace_ClearsApplyHistoryAndRollbackState()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposal.Id);

        var registry = OpenRegistry();
        var viewModel = await OpenViewModelWithSessionAsync(registry);
        var historyItem = Assert.Single(viewModel.ApplyResults);
        await viewModel.SelectApplyResultAsync(historyItem);
        Assert.True(viewModel.HasRollbackAvailable);

        await viewModel.SelectWorkspaceAsync(null);

        Assert.Empty(viewModel.ApplyResults);
        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.CanShowRollback);
    }

    [Fact]
    public async Task RapidWorkspaceReselection_DelayedLoadForStaleWorkspaceCannotPopulateApplyResultsUnderCurrentWorkspace()
    {
        Write("src/App.cs", "old\n");
        var proposalA = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposalA.Id);

        var workspaceCRoot = Path.Combine(_root, "workspace-c");
        Directory.CreateDirectory(workspaceCRoot);
        var workspaceCId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceCId, workspaceCRoot, "Repo C");

        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);
        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();

        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId));
        await viewModel.StartSessionCommand.ExecuteAsync(null);
        Assert.Single(viewModel.ApplyResults);
        var sessionAId = viewModel.Session!.Id;

        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceCId));
        Assert.Empty(viewModel.ApplyResults);
        Assert.Equal(sessionAId, viewModel.Session!.Id);

        // Re-select A while its session is still active, and prove that A's
        // matching-session dashboard load is the call held behind the gate.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedLoad = new TaskCompletionSource<(AedaCodeSessionId SessionId, WorkspaceId WorkspaceId)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextDashboardLoad = (sessionId, workspaceId) =>
        {
            delayedLoad.SetResult((sessionId, workspaceId));
            return gate.Task;
        };

        var switchToA = viewModel.SelectWorkspaceAsync(
            viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId));
        var capturedLoad = await delayedLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(sessionAId, capturedLoad.SessionId);
        Assert.Equal(_workspaceId, capturedLoad.WorkspaceId);
        Assert.False(switchToA.IsCompleted);

        await viewModel.SelectWorkspaceAsync(
            viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceCId));
        Assert.Equal(workspaceCId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Empty(viewModel.ApplyResults);

        gate.SetResult();
        await switchToA;

        // C was selected last: A's delayed dashboard must not have been
        // allowed to populate ApplyResults out from under it.
        Assert.Equal(workspaceCId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Empty(viewModel.ApplyResults);
    }

    [Fact]
    public async Task RolledBackApplyResult_StaysUnavailableAfterRestart_ViaPersistedProposalStatus()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var applyResultId = await ApplyProposalAsync(proposal.Id);

        // Roll back once, in the same session that applied it.
        var registry = OpenRegistry();
        var firstViewModel = await OpenViewModelWithSessionAsync(registry);
        var firstHistoryItem = Assert.Single(firstViewModel.ApplyResults);
        await firstViewModel.SelectApplyResultAsync(firstHistoryItem);
        Assert.True(firstViewModel.HasRollbackAvailable);
        await firstViewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);
        Assert.Contains("Rollback completed", firstViewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("old\n", Read("src/App.cs"));

        // Simulate a full application restart: discard every in-memory object
        // (registry, repositories, service, view model) and rebuild them fresh
        // against the same persisted database.
        var restartedRegistry = OpenRegistry();
        var restartedViewModel = await OpenViewModelWithSessionAsync(restartedRegistry);

        var rehydratedItem = Assert.Single(restartedViewModel.ApplyResults);
        Assert.Equal(applyResultId, rehydratedItem.ApplyResultId);
        Assert.Equal(1, rehydratedItem.FileCount);

        await restartedViewModel.SelectApplyResultAsync(rehydratedItem);

        // The persisted proposal - not just in-memory rollback state from the
        // prior session - is what suppresses a repeat destructive rollback.
        var (persistedProposalRepository, persistedApplyRepository) = OpenRepositories();
        var persistedProposal = await persistedProposalRepository.GetAsync(proposal.Id);
        Assert.Equal(PatchProposalStatus.RolledBack, persistedProposal!.Status);
        var persistedApplyResult = await persistedApplyRepository.GetApplyResultAsync(applyResultId);
        Assert.NotNull(persistedApplyResult);
        Assert.NotEmpty(persistedApplyResult!.Files);

        Assert.False(restartedViewModel.HasRollbackAvailable);
        Assert.False(restartedViewModel.CanShowRollback);
        Assert.False(restartedViewModel.RollbackSelectedApplyResultCommand.CanExecute(null));

        // A repeat rollback attempt must not run as a fresh destructive
        // operation: the file must remain exactly as the first rollback left it.
        await restartedViewModel.RollbackSelectedApplyResultCommand.ExecuteAsync(null);
        Assert.Equal("old\n", Read("src/App.cs"));
    }

    private async Task<PatchApplyResultId> ApplyProposalAsync(
        PatchProposalId proposalId,
        WorkspaceId? workspaceId = null,
        string? root = null)
    {
        var id = workspaceId ?? _workspaceId;
        var (proposalRepository, applyRepository) = OpenRepositories();
        var approvals = new InMemoryApprovalCheckpointStore();
        var reader = CreateReader(id, root);
        var validator = new PatchApplyValidator(proposalRepository, reader, CreateResolver(id, root));
        var applyService = new PatchApplyService(proposalRepository, applyRepository, validator, reader, approvals);
        var approval = await applyService.RequestApplyApprovalAsync(proposalId, id);
        var decision = await approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);
        var applied = await applyService.ApplyAsync(new PatchApplyRequest(proposalId, id, approval, decision));
        return applied.Id;
    }

    private PatchApplyService CreateApplyService(
        IPatchProposalRepository proposalRepository,
        IPatchApplyRepository applyRepository)
    {
        var reader = CreateReader();
        var validator = new PatchApplyValidator(proposalRepository, reader, CreateResolver());
        return new PatchApplyService(proposalRepository, applyRepository, validator, reader, new InMemoryApprovalCheckpointStore());
    }

    private FileSystemWorkspaceReader CreateReader(WorkspaceId? workspaceId = null, string? root = null)
    {
        var id = workspaceId ?? _workspaceId;
        var registry = new WorkspaceRegistry();
        registry.Register(id, root ?? _root, "Repo");
        return new FileSystemWorkspaceReader(registry, new WorkspacePathResolver(registry), new WorkspaceToolOptions());
    }

    private WorkspacePathResolver CreateResolver(WorkspaceId? workspaceId = null, string? root = null)
    {
        var id = workspaceId ?? _workspaceId;
        var registry = new WorkspaceRegistry();
        registry.Register(id, root ?? _root, "Repo");
        return new WorkspacePathResolver(registry);
    }

    private (IPatchProposalRepository Proposals, IPatchApplyRepository Applies) OpenRepositories()
    {
        var proposals = new SqlitePatchProposalRepository(_proposalsDb);
        var applies = new SqlitePatchApplyRepository(_applyDb);
        proposals.InitializeAsync().GetAwaiter().GetResult();
        applies.InitializeAsync().GetAwaiter().GetResult();
        return (proposals, applies);
    }

    private WorkspaceRegistry OpenRegistry()
    {
        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo");
        return registry;
    }

    private async Task<AedaCodeModuleViewModel> OpenViewModelWithSessionAsync(WorkspaceRegistry registry)
    {
        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);
        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(
            viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId));
        await viewModel.StartSessionCommand.ExecuteAsync(null);
        return viewModel;
    }

    private static AedaCodeModuleViewModel BuildViewModel(
        WorkspaceRegistry registry,
        IAedaCodeModuleService service)
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
            hasPatchApply: true,
            hasPatchRollback: true,
            hasControlledValidation: true,
            hasAedaModules: true,
            hasAedaCodeModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true,
            hasCodeTaskTimeline: true,
            hasTaskCenter: true,
            hasActivityTimeline: true,
            hasApprovalInbox: true,
            hasTaskArtifactLinks: true);
        var moduleRegistry = new AedaModuleRegistry(
            [AedaCodeModuleDescriptorFactory.Create(capabilities)]);
        var approvalStore = new InMemoryApprovalCheckpointStore();
        return new AedaCodeModuleViewModel(
            service,
            moduleRegistry,
            registry,
            new FakeTaskCenterService(),
            approvalStore);
    }

    private static PatchProposalFileEdit Edit(
        string path,
        string? original,
        string? proposed,
        PatchProposalFileChangeKind kind = PatchProposalFileChangeKind.Modify) =>
        new(path, original, proposed, kind);

    private async Task<PatchProposal> SaveProposalAsync(
        IReadOnlyList<PatchProposalFileEdit> edits,
        WorkspaceId? workspaceId = null)
    {
        var files = edits.Select(edit => new UnifiedDiffBuilder().BuildFileDiff(edit)).ToArray();
        var now = DateTimeOffset.UtcNow;
        var proposal = new PatchProposal(
            PatchProposalId.NewId(),
            workspaceId ?? _workspaceId,
            "Apply proposal",
            "Apply safely",
            PatchProposalStatus.ReadyForReview,
            PatchProposalRisk.Low,
            ["small_text_change"],
            files,
            [],
            new ValidationPlanService().CreatePlan(files),
            now,
            now);
        var (proposalRepository, _) = OpenRepositories();
        await proposalRepository.CreateAsync(proposal);
        return proposal;
    }

    private string PathFor(string relativePath, string? root = null) =>
        Path.Combine(root ?? _root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relativePath, string content, string? root = null)
    {
        var path = PathFor(relativePath, root);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relativePath, string? root = null) => File.ReadAllText(PathFor(relativePath, root));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A minimal <see cref="IAedaCodeModuleService"/> that delegates apply/rollback
    /// operations to a real <see cref="PatchApplyService"/> backed by SQLite
    /// repositories, so tests exercise genuine persistence rather than an
    /// in-memory stand-in. Session state is intentionally NOT persisted here,
    /// mirroring the production AedaCodeModuleService, whose session list is
    /// also purely in-memory - only proposals, apply results, and backups
    /// survive a restart.
    /// </summary>
    private sealed class PersistedApplyModuleService(
        IPatchApplyService applyService,
        IPatchProposalRepository proposalRepository) : IAedaCodeModuleService
    {
        private readonly Dictionary<AedaCodeSessionId, WorkspaceId> _sessions = [];

        public Task<AedaCodeSession> StartSessionAsync(
            WorkspaceId workspaceId,
            string? safeSummary = null,
            CancellationToken cancellationToken = default)
        {
            var session = new AedaCodeSession(
                AedaCodeSessionId.NewId(),
                workspaceId,
                "Repo",
                null,
                null,
                null,
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                AedaCodeSessionStatus.Active,
                safeSummary ?? "Session");
            _sessions[session.Id] = workspaceId;
            return Task.FromResult(session);
        }

        public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
            int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeSession>>([]);

        public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
            WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AedaCodeWorkspaceSummary(workspaceId, "Repo", true));

        public Task<CodeContextPack> ReadFilesAsync(
            WorkspaceId workspaceId, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeContextPack> SearchAsync(
            CodeContextSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AedaCodeContextSearchResult> SearchContextFilesAsync(
            AedaCodeContextSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AedaCodeTargetSnippetCandidate>> ListTargetSnippetCandidatesAsync(
            AedaCodeTargetSnippetRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeChangePlan> CreatePlanAsync(
            CodeChangeRequest request, CodeContextPack context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchProposal> CreateProposalAsync(
            PatchProposalCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AedaCodeProposalCreationResult> CreateProposalFromRequestAsync(
            AedaCodeProposalCreationRequest request,
            IProgress<AedaCodeProposalCreationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
            WorkspaceId workspaceId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeProposalSummary>>([]);

        public Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(
            WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ValidationCommandTemplate>>([]);

        public Task<PatchApplyPlan> DryRunApplyAsync(
            PatchApplyRequest request, CancellationToken cancellationToken = default) =>
            applyService.DryRunAsync(request, cancellationToken);

        public Task<ApprovalRequest> RequestApplyApprovalAsync(
            PatchProposalId proposalId, WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            applyService.RequestApplyApprovalAsync(proposalId, workspaceId, cancellationToken);

        public Task<PatchApplyResult> ApplyApprovedProposalAsync(
            PatchApplyRequest request, CancellationToken cancellationToken = default) =>
            applyService.ApplyAsync(request, cancellationToken);

        public Task<PatchProposal?> GetProposalAsync(
            PatchProposalId proposalId, CancellationToken cancellationToken = default) =>
            proposalRepository.GetAsync(proposalId, cancellationToken);

        public Task<PatchApplyResult?> GetApplyResultAsync(
            PatchApplyResultId applyResultId, CancellationToken cancellationToken = default) =>
            applyService.GetApplyResultAsync(applyResultId, cancellationToken);

        public Task<PatchRollbackResult> RollbackAsync(
            PatchRollbackRequest request, CancellationToken cancellationToken = default) =>
            applyService.RollbackAsync(request, cancellationToken);

        public Task<ValidationRun> CreateValidationRunAsync(
            ValidationRunRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ApprovalRequest> RequestValidationApprovalAsync(
            ValidationRunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ValidationRun> RunApprovedValidationAsync(
            ValidationRunId runId,
            ApprovalRequest approvalRequest,
            ApprovalDecision approvalDecision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <summary>
        /// When set, the next <see cref="GetDashboardAsync"/> call awaits this
        /// before proceeding, then clears itself. Lets tests simulate a slow
        /// dashboard load to exercise the workspace-switch race guard.
        /// </summary>
        public Func<AedaCodeSessionId, WorkspaceId, Task>? DelayNextDashboardLoad;

        public async Task<AedaCodeDashboardModel> GetDashboardAsync(
            AedaCodeSessionId sessionId, CancellationToken cancellationToken = default)
        {
            var workspaceId = _sessions[sessionId];
            if (DelayNextDashboardLoad is { } delay)
            {
                DelayNextDashboardLoad = null;
                await delay(sessionId, workspaceId);
            }

            var recent = await applyService.ListRecentApplyResultsAsync(50, cancellationToken);
            var applySummaries = recent
                .Where(result => result.WorkspaceId == workspaceId)
                .Select(result => new AedaCodeApplySummary(
                    result.Id,
                    result.ProposalId,
                    result.Status,
                    result.Files.Count,
                    result.UpdatedAtUtc))
                .ToArray();
            return new AedaCodeDashboardModel(
                new AedaCodeSession(
                    sessionId,
                    workspaceId,
                    "Repo",
                    null,
                    null,
                    null,
                    null,
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow,
                    AedaCodeSessionStatus.Active,
                    "Session"),
                new AedaCodeWorkspaceSummary(workspaceId, "Repo", true),
                [],
                applySummaries,
                [],
                []);
        }
    }

    private sealed class FakeTaskCenterService : IAedaTaskCenterService
    {
        private readonly AedaTaskSummary _task = new(
            TaskId.NewId(),
            "Code task",
            new AedaTaskStatusBadge(AedaTaskCenterStatus.Completed, "Completed", "completed", false, true),
            new AedaTaskModuleBadge(AedaTaskCenterModule.Code, "Code", AedaModuleId.Code, "aeda-code"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "Safe task summary",
            []);

        public ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(AedaTaskFilter? filter = null, CancellationToken cancellationToken = default) =>
            new(new AedaTaskCenterDashboard([], [], [_task], [], new Dictionary<AedaTaskCenterStatus, int>(), new Dictionary<AedaTaskCenterModule, int>(), DateTimeOffset.UtcNow, "ok"));

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default) =>
            new ValueTask<IReadOnlyList<AedaTaskSummary>>([]);

        public ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(int limit, CancellationToken cancellationToken = default) =>
            new ValueTask<IReadOnlyList<AedaTaskApprovalSummary>>([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(int limit, CancellationToken cancellationToken = default) =>
            new ValueTask<IReadOnlyList<AedaTaskSummary>>([_task]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(int limit, CancellationToken cancellationToken = default) =>
            new ValueTask<IReadOnlyList<AedaTaskSummary>>([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(AedaTaskCenterModule module, int limit, CancellationToken cancellationToken = default) =>
            new ValueTask<IReadOnlyList<AedaTaskSummary>>(module == AedaTaskCenterModule.Code ? [_task] : []);

        public ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(TaskId taskId, int limit = 100, CancellationToken cancellationToken = default) =>
            new(Array.Empty<AedaTaskActivityGroup>());

        public ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(TaskId taskId, Guid eventId, CancellationToken cancellationToken = default) =>
            new ValueTask<AedaTaskTimelineItem?>((AedaTaskTimelineItem?)null);

        public ValueTask CancelTaskAsync(TaskId taskId, TaskCancellationReason reason, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
