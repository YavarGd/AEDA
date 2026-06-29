using System.Text.Json;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;
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
    public async Task ProposalService_CapturesFullWorkspaceBaselineIgnoringModelOriginal()
    {
        var registry = new WorkspaceRegistry();
        Directory.CreateDirectory(_root);
        var workspace = registry.Register(_root, "Test");
        var original = string.Join(
            "\n",
            Enumerable.Range(0, 2_000).Select(index => $"line {index}")) + "\n";
        var proposed = original.Replace("line 1999", "line 1999 // docs", StringComparison.Ordinal);
        Write("src/App.cs", original);
        var before = File.ReadAllText(Path.Combine(_root, "src", "App.cs"));
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var reader = CreateReader(registry);
        var service = new PatchProposalService(
            repository,
            new UnifiedDiffBuilder(),
            new PatchRiskClassifier(),
            new ValidationPlanService(),
            reader);

        var proposal = await service.CreateProposalAsync(new PatchProposalCreateRequest(
            workspace.Id,
            "Change app",
            "Change text",
            [new PatchProposalFileEdit("src/App.cs", "line 0\nline 1\n", proposed)],
            []));
        var plan = await new PatchApplyValidator(
            repository,
            reader,
            new WorkspacePathResolver(registry)).DryRunAsync(
                new PatchApplyRequest(proposal.Id, workspace.Id));

        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "src", "App.cs")));
        Assert.Equal(PatchApplyStatus.DryRunPassed, plan.Status);
        Assert.DoesNotContain(PatchApplyFailureReason.StaleOriginalContent, plan.FailureReasons);
        Assert.Equal(original, proposal.Files[0].OriginalContent);
        Assert.Equal(CodeContextService.ComputeHash(original), proposal.Files[0].OriginalContentHash);
    }

    [Fact]
    public async Task ProposalService_RealChangeAfterProposalStillFailsStale()
    {
        var registry = new WorkspaceRegistry();
        Directory.CreateDirectory(_root);
        var workspace = registry.Register(_root, "Test");
        Write("src/App.cs", "old\n");
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var reader = CreateReader(registry);
        var service = new PatchProposalService(
            repository,
            new UnifiedDiffBuilder(),
            new PatchRiskClassifier(),
            new ValidationPlanService(),
            reader);
        var proposal = await service.CreateProposalAsync(new PatchProposalCreateRequest(
            workspace.Id,
            "Change app",
            "Change text",
            [new PatchProposalFileEdit("src/App.cs", null, "new\n")],
            []));

        Write("src/App.cs", "user changed\n");
        var plan = await new PatchApplyValidator(
            repository,
            reader,
            new WorkspacePathResolver(registry)).DryRunAsync(
                new PatchApplyRequest(proposal.Id, workspace.Id));

        Assert.Equal(PatchApplyStatus.DryRunFailed, plan.Status);
        Assert.Contains(PatchApplyFailureReason.StaleOriginalContent, plan.FailureReasons);
    }

    [Theory]
    [InlineData("lf")]
    [InlineData("crlf")]
    [InlineData("utf8bom")]
    public async Task ProposalService_BaselineMatchesDryRunForSupportedTextRepresentations(
        string representation)
    {
        var registry = new WorkspaceRegistry();
        Directory.CreateDirectory(_root);
        var workspace = registry.Register(_root, "Test");
        var content = representation == "crlf"
            ? "class App\r\n{\r\n}\r\n"
            : "class App\n{\n}\n";
        var path = Path.Combine(_root, "src", "App.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (representation == "utf8bom")
        {
            File.WriteAllBytes(
                path,
                System.Text.Encoding.UTF8.GetPreamble()
                    .Concat(System.Text.Encoding.UTF8.GetBytes(content))
                    .ToArray());
        }
        else
        {
            File.WriteAllText(path, content);
        }

        var repository = CreateRepository();
        await repository.InitializeAsync();
        var reader = CreateReader(registry);
        var service = new PatchProposalService(
            repository,
            new UnifiedDiffBuilder(),
            new PatchRiskClassifier(),
            new ValidationPlanService(),
            reader);
        var proposal = await service.CreateProposalAsync(new PatchProposalCreateRequest(
            workspace.Id,
            "Change app",
            "Change text",
            [new PatchProposalFileEdit("src/App.cs", "model original ignored", content + "// docs\n")],
            []));

        var plan = await new PatchApplyValidator(
            repository,
            reader,
            new WorkspacePathResolver(registry)).DryRunAsync(
                new PatchApplyRequest(proposal.Id, workspace.Id));

        Assert.Equal(PatchApplyStatus.DryRunPassed, plan.Status);
        Assert.DoesNotContain(PatchApplyFailureReason.StaleOriginalContent, plan.FailureReasons);
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
    public async Task DraftService_UsesCodeProviderAndBuildsSafeFileEdits()
    {
        var workspaceId = WorkspaceId.NewId();
        var provider = new FakeChatProvider(
            """
            {"title":"Add docs","summary":"Adds XML docs.","changes":[{"relativePath":"src/App.cs","proposedContent":"/// <summary>Docs.</summary>\nclass App {}\n"}]}
            """);
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "class App {}\n", "hash", "utf-8", 13, false, false)],
            [],
            [],
            false);

        var draft = await service.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Add XML docs to this helper method."),
            context));

        var edit = Assert.Single(draft.FileEdits);
        Assert.Equal("src/App.cs", edit.RelativePath);
        Assert.Equal("class App {}\n", edit.OriginalContent);
        Assert.Contains("summary", edit.ProposedContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("qwen2.5-coder:7b", provider.LastRequest?.Model);
        Assert.Contains("No markdown", provider.LastRequest?.Messages[1].Content, StringComparison.Ordinal);
        Assert.Contains("\"changes\"", provider.LastRequest?.Messages[1].Content, StringComparison.Ordinal);
        Assert.Contains("src/App.cs", provider.LastRequest?.Messages[1].Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"title":"Add docs","summary":"Adds XML docs.","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}""")]
    [InlineData("```json\n{\"title\":\"Add docs\",\"summary\":\"Adds XML docs.\",\"changes\":[{\"relativePath\":\"src/App.cs\",\"proposedContent\":\"new\"}]}\n```")]
    [InlineData("Here is the JSON:\n{\"title\":\"Add docs\",\"summary\":\"Adds XML docs.\",\"changes\":[{\"relativePath\":\"src/App.cs\",\"proposedContent\":\"new\"}]}\nDone.")]
    public async Task DraftService_AcceptsValidJsonFencesAndSurroundingProse(string output)
    {
        var workspaceId = WorkspaceId.NewId();
        var provider = new FakeChatProvider(output);
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "old", "hash", "utf-8", 3, false, false)],
            [],
            [],
            false);

        var draft = await service.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Change app."),
            context));

        Assert.Single(draft.FileEdits);
        Assert.Equal(1, provider.RequestCount);
    }

    [Theory]
    [InlineData("src/Root.cs", "src/Root.cs")]
    [InlineData("App.cs", "PersonalAI.Desktop.WinUI/ViewModels/App.cs")]
    [InlineData("ViewModels/App.cs", "PersonalAI.Desktop.WinUI/ViewModels/App.cs")]
    public async Task DraftService_AcceptsExactOrUniqueContextSuffixTargets(
        string modelTarget,
        string expectedCanonicalPath)
    {
        var workspaceId = WorkspaceId.NewId();
        var provider = new FakeChatProvider(DraftJson(modelTarget));
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [
                new CodeContextFile(workspaceId, "src/Root.cs", "old root", "hash", "utf-8", 8, false, false),
                new CodeContextFile(workspaceId, "PersonalAI.Desktop.WinUI/ViewModels/App.cs", "old vm", "hash", "utf-8", 6, false, false)
            ],
            [],
            [],
            false);

        var draft = await service.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Add XML docs."),
            context));

        var edit = Assert.Single(draft.FileEdits);
        Assert.Equal(expectedCanonicalPath, edit.RelativePath);
        Assert.Equal(
            expectedCanonicalPath == "src/Root.cs" ? "old root" : "old vm",
            edit.OriginalContent);
    }

    [Theory]
    [InlineData("App.cs")]
    [InlineData("Features/App.cs")]
    [InlineData("Missing.cs")]
    [InlineData("/src/App.cs")]
    [InlineData("../src/App.cs")]
    [InlineData("C:/safe/src/App.cs")]
    [InlineData("//server/share/App.cs")]
    [InlineData("src/App.cs:Zone.Identifier")]
    public async Task DraftService_RejectsUnsafeAmbiguousOrOutOfContextTargets(string modelTarget)
    {
        var workspaceId = WorkspaceId.NewId();
        var provider = new FakeChatProvider(DraftJson(modelTarget));
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [
                new CodeContextFile(workspaceId, "src/App.cs", "old", "hash", "utf-8", 3, false, false),
                new CodeContextFile(workspaceId, "tests/App.cs", "old", "hash", "utf-8", 3, false, false),
                new CodeContextFile(workspaceId, "src/Features/App.cs", "old", "hash", "utf-8", 3, false, false),
                new CodeContextFile(workspaceId, "tests/Features/App.cs", "old", "hash", "utf-8", 3, false, false)
            ],
            [],
            [],
            false);

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Add XML docs."),
                context)));

        Assert.Equal(AedaCodeProposalCreationFailureReason.UnsafeFileTarget, failure.Failure.Reason);
        Assert.Equal("unsafe_file_target", failure.Failure.SafeCode);
        Assert.DoesNotContain(modelTarget, failure.Failure.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("path", "proposedContent")]
    [InlineData("file", "replacement")]
    [InlineData("filePath", "newText")]
    public async Task DraftService_NormalizesCommonSchemaAliases(
        string pathField,
        string contentField)
    {
        var workspaceId = WorkspaceId.NewId();
        var output =
            $$"""
            {"title":"Add docs","summary":"Adds XML docs.","changes":[{ {{JsonSerializer.Serialize(pathField)}}:"App.cs",{{JsonSerializer.Serialize(contentField)}}:"new"}]}
            """;
        var provider = new FakeChatProvider(output);
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "old", "hash", "utf-8", 3, false, false)],
            [],
            [],
            false);

        var draft = await service.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Add XML docs."),
            context));

        var edit = Assert.Single(draft.FileEdits);
        Assert.Equal("src/App.cs", edit.RelativePath);
        Assert.Equal("new", edit.ProposedContent);
    }

    [Theory]
    [InlineData("""{"summary":"s","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}""", "missing_title")]
    [InlineData("""{"title":"t","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}""", "missing_summary")]
    [InlineData("""{"title":"t","summary":"s"}""", "missing_changes")]
    [InlineData("""{"title":"t","summary":"s","changes":"bad"}""", "changes_wrong_type")]
    [InlineData("""{"title":"t","summary":"s","changes":[]}""", "empty_changes")]
    [InlineData("""{"title":"t","summary":"s","changes":["bad"]}""", "change_wrong_type")]
    [InlineData("""{"title":"t","summary":"s","changes":[{"proposedContent":"new"}]}""", "change_missing_path")]
    [InlineData("""{"title":"t","summary":"s","changes":[{"relativePath":"src/App.cs"}]}""", "change_missing_replacement")]
    [InlineData("""{"title":"t","summary":"s","changes":[{"relativePath":"src/App.cs","path":"src/App.cs","proposedContent":"new"}]}""", "ambiguous_change_field")]
    [InlineData("""{"title":"t","summary":"s","changes":[{"relativePath":"src/App.cs","proposedContent":"new","note":"bad"}]}""", "extra_keys")]
    public async Task DraftService_InvalidSchemaIncludesSafeIssueCode(
        string output,
        string expectedIssueCode)
    {
        var workspaceId = WorkspaceId.NewId();
        var provider = new FakeChatProvider(output, output);
        var service = CreateDraftService(provider);
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "old", "hash", "utf-8", 3, false, false)],
            [],
            [],
            false);

        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            service.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Add XML docs."),
                context)));

        Assert.Equal("invalid_model_schema", failure.Failure.SafeCode);
        Assert.Equal(expectedIssueCode, failure.Failure.SchemaIssueCode);
        Assert.True(failure.Failure.RetryAttempted);
        Assert.DoesNotContain(output, failure.Failure.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DraftService_RejectsInvalidOrOutOfContextModelOutput()
    {
        var workspaceId = WorkspaceId.NewId();
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "class App {}\n", "hash", "utf-8", 13, false, false)],
            [],
            [],
            false);
        var invalidJson = CreateDraftService(new FakeChatProvider("not json", "still not json"));
        var malformedJson = CreateDraftService(new FakeChatProvider("""{"title":"Bad","summary":""", """{"title":"Bad","summary":"""));
        var multipleObjects = CreateDraftService(new FakeChatProvider(
            """{"title":"A","summary":"A","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]} {"title":"B","summary":"B","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}""",
            """{"title":"A","summary":"A","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]} {"title":"B","summary":"B","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}"""));
        var schemaMismatch = CreateDraftService(new FakeChatProvider("""{"title":"Bad","summary":"Bad"}""", """{"title":"Bad","summary":"Bad"}"""));
        var wrongType = CreateDraftService(new FakeChatProvider("""{"title":"Bad","summary":"Bad","changes":"nope"}""", """{"title":"Bad","summary":"Bad","changes":"nope"}"""));
        var outsideContext = CreateDraftService(new FakeChatProvider(
            """{"title":"Bad","summary":"Bad","changes":[{"relativePath":"src/Other.cs","proposedContent":"new"}]}"""));

        var invalid = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            invalidJson.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var malformed = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            malformedJson.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var multiple = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            multipleObjects.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var schema = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            schemaMismatch.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var wrong = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            wrongType.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var unsafeTarget = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            outsideContext.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));

        Assert.Equal(AedaCodeProposalCreationFailureReason.InvalidModelJson, invalid.Failure.Reason);
        Assert.Equal("invalid_model_json", invalid.Failure.SafeCode);
        Assert.Equal("invalid_model_json", malformed.Failure.SafeCode);
        Assert.Equal("invalid_model_json", multiple.Failure.SafeCode);
        Assert.Equal(AedaCodeProposalCreationFailureReason.InvalidModelSchema, schema.Failure.Reason);
        Assert.Equal("invalid_model_schema", schema.Failure.SafeCode);
        Assert.Equal("invalid_model_schema", wrong.Failure.SafeCode);
        Assert.Equal(AedaCodeProposalCreationFailureReason.UnsafeFileTarget, unsafeTarget.Failure.Reason);
        Assert.Equal("unsafe_file_target", unsafeTarget.Failure.SafeCode);
        Assert.DoesNotContain("not json", invalid.Failure.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, invalidJson.ChatProvider.RequestCount);
        Assert.Equal(2, schemaMismatch.ChatProvider.RequestCount);
        Assert.Equal(1, outsideContext.ChatProvider.RequestCount);
    }

    [Fact]
    public async Task DraftService_RetriesInvalidJsonOrSchemaOnce()
    {
        var workspaceId = WorkspaceId.NewId();
        var valid = """{"title":"Fixed","summary":"Fixed JSON.","changes":[{"relativePath":"src/App.cs","proposedContent":"new"}]}""";
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "old", "hash", "utf-8", 3, false, false)],
            [],
            [],
            false);
        var invalidThenValidProvider = new FakeChatProvider("not json", valid);
        var schemaThenValidProvider = new FakeChatProvider("""{"title":"Bad","summary":"Bad"}""", valid);
        var invalidStillProvider = new FakeChatProvider("not json", "still not json", valid);
        var invalidThenValid = CreateDraftService(invalidThenValidProvider);
        var schemaThenValid = CreateDraftService(schemaThenValidProvider);
        var invalidStill = CreateDraftService(invalidStillProvider);

        var draft = await invalidThenValid.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Change app."),
            context));
        var schemaDraft = await schemaThenValid.CreateDraftAsync(new CodeProposalDraftRequest(
            CodeChangeRequest.Create(workspaceId, "Change app."),
            context));
        var failure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            invalidStill.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));

        Assert.Single(draft.FileEdits);
        Assert.Single(schemaDraft.FileEdits);
        Assert.Equal(2, invalidThenValidProvider.RequestCount);
        Assert.Equal(2, schemaThenValidProvider.RequestCount);
        Assert.Equal(2, invalidStillProvider.RequestCount);
        Assert.Equal("invalid_model_json", failure.Failure.SafeCode);
    }

    [Fact]
    public async Task DraftService_MapsProviderFailuresAndCancellationSafely()
    {
        var workspaceId = WorkspaceId.NewId();
        var context = new CodeContextPack(
            workspaceId,
            [new CodeContextFile(workspaceId, "src/App.cs", "class App {}\n", "hash", "utf-8", 13, false, false)],
            [],
            [],
            false);
        var providerUnavailable = CreateDraftService(
            new FakeChatProvider("{}"),
            includeCodeCapability: false);
        var providerRejected = CreateDraftService(
            new FakeChatProvider("{}"),
            isRemote: true,
            configureSettings: settings => settings with
            {
                LocalOnlyMode = false,
                AllowRemoteChat = true,
                AllowRemoteWithWorkspaceContext = false
            });
        var cancelled = CreateDraftService(new FakeChatProvider(null, new OperationCanceledException()));
        var timedOut = CreateDraftService(new FakeChatProvider(null, new TimeoutException("raw provider timeout")));

        var unavailable = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            providerUnavailable.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var rejected = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            providerRejected.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var cancelledFailure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            cancelled.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));
        var timeoutFailure = await Assert.ThrowsAsync<AedaCodeProposalCreationException>(() =>
            timedOut.CreateDraftAsync(new CodeProposalDraftRequest(
                CodeChangeRequest.Create(workspaceId, "Change app."),
                context)));

        Assert.Equal("provider_unavailable", unavailable.Failure.SafeCode);
        Assert.Equal("provider_rejected_by_policy", rejected.Failure.SafeCode);
        Assert.Equal("model_cancelled", cancelledFailure.Failure.SafeCode);
        Assert.Equal("model_timeout", timeoutFailure.Failure.SafeCode);
        Assert.DoesNotContain("raw provider timeout", timeoutFailure.Failure.UserMessage, StringComparison.OrdinalIgnoreCase);
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

    private sealed record DraftServiceFixture(
        CodeProposalDraftService Service,
        FakeChatProvider ChatProvider)
    {
        public Task<CodeProposalDraft> CreateDraftAsync(
            CodeProposalDraftRequest request,
            IProgress<AedaCodeProposalCreationProgress>? progress = null,
            CancellationToken cancellationToken = default) =>
            Service.CreateDraftAsync(request, progress, cancellationToken);
    }

    private static DraftServiceFixture CreateDraftService(
        FakeChatProvider provider,
        bool includeCodeCapability = true,
        bool isRemote = false,
        Func<ProviderRoutingSettings, ProviderRoutingSettings>? configureSettings = null)
    {
        var providerId = new ProviderId("fake");
        var capabilities = ModelCapability.Chat | ModelCapability.StreamingChat |
            (isRemote ? ModelCapability.Remote : ModelCapability.LocalOnly);
        if (includeCodeCapability)
        {
            capabilities |= ModelCapability.Code;
        }

        var model = new ModelDescriptor(
            providerId,
            new ModelId("qwen2.5-coder:7b"),
            capabilities,
            new ModelSafetyProfile(
                IsLocalOnly: !isRemote,
                IsRemote: isRemote,
                AllowsWorkspaceContext: !isRemote,
                AllowsMemoryContext: false,
                AllowsScreenshots: false,
                AllowsClipboardOrAppContext: false),
            "Qwen Coder");
        var profile = new ProviderProfile(
            providerId,
            isRemote ? ProviderKind.CloudGateway : ProviderKind.TestFake,
            "Fake",
            ProviderEndpointClassifier.Classify(isRemote ? "https://example.test" : "http://localhost:1"),
            IsEnabled: true,
            ChatModel: model.ModelId.Value,
            EmbeddingModel: null,
            SecretReference: null,
            [model]);
        var registry = new StaticProviderRegistry([profile]);
        return new DraftServiceFixture(new CodeProposalDraftService(
            new LocalFirstModelRoutingPolicy(registry),
            new ContextPrivacyFilter(),
            new Dictionary<ProviderId, IChatProvider> { [providerId] = provider },
            () =>
            {
                var settings = ProviderRoutingSettings.Default with
                {
                    SelectedChatProvider = providerId.Value,
                    LocalOnlyMode = !isRemote
                };
                return configureSettings?.Invoke(settings) ?? settings;
            }),
            provider);
    }

    private static string DraftJson(string relativePath) =>
        $$"""
        {"title":"Add docs","summary":"Adds XML docs.","changes":[{"relativePath":{{JsonSerializer.Serialize(relativePath)}},"proposedContent":"new"}]}
        """;

    private sealed class FakeChatProvider : IChatProvider
    {
        private readonly Queue<string?> _outputs;
        private readonly Exception? _failure;

        public FakeChatProvider(string? output, Exception? failure = null)
        {
            _outputs = new Queue<string?>([output]);
            _failure = failure;
        }

        public FakeChatProvider(params string?[] outputs)
        {
            _outputs = new Queue<string?>(outputs);
        }

        public string ProviderName => "Fake";

        public ChatRequest? LastRequest { get; private set; }

        public int RequestCount { get; private set; }

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            RequestCount++;
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (_failure is not null)
            {
                throw _failure;
            }

            var output = _outputs.Count == 0 ? string.Empty : _outputs.Dequeue();
            yield return new ChatChunk(output ?? string.Empty, true);
        }
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
