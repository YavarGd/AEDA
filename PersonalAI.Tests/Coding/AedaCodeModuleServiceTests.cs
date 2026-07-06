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
            null,
            proposals,
            apply,
            validation,
            new FakeValidationCommandAllowlist(),
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
            null,
            new FakeProposalService(CreateProposal(_workspaceId)),
            new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId())),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null)),
            new FakeValidationCommandAllowlist());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetDashboardAsync(AedaCodeSessionId.NewId()));
    }

    [Fact]
    public async Task Service_CreatesProposalFromRequestWithTaskEventsAndNoApplyOrValidation()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var reader = new FakeWorkspaceReader(workspace);
        var context = new FakeCodeContextService(_workspaceId);
        var planner = new FakePlanningService();
        var draft = new FakeDraftService();
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal);
        var apply = new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id));
        var validation = new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null));
        var taskRuntime = new FakeTaskRuntime();
        var service = new AedaCodeModuleService(
            reader,
            context,
            planner,
            draft,
            proposals,
            apply,
            validation,
            new FakeValidationCommandAllowlist(),
            taskRuntime: taskRuntime);

        var result = await service.CreateProposalFromRequestAsync(
            new AedaCodeProposalCreationRequest(_workspaceId, "Add XML docs."));

        Assert.Equal(proposal.Id, result.Proposal.Id);
        Assert.Equal(1, draft.CreateCount);
        Assert.Equal(1, proposals.CreateCount);
        Assert.Equal(0, apply.ApplyCount);
        Assert.Equal(0, validation.CreateRunCount);
        Assert.Contains(taskRuntime.Events, item => item.Kind == TaskEventKind.CodeContextLoaded);
        Assert.Contains(taskRuntime.Events, item => item.Kind == TaskEventKind.PatchProposalCreated);
        Assert.Equal(1, taskRuntime.CompletedCount);
    }

    [Fact]
    public async Task Service_CreatesProposalFromRequest_UsesExplicitRequestedContextTarget()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var context = new FakeCodeContextService(_workspaceId)
        {
            SearchMatches =
            [
                "PersonalAI.Desktop.WinUI/ViewModels/AedaCodeModuleViewModel.cs"
            ]
        };
        var draft = new FakeDraftService();
        var proposal = CreateProposal(_workspaceId);
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            context,
            new FakePlanningService(),
            draft,
            new FakeProposalService(proposal),
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist());

        var result = await service.CreateProposalFromRequestAsync(
            new AedaCodeProposalCreationRequest(
                _workspaceId,
                "Add an XML documentation comment to one small private helper method in AedaCodeModuleViewModel.cs. Do not change behavior."));

        Assert.Contains("AedaCodeModuleViewModel", context.SearchQueries);
        Assert.Equal(
            "PersonalAI.Desktop.WinUI/ViewModels/AedaCodeModuleViewModel.cs",
            result.ContextRelativePaths[0]);
        Assert.Equal(result.ContextRelativePaths, draft.ContextRelativePaths);
    }

    [Fact]
    public async Task Service_SearchContextFiles_ReturnsBoundedSafeRelativeCandidates()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var reader = new FakeWorkspaceReader(workspace)
        {
            DirectoryEntries = new Dictionary<string, IReadOnlyList<WorkspaceDirectoryEntry>>(StringComparer.OrdinalIgnoreCase)
            {
                ["."] =
                [
                    new("src", "src", WorkspaceEntryType.Directory, null, DateTimeOffset.UtcNow, false, string.Empty),
                    new("README.md", "README.md", WorkspaceEntryType.File, 16, DateTimeOffset.UtcNow, false, ".md"),
                    new(".env", ".env", WorkspaceEntryType.File, 16, DateTimeOffset.UtcNow, false, string.Empty),
                    new("SecretToken.cs", "SecretToken.cs", WorkspaceEntryType.File, 16, DateTimeOffset.UtcNow, false, ".cs")
                ],
                ["src"] =
                [
                    new("App.cs", "src/App.cs", WorkspaceEntryType.File, 16, DateTimeOffset.UtcNow, false, ".cs"),
                    new("App.xaml", "src/App.xaml", WorkspaceEntryType.File, 16, DateTimeOffset.UtcNow, false, ".xaml")
                ]
            }
        };
        var service = new AedaCodeModuleService(
            reader,
            new FakeCodeContextService(_workspaceId),
            new FakePlanningService(),
            new FakeDraftService(),
            new FakeProposalService(CreateProposal(_workspaceId)),
            new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId())),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null)),
            new FakeValidationCommandAllowlist());

        var result = await service.SearchContextFilesAsync(new AedaCodeContextSearchRequest(
            _workspaceId,
            "App",
            ["src/App.cs"],
            MaxResults: 1));

        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("src/App.cs", candidate.RelativePath);
        Assert.True(candidate.IsReadable);
        Assert.True(candidate.IsAlreadySelected);
        Assert.DoesNotContain("C:\\safe", candidate.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(result.Candidates, item => item.RelativePath.Contains(".env", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.IsTruncated);
    }

    [Fact]
    public async Task Service_CreatesProposalFromRequest_UsesSelectedContextWithoutRequestSearch()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var context = new FakeCodeContextService(_workspaceId);
        var draft = new FakeDraftService();
        var proposal = CreateProposal(_workspaceId);
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            context,
            new FakePlanningService(),
            draft,
            new FakeProposalService(proposal),
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist());

        var result = await service.CreateProposalFromRequestAsync(
            new AedaCodeProposalCreationRequest(
                _workspaceId,
                "Add XML docs to something vague.",
                ContextSelection: new AedaCodeProposalContextSelection(
                    ["src/Selected.cs"])));

        Assert.Equal(["src/Selected.cs"], result.ContextRelativePaths);
        Assert.Equal(["src/Selected.cs"], draft.ContextRelativePaths);
        Assert.Equal(["src/Selected.cs"], context.LoadedRelativePaths);
        Assert.Empty(context.SearchQueries);
    }

    [Fact]
    public async Task Service_ListTargetSnippetCandidatesReadsSelectedCSharpFilesOnly()
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
            new FakeDraftService(),
            new FakeProposalService(CreateProposal(_workspaceId)),
            new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId())),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null)),
            new FakeValidationCommandAllowlist());

        var candidates = await service.ListTargetSnippetCandidatesAsync(
            new AedaCodeTargetSnippetRequest(_workspaceId, ["src/Selected.cs", "README.md"]));

        var candidate = Assert.Single(candidates);
        Assert.Equal("src/Selected.cs", candidate.RelativePath);
        Assert.Contains("private void Helper", candidate.SignaturePreview, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\", candidate.RelativePath, StringComparison.OrdinalIgnoreCase);
        Assert.True(candidate.SafePreview.Length <= 363);
    }

    [Fact]
    public async Task Service_ListTargetSnippetCandidatesReadsLargeSelectedFileBudget()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        const string path = "PersonalAI.Desktop.WinUI/ViewModels/AedaCodeModuleViewModel.cs";
        var context = new FakeCodeContextService(_workspaceId)
        {
            FileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [path] = "class ViewModel\n{\n" +
                    new string(' ', 35_000) +
                    "\n    private bool CanCreateProposal()\n    {\n        return true;\n    }\n}\n"
            }
        };
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            context,
            new FakePlanningService(),
            new FakeDraftService(),
            new FakeProposalService(CreateProposal(_workspaceId)),
            new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId())),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null)),
            new FakeValidationCommandAllowlist());

        var candidates = await service.ListTargetSnippetCandidatesAsync(
            new AedaCodeTargetSnippetRequest(_workspaceId, [path]));

        Assert.Contains(candidates, candidate =>
            candidate.SignaturePreview.Contains("CanCreateProposal", StringComparison.Ordinal));
        Assert.True(context.LastMaxCharactersPerFile > 30_000);
    }

    [Fact]
    public async Task Service_SelectedTargetSnippetIsRevalidatedAndPassedToDraft()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var context = new FakeCodeContextService(_workspaceId);
        var draft = new FakeDraftService();
        var proposal = CreateProposal(_workspaceId);
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            context,
            new FakePlanningService(),
            draft,
            new FakeProposalService(proposal),
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist());
        var candidate = Assert.Single(await service.ListTargetSnippetCandidatesAsync(
            new AedaCodeTargetSnippetRequest(_workspaceId, ["src/Selected.cs"])));

        await service.CreateProposalFromRequestAsync(
            new AedaCodeProposalCreationRequest(
                _workspaceId,
                "Add XML docs to the selected method.",
                ContextSelection: new AedaCodeProposalContextSelection(
                    ["src/Selected.cs"],
                    new AedaCodeSelectedTargetSnippet(candidate.Id, candidate.RelativePath))));

        Assert.NotNull(draft.SelectedTargetSnippet);
        Assert.Equal(candidate.Id, draft.SelectedTargetSnippet?.Id);
        Assert.Equal("src/Selected.cs", draft.SelectedTargetSnippet?.RelativePath);
        Assert.Contains("private void Helper", draft.SelectedTargetSnippet?.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Service_SelectedTargetSnippetAfterThirtyThousandCharactersRevalidatesDuringProposalCreation()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        const string path = "PersonalAI.Desktop.WinUI/ViewModels/AedaCodeModuleViewModel.cs";
        var content = "class ViewModel\n{\n" +
            new string(' ', 35_000) +
            "\n    private bool CanCreateProposal()\n    {\n        return true;\n    }\n}\n";
        var context = new FakeCodeContextService(_workspaceId)
        {
            FileContents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [path] = content
            }
        };
        var draft = new FakeDraftService();
        var proposal = CreateProposal(_workspaceId);
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            context,
            new FakePlanningService(),
            draft,
            new FakeProposalService(proposal),
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist());
        var candidate = Assert.Single(await service.ListTargetSnippetCandidatesAsync(
            new AedaCodeTargetSnippetRequest(_workspaceId, [path])));

        await service.CreateProposalFromRequestAsync(
            new AedaCodeProposalCreationRequest(
                _workspaceId,
                "Add XML docs to the selected method.",
                ContextSelection: new AedaCodeProposalContextSelection(
                    [path],
                    new AedaCodeSelectedTargetSnippet(candidate.Id, candidate.RelativePath))));

        Assert.NotNull(draft.SelectedTargetSnippet);
        Assert.Equal(candidate.Id, draft.SelectedTargetSnippet?.Id);
        Assert.Contains("CanCreateProposal", draft.SelectedTargetSnippet?.Text, StringComparison.Ordinal);
        Assert.True(context.LastMaxCharactersPerFile > 30_000);
    }

    [Fact]
    public async Task Service_StaleSelectedTargetSnippetFailsBeforeDraftPersistenceApplyOrValidation()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal);
        var apply = new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id));
        var validation = new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null));
        var draft = new FakeDraftService();
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId),
            new FakePlanningService(),
            draft,
            proposals,
            apply,
            validation,
            new FakeValidationCommandAllowlist());

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateProposalFromRequestAsync(
                new AedaCodeProposalCreationRequest(
                    _workspaceId,
                    "Add XML docs to the selected method.",
                    ContextSelection: new AedaCodeProposalContextSelection(
                        ["src/Selected.cs"],
                        new AedaCodeSelectedTargetSnippet("stale-id", "src/Selected.cs")))));

        Assert.Equal(AedaCodeProposalCreationFailureReason.SelectedTargetStale, failure.Failure.Reason);
        Assert.Equal(0, draft.CreateCount);
        Assert.Equal(0, proposals.CreateCount);
        Assert.Equal(0, apply.ApplyCount);
        Assert.Equal(0, validation.CreateRunCount);
    }

    [Fact]
    public async Task Service_SelectedContextUnavailableFailsBeforeDraftPersistenceApplyOrValidation()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var proposals = new FakeProposalService(CreateProposal(_workspaceId));
        var apply = new FakeApplyService(CreateApplyResult(_workspaceId, PatchProposalId.NewId()));
        var validation = new FakeValidationRunnerService(CreateValidationRun(_workspaceId, PatchProposalId.NewId(), null));
        var draft = new FakeDraftService();
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId) { ReturnEmptyContext = true },
            new FakePlanningService(),
            draft,
            proposals,
            apply,
            validation,
            new FakeValidationCommandAllowlist());

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateProposalFromRequestAsync(new AedaCodeProposalCreationRequest(
                _workspaceId,
                "Add XML docs.",
                ContextSelection: new AedaCodeProposalContextSelection(["src/Missing.cs"]))));

        Assert.Equal(AedaCodeProposalCreationFailureReason.SelectedContextUnavailable, failure.Failure.Reason);
        Assert.Equal(0, draft.CreateCount);
        Assert.Equal(0, proposals.CreateCount);
        Assert.Equal(0, apply.ApplyCount);
        Assert.Equal(0, validation.CreateRunCount);
    }

    [Fact]
    public async Task Service_NoSafeContextFailsWithSpecificTimelineReasonAndNoProposal()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal);
        var taskRuntime = new FakeTaskRuntime();
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId) { ReturnEmptyContext = true },
            new FakePlanningService(),
            new FakeDraftService(),
            proposals,
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist(),
            taskRuntime: taskRuntime);

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateProposalFromRequestAsync(
                new AedaCodeProposalCreationRequest(_workspaceId, "Add XML docs.")));

        Assert.Equal(AedaCodeProposalCreationFailureReason.NoSafeContext, failure.Failure.Reason);
        Assert.Equal("no_safe_context", taskRuntime.FailedSafeCode);
        Assert.Equal(0, proposals.CreateCount);
        Assert.Contains("Select one or more files", failure.Failure.NextStepHint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Service_UnsafeDraftTargetFailsBeforePersistenceApplyValidationOrRollback()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal);
        var apply = new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id));
        var validation = new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null));
        var taskRuntime = new FakeTaskRuntime();
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId),
            new FakePlanningService(),
            new FakeDraftService
            {
                Failure = new AedaCodeProposalCreationException(
                    AedaCodeProposalCreationFailure.FromReason(
                        AedaCodeProposalCreationFailureReason.UnsafeFileTarget))
            },
            proposals,
            apply,
            validation,
            new FakeValidationCommandAllowlist(),
            taskRuntime: taskRuntime);

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateProposalFromRequestAsync(
                new AedaCodeProposalCreationRequest(_workspaceId, "Add XML docs.")));

        Assert.Equal(AedaCodeProposalCreationFailureReason.UnsafeFileTarget, failure.Failure.Reason);
        Assert.Equal("unsafe_file_target", taskRuntime.FailedSafeCode);
        Assert.Equal(0, proposals.CreateCount);
        Assert.Equal(0, apply.ApplyCount);
        Assert.Equal(0, apply.RollbackCount);
        Assert.Equal(0, validation.CreateRunCount);
        Assert.DoesNotContain("AedaCodeModuleViewModel.cs", failure.Failure.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Service_ProposalValidationFailureDoesNotPersistAndUsesSpecificReason()
    {
        var workspace = new WorkspaceDescriptor(
            _workspaceId,
            "Code workspace",
            "C:\\safe",
            DateTimeOffset.UtcNow,
            WorkspaceAccessPolicy.ReadOnly);
        var proposal = CreateProposal(_workspaceId);
        var proposals = new FakeProposalService(proposal)
        {
            CreateFailure = new InvalidOperationException("unsafe_patch_path")
        };
        var taskRuntime = new FakeTaskRuntime();
        var service = new AedaCodeModuleService(
            new FakeWorkspaceReader(workspace),
            new FakeCodeContextService(_workspaceId),
            new FakePlanningService(),
            new FakeDraftService(),
            proposals,
            new FakeApplyService(CreateApplyResult(_workspaceId, proposal.Id)),
            new FakeValidationRunnerService(CreateValidationRun(_workspaceId, proposal.Id, null)),
            new FakeValidationCommandAllowlist(),
            taskRuntime: taskRuntime);

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateProposalFromRequestAsync(
                new AedaCodeProposalCreationRequest(_workspaceId, "Add XML docs.")));

        Assert.Equal(AedaCodeProposalCreationFailureReason.ProposalValidationFailed, failure.Failure.Reason);
        Assert.Equal("proposal_validation_failed", taskRuntime.FailedSafeCode);
        Assert.Equal(1, proposals.CreateCount);
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
        public IReadOnlyDictionary<string, IReadOnlyList<WorkspaceDirectoryEntry>>? DirectoryEntries { get; init; }

        public HashSet<string> UnreadableRelativePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public WorkspaceDescriptor GetWorkspace(WorkspaceId workspaceId) =>
            workspaceId == workspace.Id
                ? workspace
                : throw new WorkspaceAccessException("workspace_not_found", "Workspace was not registered.");

        public IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
            WorkspaceId workspaceId,
            string relativePath,
            int maxEntries,
            bool includeHidden,
            CancellationToken cancellationToken = default)
        {
            _ = GetWorkspace(workspaceId);
            var normalized = string.IsNullOrWhiteSpace(relativePath)
                ? "."
                : relativePath.Replace('\\', '/').Trim('/');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = ".";
            }

            if (DirectoryEntries is not null)
            {
                return DirectoryEntries.TryGetValue(normalized, out var entries)
                    ? entries.Take(maxEntries).ToArray()
                    : [];
            }

            return
            [
                new("src", "src", WorkspaceEntryType.Directory, null, DateTimeOffset.UtcNow, false, string.Empty),
                new("README.md", "README.md", WorkspaceEntryType.File, 4, DateTimeOffset.UtcNow, false, ".md")
            ];
        }

        public WorkspaceTextFile ReadTextFile(
            WorkspaceId workspaceId,
            string relativePath,
            int maxCharacters,
            CancellationToken cancellationToken = default)
        {
            _ = GetWorkspace(workspaceId);
            if (UnreadableRelativePaths.Contains(relativePath))
            {
                throw new WorkspaceAccessException("file_not_found", "Workspace file was not found.");
            }

            return new(relativePath.Replace('\\', '/'), "class App {}", "utf-8", 12, false, false);
        }

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
        public bool ReturnEmptyContext { get; init; }

        public IReadOnlyList<string> SearchMatches { get; init; } = [];

        public IReadOnlyDictionary<string, string> FileContents { get; init; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public List<string> SearchQueries { get; } = [];

        public IReadOnlyList<string> LoadedRelativePaths { get; private set; } = [];

        public int LastMaxCharactersPerFile { get; private set; }

        public Task<CodeContextPack> LoadFilesAsync(
            WorkspaceId workspaceId,
            IReadOnlyList<string> relativePaths,
            int maxFiles = 20,
            int maxCharactersPerFile = 100_000,
            CancellationToken cancellationToken = default)
        {
            LoadedRelativePaths = relativePaths.Take(maxFiles).ToArray();
            LastMaxCharactersPerFile = maxCharactersPerFile;
            return Task.FromResult(ReturnEmptyContext
                ? new CodeContextPack(workspaceId, [], [], ["no_safe_context"], false)
                : new CodeContextPack(
                    workspaceId,
                    LoadedRelativePaths
                        .Select(path => new CodeContextFile(
                            workspaceId,
                            path,
                            FileContents.TryGetValue(path, out var content)
                                ? content
                                : Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)
                                ? "class App\n{\n    private void Helper()\n    {\n        DoWork();\n    }\n}\n"
                                : "old",
                            "old-hash",
                            "utf-8",
                            70,
                            false,
                            false))
                        .ToArray(),
                    [],
                    [],
                    false));
        }

        public Task<CodeContextPack> SearchAsync(
            CodeContextSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            SearchQueries.Add(request.Query);
            var matches = SearchMatches.Count == 0
                ? [new CodeContextSearchMatch("src/App.cs", 1, "old")]
                : SearchMatches
                    .Select(path => new CodeContextSearchMatch(path, 1, "old"))
                    .ToArray();
            return Task.FromResult(new CodeContextPack(workspaceId, [], matches, [], false));
        }
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
        public int CreateCount { get; private set; }

        public Exception? CreateFailure { get; init; }

        public Task<PatchProposal> CreateProposalAsync(
            PatchProposalCreateRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            if (CreateFailure is not null)
            {
                return Task.FromException<PatchProposal>(CreateFailure);
            }

            return Task.FromResult(proposal);
        }

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
        public int ApplyCount { get; private set; }

        public int RollbackCount { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return Task.FromResult(result);
        }

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
            RollbackCount++;
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
        public int CreateRunCount { get; private set; }

        public Task<ValidationRun> CreateRunAsync(
            ValidationRunRequest request,
            CancellationToken cancellationToken = default)
        {
            CreateRunCount++;
            return Task.FromResult(run);
        }

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

    private sealed class FakeValidationCommandAllowlist : IValidationCommandAllowlist
    {
        public IReadOnlyList<ValidationCommandTemplate> ListTemplates() =>
        [
            new(
                "dotnet-build-debug",
                "Build solution",
                "dotnet",
                ["build", "PersonalAI.slnx"],
                TimeSpan.FromMinutes(3),
                "PersonalAI.slnx")
        ];

        public bool TryCreateCommand(
            ValidationRunRequest request,
            WorkspaceDescriptor workspace,
            out ValidationCommand command,
            out ValidationFailureReason failureReason)
        {
            command = new ValidationCommand(
                request.TemplateId,
                "dotnet",
                ["build", "PersonalAI.slnx"],
                ".",
                TimeSpan.FromMinutes(3));
            failureReason = ValidationFailureReason.UnknownSafeFailure;
            return true;
        }
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

    private sealed class FakeDraftService : ICodeProposalDraftService
    {
        public int CreateCount { get; private set; }

        public IReadOnlyList<string> ContextRelativePaths { get; private set; } = [];

        public CodeProposalSelectedTargetSnippet? SelectedTargetSnippet { get; private set; }

        public AedaCodeProposalCreationException? Failure { get; init; }

        public Task<CodeProposalDraft> CreateDraftAsync(
            CodeProposalDraftRequest request,
            IProgress<AedaCodeProposalCreationProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            if (Failure is not null)
            {
                return Task.FromException<CodeProposalDraft>(Failure);
            }

            ContextRelativePaths = request.Context.Files
                .Select(file => file.RelativePath)
                .ToArray();
            SelectedTargetSnippet = request.SelectedTargetSnippet;
            var file = request.Context.Files[0];
            return Task.FromResult(new CodeProposalDraft(
                "Add docs",
                "Adds XML docs.",
                [new PatchProposalFileEdit(file.RelativePath, file.Content, file.Content + "\n// docs")],
                []));
        }
    }

    private sealed class FakeTaskRuntime : ITaskRuntime
    {
        private readonly TaskRun _run = TaskRun.Create("Create code proposal", "aeda-code");

        public List<(TaskEventKind Kind, string Summary)> Events { get; } = [];

        public int CompletedCount { get; private set; }

        public string? FailedSafeCode { get; private set; }

        public ValueTask<TaskRun> StartTaskAsync(
            string title,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(_run);

        public ValueTask<TaskRun> StartTaskAsync(
            string title,
            string source = "unknown",
            Guid? conversationId = null,
            string? model = null,
            string? provider = null,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_run);

        public ValueTask AppendEventAsync(
            TaskId taskId,
            TaskEventKind kind,
            string summary,
            CancellationToken cancellationToken = default)
        {
            Events.Add((kind, summary));
            return ValueTask.CompletedTask;
        }

        public ValueTask AttachArtifactAsync(
            TaskId taskId,
            TaskArtifact artifact,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default)
        {
            CompletedCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask CancelTaskAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask FailTaskAsync(
            TaskId taskId,
            string safeErrorCode,
            CancellationToken cancellationToken = default)
        {
            FailedSafeCode = safeErrorCode;
            Events.Add((TaskEventKind.TaskFailed, safeErrorCode));
            return ValueTask.CompletedTask;
        }

        public ValueTask<TaskRunRecord?> GetTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRunRecord?>(null);
    }

    private static ApprovalRequest CreateApproval(
        ApprovalKind kind,
        string resourceScope) =>
        ApprovalRequest.Create(
            new ApprovalScope(TaskId.NewId(), kind, resourceScope),
            "Approval",
            "Approval body");
}
