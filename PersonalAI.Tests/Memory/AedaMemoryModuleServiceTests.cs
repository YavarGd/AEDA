using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Tests.Memory;

public sealed class AedaMemoryModuleServiceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public AedaMemoryModuleServiceTests()
    {
        _databasePath = Path.Combine(_directory, "aeda-memory.db");
    }

    [Fact]
    public async Task Dashboard_LoadsBoundedSummariesAndPolicy()
    {
        var service = await CreateServiceAsync();
        await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Remember concise release notes.",
            "Explicit user save"));
        await service.CreateProjectFactAsync(new AedaMemoryCreateRequest(
            MemoryKind.ProjectFact,
            MemoryScope.Project,
            "Project brain uses explicit source attribution.",
            "Project note",
            ProjectId: "project-a"));

        var dashboard = await service.GetDashboardAsync();

        Assert.Equal(2, dashboard.TotalMemoryCount);
        Assert.True(dashboard.Policy.LocalOnly);
        Assert.False(dashboard.Policy.AutomaticMemoryEnabled);
        Assert.Equal("Automatic memory disabled", dashboard.Privacy.AutomaticMemoryStatus);
        Assert.Contains(dashboard.RecentMemories, item => item.SourceLabel == "Explicit user save");
        Assert.Contains("ExplicitUserPreference", dashboard.CountsByKind.Keys);
    }

    [Fact]
    public async Task MemoryFlows_CreateSearchDetailUpdateArchiveDeleteSafely()
    {
        var service = await CreateServiceAsync();

        var created = await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Remember that UI lists are bounded.",
            "Explicit user save"));
        var search = await service.SearchMemoriesAsync("bounded");
        var detail = await service.GetMemoryDetailAsync(new MemoryId(created.Memory!.Id));
        var secretUpdate = await service.UpdateMemoryAsync(new AedaMemoryUpdateRequest(
            new MemoryId(created.Memory.Id),
            "api_key=123456789abcdef",
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            MemorySensitivity.Normal));
        var update = await service.UpdateMemoryAsync(new AedaMemoryUpdateRequest(
            new MemoryId(created.Memory.Id),
            "Remember that UI lists stay bounded.",
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            MemorySensitivity.Normal));
        var archive = await service.ArchiveMemoryAsync(new MemoryId(created.Memory.Id));
        var restore = await service.RestoreArchivedMemoryAsync(new MemoryId(created.Memory.Id));
        var delete = await service.DeleteMemoryAsync(new MemoryId(created.Memory.Id));
        var afterDelete = await service.GetMemoryDetailAsync(new MemoryId(created.Memory.Id));

        Assert.True(created.Succeeded);
        Assert.Single(search);
        Assert.NotNull(detail);
        Assert.Equal("Explicit user save", detail!.Source.DisplayName);
        Assert.Equal("secret_memory_rejected", secretUpdate.SafeReasonCode);
        Assert.True(update.Succeeded);
        Assert.True(archive.Succeeded);
        Assert.Equal("Archived", archive.Memory!.Visibility);
        Assert.True(restore.Succeeded);
        Assert.Equal("Active", restore.Memory!.Visibility);
        Assert.True(delete.Succeeded);
        Assert.Null(afterDelete);
    }

    [Fact]
    public async Task SensitiveMemory_RequiresExplicitApproval()
    {
        var service = await CreateServiceAsync();

        var denied = await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Sensitive but approved only through a flag.",
            "Explicit user save",
            MemorySensitivity.Sensitive));
        var allowed = await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Sensitive but explicitly approved.",
            "Explicit user save",
            MemorySensitivity.Sensitive,
            SensitiveApproved: true));

        Assert.False(denied.Succeeded);
        Assert.Equal("sensitive_memory_requires_approval", denied.SafeReasonCode);
        Assert.True(allowed.Succeeded);
        Assert.Equal("Protected", allowed.Memory!.SensitivityStatus);
    }

    [Fact]
    public async Task KnowledgeOverview_UsesRelativePathsAndBoundedChunkPreview()
    {
        var repository = new SqliteKnowledgeRepository(_databasePath);
        await repository.InitializeAsync();
        var workspaceId = new WorkspaceId("workspace-1");
        var source = new KnowledgeSource(
            KnowledgeSourceType.WorkspaceFile,
            DateTimeOffset.UtcNow,
            workspaceId,
            "src/Brain.cs");
        var chunks = KnowledgeChunker.ChunkText(
            "doc-1",
            "C:\\absolute\\Brain.cs",
            new string('a', 900) + " searchable memory knowledge",
            source);
        await repository.UpsertDocumentAsync(chunks.Document, chunks.Chunks);
        var service = await CreateServiceAsync(knowledgeRepository: repository);

        var documents = await service.ListIndexedDocumentsAsync("workspace-1");
        var chunkSummaries = await service.ListChunksForDocumentAsync("doc-1");
        var search = await service.SearchIndexedKnowledgeAsync("searchable");

        Assert.Single(documents);
        Assert.Equal("src/Brain.cs", documents[0].RelativePath);
        Assert.False(Path.IsPathRooted(documents[0].RelativePath!));
        Assert.True(documents[0].TraceId.Length <= 16);
        Assert.NotEmpty(chunkSummaries);
        Assert.All(chunkSummaries, chunk => Assert.True(chunk.PreviewText.Length <= 280));
        Assert.NotEmpty(search);
    }

    [Fact]
    public async Task RetrievalPreview_IsExplicitBoundedAndExcludesSensitiveByDefault()
    {
        var service = await CreateServiceAsync();
        await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Visible retrieval memory about dashboards.",
            "Explicit user save"));
        await service.CreateExplicitMemoryAsync(new AedaMemoryCreateRequest(
            MemoryKind.ExplicitUserPreference,
            MemoryScope.Global,
            "Sensitive retrieval memory about dashboards.",
            "Explicit user save",
            MemorySensitivity.Sensitive,
            SensitiveApproved: true));

        var empty = await service.PreviewRetrievalAsync("");
        var preview = await service.PreviewRetrievalAsync("dashboards", limit: 3);

        Assert.Empty(empty);
        Assert.Single(preview);
        Assert.DoesNotContain(preview, item =>
            item.PreviewText.Contains("Sensitive", StringComparison.OrdinalIgnoreCase));
        Assert.All(preview, item => Assert.True(item.PreviewText.Length <= 280));
        Assert.All(preview, item => Assert.False(string.IsNullOrWhiteSpace(item.SourceLabel)));
    }

    private async Task<AedaMemoryModuleService> CreateServiceAsync(
        SqliteKnowledgeRepository? knowledgeRepository = null)
    {
        var memoryRepository = new SqliteMemoryRepository(_databasePath);
        await memoryRepository.InitializeAsync();
        knowledgeRepository ??= new SqliteKnowledgeRepository(_databasePath);
        await knowledgeRepository.InitializeAsync();
        var policy = MemoryPolicy.Default;
        var memoryService = new MemoryService(
            memoryRepository,
            new MemoryPolicyEvaluator(),
            policy);
        var capabilities = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: true,
            retrievalEnabled: true,
            hasAedaModules: true,
            hasAedaMemoryModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true);
        return new AedaMemoryModuleService(
            memoryRepository,
            memoryService,
            capabilities,
            policy,
            knowledgeRepository,
            new RetrievalService(memoryRepository, knowledgeRepository));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
