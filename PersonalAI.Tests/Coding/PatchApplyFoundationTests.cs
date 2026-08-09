using System.Diagnostics;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Tasks;
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
    public async Task DryRun_RejectsOldDestructiveLargeDeletionProposal()
    {
        await InitializeAsync();
        var original = string.Join(
            "\n",
            Enumerable.Range(0, 2_000).Select(index => $"public void M{index}() {{ }}")) + "\n";
        Write("src/App.cs", original);
        var proposal = await SaveProposalAsync([
            Edit("src/App.cs", original, "/// <summary>Docs.</summary>\nprivate void Helper() {}\n")
        ]);
        var validator = CreateValidator();

        var plan = await validator.DryRunAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id));

        Assert.Equal(PatchApplyStatus.DryRunFailed, plan.Status);
        Assert.Contains(PatchApplyFailureReason.UnsafeLargeDeletion, plan.FailureReasons);
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
    public async Task Apply_BindsApprovalToRequestActionTaskWorkspaceAndConsumesItOnce()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);
        var validationApproval = await _approvals.RequestAsync(ApprovalRequest.Create(
            new ApprovalScope(TaskId.NewId(), ApprovalKind.ValidationRun, $"validation-run:{_workspace.Id}:{ValidationRunId.NewId()}"),
            "Wrong action",
            "Wrong action"));
        var validationDecision = await _approvals.DecideAsync(
            validationApproval,
            ApprovalDecisionKind.AllowOnce);

        var blocked = new[]
        {
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval with { RequestId = Guid.NewGuid() },
                decision)),
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval,
                decision with { DecisionId = Guid.NewGuid() })),
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval with { Scope = approval.Scope with { Kind = ApprovalKind.ValidationRun } },
                decision)),
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval with { Scope = approval.Scope with { TaskId = TaskId.NewId() } },
                decision)),
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval with { Scope = approval.Scope with { ResourceScope = $"patch-apply:{WorkspaceId.NewId()}:{proposal.Id}" } },
                decision)),
            await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                validationApproval,
                validationDecision))
        };
        var denied = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            ApprovalDecision.Deny(approval)));

        Assert.All(blocked, result => Assert.Contains(
            PatchApplyFailureReason.ApprovalMissing,
            result.FailureReasons));
        Assert.Contains(PatchApplyFailureReason.ApprovalDenied, denied.FailureReasons);
        Assert.Equal("old\n", Read("src/App.cs"));

        var replacement = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var replacementDecision = await _approvals.DecideAsync(
            replacement,
            ApprovalDecisionKind.AllowOnce);
        var stale = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            decision));
        var applied = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            replacement,
            replacementDecision));
        var replay = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            replacement,
            replacementDecision));

        Assert.Contains(PatchApplyFailureReason.ApprovalMissing, stale.FailureReasons);
        Assert.Equal(PatchApplyStatus.Applied, applied.Status);
        Assert.Contains(PatchApplyFailureReason.ApprovalMissing, replay.FailureReasons);
        Assert.Equal("new\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task Apply_RejectsTaskScopedApprovalWithoutWriting()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(
            approval,
            ApprovalDecisionKind.AllowForTask);

        var result = await service.ApplyAsync(new PatchApplyRequest(
            proposal.Id,
            _workspace.Id,
            approval,
            decision));

        Assert.Contains(PatchApplyFailureReason.ApprovalMissing, result.FailureReasons);
        Assert.Equal("old\n", Read("src/App.cs"));
    }

    [Fact]
    public async Task Apply_JournalFailurePreventsFirstMutation()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var repository = new InterceptingApplyRepository(
            _applyRepository,
            (save, result, backups, _) =>
            {
                Assert.Equal(1, save);
                Assert.Equal(PatchApplyStatus.Applying, result.Status);
                Assert.Single(backups);
                Assert.Equal("old\n", Read("src/App.cs"));
                throw new IOException("journal_failed");
            });
        var service = new PatchApplyService(
            _proposalRepository,
            repository,
            CreateValidator(),
            _reader,
            _approvals);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(
            new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision)));

        Assert.Equal("old\n", Read("src/App.cs"));
        Assert.Empty(await _applyRepository.ListRecentApplyResultsAsync());
    }

    [Fact]
    public async Task Apply_CancellationBeforeJournalPreventsFirstMutation()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        using var cancellation = new CancellationTokenSource();
        var cancellingReader = new AfterReadWorkspaceReader(_reader, 2, cancellation.Cancel);
        var service = new PatchApplyService(
            _proposalRepository,
            _applyRepository,
            new PatchApplyValidator(_proposalRepository, cancellingReader, _resolver),
            cancellingReader,
            _approvals);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(
            new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision),
            cancellation.Token));

        Assert.Equal("old\n", Read("src/App.cs"));
        Assert.Empty(await _applyRepository.ListRecentApplyResultsAsync());
    }

    [Fact]
    public async Task Apply_InterruptedFinalizationLeavesApplyingJournalVisibleAfterRestart()
    {
        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        PatchApplyResultId resultId = default;
        var repository = new InterceptingApplyRepository(
            _applyRepository,
            (save, result, backups, _) =>
            {
                resultId = result.Id;
                if (save == 2)
                {
                    Assert.Empty(backups);
                    throw new IOException("simulated_process_interruption");
                }
            });
        var service = new PatchApplyService(
            _proposalRepository,
            repository,
            CreateValidator(),
            _reader,
            _approvals);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        await Assert.ThrowsAsync<IOException>(() => service.ApplyAsync(
            new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision)));

        var restartedRepository = new SqlitePatchApplyRepository(Path.Combine(_root, "apply.db"));
        await restartedRepository.InitializeAsync();
        var interrupted = await restartedRepository.GetApplyResultAsync(resultId);
        var backups = await restartedRepository.ListBackupsAsync(resultId);
        Assert.Equal("new\n", Read("src/App.cs"));
        Assert.NotNull(interrupted);
        Assert.Equal(PatchApplyStatus.Applying, interrupted.Status);
        Assert.Single(interrupted.Files);
        Assert.Equal(PatchApplyStatus.Applying, interrupted.Files[0].Status);
        Assert.Single(backups);
        Assert.Equal("old\n", backups[0].OriginalContent);
        Assert.Equal(CodeContextService.ComputeHash("old\n"), backups[0].OriginalContentHash);
    }

    [Fact]
    public async Task Apply_CancellationAfterFirstWritePersistsPartialRecoveryWithCancelledCallerToken()
    {
        await InitializeAsync();
        Write("src/One.cs", "one\n");
        Write("src/Two.cs", "two\n");
        var proposal = await SaveProposalAsync([
            Edit("src/One.cs", "one\n", "changed one\n"),
            Edit("src/Two.cs", "two\n", "changed two\n")
        ]);
        using var cancellation = new CancellationTokenSource();
        var runtime = new CancelOnFirstAppliedFileTaskRuntime(cancellation);
        var repository = new InterceptingApplyRepository(
            _applyRepository,
            (save, _, backups, token) =>
            {
                if (save == 2)
                {
                    Assert.Empty(backups);
                    Assert.False(token.IsCancellationRequested);
                }
            });
        var service = new PatchApplyService(
            _proposalRepository,
            repository,
            CreateValidator(),
            _reader,
            _approvals,
            runtime);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ApplyAsync(
            new PatchApplyRequest(proposal.Id, _workspace.Id, approval, decision),
            cancellation.Token);

        var restartedRepository = new SqlitePatchApplyRepository(Path.Combine(_root, "apply.db"));
        await restartedRepository.InitializeAsync();
        var persisted = await restartedRepository.GetApplyResultAsync(result.Id);
        var backups = await restartedRepository.ListBackupsAsync(result.Id);
        Assert.Equal(PatchApplyStatus.PartiallyApplied, result.Status);
        Assert.Equal("changed one\n", Read("src/One.cs"));
        Assert.Equal("two\n", Read("src/Two.cs"));
        Assert.NotNull(persisted);
        Assert.Equal(PatchApplyStatus.PartiallyApplied, persisted.Status);
        Assert.Equal(PatchApplyStatus.Applied, persisted.Files[0].Status);
        Assert.Equal(PatchApplyStatus.Cancelled, persisted.Files[1].Status);
        Assert.Equal(2, backups.Count);
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
        Assert.Equal(PatchApplyStatus.Applied, (await _applyRepository.GetApplyResultAsync(result.Id))?.Status);
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
    public async Task Apply_ParentChangedToReparseAfterBackupDoesNotEscapeWorkspace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "App.cs"), "outside\n");
        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var swappingReader = new AfterReadWorkspaceReader(_reader, 2, () =>
        {
            Directory.Move(PathFor("src"), PathFor("src-original"));
            CreateJunction(PathFor("src"), outside);
        });
        var service = new PatchApplyService(
            _proposalRepository,
            _applyRepository,
            new PatchApplyValidator(_proposalRepository, swappingReader, _resolver),
            swappingReader,
            _approvals);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        try
        {
            var result = await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval,
                decision));

            Assert.Equal(PatchApplyStatus.Failed, result.Status);
            Assert.Contains(PatchApplyFailureReason.PathOutsideWorkspace, result.FailureReasons);
            Assert.Equal("outside\n", File.ReadAllText(Path.Combine(outside, "App.cs")));
            Assert.Equal("old\n", File.ReadAllText(PathFor("src-original/App.cs")));
        }
        finally
        {
            if (Directory.Exists(PathFor("src")))
            {
                Directory.Delete(PathFor("src"));
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Apply_ExistingReparseParentIsRejectedWithoutChangingExternalFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await InitializeAsync();
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "App.cs"), "outside\n");
        CreateJunction(PathFor("linked"), outside);
        var proposal = await SaveProposalAsync([Edit("linked/App.cs", "outside\n", "changed\n")]);
        var service = CreateService();
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        try
        {
            var result = await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval,
                decision));

            Assert.Equal(PatchApplyStatus.Failed, result.Status);
            Assert.Contains(PatchApplyFailureReason.PathOutsideWorkspace, result.FailureReasons);
            Assert.Equal("outside\n", File.ReadAllText(Path.Combine(outside, "App.cs")));
        }
        finally
        {
            Directory.Delete(PathFor("linked"));
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_AddParentChangedToReparseAfterDryRunDoesNotEscapeWorkspace()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await InitializeAsync();
        Directory.CreateDirectory(PathFor("nested"));
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "sentinel.txt"), "outside\n");
        var proposal = await SaveProposalAsync([
            Edit("nested/deeper/New.cs", null, "created\n", PatchProposalFileChangeKind.Add)
        ]);
        var initialService = CreateService();
        var approval = await initialService.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);
        var swappingRepository = new AfterGetProposalRepository(_proposalRepository, 2, () =>
        {
            Directory.Move(PathFor("nested"), PathFor("nested-original"));
            CreateJunction(PathFor("nested"), outside);
        });
        var service = new PatchApplyService(
            swappingRepository,
            _applyRepository,
            new PatchApplyValidator(swappingRepository, _reader, _resolver),
            _reader,
            _approvals);

        try
        {
            var result = await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval,
                decision));

            Assert.Equal(PatchApplyStatus.Failed, result.Status);
            Assert.Contains(PatchApplyFailureReason.PathOutsideWorkspace, result.FailureReasons);
            Assert.Equal("outside\n", File.ReadAllText(Path.Combine(outside, "sentinel.txt")));
            Assert.False(File.Exists(Path.Combine(outside, "deeper", "New.cs")));
            Assert.Empty(Directory.EnumerateFileSystemEntries(PathFor("nested-original")));
        }
        finally
        {
            Directory.Delete(PathFor("nested"));
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public async Task Apply_FinalTargetReparseIsRejected()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        await InitializeAsync();
        Write("src/App.cs", "old\n");
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(outsideFile, "outside\n");

        var proposal = await SaveProposalAsync([Edit("src/App.cs", "old\n", "new\n")]);
        var swappingReader = new AfterReadWorkspaceReader(_reader, 2, () =>
        {
            File.Move(PathFor("src/App.cs"), PathFor("src/App-original.cs"));
            CreateJunction(PathFor("src/App.cs"), outside);
        });
        var service = new PatchApplyService(
            _proposalRepository,
            _applyRepository,
            new PatchApplyValidator(_proposalRepository, swappingReader, _resolver),
            swappingReader,
            _approvals);
        var approval = await service.RequestApplyApprovalAsync(proposal.Id, _workspace.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        try
        {
            var result = await service.ApplyAsync(new PatchApplyRequest(
                proposal.Id,
                _workspace.Id,
                approval,
                decision));

            Assert.Equal(PatchApplyStatus.Failed, result.Status);
            Assert.Contains(PatchApplyFailureReason.PathOutsideWorkspace, result.FailureReasons);
            Assert.Equal("outside\n", File.ReadAllText(outsideFile));
            Assert.Equal("old\n", File.ReadAllText(PathFor("src/App-original.cs")));
        }
        finally
        {
            Directory.Delete(PathFor("src/App.cs"));
            Directory.Delete(outside, recursive: true);
        }
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
        var restartedRepository = new SqlitePatchApplyRepository(Path.Combine(_root, "apply.db"));
        await restartedRepository.InitializeAsync();
        var persisted = await restartedRepository.GetApplyResultAsync(result.Id);
        Assert.NotNull(persisted);
        Assert.Equal(PatchApplyStatus.PartiallyApplied, persisted.Status);
        Assert.Equal(2, persisted.Files.Count);
        Assert.Equal(PatchApplyStatus.Applied, persisted.Files[0].Status);
        Assert.Equal(PatchApplyStatus.Failed, persisted.Files[1].Status);
        Assert.Equal(2, (await restartedRepository.ListBackupsAsync(result.Id)).Count);
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

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(junctionPath);
        startInfo.ArgumentList.Add(targetPath);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Windows junction test helper.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to create the disposable test junction: {process.StandardError.ReadToEnd()}");
        }
    }

    private sealed class AfterGetProposalRepository(
        IPatchProposalRepository inner,
        int triggerGet,
        Action afterGet) : IPatchProposalRepository
    {
        private int _getCount;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public Task CreateAsync(PatchProposal proposal, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(proposal, cancellationToken);

        public async Task<PatchProposal?> GetAsync(
            PatchProposalId proposalId,
            CancellationToken cancellationToken = default)
        {
            var proposal = await inner.GetAsync(proposalId, cancellationToken);
            if (Interlocked.Increment(ref _getCount) == triggerGet)
            {
                afterGet();
            }

            return proposal;
        }

        public Task<IReadOnlyList<PatchProposal>> ListRecentAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            inner.ListRecentAsync(limit, cancellationToken);

        public Task UpdateStatusAsync(
            PatchProposalId proposalId,
            PatchProposalStatus status,
            CancellationToken cancellationToken = default) =>
            inner.UpdateStatusAsync(proposalId, status, cancellationToken);
    }

    private sealed class AfterReadWorkspaceReader(
        IWorkspaceReader inner,
        int triggerRead,
        Action afterRead) : IWorkspaceReader
    {
        private int _readCount;

        public WorkspaceDescriptor GetWorkspace(WorkspaceId workspaceId) =>
            inner.GetWorkspace(workspaceId);

        public IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
            WorkspaceId workspaceId,
            string relativePath,
            int maxEntries,
            bool includeHidden,
            CancellationToken cancellationToken = default) =>
            inner.ListDirectory(workspaceId, relativePath, maxEntries, includeHidden, cancellationToken);

        public WorkspaceTextFile ReadTextFile(
            WorkspaceId workspaceId,
            string relativePath,
            int maxCharacters,
            CancellationToken cancellationToken = default)
        {
            var result = inner.ReadTextFile(workspaceId, relativePath, maxCharacters, cancellationToken);
            if (Interlocked.Increment(ref _readCount) == triggerRead)
            {
                afterRead();
            }

            return result;
        }

        public WorkspaceSearchResult SearchText(
            WorkspaceId workspaceId,
            string query,
            string relativeDirectory,
            string? filePattern,
            bool matchCase,
            int maxResults,
            CancellationToken cancellationToken = default) =>
            inner.SearchText(
                workspaceId,
                query,
                relativeDirectory,
                filePattern,
                matchCase,
                maxResults,
                cancellationToken);
    }

    private sealed class InterceptingApplyRepository(
        IPatchApplyRepository inner,
        Action<int, PatchApplyResult, IReadOnlyList<PatchApplyBackup>, CancellationToken> beforeSave)
        : IPatchApplyRepository
    {
        private int _saveCount;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            inner.InitializeAsync(cancellationToken);

        public async Task CreateApplyResultAsync(
            PatchApplyResult result,
            IReadOnlyList<PatchApplyBackup> backups,
            CancellationToken cancellationToken = default)
        {
            beforeSave(Interlocked.Increment(ref _saveCount), result, backups, cancellationToken);
            await inner.CreateApplyResultAsync(result, backups, cancellationToken);
        }

        public Task<PatchApplyResult?> GetApplyResultAsync(
            PatchApplyResultId resultId,
            CancellationToken cancellationToken = default) =>
            inner.GetApplyResultAsync(resultId, cancellationToken);

        public Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            inner.ListRecentApplyResultsAsync(limit, cancellationToken);

        public Task<IReadOnlyList<PatchApplyBackup>> ListBackupsAsync(
            PatchApplyResultId resultId,
            CancellationToken cancellationToken = default) =>
            inner.ListBackupsAsync(resultId, cancellationToken);

        public Task CreateRollbackResultAsync(
            PatchRollbackResult result,
            CancellationToken cancellationToken = default) =>
            inner.CreateRollbackResultAsync(result, cancellationToken);

        public Task<PatchRollbackResult?> GetRollbackResultAsync(
            PatchRollbackResultId resultId,
            CancellationToken cancellationToken = default) =>
            inner.GetRollbackResultAsync(resultId, cancellationToken);
    }

    private sealed class CancelOnFirstAppliedFileTaskRuntime(CancellationTokenSource cancellation)
        : ITaskRuntime
    {
        private readonly TaskRun _run = TaskRun.Create("Patch apply", "aeda-code");

        public ValueTask<TaskRun> StartTaskAsync(string title, CancellationToken cancellationToken) =>
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
            if (kind == TaskEventKind.PatchFileApplied)
            {
                cancellation.Cancel();
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask AttachArtifactAsync(
            TaskId taskId,
            TaskArtifact artifact,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CompleteTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask CancelTaskAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask FailTaskAsync(
            TaskId taskId,
            string safeErrorCode,
            CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask<TaskRunRecord?> GetTaskAsync(
            TaskId taskId,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<TaskRunRecord?>(null);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
