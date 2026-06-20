using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Coding;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Coding;

public sealed class CodingProposalFoundationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CodeContext_ReadsRegisteredWorkspaceFileAndHashesContent()
    {
        var (workspace, service) = CreateContextService();
        Write("src/App.cs", "class App {}\n");

        var pack = await service.LoadFilesAsync(workspace.Id, ["src/App.cs"]);

        var file = Assert.Single(pack.Files);
        Assert.Equal("src/App.cs", file.RelativePath);
        Assert.Equal(CodeContextService.ComputeHash("class App {}\n"), file.ContentHash);
        Assert.Empty(pack.SkippedSafeReasons);
    }

    [Fact]
    public async Task CodeContext_RejectsTraversalAndSkipsUnsupportedFiles()
    {
        var (workspace, service) = CreateContextService();
        Write("image.png", "\0binary");

        var pack = await service.LoadFilesAsync(
            workspace.Id,
            ["../outside.txt", "image.png"]);

        Assert.Empty(pack.Files);
        Assert.Contains("path_outside_workspace", pack.SkippedSafeReasons);
        Assert.Contains("unsupported_file_type", pack.SkippedSafeReasons);
    }

    [Fact]
    public async Task CodeContext_SearchIsBounded()
    {
        var (workspace, service) = CreateContextService();
        Write("src/A.cs", "needle\n");

        var pack = await service.SearchAsync(new CodeContextSearchRequest(
            workspace.Id,
            "needle",
            ".",
            "*.cs",
            MaxResults: 1));

        Assert.Single(pack.SearchMatches);
        Assert.Equal("src/A.cs", pack.SearchMatches[0].RelativePath);
    }

    [Fact]
    public async Task CodeContext_HonorsCancellation()
    {
        var (workspace, service) = CreateContextService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.LoadFilesAsync(workspace.Id, ["x.cs"], cancellationToken: cts.Token));
    }

    [Fact]
    public void DiffBuilder_CreatesReplacementInsertionDeletionAndNoOpDiffs()
    {
        var builder = new UnifiedDiffBuilder();

        var replace = builder.BuildFileDiff(new PatchProposalFileEdit(
            "a.txt",
            "one\ntwo\n",
            "one\nthree\n"));
        var insert = builder.BuildFileDiff(new PatchProposalFileEdit(
            "b.txt",
            "one\n",
            "one\ntwo\n"));
        var delete = builder.BuildFileDiff(new PatchProposalFileEdit(
            "c.txt",
            "one\ntwo\n",
            "one\n"));
        var noop = builder.BuildFileDiff(new PatchProposalFileEdit(
            "d.txt",
            "same\n",
            "same\n"));

        Assert.Contains("-two", replace.UnifiedDiff);
        Assert.Contains("+three", replace.UnifiedDiff);
        Assert.Contains("+two", insert.UnifiedDiff);
        Assert.Contains("-two", delete.UnifiedDiff);
        Assert.Equal(PatchProposalFileChangeKind.NoOp, noop.ChangeKind);
    }

    [Fact]
    public void DiffBuilder_RejectsUnsafeBinaryAndLargeDiffs()
    {
        var builder = new UnifiedDiffBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.BuildFileDiff(
            new PatchProposalFileEdit("../x.txt", "a", "b")));
        Assert.Throws<InvalidOperationException>(() => builder.BuildFileDiff(
            new PatchProposalFileEdit("x.txt", "a", "\0b")));
        Assert.Throws<InvalidOperationException>(() => builder.BuildFileDiff(
            new PatchProposalFileEdit("x.txt", "", new string('a', 1000)),
            maxDiffCharacters: 10));
    }

    [Theory]
    [InlineData("docs/readme.md", "old", "new", PatchProposalRisk.Low)]
    [InlineData("PersonalAI.Core/PersonalAI.Core.csproj", "old", "new", PatchProposalRisk.Medium)]
    [InlineData("PersonalAI.Core/Providers/Routing.cs", "old", "new", PatchProposalRisk.High)]
    [InlineData("../secret.txt", "old", "new", PatchProposalRisk.Blocked)]
    [InlineData("src/a.cs", "old", "sk-secret", PatchProposalRisk.Blocked)]
    public void RiskClassifier_ClassifiesConservatively(
        string path,
        string original,
        string proposed,
        PatchProposalRisk expected)
    {
        var file = path.Contains("..", StringComparison.Ordinal)
            ? new PatchProposalFile(
                path,
                PatchProposalFileChangeKind.Modify,
                original,
                proposed,
                string.Empty,
                string.Empty,
                string.Empty,
                [])
            : new UnifiedDiffBuilder().BuildFileDiff(
                new PatchProposalFileEdit(path, original, proposed));

        var (risk, reasons) = new PatchRiskClassifier().Classify([file]);

        Assert.Equal(expected, risk);
        Assert.NotEmpty(reasons);
    }

    [Fact]
    public void ValidationPlan_SuggestsCommandsButDoesNotExecute()
    {
        var file = new UnifiedDiffBuilder().BuildFileDiff(
            new PatchProposalFileEdit("PersonalAI.Tests/Coding/FooTests.cs", "old", "new"));

        var plan = new ValidationPlanService().CreatePlan([file]);

        Assert.Contains(plan.SuggestedCommands, item => item.Command.Contains("dotnet test"));
        Assert.Contains(plan.SuggestedCommands, item => item.Command.Contains("dotnet build"));
        Assert.DoesNotContain(plan.SuggestedCommands, item => item.Command.Contains("git reset"));
    }

    [Fact]
    public async Task Repository_IsIdempotentAndPersistsProposal()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        await repository.InitializeAsync();
        var proposal = CreateProposal();

        await repository.CreateAsync(proposal);
        var loaded = await repository.GetAsync(proposal.Id);
        var recent = await repository.ListRecentAsync(10);
        await repository.UpdateStatusAsync(proposal.Id, PatchProposalStatus.Rejected);
        var rejected = await repository.GetAsync(proposal.Id);

        Assert.NotNull(loaded);
        Assert.Single(recent);
        Assert.Equal(PatchProposalStatus.Rejected, rejected!.Status);
    }

    [Fact]
    public async Task ProposalService_CreatesProposalAndApprovalWithoutWorkspaceWrite()
    {
        var registry = new WorkspaceRegistry();
        Directory.CreateDirectory(_root);
        var workspace = registry.Register(_root, "Test");
        Write("src/App.cs", "old\n");
        var before = File.ReadAllText(Path.Combine(_root, "src", "App.cs"));
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var approvals = new InMemoryApprovalCheckpointStore();
        var service = new PatchProposalService(
            repository,
            new UnifiedDiffBuilder(),
            new PatchRiskClassifier(),
            new ValidationPlanService(),
            CreateReader(registry),
            approvals);

        var proposal = await service.CreateProposalAsync(new PatchProposalCreateRequest(
            workspace.Id,
            "Change app",
            "Change text",
            [new PatchProposalFileEdit("src/App.cs", "old\n", "new\n")],
            []));
        var approval = await service.RequestApprovalAsync(proposal.Id);

        Assert.Equal(PatchProposalStatus.ReadyForReview, proposal.Status);
        Assert.Contains("+new", proposal.Files[0].UnifiedDiff);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "src", "App.cs")));
        Assert.Equal(ApprovalKind.ApproveFutureApply, approval.Scope.Kind);
    }

    [Fact]
    public async Task Planner_CreatesDeterministicPlan()
    {
        var workspaceId = WorkspaceId.NewId();
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "content", "hash", "utf-8", 7, false, false)],
            [],
            [],
            false);

        var plan = await new CodeChangePlanningService(new ValidationPlanService())
            .CreatePlanAsync(CodeChangeRequest.Create(workspaceId, "Update app"), context);

        Assert.Contains("src/App.cs", plan.AffectedRelativePaths);
        Assert.NotEmpty(plan.Steps);
        Assert.NotEmpty(plan.ValidationPlan.SuggestedCommands);
    }

    [Fact]
    public void Capabilities_ExposeProposalAndDeferApplyAndTests()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
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
            hasPatchReview: true);

        Assert.True(registry.GetStatus(BackendCapability.PatchProposal).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.PatchApply).IsAvailable);
        Assert.Equal("patch_apply_unavailable", registry.GetStatus(BackendCapability.PatchApply).SafeReasonCode);
        Assert.False(registry.GetStatus(BackendCapability.TestExecution).IsAvailable);
    }

    private (WorkspaceDescriptor Workspace, CodeContextService Service) CreateContextService()
    {
        Directory.CreateDirectory(_root);
        var registry = new WorkspaceRegistry();
        var workspace = registry.Register(_root, "Test");
        return (workspace, new CodeContextService(CreateReader(registry)));
    }

    private IWorkspaceReader CreateReader(WorkspaceRegistry? registry = null)
    {
        registry ??= new WorkspaceRegistry();
        if (registry.List().Count == 0)
        {
            registry.Register(_root, "Test");
        }

        return new FileSystemWorkspaceReader(
            registry,
            new WorkspacePathResolver(registry),
            new WorkspaceToolOptions());
    }

    private SqlitePatchProposalRepository CreateRepository() =>
        new(Path.Combine(_root, "proposals.db"));

    private PatchProposal CreateProposal()
    {
        var workspaceId = WorkspaceId.NewId();
        var file = new UnifiedDiffBuilder().BuildFileDiff(
            new PatchProposalFileEdit("src/App.cs", "old", "new"));
        var now = DateTimeOffset.UtcNow;
        return new PatchProposal(
            PatchProposalId.NewId(),
            workspaceId,
            "Title",
            "Summary",
            PatchProposalStatus.ReadyForReview,
            PatchProposalRisk.Low,
            ["small_text_change"],
            [file],
            [],
            new ValidationPlanService().CreatePlan([file]),
            now,
            now);
    }

    private void Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
