using Microsoft.Data.Sqlite;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Coding;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Coding;

public sealed class ValidationRunnerFoundationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.Tests",
        Guid.NewGuid().ToString("N"));
    private readonly WorkspaceRegistry _registry = new();
    private readonly WorkspaceDescriptor _workspace;
    private readonly FileSystemWorkspaceReader _reader;
    private readonly SqliteValidationRunRepository _repository;
    private readonly InMemoryApprovalCheckpointStore _approvals = new();
    private readonly FakeProcessRunner _runner = new();

    public ValidationRunnerFoundationTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "PersonalAI.slnx"), "solution");
        Directory.CreateDirectory(Path.Combine(_root, "PersonalAI.Tests"));
        File.WriteAllText(
            Path.Combine(_root, "PersonalAI.Tests", "PersonalAI.Tests.csproj"),
            "<Project />");
        _workspace = _registry.Register(_root, "Validation");
        var resolver = new WorkspacePathResolver(_registry);
        _reader = new FileSystemWorkspaceReader(
            _registry,
            resolver,
            new WorkspaceToolOptions());
        _repository = new SqliteValidationRunRepository(
            Path.Combine(_root, "validation.db"));
    }

    [Fact]
    public void Allowlist_AllowsKnownTemplatesAndRejectsUnknownOrUnsafeWorkingDirectory()
    {
        var allowlist = new ValidationCommandAllowlist();

        Assert.True(allowlist.TryCreateCommand(
            new ValidationRunRequest(_workspace.Id, "dotnet-test-personalai"),
            _workspace,
            out var testCommand,
            out _));
        Assert.Equal("dotnet", testCommand.Executable);
        Assert.Contains("test", testCommand.Arguments);

        Assert.True(allowlist.TryCreateCommand(
            new ValidationRunRequest(_workspace.Id, "dotnet-build-debug"),
            _workspace,
            out _,
            out _));
        Assert.True(allowlist.TryCreateCommand(
            new ValidationRunRequest(_workspace.Id, "dotnet-build-release"),
            _workspace,
            out _,
            out _));
        Assert.False(allowlist.TryCreateCommand(
            new ValidationRunRequest(_workspace.Id, "git-status"),
            _workspace,
            out _,
            out var unknownFailure));
        Assert.Equal(ValidationFailureReason.CommandNotAllowed, unknownFailure);
        Assert.False(allowlist.TryCreateCommand(
            new ValidationRunRequest(_workspace.Id, "dotnet-build-debug", ".."),
            _workspace,
            out _,
            out var outsideFailure));
        Assert.Equal(ValidationFailureReason.WorkingDirectoryOutsideWorkspace, outsideFailure);
    }

    [Fact]
    public async Task Approval_IsRequiredDeniedControlledAndScopeIsolated()
    {
        await _repository.InitializeAsync();
        _runner.Next = Success("ok");
        var service = CreateService();
        var run = await service.CreateRunAsync(new ValidationRunRequest(
            _workspace.Id,
            "dotnet-build-debug"));
        var approval = await service.RequestApprovalAsync(run.Id);
        var denied = await _approvals.DecideAsync(approval, ApprovalDecisionKind.Deny);
        var wrongApproval = ApprovalRequest.Create(
            new ApprovalScope(PersonalAI.Core.Tasks.TaskId.NewId(), ApprovalKind.ValidationRun, "validation-run:other"),
            "Wrong",
            "Wrong");
        var allowed = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var deniedResult = await service.ExecuteAsync(run.Id, approval, denied);
        var missingResult = await service.ExecuteAsync(run.Id, wrongApproval, allowed);

        Assert.Equal(ValidationRunStatus.Rejected, deniedResult.Status);
        Assert.Contains(ValidationFailureReason.ApprovalDenied, deniedResult.FailureReasons);
        Assert.Equal(ValidationRunStatus.Blocked, missingResult.Status);
        Assert.Contains(ValidationFailureReason.ApprovalMissing, missingResult.FailureReasons);
        Assert.Equal(0, _runner.RunCount);
    }

    [Fact]
    public async Task ApprovedRun_ExecutesAndPersistsSuccess()
    {
        await _repository.InitializeAsync();
        _runner.Next = Success("build ok");
        var service = CreateService();
        var run = await service.CreateRunAsync(new ValidationRunRequest(
            _workspace.Id,
            "dotnet-build-debug",
            ProposalId: PatchProposalId.NewId(),
            ApplyResultId: PatchApplyResultId.NewId()));
        var approval = await service.RequestApprovalAsync(run.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ExecuteAsync(run.Id, approval, decision);
        var loaded = await service.GetRunAsync(run.Id);
        var recent = await service.ListRecentAsync(_workspace.Id, run.ProposalId);

        Assert.Equal(ValidationRunStatus.Succeeded, result.Status);
        Assert.Equal(0, result.CommandResult!.ExitCode);
        Assert.Equal("build ok", result.CommandResult.Stdout.Text);
        Assert.NotNull(loaded);
        Assert.Single(recent);
        Assert.Equal(1, _runner.RunCount);
    }

    [Theory]
    [InlineData(1, false, false, false, ValidationRunStatus.Failed)]
    [InlineData(null, true, false, false, ValidationRunStatus.TimedOut)]
    [InlineData(null, false, true, false, ValidationRunStatus.Cancelled)]
    [InlineData(null, false, false, true, ValidationRunStatus.Failed)]
    public async Task ProcessResults_MapToSafeStatuses(
        int? exitCode,
        bool timedOut,
        bool cancelled,
        bool startFailed,
        ValidationRunStatus expected)
    {
        await _repository.InitializeAsync();
        _runner.Next = new ControlledProcessResult(
            exitCode,
            timedOut,
            cancelled,
            startFailed,
            "out",
            "err",
            false,
            false,
            TimeSpan.FromMilliseconds(5));
        var service = CreateService();
        var run = await service.CreateRunAsync(new ValidationRunRequest(_workspace.Id, "dotnet-build-debug"));
        var approval = await service.RequestApprovalAsync(run.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ExecuteAsync(run.Id, approval, decision);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Output_IsRedactedAndTruncatedBeforePersistence()
    {
        await _repository.InitializeAsync();
        var raw = "sk-secret123 Bearer abc.def secret-ref:provider/key " +
            new string('x', 30_000);
        _runner.Next = Success(raw);
        var service = CreateService();
        var run = await service.CreateRunAsync(new ValidationRunRequest(_workspace.Id, "dotnet-build-debug"));
        var approval = await service.RequestApprovalAsync(run.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ExecuteAsync(run.Id, approval, decision);

        Assert.Equal(ValidationRunStatus.Failed, result.Status);
        Assert.Contains(ValidationFailureReason.OutputLimitExceeded, result.FailureReasons);
        Assert.DoesNotContain("sk-secret123", result.CommandResult!.Stdout.Text);
        Assert.DoesNotContain("abc.def", result.CommandResult.Stdout.Text);
        Assert.True(result.CommandResult.Stdout.IsTruncated);
    }

    [Fact]
    public async Task BlockedCommand_DoesNotExecute()
    {
        await _repository.InitializeAsync();
        var service = CreateService();
        var run = await service.CreateRunAsync(new ValidationRunRequest(
            _workspace.Id,
            "cmd.exe"));
        var approval = await service.RequestApprovalAsync(run.Id);
        var decision = await _approvals.DecideAsync(approval, ApprovalDecisionKind.AllowOnce);

        var result = await service.ExecuteAsync(run.Id, approval, decision);

        Assert.Equal(ValidationRunStatus.Blocked, result.Status);
        Assert.Contains(ValidationFailureReason.CommandNotAllowed, result.FailureReasons);
        Assert.Equal(0, _runner.RunCount);
    }

    [Fact]
    public async Task Repository_IsIdempotentAndListsBoundedResults()
    {
        await _repository.InitializeAsync();
        await _repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < 3; index++)
        {
            await _repository.CreateAsync(new ValidationRun(
                ValidationRunId.NewId(),
                _workspace.Id,
                "dotnet-build-debug",
                ".",
                ValidationRunStatus.Created,
                index == 0 ? new PatchProposalId(Guid.NewGuid()) : null,
                null,
                null,
                [],
                now,
                now));
        }

        var listed = await _repository.ListRecentAsync(_workspace.Id, limit: 2);

        Assert.Equal(2, listed.Count);
    }

    [Fact]
    public void Capabilities_ExposeControlledValidationAndKeepShellGitUnavailable()
    {
        var configured = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasControlledValidation: true);
        var unconfigured = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true);

        Assert.True(configured.GetStatus(BackendCapability.ControlledValidation).IsAvailable);
        Assert.True(configured.GetStatus(BackendCapability.TestExecution).IsAvailable);
        Assert.False(unconfigured.GetStatus(BackendCapability.ControlledValidation).IsAvailable);
        Assert.False(configured.GetStatus(BackendCapability.ShellExecution).IsAvailable);
        Assert.False(configured.GetStatus(BackendCapability.GitMutation).IsAvailable);
    }

    private ValidationRunnerService CreateService() =>
        new(
            _repository,
            new ValidationCommandAllowlist(),
            _runner,
            _reader,
            _approvals);

    private static ControlledProcessResult Success(string stdout) =>
        new(
            0,
            TimedOut: false,
            Cancelled: false,
            StartFailed: false,
            stdout,
            string.Empty,
            StdoutTruncated: false,
            StderrTruncated: false,
            TimeSpan.FromMilliseconds(10));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeProcessRunner : IControlledProcessRunner
    {
        public ControlledProcessResult Next { get; set; } = Success(string.Empty);

        public int RunCount { get; private set; }

        public Task<ControlledProcessResult> RunAsync(
            ControlledProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            RunCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Next);
        }
    }
}
