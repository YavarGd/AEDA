using Microsoft.Data.Sqlite;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Coding;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Coding;

public sealed class PatchApplyFoundationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly WorkspaceRegistry _registry = new();
    private readonly WorkspaceDescriptor _workspace;
    private readonly FileSystemWorkspaceReader _reader;
    private readonly WorkspacePathResolver _resolver;
    private readonly SqlitePatchProposalRepository _proposalRepository;
    private readonly SqlitePatchApplyRepository _applyRepository;
    private readonly InMemoryApprovalCheckpointStore _approvals = new();

    public PatchApplyFoundationTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = _registry.Register(_root, "Apply");
        _resolver = new WorkspacePathResolver(_registry);
        _reader = new FileSystemWorkspaceReader(
            _registry,
            _resolver,
            new WorkspaceToolOptions());
        _proposalRepository = new SqlitePatchProposalRepository(
            Path.Combine(_root, "proposals.db"));
        _applyRepository = new SqlitePatchApplyRepository(
            Path.Combine(_root, "apply.db"));
    }

    [Fact]
    public async Task DryRun_PassesForModifyNewFileAndNoOp()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([
            Edit("src/App.cs", "old\n", "new\n"),
            Edit("src/New.cs", null, "created\n", PatchProposalFileChangeKind.Add),
            Edit("src/Same.cs", "same\n", "same\n", PatchProposalFileChangeKind.NoOp)
        ]);
        Write("src/Same.cs", "same\n");
        var validator = CreateValidator();

        var plan = await validator.DryRunAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id));

        Assert.Equal(PatchApplyStatus.DryRunPassed, plan.Status);
        Assert.Equal(3, plan.Operations.Count);
    }

    [Fact]
    public async Task DryRun_RejectsStaleHashDeletionBlockedRiskAndMissingProposal()
    {
        await InitializeAsync();
        Write("src/App.cs", "changed\n");
        var stale = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var delete = await SaveProposalAsync([Edit("src/Delete.cs", "old\n", null, PatchProposalFileChangeKind.Delete)]);
        var blocked = await SaveProposalAsync([Edit("src/Secret.cs", "old\n", "sk-secret\n")], PatchProposalRisk.Blocked);
        var validator = CreateValidator();

        var stalePlan = await validator.DryRunAsync(new PatchApplyRequest(stale.Id, _workspace.Id));
        var deletePlan = await validator.DryRunAsync(new PatchApplyRequest(delete.Id, _workspace.Id));
        var blockedPlan = await validator.DryRunAsync(new PatchApplyRequest(blocked.Id, _workspace.Id));
        var missingPlan = await validator.DryRunAsync(new PatchApplyRequest(PatchProposalId.NewId(), _workspace.Id));

        Assert.Contains(PatchApplyFailureReason.StaleOriginalContent, stalePlan.FailureReasons);
        Assert.Contains(PatchApplyFailureReason.DeleteNotAllowed, deletePlan.FailureReasons);
        Assert.Contains(PatchApplyFailureReason.ProposalNotReady, blockedPlan.FailureReasons);
        Assert.Contains(PatchApplyFailureReason.ProposalNotFound, missingPlan.FailureReasons);
    }

    [Fact]
    public async Task Apply_RequiresApprovalAndTreatsDenialAsControlledOutcome()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var denied = await _approvals.DecideAsync(approval, ApprovalDecisionKind.Deny);

        var missing = await service.ApplyAsync(new PatchApplyRequest(proposal.Id, _workspace.Id));
        var deniedResult = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            denied));

        Assert.Contains(PatchApplyFailureReason.ApprovalMissing, missing.FailureReasons);
        Assert.Contains(PatchApplyFailureReason.ApprovalDenied, deniedResult.FailureReasons);
        Assert.Equal("old\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task Apply_ApprovedModifyCreatesBackupAndRollbackRestores()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            decision));
        var backups = await _applyRepository.ListBackupsAsync(result.Id);

        Assert.Equal(PatchApplyStatus.Applied, result.Status);
        Assert.Equal("new\n", Read("src/App.cs"));
        Assert.Single(backups);
        var rollback = await service.RollbackAsync(new PatchRollbackRequest(
            result.Id,
            _workspace.Id));
        Assert.Equal(PatchApplyStatus.RolledBack, rollback.Status);
        Assert.Equal("old\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task Apply_ApprovedNewFileCreatesInsideWorkspace()
    {
        await InitializeAsync();
        var proposal = await SaveProposalAsync([
            Edit("src/New.cs", null, "created\n", PatchProposalFileChangeKind.Add)
        ]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            decision));

        Assert.Equal(PatchApplyStatus.Applied, result.Status);
        Assert.Equal("created\n", Read("src/New.cs"));
    }

    [Fact]
    public async Task Apply_NoOpDoesNotWrite()
    {
        await InitializeAsync();
        Write("src/App.cs", "same\n");
        var before = File.GetLastWriteTimeUtc(PathFor("src/App.cs"));
        var proposal = await SaveProposalAsync([
            Edit("src/App.cs", "same\n", "same\n", PatchProposalFileChangeKind.NoOp)
        ]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ApplyAsync(new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision));

        Assert.Equal(PatchApplyStatus.Applied, result.Status);
        Assert.Equal(before, File.GetLastWriteTimeUtc(PathFor("src/App.cs")));
    }

    [Fact]
    public async Task Apply_RecordsPartialFailure()
    {
        await InitializeAsync();
        Write("src/One.cs", "one\n");
        var good = new UnifiedDiffBuilder().BuildFileDiff(Edit("src/One.cs", "one\n", "two\n"));
        var bad = good with
        {
            RelativePath = "src/Bad.cs",
            ChangeKind = PatchProposalFileChangeKind.Add,
            OriginalContent = string.Empty,
            ProposedContent = null,
            OriginalContentHash = CodeContextService.ComputeHash(string.Empty)
        };
        var proposal = CreateProposal([good, bad]);
        await _proposalRepository.CreateAsync(proposal);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ApplyAsync(new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision));

        Assert.Equal(PatchApplyStatus.PartiallyApplied, result.Status);
        Assert.Equal("two\n", Read("src/One.cs"));
        Assert.Contains(PatchApplyFailureReason.WriteFailed, result.FailureReasons);
    }

    [Fact]
    public async Task Rollback_RejectsChangedTargetAndMissingBackup()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);
        var result = await service.ApplyAsync(new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision));
        Write("src/App.cs", "user changed\n");

        var rollback = await service.RollbackAsync(new PatchRollbackRequest(result.Id, _workspace.Id));
        var missing = await service.RollbackAsync(new PatchRollbackRequest(PatchApplyResultId.NewId(), _workspace.Id));

        Assert.Equal(PatchApplyStatus.RollbackFailed, rollback.Status);
        Assert.Contains(PatchApplyFailureReason.StaleOriginalContent, rollback.FailureReasons);
        Assert.Equal(PatchApplyStatus.RollbackFailed, missing.Status);
    }

    [Fact]
    public async Task Persistence_ReloadsApplyBackupAndRollbackResults()
    {
        await InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var result = new PatchApplyResult(
            PatchApplyResultId.NewId(),
            PatchProposalId.NewId(),
            _workspace.Id,
            PatchApplyStatus.Applied,
            [new PatchApplyFileResult("src/App.cs", PatchProposalFileChangeKind.Modify, PatchApplyStatus.Applied)],
            [],
            now,
            now);
        var backup = new PatchApplyBackup(
            result.Id,
            result.ProposalId,
            _workspace.Id,
            "src/App.cs",
            "old\n",
            CodeContextService.ComputeHash("old\n"),
            CodeContextService.ComputeHash("new\n"),
            now,
            PatchProposalFileChangeKind.Modify);
        await _applyRepository.CreateApplyResultAsync(result, [backup]);
        var rollback = new PatchRollbackResult(
            PatchRollbackResultId.NewId(),
            result.Id,
            _workspace.Id,
            PatchApplyStatus.RolledBack,
            [],
            [],
            now,
            now);
        await _applyRepository.CreateRollbackResultAsync(rollback);

        Assert.NotNull(await _applyRepository.GetApplyResultAsync(result.Id));
        Assert.Single(await _applyRepository.ListBackupsAsync(result.Id));
        Assert.NotNull(await _applyRepository.GetRollbackResultAsync(rollback.Id));
        Assert.Single(await _applyRepository.ListRecentApplyResultsAsync(10));
    }

    [Fact]
    public void Capabilities_EnableApplyAndRollbackWhenConfigured()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasPatchProposal: true,
            hasPatchReview: true,
            hasPatchApply: true,
            hasPatchRollback: true);

        Assert.True(registry.GetStatus(BackendCapability.PatchApply).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.PatchRollback).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.TestExecution).IsAvailable);
        Assert.Equal("git_mutation_deferred", registry.GetStatus(BackendCapability.GitMutation).SafeReasonCode);
    }

    private async Task InitializeAsync()
    {
        await _proposalRepository.InitializeAsync();
        await _applyRepository.InitializeAsync();
    }

    private PatchApplyValidator CreateValidator() =>
        new(_proposalRepository, _reader, _resolver);

    private PatchApplyService CreateService() =>
        new(
            _proposalRepository,
            _applyRepository,
            CreateValidator(),
            _reader,
            _approvals);

    private async Task<PatchProposal> SaveProposalAsync(
        IReadOnlyList<PatchProposalFileEdit> edits,
        PatchProposalRisk risk = PatchProposalRisk.Low)
    {
        var files = edits.Select(edit => new UnifiedDiffBuilder().BuildFileDiff(edit)).ToArray();
        var proposal = CreateProposal(files, risk);
        await _proposalRepository.CreateAsync(proposal);
        return proposal;
    }

    private PatchProposal CreateProposal(
        IReadOnlyList<PatchProposalFile> files,
        PatchProposalRisk risk = PatchProposalRisk.Low)
    {
        var now = DateTimeOffset.UtcNow;
        return new PatchProposal(
            PatchProposalId.NewId(),
            _workspace.Id,
            "Apply proposal",
            "Apply safely",
            risk == PatchProposalRisk.Blocked ? PatchProposalStatus.Failed : PatchProposalStatus.ReadyForReview,
            risk,
            risk == PatchProposalRisk.Blocked ? ["secret_looking_content"] : ["small_text_change"],
            files,
            [],
            new ValidationPlanService().CreatePlan(files),
            now,
            now);
    }

    private static PatchProposalFileEdit Edit(
        string path,
        string? original,
        string? proposed,
        PatchProposalFileChangeKind kind = PatchProposalFileChangeKind.Modify) =>
        new(path, original, proposed, kind);

    private string PathFor(string relativePath) =>
        Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private void Write(string relativePath, string content)
    {
        var path = PathFor(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private string Read(string relativePath) => File.ReadAllText(PathFor(relativePath));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
