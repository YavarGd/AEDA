using PersonalAI.Core.Memory;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class MemoryServicePolicyTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public MemoryServicePolicyTests()
    {
        _databasePath = Path.Combine(_directory, "memory-service.db");
    }

    [Fact]
    public async Task ExplicitMemorySave_AllowedByDefault()
    {
        var service = await CreateServiceAsync();

        var result = await service.SaveExplicitMemoryAsync(CreateRequest(
            "Remember that I prefer focused test summaries."));

        Assert.True(result.Succeeded);
        Assert.NotNull(result.Memory);
        Assert.Equal(MemoryKind.ExplicitUserPreference, result.Memory.Kind);
    }

    [Fact]
    public async Task AutomaticMemory_IsDisabledByDefault()
    {
        var service = await CreateServiceAsync();

        var result = await service.SaveProjectFactAsync(
            CreateRequest("Project uses WinUI.", isExplicit: false) with
            {
                Scope = MemoryScope.Project
            });

        Assert.False(result.Succeeded);
        Assert.Equal("automatic_memory_disabled", result.SafeReasonCode);
    }

    [Fact]
    public async Task SensitiveMemoryRequiresApprovalFlag()
    {
        var service = await CreateServiceAsync();
        var request = CreateRequest("User medical preference, approved.") with
        {
            Sensitivity = MemorySensitivity.Sensitive
        };

        var denied = await service.SaveExplicitMemoryAsync(request);
        var allowed = await service.SaveExplicitMemoryAsync(
            request with { SensitiveApproved = true });

        Assert.False(denied.Succeeded);
        Assert.Equal("sensitive_memory_requires_approval", denied.SafeReasonCode);
        Assert.True(allowed.Succeeded);
    }

    [Fact]
    public async Task SecretsAndPrivateReasoning_AreRejected()
    {
        var service = await CreateServiceAsync();

        var secret = await service.SaveExplicitMemoryAsync(
            CreateRequest("api_key=123456789abcdef"));
        var reasoning = await service.SaveExplicitMemoryAsync(
            CreateRequest("Store my private reasoning chain-of-thought."));

        Assert.Equal("secret_memory_rejected", secret.SafeReasonCode);
        Assert.Equal("private_reasoning_rejected", reasoning.SafeReasonCode);
    }

    [Fact]
    public async Task ExcludedScope_IsDenied()
    {
        var repository = new SqliteMemoryRepository(_databasePath);
        await repository.InitializeAsync();
        var service = new MemoryService(
            repository,
            new MemoryPolicyEvaluator(),
            MemoryPolicy.Default with
            {
                ExclusionRules = [new MemoryExclusionRule("blocked-project")]
            });

        var result = await service.SaveProjectFactAsync(
            CreateRequest("Blocked project fact.") with
            {
                Scope = MemoryScope.Project,
                ProjectId = "blocked-project"
            });

        Assert.False(result.Succeeded);
        Assert.Equal("memory_scope_excluded", result.SafeReasonCode);
    }

    [Fact]
    public async Task TaskOutcome_IsExplicitOnlyAndSearchable()
    {
        var service = await CreateServiceAsync();
        var taskId = TaskId.NewId();

        var result = await service.SaveTaskOutcomeAsync(
            CreateRequest("Task outcome: tests passed.") with
            {
                Kind = MemoryKind.TaskOutcome,
                Scope = MemoryScope.Task,
                TaskRunId = taskId,
                Source = new MemorySource(
                    "task_outcome_request",
                    DateTimeOffset.UtcNow,
                    TaskRunId: taskId,
                    Excerpt: new string('x', 700))
            });
        var search = await service.SearchAsync(new MemorySearchQuery(
            Text: "tests passed",
            TaskRunId: taskId));

        Assert.True(result.Succeeded);
        Assert.True(result.Memory?.Source.Excerpt?.Length <= MemorySource.MaxExcerptCharacters);
        Assert.Single(search);
    }

    [Fact]
    public async Task DeleteMemory_RemovesRecord()
    {
        var service = await CreateServiceAsync();
        var result = await service.SaveExplicitMemoryAsync(CreateRequest("Delete me."));

        await service.DeleteAsync(result.Memory!.Id);
        var search = await service.SearchAsync(new MemorySearchQuery(Text: "Delete me"));

        Assert.Empty(search);
    }

    private async Task<MemoryService> CreateServiceAsync()
    {
        var repository = new SqliteMemoryRepository(_databasePath);
        await repository.InitializeAsync();
        return new MemoryService(
            repository,
            new MemoryPolicyEvaluator(),
            MemoryPolicy.Default);
    }

    private static SaveMemoryRequest CreateRequest(
        string text,
        bool isExplicit = true)
    {
        var now = DateTimeOffset.UtcNow;
        return new SaveMemoryRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            text,
            new MemorySource(
                "explicit_user_save",
                now,
                Excerpt: text,
                Confidence: MemoryConfidence.High),
            isExplicit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
