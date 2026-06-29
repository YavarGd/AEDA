using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.ViewModels;
using PersonalAI.Infrastructure.Modules;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Coding;

public sealed class AedaCodeWorkflowViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.AedaCodeWorkflow.Tests",
        Guid.NewGuid().ToString("N"));

    public AedaCodeWorkflowViewModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public async Task InitializeAsync_LoadsSafelyWithNoWorkspace()
    {
        var viewModel = CreateViewModel(new WorkspaceRegistry(), new FakeAedaCodeModuleService());

        await viewModel.InitializeAsync();

        Assert.True(viewModel.HasNoWorkspaces);
        Assert.False(viewModel.StartSessionCommand.CanExecute(null));
        Assert.Contains("Register a workspace", viewModel.SafeStatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SelectWorkspace_LoadsBoundedProposalSummariesAndTemplates()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = Enumerable.Range(0, 12)
                .Select(index => CreateProposalSummary($"Proposal {index}"))
                .ToArray(),
            Templates =
            [
                new ValidationCommandTemplate(
                    "dotnet-build-debug",
                    "Build solution",
                    "dotnet",
                    ["build", "PersonalAI.slnx"],
                    TimeSpan.FromMinutes(3),
                    "PersonalAI.slnx")
            ]
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();

        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());

        Assert.Equal(6, viewModel.Proposals.Count);
        Assert.Single(viewModel.ValidationTemplates);
        Assert.DoesNotContain(_root, viewModel.WorkspaceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SelectProposal_LoadsBoundedSafeDetailAndDiff()
    {
        var registry = CreateRegistry();
        var proposal = CreateProposal(
            @"C:\secret\Program.cs",
            "+ password: hunter2",
            Enumerable.Range(0, 30).Select(index => $"src/File{index}.cs").ToArray());
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());

        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());

        Assert.Equal(20, viewModel.ProposalFiles.Count);
        Assert.Contains("Status:", viewModel.SelectedProposalMetadataText, StringComparison.Ordinal);
        Assert.Contains("Safe summary", viewModel.SelectedProposalSummaryText, StringComparison.Ordinal);
        Assert.Contains("Created", viewModel.SelectedProposalTimestampText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\secret", viewModel.UnifiedDiffPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", viewModel.UnifiedDiffPreview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", viewModel.UnifiedDiffPreview, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.DryRunSelectedProposalCommand.CanExecute(null));
        Assert.False(viewModel.ApplyApprovedProposalCommand.CanExecute(null));
    }

    [Fact]
    public async Task DryRunApprovalAndApply_DelegateToExistingServiceAndRespectGates()
    {
        var registry = CreateRegistry();
        var proposal = CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal,
            DryRunPlan = new PatchApplyPlan(
                proposal.Id,
                registry.List().Single().Id,
                PatchApplyStatus.DryRunPassed,
                [new PatchApplyOperation("src/App.cs", PatchProposalFileChangeKind.Modify, "old", "new")],
                [],
                RequiresApproval: true)
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());

        await viewModel.DryRunSelectedProposalAsync();
        await viewModel.RequestApplyApprovalAsync();

        Assert.Equal(1, service.DryRunCount);
        Assert.Equal(1, service.ApplyApprovalRequestCount);
        Assert.Contains("1. Dry run", viewModel.ReviewGateOrderText, StringComparison.Ordinal);
        Assert.Contains("Dry run passed", viewModel.DryRunDetailText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.ApplyApprovedProposalCommand.CanExecute(null));

        await viewModel.DenyApplyApprovalAsync();
        Assert.Contains("denied by user", viewModel.ApplyApprovalStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.ApplyApprovedProposalCommand.CanExecute(null));

        await viewModel.RequestApplyApprovalAsync();
        await viewModel.AllowApplyOnceAsync();
        await viewModel.ApplyApprovedProposalAsync();

        Assert.Equal(1, service.ApplyCount);
        Assert.Equal(PatchApplyStatus.Applied, viewModel.ApplySummaries.Single().Status);
        Assert.Contains("Backup checkpoint", viewModel.ApplyResultDetailText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Rollback available", viewModel.RollbackAvailabilityText, StringComparison.OrdinalIgnoreCase);
        Assert.True(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task ApplyApproval_DoesNotEnableApplyAfterLatestDryRunFails()
    {
        var registry = CreateRegistry();
        var proposal = CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
        var workspace = registry.List().Single();
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal,
            DryRunPlan = new PatchApplyPlan(
                proposal.Id,
                workspace.Id,
                PatchApplyStatus.DryRunPassed,
                [new PatchApplyOperation("src/App.cs", PatchProposalFileChangeKind.Modify, "old", "new")],
                [],
                RequiresApproval: true)
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());

        await viewModel.DryRunSelectedProposalAsync();
        await viewModel.RequestApplyApprovalAsync();
        await viewModel.AllowApplyOnceAsync();
        Assert.True(viewModel.ApplyApprovedProposalCommand.CanExecute(null));

        service.DryRunPlan = new PatchApplyPlan(
            proposal.Id,
            workspace.Id,
            PatchApplyStatus.DryRunFailed,
            [],
            [PatchApplyFailureReason.StaleOriginalContent],
            RequiresApproval: true);
        await viewModel.DryRunSelectedProposalAsync();

        Assert.False(viewModel.ApplyApprovedProposalCommand.CanExecute(null));
        Assert.False(viewModel.RequestApplyApprovalCommand.CanExecute(null));
        Assert.False(viewModel.AllowApplyOnceCommand.CanExecute(null));
        Assert.True(viewModel.IsDryRunStale);
        Assert.Contains("proposal is stale", viewModel.DryRunDetailText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current file state", viewModel.DryRunDetailText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StaleOriginalContent", viewModel.DryRunDetailText, StringComparison.Ordinal);
        Assert.Contains("Re-enter the request", viewModel.StaleProposalRecoveryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not requested", viewModel.ApplyApprovalStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("StaleOriginalContent", viewModel.DryRunDetailText, StringComparison.Ordinal);
        Assert.Equal(0, service.ApplyCount);
        Assert.Equal(0, service.ValidationRunCount);
        Assert.Equal(0, service.RollbackCount);
        Assert.Equal(0, service.CreateProposalFromRequestCount);
    }

    [Fact]
    public void StaleDryRunTimelineLabel_IsReadableAndPreservesSafeCodeDetail()
    {
        var item = new AedaTaskTimelineItem(
            Guid.NewGuid().ToString("N"),
            TaskId.NewId(),
            DateTimeOffset.UtcNow,
            "Patch dry run failed",
            "Patch dry run failed: StaleOriginalContent.",
            null,
            new AedaTaskStatusBadge(
                AedaTaskCenterStatus.Failed,
                "Failed",
                "task_failed",
                NeedsAttention: true,
                IsTerminal: true),
            new AedaTaskModuleBadge(AedaTaskCenterModule.Code, "Code", AedaModuleId.Code, "aeda-code"),
            []);

        var mapped = AedaCodeTimelineEventItem.From(item);

        Assert.Equal("Dry run blocked: proposal is stale", mapped.Label);
        Assert.Contains("StaleOriginalContent", mapped.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("raw payload", mapped.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rollback_IsHiddenForAppliedNoOpResult()
    {
        var registry = CreateRegistry();
        var proposal = CreateProposal("src/App.cs", " same", ["src/App.cs"]);
        var workspace = registry.List().Single();
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("No op safely", proposal.Id)],
            ProposalDetail = proposal,
            DryRunPlan = new PatchApplyPlan(
                proposal.Id,
                workspace.Id,
                PatchApplyStatus.DryRunPassed,
                [new PatchApplyOperation("src/App.cs", PatchProposalFileChangeKind.NoOp, "same", "same")],
                [],
                RequiresApproval: true),
            ApplyResultFactory = request => new PatchApplyResult(
                PatchApplyResultId.NewId(),
                request.ProposalId,
                request.WorkspaceId,
                PatchApplyStatus.Applied,
                [new PatchApplyFileResult("src/App.cs", PatchProposalFileChangeKind.NoOp, PatchApplyStatus.Applied)],
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow)
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());

        await viewModel.DryRunSelectedProposalAsync();
        await viewModel.RequestApplyApprovalAsync();
        await viewModel.AllowApplyOnceAsync();
        await viewModel.ApplyApprovedProposalAsync();

        Assert.False(viewModel.HasRollbackAvailable);
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
        Assert.Contains("Rollback unavailable", viewModel.ApplyResultDetailText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidationFlow_ExposesOnlyTemplatesAndSanitizesOutput()
    {
        var registry = CreateRegistry();
        var proposal = CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal,
            Templates =
            [
                new ValidationCommandTemplate(
                    "dotnet-test-personalai",
                    "Run tests",
                    "dotnet",
                    ["test", "PersonalAI.Tests\\PersonalAI.Tests.csproj"],
                    TimeSpan.FromMinutes(5),
                    "PersonalAI.Tests\\PersonalAI.Tests.csproj")
            ]
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());

        await viewModel.CreateValidationRunAsync();
        await viewModel.RequestValidationApprovalAsync();
        await viewModel.AllowValidationOnceAsync();
        await viewModel.RunApprovedValidationAsync();

        Assert.Single(viewModel.ValidationTemplates);
        Assert.DoesNotContain("cmd.exe", viewModel.ValidationTemplates.Single().SafeCommandSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allowlisted", viewModel.ValidationTemplateStatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, service.ValidationApprovalRequestCount);
        Assert.Equal(1, service.ValidationRunCount);
        Assert.Contains("Exit code: 0", viewModel.ValidationRunDetailText, StringComparison.Ordinal);
        Assert.Contains("Duration:", viewModel.ValidationRunDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\secret", viewModel.ValidationOutputPreview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", viewModel.ValidationOutputPreview, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RollbackAndTimeline_DelegateAndShowSafeTaskEvents()
    {
        var registry = CreateRegistry();
        var taskCenter = new FakeTaskCenterService();
        var proposal = CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal,
            DryRunPlan = new PatchApplyPlan(
                proposal.Id,
                registry.List().Single().Id,
                PatchApplyStatus.DryRunPassed,
                [],
                [],
                RequiresApproval: true)
        };
        var viewModel = CreateViewModel(registry, service, taskCenter);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());
        await viewModel.DryRunSelectedProposalAsync();
        await viewModel.RequestApplyApprovalAsync();
        await viewModel.AllowApplyOnceAsync();
        await viewModel.ApplyApprovedProposalAsync();

        await viewModel.RollbackSelectedApplyResultAsync();
        Assert.Equal("Rollback completed.", viewModel.SafeStatusMessage);
        await viewModel.SelectTaskAsync(viewModel.RecentCodeTasks.Single());

        Assert.Equal(1, service.RollbackCount);
        Assert.Single(viewModel.SelectedTaskTimeline);
        Assert.Single(viewModel.CodeTimelineGroups);
        Assert.Contains("Created proposal", viewModel.CodeTimelineGroups.Single().Items.Single().Label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw payload", viewModel.CodeTimelineGroups.Single().Items.Single().Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SupervisedActions_RefreshTaskTimeline()
    {
        var registry = CreateRegistry();
        var taskCenter = new FakeTaskCenterService();
        var proposal = CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
        var service = new FakeAedaCodeModuleService
        {
            ProposalSummaries = [CreateProposalSummary("Fix safely", proposal.Id)],
            ProposalDetail = proposal,
            DryRunPlan = new PatchApplyPlan(
                proposal.Id,
                registry.List().Single().Id,
                PatchApplyStatus.DryRunPassed,
                [],
                [],
                RequiresApproval: true)
        };
        var viewModel = CreateViewModel(registry, service, taskCenter);
        await viewModel.InitializeAsync();
        await viewModel.SelectWorkspaceAsync(viewModel.Workspaces.Single());
        await viewModel.SelectProposalAsync(viewModel.Proposals.Single());
        var initialTimelineRequests = taskCenter.TimelineRequestCount;

        await viewModel.DryRunSelectedProposalAsync();
        await viewModel.RequestApplyApprovalAsync();
        await viewModel.AllowApplyOnceAsync();
        await viewModel.ApplyApprovedProposalAsync();

        Assert.True(taskCenter.TimelineRequestCount >= initialTimelineRequests + 4);
        Assert.Single(viewModel.CodeTimelineGroups);
        Assert.Equal(1, service.ApplyCount);
    }

    [Fact]
    public void SafetyCommands_DoNotExposeGitShellOrAutomaticActions()
    {
        var viewModel = CreateViewModel(new WorkspaceRegistry(), new FakeAedaCodeModuleService());

        Assert.False(viewModel.CreateProposalCommand.CanExecute(null));
        Assert.False(viewModel.ApplyApprovedProposalCommand.CanExecute(null));
        Assert.False(viewModel.RunApprovedValidationCommand.CanExecute(null));
        Assert.False(viewModel.RollbackSelectedApplyResultCommand.CanExecute(null));
    }

    [Fact]
    public async Task CreateProposal_DisabledWithoutWorkspaceEmptyRequestAndOverLimit()
    {
        var viewModel = CreateViewModel(new WorkspaceRegistry(), new FakeAedaCodeModuleService());
        await viewModel.InitializeAsync();

        viewModel.ProposalRequest = "Add XML docs.";
        Assert.False(viewModel.CreateProposalCommand.CanExecute(null));

        var registry = CreateRegistry();
        viewModel = CreateViewModel(registry, new FakeAedaCodeModuleService());
        await viewModel.InitializeAsync();
        Assert.False(viewModel.CreateProposalCommand.CanExecute(null));

        viewModel.ProposalRequest = new string('a', AedaCodeModuleViewModel.MaxProposalRequestCharacters + 1);
        Assert.False(viewModel.CreateProposalCommand.CanExecute(null));
    }

    [Fact]
    public async Task CreateProposal_DelegatesShowsDetailAndDoesNotRunApplyOrValidation()
    {
        var registry = CreateRegistry();
        var workspace = registry.List().Single();
        var proposal = CreateProposal("src/App.cs", "+ docs", ["src/App.cs"]) with
        {
            WorkspaceId = workspace.Id
        };
        var service = new FakeAedaCodeModuleService
        {
            CreatedProposal = proposal,
            ProposalDetail = proposal
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Add XML docs to this helper method.";
        viewModel.ProposalTitle = "Docs";

        await viewModel.CreateProposalAsync();

        Assert.Equal(1, service.CreateProposalFromRequestCount);
        Assert.Single(viewModel.Proposals);
        Assert.Equal(proposal.Id, viewModel.SelectedProposal?.ProposalId);
        Assert.Contains("ready for review", viewModel.ProposalCreationStateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("+ docs", viewModel.UnifiedDiffPreview, StringComparison.Ordinal);
        Assert.Contains("No files changed", viewModel.SafeStatusMessage, StringComparison.Ordinal);
        Assert.Equal(0, service.ApplyCount);
        Assert.Equal(0, service.ValidationRunCount);
        Assert.Equal(0, service.DryRunCount);
    }

    [Fact]
    public async Task CreateProposal_RunningStateDoesNotBlockAndCancelClearsState()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            DelayUntilCancelled = true
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Add XML docs.";

        var running = viewModel.CreateProposalAsync();
        await service.Started.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(running.IsCompleted);
        Assert.True(viewModel.IsCreatingProposal);
        Assert.False(viewModel.CreateProposalCommand.CanExecute(null));
        Assert.True(viewModel.CancelProposalCreationCommand.CanExecute(null));

        viewModel.CancelProposalCreation();
        await running.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(viewModel.IsCreatingProposal);
        Assert.Equal("model_cancelled", viewModel.ProposalCreationFailure?.SafeCode);
        Assert.Equal(AedaCodeProposalCreationPhase.Cancelled, viewModel.ProposalCreationPhase);
        Assert.True(viewModel.CreateProposalCommand.CanExecute(null));
        Assert.Empty(viewModel.Proposals);
    }

    [Fact]
    public async Task CreateProposal_ProgressAndSchemaIssueAreExposedSafely()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            CreateProposalFailure = new AedaCodeProposalCreationException(
                AedaCodeProposalCreationFailure.FromReason(
                    AedaCodeProposalCreationFailureReason.InvalidModelSchema,
                    "missing_title",
                    retryAttempted: true)),
            ProgressToReport =
            [
                new AedaCodeProposalCreationProgress(
                    AedaCodeProposalCreationPhase.LoadingBoundedContext,
                    SafeContextFileCount: 3),
                new AedaCodeProposalCreationProgress(
                    AedaCodeProposalCreationPhase.RetryingStructuredDraft,
                    SafeContextFileCount: 3,
                    RetryAttempted: true,
                    SchemaIssueCode: "missing_title")
            ]
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Add XML docs.";

        await viewModel.CreateProposalAsync();

        Assert.Equal("invalid_model_schema", viewModel.ProposalCreationFailure?.SafeCode);
        Assert.Equal("missing_title", viewModel.ProposalCreationSchemaIssueCode);
        Assert.True(viewModel.ProposalCreationRetryAttempted);
        Assert.Contains("Schema issue: missing_title", viewModel.ProposalCreationProgressText, StringComparison.Ordinal);
        Assert.Contains("structured retry: yes", viewModel.ProposalCreationFailureDetailText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("{\"title\"", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateProposal_InvalidModelOutputHandledSafely()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            CreateProposalFailure = new InvalidOperationException("model_patch_invalid")
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Make a safe change.";

        await viewModel.CreateProposalAsync();

        Assert.Empty(viewModel.Proposals);
        Assert.Equal("invalid_model_json", viewModel.ProposalCreationFailure?.SafeCode);
        Assert.Contains("Retry is available", viewModel.ProposalCreationStateText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("valid proposal JSON", viewModel.SafeStatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Try a smaller", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invalid_model_json", viewModel.ProposalCreationFailureDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("not json", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, service.ApplyCount);
        Assert.Equal(0, service.ValidationRunCount);
    }

    [Fact]
    public async Task CreateProposal_UnsafeFileTargetShowsSafeActionableMessage()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            CreateProposalFailure = new AedaCodeProposalCreationException(
                AedaCodeProposalCreationFailure.FromReason(
                    AedaCodeProposalCreationFailureReason.UnsafeFileTarget))
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Add XML docs to AedaCodeModuleViewModel.cs.";

        await viewModel.CreateProposalAsync();

        Assert.Empty(viewModel.Proposals);
        Assert.Equal("unsafe_file_target", viewModel.ProposalCreationFailure?.SafeCode);
        Assert.Contains("not uniquely available", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exact relative path", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unsafe_file_target", viewModel.ProposalCreationFailureDetailText, StringComparison.Ordinal);
        Assert.DoesNotContain("AedaCodeModuleViewModel.cs", viewModel.ProposalCreationFailureText, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, service.ApplyCount);
        Assert.Equal(0, service.ValidationRunCount);
    }

    [Fact]
    public async Task CreateProposal_RetryAvailableAfterFailure()
    {
        var registry = CreateRegistry();
        var service = new FakeAedaCodeModuleService
        {
            CreateProposalFailure = new AedaCodeProposalCreationException(
                AedaCodeProposalCreationFailure.FromReason(
                    AedaCodeProposalCreationFailureReason.NoSafeContext))
        };
        var viewModel = CreateViewModel(registry, service);
        await viewModel.InitializeAsync();
        viewModel.ProposalRequest = "Make a safe change.";

        await viewModel.CreateProposalAsync();

        Assert.Equal("no_safe_context", viewModel.ProposalCreationFailure?.SafeCode);
        Assert.True(viewModel.CreateProposalCommand.CanExecute(null));
        Assert.False(viewModel.CancelProposalCreationCommand.CanExecute(null));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private WorkspaceRegistry CreateRegistry()
    {
        var registry = new WorkspaceRegistry();
        registry.Register(_root, "Repo");
        return registry;
    }

    private static AedaCodeModuleViewModel CreateViewModel(
        WorkspaceRegistry workspaceRegistry,
        FakeAedaCodeModuleService service,
        FakeTaskCenterService? taskCenterService = null)
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
        return new AedaCodeModuleViewModel(
            service,
            moduleRegistry,
            workspaceRegistry,
            taskCenterService ?? new FakeTaskCenterService(),
            new InMemoryApprovalCheckpointStore());
    }

    private static AedaCodeProposalSummary CreateProposalSummary(
        string title,
        PatchProposalId? id = null) =>
        new(
            id ?? PatchProposalId.NewId(),
            title,
            PatchProposalStatus.ReadyForReview,
            new AedaCodeRiskBadge(PatchProposalRisk.Low, "Low", "safe_low_risk"),
            ["src/App.cs"],
            DateTimeOffset.UtcNow);

    private static PatchProposal CreateProposal(
        string diffPath,
        string diffLine,
        IReadOnlyList<string> files)
    {
        var id = PatchProposalId.NewId();
        return new PatchProposal(
            id,
            WorkspaceId.NewId(),
            "Fix safely",
            "Safe summary",
            PatchProposalStatus.ReadyForReview,
            PatchProposalRisk.Low,
            ["safe_low_risk"],
            files.Select((path, index) => new PatchProposalFile(
                path,
                PatchProposalFileChangeKind.Modify,
                "old",
                "new",
                "old-hash",
                "new-hash",
                index == 0
                    ? $"--- {diffPath}\n+++ {diffPath}\n@@\n{diffLine}"
                    : $"--- {path}\n+++ {path}\n@@\n+ changed",
                [])).ToArray(),
            [new PatchProposalSource(WorkspaceId.NewId(), "src/App.cs", "hash", "context")],
            new PatchProposalValidationPlan(
                [new PatchProposalValidationCommand("dotnet test", "Run tests")],
                ["Review changed files"]),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
    }

    private sealed class FakeAedaCodeModuleService : IAedaCodeModuleService
    {
        public IReadOnlyList<AedaCodeProposalSummary> ProposalSummaries { get; set; } = [];

        public PatchProposal? ProposalDetail { get; set; }

        public PatchApplyPlan? DryRunPlan { get; set; }

        public IReadOnlyList<ValidationCommandTemplate> Templates { get; init; } = [];

        public int DryRunCount { get; private set; }

        public int ApplyApprovalRequestCount { get; private set; }

        public int ApplyCount { get; private set; }

        public int ValidationApprovalRequestCount { get; private set; }

        public int ValidationRunCount { get; private set; }

        public int RollbackCount { get; private set; }

        public int CreateProposalFromRequestCount { get; private set; }

        public PatchProposal? CreatedProposal { get; init; }

        public Exception? CreateProposalFailure { get; init; }

        public bool DelayUntilCancelled { get; init; }

        public Func<PatchApplyRequest, PatchApplyResult>? ApplyResultFactory { get; init; }

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<AedaCodeProposalCreationProgress> ProgressToReport { get; init; } = [];

        public Task<AedaCodeSession> StartSessionAsync(WorkspaceId workspaceId, string? safeSummary = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AedaCodeSession(
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
                safeSummary ?? "Session"));

        public Task<IReadOnlyList<AedaCodeSession>> ListRecentSessionsAsync(int limit = 20, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeSession>>([]);

        public Task<AedaCodeWorkspaceSummary> GetWorkspaceSummaryAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AedaCodeWorkspaceSummary(workspaceId, "Repo", true, 1, 1));

        public Task<CodeContextPack> ReadFilesAsync(WorkspaceId workspaceId, IReadOnlyList<string> relativePaths, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeContextPack> SearchAsync(CodeContextSearchRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CodeChangePlan> CreatePlanAsync(CodeChangeRequest request, CodeContextPack context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<PatchProposal> CreateProposalAsync(PatchProposalCreateRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AedaCodeProposalCreationResult> CreateProposalFromRequestAsync(
            AedaCodeProposalCreationRequest request,
            IProgress<AedaCodeProposalCreationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CreateProposalFromRequestCount++;
            Started.TrySetResult();
            foreach (var item in ProgressToReport)
            {
                progress?.Report(item);
            }

            if (DelayUntilCancelled)
            {
                return WaitUntilCancelledAsync(cancellationToken);
            }

            if (CreateProposalFailure is not null)
            {
                return Task.FromException<AedaCodeProposalCreationResult>(CreateProposalFailure);
            }

            var proposal = CreatedProposal ?? CreateProposal("src/App.cs", "+ changed", ["src/App.cs"]);
            ProposalDetail = proposal;
            var summary = new AedaCodeProposalSummary(
                proposal.Id,
                proposal.Title,
                proposal.Status,
                new AedaCodeRiskBadge(proposal.Risk, proposal.Risk.ToString(), string.Join(", ", proposal.RiskReasons)),
                proposal.Files.Select(file => file.RelativePath).ToArray(),
                proposal.UpdatedAtUtc);
            ProposalSummaries = [summary];
            return Task.FromResult(new AedaCodeProposalCreationResult(
                proposal,
                summary,
                proposal.Sources.Select(source => source.RelativePath).ToArray(),
                []));
        }

        private async Task<AedaCodeProposalCreationResult> WaitUntilCancelledAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }

        public Task<PatchProposal?> GetProposalAsync(PatchProposalId proposalId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ProposalDetail);

        public Task<IReadOnlyList<AedaCodeProposalSummary>> ListProposalSummariesAsync(WorkspaceId workspaceId, int limit = 50, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AedaCodeProposalSummary>>(ProposalSummaries.Take(limit).ToArray());

        public Task<IReadOnlyList<ValidationCommandTemplate>> ListValidationTemplatesAsync(WorkspaceId workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Templates);

        public Task<PatchApplyPlan> DryRunApplyAsync(PatchApplyRequest request, CancellationToken cancellationToken = default)
        {
            DryRunCount++;
            return Task.FromResult(DryRunPlan ?? new PatchApplyPlan(
                request.ProposalId,
                request.WorkspaceId,
                PatchApplyStatus.DryRunPassed,
                [],
                [],
                RequiresApproval: true));
        }

        public Task<ApprovalRequest> RequestApplyApprovalAsync(PatchProposalId proposalId, WorkspaceId workspaceId, CancellationToken cancellationToken = default)
        {
            ApplyApprovalRequestCount++;
            return Task.FromResult(ApprovalRequest.Create(
                new ApprovalScope(TaskId.NewId(), ApprovalKind.ApproveFutureApply, $"patch-apply:{workspaceId}:{proposalId}"),
                "Approve apply",
                "Approve apply."));
        }

        public Task<PatchApplyResult> ApplyApprovedProposalAsync(PatchApplyRequest request, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            if (ApplyResultFactory is not null)
            {
                return Task.FromResult(ApplyResultFactory(request));
            }

            return Task.FromResult(new PatchApplyResult(
                PatchApplyResultId.NewId(),
                request.ProposalId,
                request.WorkspaceId,
                request.ApprovalDecision?.IsAllowed == true ? PatchApplyStatus.Applied : PatchApplyStatus.Failed,
                [new PatchApplyFileResult("src/App.cs", PatchProposalFileChangeKind.Modify, PatchApplyStatus.Applied)],
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }

        public Task<PatchApplyResult?> GetApplyResultAsync(PatchApplyResultId applyResultId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PatchApplyResult?>(null);

        public Task<PatchRollbackResult> RollbackAsync(PatchRollbackRequest request, CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.FromResult(new PatchRollbackResult(
                PatchRollbackResultId.NewId(),
                request.ApplyResultId,
                request.WorkspaceId,
                PatchApplyStatus.RolledBack,
                [new PatchApplyFileResult("src/App.cs", PatchProposalFileChangeKind.Modify, PatchApplyStatus.RolledBack)],
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }

        public Task<ValidationRun> CreateValidationRunAsync(ValidationRunRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ValidationRun(
                ValidationRunId.NewId(),
                request.WorkspaceId,
                request.TemplateId,
                ".",
                ValidationRunStatus.Created,
                request.ProposalId,
                request.ApplyResultId,
                null,
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        public Task<ApprovalRequest> RequestValidationApprovalAsync(ValidationRunId runId, CancellationToken cancellationToken = default)
        {
            ValidationApprovalRequestCount++;
            return Task.FromResult(ApprovalRequest.Create(
                new ApprovalScope(TaskId.NewId(), ApprovalKind.ValidationRun, $"validation-run:test:{runId}"),
                "Approve validation",
                "Approve validation."));
        }

        public Task<ValidationRun> RunApprovedValidationAsync(ValidationRunId runId, ApprovalRequest approvalRequest, ApprovalDecision approvalDecision, CancellationToken cancellationToken = default)
        {
            ValidationRunCount++;
            return Task.FromResult(new ValidationRun(
                runId,
                WorkspaceId.NewId(),
                "dotnet-test-personalai",
                ".",
                ValidationRunStatus.Succeeded,
                null,
                null,
                new ValidationCommandResult(
                    0,
                    ValidationRunStatus.Succeeded,
                    new ValidationOutputChunk(@"ok C:\secret password: hunter2", false),
                    new ValidationOutputChunk(string.Empty, false),
                    TimeSpan.FromSeconds(1)),
                [],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }

        public Task<AedaCodeDashboardModel> GetDashboardAsync(AedaCodeSessionId sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

        public int TimelineRequestCount { get; private set; }

        public ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(TaskId taskId, int limit = 100, CancellationToken cancellationToken = default) =>
            new(CreateTimeline(taskId));

        private IReadOnlyList<AedaTaskActivityGroup> CreateTimeline(TaskId taskId)
        {
            TimelineRequestCount++;
            return
            [
                new AedaTaskActivityGroup(
                    "code",
                    "Code",
                    [
                        new AedaTaskTimelineItem(
                            Guid.NewGuid().ToString("N"),
                            taskId,
                            DateTimeOffset.UtcNow,
                            "Patch proposal created",
                            "Safe event summary",
                            null,
                            new AedaTaskStatusBadge(AedaTaskCenterStatus.Completed, "Completed", "completed", false, true),
                            new AedaTaskModuleBadge(AedaTaskCenterModule.Code, "Code", AedaModuleId.Code, "aeda-code"),
                            [])
                    ])
            ];
        }

        public ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(TaskId taskId, Guid eventId, CancellationToken cancellationToken = default) =>
            new ValueTask<AedaTaskTimelineItem?>((AedaTaskTimelineItem?)null);

        public ValueTask CancelTaskAsync(TaskId taskId, TaskCancellationReason reason, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }
}
