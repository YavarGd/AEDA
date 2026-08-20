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
    public async Task DelayedProposalListForStaleWorkspace_DoesNotMixIntoNewlySelectedWorkspaceState()
    {
        var workspaceBRoot = Path.Combine(_root, "workspace-mixed-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);

        var proposalAId = PatchProposalId.NewId();
        var proposalBId = PatchProposalId.NewId();
        var risk = new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "small_text_change");
        service.ProposalsByWorkspace[_workspaceId] =
            [new AedaCodeProposalSummary(proposalAId, "Proposal A", PatchProposalStatus.ReadyForReview, risk, ["src/App.cs"], DateTimeOffset.UtcNow)];
        service.ProposalsByWorkspace[workspaceBId] =
            [new AedaCodeProposalSummary(proposalBId, "Proposal B", PatchProposalStatus.ReadyForReview, risk, ["src/Other.cs"], DateTimeOffset.UtcNow)];
        service.TemplatesByWorkspace[_workspaceId] =
            [new ValidationCommandTemplate("template-a", "Template A", "dotnet", ["test"], TimeSpan.FromMinutes(1), "src/App.cs")];
        service.TemplatesByWorkspace[workspaceBId] =
            [new ValidationCommandTemplate("template-b", "Template B", "dotnet", ["test"], TimeSpan.FromMinutes(1), "src/Other.cs")];

        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();

        var workspaceA = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId);
        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId);

        // Begin loading A, but delay its proposal-list retrieval so it is
        // still in flight when the user switches to B.
        var delayedProposalLoad = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextProposalList = workspaceId =>
        {
            delayedProposalLoad.SetResult(workspaceId);
            return gate.Task;
        };

        var switchToA = viewModel.SelectWorkspaceAsync(workspaceA);
        var captured = await delayedProposalLoad.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(_workspaceId, captured);
        Assert.False(switchToA.IsCompleted);

        // Switch to B before A's proposal list resolves; B's own load is not
        // delayed and completes fully.
        await viewModel.SelectWorkspaceAsync(workspaceB);
        Assert.Single(viewModel.Proposals, item => item.ProposalId == proposalBId);
        Assert.Single(viewModel.ValidationTemplates, item => item.Id == "template-b");

        // Now release A's delayed proposal list (its subsequent template
        // request, for the still-stale workspace, resolves immediately).
        // The whole stale snapshot must be discarded silently - never
        // committed under B, and never mixed with B's own state.
        gate.SetResult();
        await switchToA;

        Assert.DoesNotContain(viewModel.Proposals, item => item.ProposalId == proposalAId);
        Assert.Single(viewModel.Proposals, item => item.ProposalId == proposalBId);
        Assert.DoesNotContain(viewModel.ValidationTemplates, item => item.Id == "template-a");
        Assert.Single(viewModel.ValidationTemplates, item => item.Id == "template-b");
        Assert.Equal(workspaceBId, viewModel.SelectedWorkspace!.WorkspaceId);
    }

    [Fact]
    public async Task RefreshWithSessionBelongingToDifferentWorkspace_DoesNotApplyOrReselectThatWorkspace()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        await ApplyProposalAsync(proposal.Id);

        var workspaceBRoot = Path.Combine(_root, "workspace-refresh-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var viewModel = await OpenViewModelWithSessionAsync(registry);
        Assert.Single(viewModel.ApplyResults);
        Assert.Equal(_workspaceId, viewModel.Session!.WorkspaceId);

        // Switch to B without starting a session there: the active session
        // still belongs to A.
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId));
        Assert.Empty(viewModel.ApplyResults);

        await viewModel.RefreshCommand.ExecuteAsync(null);

        // B must remain selected; A's dashboard/history must not have been
        // applied beneath it, and ApplyDashboard must not have silently
        // reselected A.
        Assert.Equal(workspaceBId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Empty(viewModel.ApplyResults);
        Assert.Empty(viewModel.Proposals);
    }

    [Fact]
    public async Task DelayedStartSessionForStaleWorkspace_DoesNotReselectOrPopulateThatWorkspace()
    {
        var workspaceBRoot = Path.Combine(_root, "workspace-start-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);
        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();

        var workspaceA = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId);
        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId);

        await viewModel.SelectWorkspaceAsync(workspaceA);

        var delayedStart = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextStartSession = workspaceId =>
        {
            delayedStart.SetResult(workspaceId);
            return gate.Task;
        };

        var startA = viewModel.StartSessionCommand.ExecuteAsync(null);
        var captured = await delayedStart.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(_workspaceId, captured);
        Assert.False(startA.IsCompleted);
        Assert.Null(viewModel.Session);

        // Switch to B before A's session finishes starting.
        await viewModel.SelectWorkspaceAsync(workspaceB);
        Assert.Null(viewModel.Session);
        Assert.Empty(viewModel.ApplyResults);

        // Release A's delayed session start.
        gate.SetResult();
        await startA;

        // B remains selected, with no session and no dashboard/history
        // committed under it as a result of A's stale completion.
        Assert.Equal(workspaceBId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Null(viewModel.Session);
        Assert.Empty(viewModel.ApplyResults);
    }

    [Fact]
    public async Task DelayedApplyCompletionForStaleWorkspace_DoesNotSurfaceUnderNewlySelectedWorkspace()
    {
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);

        var workspaceBRoot = Path.Combine(_root, "workspace-apply-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        // AllowApplyOnceAsync decides against the view model's own approval
        // store, while RequestApplyApprovalAsync issues the request through
        // whatever store the module service's PatchApplyService was built
        // with - share one store between both so the decision round-trips,
        // exactly as production composition wires a single approval store
        // throughout.
        var sharedApprovals = new InMemoryApprovalCheckpointStore();
        var (proposalRepository, applyRepository) = OpenRepositories();
        var reader = CreateReader();
        var validator = new PatchApplyValidator(proposalRepository, reader, CreateResolver());
        var patchApplyService = new PatchApplyService(proposalRepository, applyRepository, validator, reader, sharedApprovals);
        var service = new PersistedApplyModuleService(patchApplyService, proposalRepository);
        var viewModel = BuildViewModel(registry, service, sharedApprovals);
        await viewModel.InitializeAsync();

        var workspaceA = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId);
        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId);

        await viewModel.SelectWorkspaceAsync(workspaceA);
        await viewModel.StartSessionCommand.ExecuteAsync(null);

        var proposalItem = new AedaCodeProposalItem(
            proposal.Id,
            proposal.Title,
            proposal.Status,
            "Low",
            "small",
            proposal.Files.Count,
            "just now",
            "safe",
            new AedaCodeProposalSummary(
                proposal.Id,
                proposal.Title,
                proposal.Status,
                new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "small_text_change"),
                proposal.Files.Select(file => file.RelativePath).ToArray(),
                proposal.UpdatedAtUtc));
        await viewModel.SelectProposalAsync(proposalItem);
        await viewModel.DryRunSelectedProposalCommand.ExecuteAsync(null);
        await viewModel.RequestApplyApprovalCommand.ExecuteAsync(null);
        await viewModel.AllowApplyOnceCommand.ExecuteAsync(null);

        // Delay the Apply completion until after the backend result already
        // exists (persisted), but before it is returned to the view model.
        var delayedApply = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextApplyCompletion = request =>
        {
            delayedApply.SetResult(request.WorkspaceId);
            return gate.Task;
        };

        var applyA = viewModel.ApplyApprovedProposalCommand.ExecuteAsync(null);
        var capturedApply = await delayedApply.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(_workspaceId, capturedApply);
        Assert.False(applyA.IsCompleted);

        // Switch to B while A's apply result is held behind the gate.
        await viewModel.SelectWorkspaceAsync(workspaceB);
        Assert.Empty(viewModel.ApplyResults);
        Assert.False(viewModel.HasRollbackAvailable);

        gate.SetResult();
        await applyA;

        // B's visible Apply history/current result must remain untouched by
        // A's stale completion.
        Assert.Equal(workspaceBId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Empty(viewModel.ApplyResults);
        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.CanShowRollback);

        // The persisted result is not discarded or corrupted: it is
        // available the next time A is legitimately reloaded.
        var (persistedProposalRepository, persistedApplyRepository) = OpenRepositories();
        var applyResults = await persistedApplyRepository.ListRecentApplyResultsAsync(50, CancellationToken.None);
        var persisted = Assert.Single(applyResults, result => result.ProposalId == proposal.Id);
        Assert.Equal(_workspaceId, persisted.WorkspaceId);
        Assert.Equal(PatchApplyStatus.Applied, persisted.Status);
    }

    [Fact]
    public async Task RapidWorkspaceSwitch_AtoBtoC_CompletingInReverseOrder_LeavesOnlyCState()
    {
        var workspaceBRoot = Path.Combine(_root, "workspace-rapid-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();
        var workspaceCRoot = Path.Combine(_root, "workspace-rapid-c");
        Directory.CreateDirectory(workspaceCRoot);
        var workspaceCId = WorkspaceId.NewId();

        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");
        registry.Register(workspaceCId, workspaceCRoot, "Repo C");

        var (proposalRepository, applyRepository) = OpenRepositories();
        var service = new PersistedApplyModuleService(CreateApplyService(proposalRepository, applyRepository), proposalRepository);

        var proposalAId = PatchProposalId.NewId();
        var proposalBId = PatchProposalId.NewId();
        var proposalCId = PatchProposalId.NewId();
        var risk = new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "small_text_change");
        service.ProposalsByWorkspace[_workspaceId] =
            [new AedaCodeProposalSummary(proposalAId, "Proposal A", PatchProposalStatus.ReadyForReview, risk, ["src/A.cs"], DateTimeOffset.UtcNow)];
        service.ProposalsByWorkspace[workspaceBId] =
            [new AedaCodeProposalSummary(proposalBId, "Proposal B", PatchProposalStatus.ReadyForReview, risk, ["src/B.cs"], DateTimeOffset.UtcNow)];
        service.ProposalsByWorkspace[workspaceCId] =
            [new AedaCodeProposalSummary(proposalCId, "Proposal C", PatchProposalStatus.ReadyForReview, risk, ["src/C.cs"], DateTimeOffset.UtcNow)];

        var viewModel = BuildViewModel(registry, service);
        await viewModel.InitializeAsync();

        var workspaceA = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId);
        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId);
        var workspaceC = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceCId);

        // Delay A's proposal list so A's overall load outlives B's and C's.
        var gateA = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedA = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextProposalList = workspaceId =>
        {
            delayedA.SetResult(workspaceId);
            return gateA.Task;
        };

        var switchToA = viewModel.SelectWorkspaceAsync(workspaceA);
        await delayedA.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(switchToA.IsCompleted);

        // B is selected next; delay its proposal list too, so it also
        // outlives what follows.
        var gateB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayedB = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.DelayNextProposalList = workspaceId =>
        {
            delayedB.SetResult(workspaceId);
            return gateB.Task;
        };

        var switchToB = viewModel.SelectWorkspaceAsync(workspaceB);
        await delayedB.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(switchToB.IsCompleted);

        // Finally select C, whose own load is not delayed and completes
        // immediately.
        await viewModel.SelectWorkspaceAsync(workspaceC);
        Assert.Single(viewModel.Proposals, item => item.ProposalId == proposalCId);

        // Complete in reverse order: A's load resolves first, then B's -
        // both stale relative to the current (C) generation.
        gateA.SetResult();
        await switchToA;
        Assert.Single(viewModel.Proposals, item => item.ProposalId == proposalCId);
        Assert.Equal(workspaceCId, viewModel.SelectedWorkspace!.WorkspaceId);

        gateB.SetResult();
        await switchToB;

        // Final presentation must contain only C's state.
        Assert.Equal(workspaceCId, viewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Single(viewModel.Proposals, item => item.ProposalId == proposalCId);
        Assert.DoesNotContain(viewModel.Proposals, item => item.ProposalId == proposalAId);
        Assert.DoesNotContain(viewModel.Proposals, item => item.ProposalId == proposalBId);
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

    [Fact]
    public async Task SelectProposalAsync_DelayedDetailForStaleWorkspace_DoesNotSurfaceUnderNewlySelectedWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<PatchProposalId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextProposalDetail = proposalId =>
        {
            started.SetResult(proposalId);
            return gate.Task;
        };

        var selectA = fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        Assert.Equal(fixture.ProposalA.Id, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await selectA;

        Assert.Equal(fixture.WorkspaceB.WorkspaceId, fixture.ViewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Null(fixture.ViewModel.SelectedProposalDetail);
        Assert.Empty(fixture.ViewModel.ProposalFiles);
        Assert.Contains("Select a proposal", fixture.ViewModel.UnifiedDiffPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Select a proposal", fixture.ViewModel.ValidationPlanText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("No proposal selected.", fixture.ViewModel.HashStatusText);
        Assert.Equal("No source context loaded.", fixture.ViewModel.SourceSummaryText);
        Assert.Equal("template-b", fixture.ViewModel.SelectedValidationTemplate?.Id);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task SelectProposalAsync_AtoBtoA_OldGenerationCannotOverwriteNewSelection()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        var oldDetail = fixture.ProposalA with { Title = "Old A detail" };
        var newDetail = fixture.ProposalA with { Title = "New A detail" };
        PatchProposal currentDetail = oldDetail;
        fixture.Service.ProposalDetailFactory = proposalId =>
            proposalId == fixture.ProposalA.Id ? currentDetail : fixture.ProposalB;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextProposalDetail = _ =>
        {
            started.SetResult();
            return gate.Task;
        };

        var oldSelection = fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceA);
        currentDetail = newDetail;
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        Assert.Equal("New A detail", fixture.ViewModel.SelectedProposalDetail?.Title);

        gate.SetResult();
        await oldSelection;

        Assert.Equal(fixture.WorkspaceA.WorkspaceId, fixture.ViewModel.SelectedWorkspace!.WorkspaceId);
        Assert.Equal("New A detail", fixture.ViewModel.SelectedProposalDetail?.Title);
        Assert.Equal("Proposal detail loaded.", fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task SearchContextFilesAsync_DelayedResultsForStaleWorkspace_DoesNotPopulateCandidatesUnderNewWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextContextSearch = request =>
        {
            started.SetResult(request.WorkspaceId);
            return gate.Task;
        };

        var searchA = fixture.ViewModel.SearchContextFilesAsync();
        Assert.Equal(fixture.WorkspaceA.WorkspaceId, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await searchA;

        Assert.Empty(fixture.ViewModel.ContextFileCandidates);
        Assert.Empty(fixture.ViewModel.SelectedContextFiles);
        Assert.Empty(fixture.ViewModel.TargetSnippetCandidates);
        Assert.Null(fixture.ViewModel.SelectedTargetSnippet);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task AddContextFileAsync_DelayedReadForStaleWorkspace_DoesNotPopulateSelectedContextUnderNewWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SearchContextFilesAsync();
        var candidateA = fixture.ViewModel.ContextFileCandidates.Single();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextFileRead = (workspaceId, _) =>
        {
            started.SetResult(workspaceId);
            return gate.Task;
        };

        var addA = fixture.ViewModel.AddContextFileAsync(candidateA);
        Assert.Equal(fixture.WorkspaceA.WorkspaceId, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await addA;

        Assert.Empty(fixture.ViewModel.SelectedContextFiles);
        Assert.Empty(fixture.ViewModel.ContextFileCandidates);
        Assert.Empty(fixture.ViewModel.TargetSnippetCandidates);
        Assert.Null(fixture.ViewModel.SelectedTargetSnippet);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task DryRunSelectedProposalAsync_DelayedResultForStaleWorkspace_DoesNotOverwriteNewWorkspaceState()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextDryRun = request =>
        {
            started.SetResult(request.WorkspaceId);
            return gate.Task;
        };

        var dryRunA = fixture.ViewModel.DryRunSelectedProposalAsync();
        Assert.Equal(fixture.WorkspaceA.WorkspaceId, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await dryRunA;

        Assert.Equal("Dry run not started.", fixture.ViewModel.DryRunStatusText);
        Assert.Equal("Apply approval not requested.", fixture.ViewModel.ApplyApprovalStatusText);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task ApplyApprovalCompletionForStaleWorkspace_DoesNotReplaceOrClearCurrentWorkspaceApprovalState()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        await fixture.ViewModel.DryRunSelectedProposalAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextApplyApprovalRequest = (_, workspaceId) =>
        {
            started.SetResult(workspaceId);
            return gate.Task;
        };

        var approvalA = fixture.ViewModel.RequestApplyApprovalAsync();
        Assert.Equal(fixture.WorkspaceA.WorkspaceId, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalB.Id));
        await fixture.ViewModel.DryRunSelectedProposalAsync();
        await fixture.ViewModel.RequestApplyApprovalAsync();
        await fixture.ViewModel.AllowApplyOnceAsync();
        Assert.True(fixture.ViewModel.ApplyApprovedProposalCommand.CanExecute(null));
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await approvalA;

        Assert.Equal("Apply approval granted once.", fixture.ViewModel.ApplyApprovalStatusText);
        Assert.True(fixture.ViewModel.ApplyApprovedProposalCommand.CanExecute(null));
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task ValidationCreationForStaleWorkspace_DoesNotSurfaceUnderNewWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<WorkspaceId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextValidationCreation = request =>
        {
            started.SetResult(request.WorkspaceId);
            return gate.Task;
        };

        var createA = fixture.ViewModel.CreateValidationRunAsync();
        Assert.Equal(fixture.WorkspaceA.WorkspaceId, await started.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;

        gate.SetResult();
        await createA;

        Assert.Empty(fixture.ViewModel.ValidationRuns);
        Assert.Equal("No validation run selected.", fixture.ViewModel.ValidationResultText);
        Assert.Equal("Validation approval not requested.", fixture.ViewModel.ValidationApprovalStatusText);
        Assert.DoesNotContain("A validation", fixture.ViewModel.ValidationOutputPreview, StringComparison.Ordinal);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task ValidationApprovalForStaleWorkspace_DoesNotRemainExecutableUnderNewWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        await fixture.ViewModel.CreateValidationRunAsync();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource<ValidationRunId>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Service.DelayNextValidationApprovalRequest = runId =>
        {
            started.SetResult(runId);
            return gate.Task;
        };

        var approvalA = fixture.ViewModel.RequestValidationApprovalAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);

        gate.SetResult();
        await approvalA;

        Assert.Equal("No validation run selected.", fixture.ViewModel.ValidationResultText);
        Assert.Equal("Validation approval not requested.", fixture.ViewModel.ValidationApprovalStatusText);
        Assert.False(fixture.ViewModel.RunApprovedValidationCommand.CanExecute(null));
    }

    [Fact]
    public async Task RunApprovedValidationAsync_CannotExecuteStaleValidationRunUnderNewlySelectedWorkspace()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        await fixture.ViewModel.CreateValidationRunAsync();
        await fixture.ViewModel.RequestValidationApprovalAsync();
        await fixture.ViewModel.AllowValidationOnceAsync();
        Assert.True(fixture.ViewModel.RunApprovedValidationCommand.CanExecute(null));

        await fixture.ViewModel.SelectWorkspaceAsync(fixture.WorkspaceB);
        var workspaceBStatus = fixture.ViewModel.SafeStatusMessage;
        Assert.False(fixture.ViewModel.RunApprovedValidationCommand.CanExecute(null));
        await fixture.ViewModel.RunApprovedValidationAsync();

        Assert.Equal(0, fixture.Service.ValidationExecutionCount);
        Assert.False(fixture.ViewModel.RunApprovedValidationCommand.CanExecute(null));
        Assert.DoesNotContain("A validation output", fixture.ViewModel.ValidationOutputPreview, StringComparison.Ordinal);
        Assert.Equal(workspaceBStatus, fixture.ViewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task CurrentWorkspaceOperationsStillCompleteNormally()
    {
        var fixture = await CreateCorrelationFixtureAsync();
        await fixture.ViewModel.SelectProposalAsync(
            fixture.ViewModel.Proposals.Single(item => item.ProposalId == fixture.ProposalA.Id));
        await fixture.ViewModel.SearchContextFilesAsync();
        await fixture.ViewModel.AddContextFileAsync(fixture.ViewModel.ContextFileCandidates.Single());
        await fixture.ViewModel.DryRunSelectedProposalAsync();
        await fixture.ViewModel.RequestApplyApprovalAsync();
        await fixture.ViewModel.AllowApplyOnceAsync();
        await fixture.ViewModel.CreateValidationRunAsync();
        await fixture.ViewModel.RequestValidationApprovalAsync();
        await fixture.ViewModel.AllowValidationOnceAsync();
        Assert.True(fixture.ViewModel.RunApprovedValidationCommand.CanExecute(null));
        await fixture.ViewModel.RunApprovedValidationAsync();

        Assert.Equal(fixture.ProposalA.Id, fixture.ViewModel.SelectedProposalDetail?.Id);
        Assert.Single(fixture.ViewModel.SelectedContextFiles);
        Assert.Contains(nameof(PatchApplyStatus.DryRunPassed), fixture.ViewModel.DryRunStatusText, StringComparison.Ordinal);
        Assert.Contains(nameof(ValidationRunStatus.Succeeded), fixture.ViewModel.ValidationResultText, StringComparison.Ordinal);
        Assert.Contains("A validation output", fixture.ViewModel.ValidationOutputPreview, StringComparison.Ordinal);
        Assert.Equal(1, fixture.Service.ValidationExecutionCount);
        Assert.Equal("Validation run completed.", fixture.ViewModel.SafeStatusMessage);
    }

    private async Task<CorrelationFixture> CreateCorrelationFixtureAsync()
    {
        var workspaceBRoot = Path.Combine(_root, "workspace-correlation-b");
        Directory.CreateDirectory(workspaceBRoot);
        var workspaceBId = WorkspaceId.NewId();
        var registry = new WorkspaceRegistry();
        registry.Register(_workspaceId, _root, "Repo A");
        registry.Register(workspaceBId, workspaceBRoot, "Repo B");

        var proposalA = await SaveProposalAsync([Edit("src/A.cs", "old A\n", "new A\n")]);
        var proposalB = await SaveProposalAsync(
            [Edit("src/B.cs", "old B\n", "new B\n")],
            workspaceBId);
        var sharedApprovals = new InMemoryApprovalCheckpointStore();
        var (proposalRepository, applyRepository) = OpenRepositories();
        var reader = CreateReader();
        var validator = new PatchApplyValidator(proposalRepository, reader, CreateResolver());
        var patchApplyService = new PatchApplyService(
            proposalRepository,
            applyRepository,
            validator,
            reader,
            sharedApprovals);
        var service = new PersistedApplyModuleService(patchApplyService, proposalRepository)
        {
            ApprovalStore = sharedApprovals,
            DryRunPlanFactory = request => new PatchApplyPlan(
                request.ProposalId,
                request.WorkspaceId,
                PatchApplyStatus.DryRunPassed,
                [],
                [],
                RequiresApproval: true),
            ContextCandidates =
            [
                CreateContextCandidate(_workspaceId, "src/A.cs"),
                CreateContextCandidate(workspaceBId, "src/B.cs")
            ],
            TargetSnippetCandidates =
            [
                new AedaCodeTargetSnippetCandidate(
                    "snippet-a",
                    "src/A.cs",
                    "A",
                    "class A",
                    1,
                    1,
                    7,
                    false,
                    "class A")
            ]
        };
        var risk = new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "small_text_change");
        service.ProposalsByWorkspace[_workspaceId] =
            [new AedaCodeProposalSummary(proposalA.Id, proposalA.Title, proposalA.Status, risk, ["src/A.cs"], proposalA.UpdatedAtUtc)];
        service.ProposalsByWorkspace[workspaceBId] =
            [new AedaCodeProposalSummary(proposalB.Id, proposalB.Title, proposalB.Status, risk, ["src/B.cs"], proposalB.UpdatedAtUtc)];
        service.TemplatesByWorkspace[_workspaceId] =
            [new ValidationCommandTemplate("template-a", "Template A", "dotnet", ["test"], TimeSpan.FromMinutes(1), "src/A.cs")];
        service.TemplatesByWorkspace[workspaceBId] =
            [new ValidationCommandTemplate("template-b", "Template B", "dotnet", ["test"], TimeSpan.FromMinutes(1), "src/B.cs")];

        var viewModel = BuildViewModel(registry, service, sharedApprovals);
        await viewModel.InitializeAsync();
        var workspaceA = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == _workspaceId);
        var workspaceB = viewModel.Workspaces.Single(workspace => workspace.WorkspaceId == workspaceBId);
        await viewModel.SelectWorkspaceAsync(workspaceA);
        return new CorrelationFixture(viewModel, service, workspaceA, workspaceB, proposalA, proposalB);
    }

    private static AedaCodeContextFileCandidate CreateContextCandidate(WorkspaceId workspaceId, string relativePath) =>
        new(
            workspaceId,
            relativePath,
            Path.GetFileName(relativePath),
            Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? ".",
            Path.GetExtension(relativePath),
            "C#",
            "13 B",
            13,
            true,
            false,
            null);

    private sealed record CorrelationFixture(
        AedaCodeModuleViewModel ViewModel,
        PersistedApplyModuleService Service,
        AedaCodeWorkspaceItem WorkspaceA,
        AedaCodeWorkspaceItem WorkspaceB,
        PatchProposal ProposalA,
        PatchProposal ProposalB);

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
        IAedaCodeModuleService service,
        IApprovalCheckpointStore? approvalStore = null)
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
        approvalStore ??= new InMemoryApprovalCheckpointStore();
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
        private readonly Dictionary<ValidationRunId, ValidationRun> _validationRuns = [];

        public IApprovalCheckpointStore? ApprovalStore { private get; set; }

        public Func<PatchProposalId, PatchProposal?>? ProposalDetailFactory { get; set; }

        public Func<PatchProposalId, Task>? DelayNextProposalDetail;

        public IReadOnlyList<AedaCodeContextFileCandidate> ContextCandidates { get; set; } = [];

        public Func<AedaCodeContextSearchRequest, Task>? DelayNextContextSearch;

        public Func<WorkspaceId, IReadOnlyList<string>, Task>? DelayNextFileRead;

        public IReadOnlyList<AedaCodeTargetSnippetCandidate> TargetSnippetCandidates { get; set; } = [];

        public Func<PatchApplyRequest, PatchApplyPlan>? DryRunPlanFactory { get; set; }

        public Func<PatchApplyRequest, Task>? DelayNextDryRun;

        public Func<PatchProposalId, WorkspaceId, Task>? DelayNextApplyApprovalRequest;

        public Func<ValidationRunRequest, Task>? DelayNextValidationCreation;

        public Func<ValidationRunId, Task>? DelayNextValidationApprovalRequest;

        public Func<ValidationRunId, Task>? DelayNextValidationExecution;

        public int ValidationExecutionCount { get; private set; }

        /// <summary>
        /// When set, the next <see cref="StartSessionAsync"/> call for the
        /// given workspace awaits this before returning, then clears itself.
        /// Lets tests simulate a slow session start.
        /// </summary>
        public Func<WorkspaceId, Task>? DelayNextStartSession;

        public async Task<AedaCodeSession> StartSessionAsync(
            WorkspaceId workspaceId,
            string? safeSummary = null,
            CancellationToken cancellationToken = default)
        {
            if (DelayNextStartSession is { } delay)
            {
                DelayNextStartSession = null;
                await delay(workspaceId);
            }

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
            return session;
        }

        public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(
            int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeSession>>([]);

        public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(
            WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AedaCodeWorkspaceSummary(workspaceId, "Repo", true));

        public async Task<CodeContextPack> ReadFilesAsync(
            WorkspaceId workspaceId, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default)
        {
            var files = relativePaths.Select(path => new CodeContextFile(
                workspaceId,
                path,
                "class App { }",
                "context-hash",
                "utf-8",
                13,
                false,
                false)).ToArray();
            if (DelayNextFileRead is { } delay)
            {
                DelayNextFileRead = null;
                await delay(workspaceId, relativePaths);
            }

            return new CodeContextPack(workspaceId, files, [], [], false);
        }

        public Task<CodeContextPack> SearchAsync(
            CodeContextSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<AedaCodeContextSearchResult> SearchContextFilesAsync(
            AedaCodeContextSearchRequest request, CancellationToken cancellationToken = default)
        {
            AedaCodeContextFileCandidate[] candidates = ContextCandidates.Count == 0
                ? [new AedaCodeContextFileCandidate(
                    request.WorkspaceId,
                    "src/App.cs",
                    "App.cs",
                    "src",
                    ".cs",
                    "C#",
                    "13 B",
                    13,
                    true,
                    false,
                    null)]
                : ContextCandidates.Where(candidate => candidate.WorkspaceId == request.WorkspaceId).ToArray();
            if (DelayNextContextSearch is { } delay)
            {
                DelayNextContextSearch = null;
                await delay(request);
            }

            return new AedaCodeContextSearchResult(request.WorkspaceId, candidates, false, []);
        }

        public Task<IReadOnlyList<AedaCodeTargetSnippetCandidate>> ListTargetSnippetCandidatesAsync(
            AedaCodeTargetSnippetRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeTargetSnippetCandidate>>(
                TargetSnippetCandidates
                    .Where(candidate => request.SelectedRelativePaths.Contains(
                        candidate.RelativePath,
                        StringComparer.OrdinalIgnoreCase))
                    .ToArray());

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

        /// <summary>
        /// Per-workspace proposal/template fixtures a test can populate so
        /// <see cref="ListProposalSummariesAsync"/>/<see cref="ListValidationTemplatesAsync"/>
        /// return distinguishable, workspace-specific data instead of always
        /// empty lists.
        /// </summary>
        public Dictionary<WorkspaceId, IReadOnlyList<AedaCodeProposalSummary>> ProposalsByWorkspace { get; } = [];

        public Dictionary<WorkspaceId, IReadOnlyList<ValidationCommandTemplate>> TemplatesByWorkspace { get; } = [];

        /// <summary>
        /// When set, the next <see cref="ListProposalSummariesAsync"/> call
        /// for the given workspace awaits this before returning, then clears
        /// itself.
        /// </summary>
        public Func<WorkspaceId, Task>? DelayNextProposalList;

        /// <summary>
        /// When set, the next <see cref="ListValidationTemplatesAsync"/> call
        /// for the given workspace awaits this before returning, then clears
        /// itself.
        /// </summary>
        public Func<WorkspaceId, Task>? DelayNextTemplateList;

        public async Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(
            WorkspaceId workspaceId, int limit = 50, CancellationToken cancellationToken = default)
        {
            if (DelayNextProposalList is { } delay)
            {
                DelayNextProposalList = null;
                await delay(workspaceId);
            }

            return ProposalsByWorkspace.TryGetValue(workspaceId, out var proposals) ? proposals : [];
        }

        public async Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(
            WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            if (DelayNextTemplateList is { } delay)
            {
                DelayNextTemplateList = null;
                await delay(workspaceId);
            }

            return TemplatesByWorkspace.TryGetValue(workspaceId, out var templates) ? templates : [];
        }

        public async Task<PatchApplyPlan> DryRunApplyAsync(
            PatchApplyRequest request, CancellationToken cancellationToken = default)
        {
            var plan = DryRunPlanFactory is null
                ? await applyService.DryRunAsync(request, cancellationToken)
                : DryRunPlanFactory(request);
            if (DelayNextDryRun is { } delay)
            {
                DelayNextDryRun = null;
                await delay(request);
            }

            return plan;
        }

        public async Task<ApprovalRequest> RequestApplyApprovalAsync(
            PatchProposalId proposalId, WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            var request = await applyService.RequestApplyApprovalAsync(proposalId, workspaceId, cancellationToken);
            if (DelayNextApplyApprovalRequest is { } delay)
            {
                DelayNextApplyApprovalRequest = null;
                await delay(proposalId, workspaceId);
            }

            return request;
        }

        /// <summary>
        /// When set, the next <see cref="ApplyApprovedProposalAsync"/> call
        /// awaits this AFTER the real apply has already completed (the
        /// backend result already exists/is persisted) but BEFORE returning
        /// it to the caller, then clears itself. Lets tests simulate a slow
        /// apply completion without affecting persisted state.
        /// </summary>
        public Func<PatchApplyRequest, Task>? DelayNextApplyCompletion;

        public async Task<PatchApplyResult> ApplyApprovedProposalAsync(
            PatchApplyRequest request, CancellationToken cancellationToken = default)
        {
            var result = await applyService.ApplyAsync(request, cancellationToken);
            if (DelayNextApplyCompletion is { } delay)
            {
                DelayNextApplyCompletion = null;
                await delay(request);
            }

            return result;
        }

        public async Task<PatchProposal?> GetProposalAsync(
            PatchProposalId proposalId, CancellationToken cancellationToken = default)
        {
            var proposal = ProposalDetailFactory is null
                ? await proposalRepository.GetAsync(proposalId, cancellationToken)
                : ProposalDetailFactory(proposalId);
            if (DelayNextProposalDetail is { } delay)
            {
                DelayNextProposalDetail = null;
                await delay(proposalId);
            }

            return proposal;
        }

        public Task<PatchApplyResult?> GetApplyResultAsync(
            PatchApplyResultId applyResultId, CancellationToken cancellationToken = default) =>
            applyService.GetApplyResultAsync(applyResultId, cancellationToken);

        public Task<PatchRollbackResult> RollbackAsync(
            PatchRollbackRequest request, CancellationToken cancellationToken = default) =>
            applyService.RollbackAsync(request, cancellationToken);

        public async Task<ValidationRun> CreateValidationRunAsync(
            ValidationRunRequest request, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var run = new ValidationRun(
                ValidationRunId.NewId(),
                request.WorkspaceId,
                request.TemplateId,
                ".",
                ValidationRunStatus.Created,
                request.ProposalId,
                request.ApplyResultId,
                null,
                [],
                now,
                now);
            _validationRuns[run.Id] = run;
            if (DelayNextValidationCreation is { } delay)
            {
                DelayNextValidationCreation = null;
                await delay(request);
            }

            return run;
        }

        public async Task<ApprovalRequest> RequestValidationApprovalAsync(
            ValidationRunId runId, CancellationToken cancellationToken = default)
        {
            var run = _validationRuns[runId];
            var request = ApprovalRequest.Create(
                new ApprovalScope(
                    new TaskId(runId.Value),
                    ApprovalKind.ValidationRun,
                    $"validation-run:{run.WorkspaceId}:{runId}"),
                "Approve validation",
                "Approve validation.");
            request = await (ApprovalStore ?? throw new InvalidOperationException("approval_store_missing"))
                .RequestAsync(request, cancellationToken);
            if (DelayNextValidationApprovalRequest is { } delay)
            {
                DelayNextValidationApprovalRequest = null;
                await delay(runId);
            }

            return request;
        }

        public async Task<ValidationRun> RunApprovedValidationAsync(
            ValidationRunId runId,
            ApprovalRequest approvalRequest,
            ApprovalDecision approvalDecision,
            CancellationToken cancellationToken = default)
        {
            ValidationExecutionCount++;
            var run = _validationRuns[runId] with
            {
                Status = ValidationRunStatus.Succeeded,
                CommandResult = new ValidationCommandResult(
                    0,
                    ValidationRunStatus.Succeeded,
                    new ValidationOutputChunk("A validation output", false),
                    new ValidationOutputChunk(string.Empty, false),
                    TimeSpan.FromMilliseconds(10)),
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _validationRuns[runId] = run;
            if (DelayNextValidationExecution is { } delay)
            {
                DelayNextValidationExecution = null;
                await delay(runId);
            }

            return run;
        }

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
