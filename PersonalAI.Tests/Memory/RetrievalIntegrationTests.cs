using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class RetrievalIntegrationTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Retrieval_CombinesMemoryAndIndexedChunksWithTextFallback()
    {
        var memoryRepository = await CreateMemoryRepositoryAsync("retrieval-text.db");
        var workspaceId = WorkspaceId.NewId();
        await memoryRepository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            "Project memory says use SQLite for local RAG.",
            MemoryKind.ProjectFact,
            MemoryScope.Workspace,
            workspaceId));
        var knowledge = await CreateKnowledgeRepositoryAsync("knowledge-text.db");
        await StoreChunkAsync(
            knowledge,
            workspaceId,
            "docs/rag.md",
            "Workspace chunk explains SQLite vector storage.");
        var retrieval = new RetrievalService(memoryRepository, knowledge);

        var pack = await retrieval.RetrieveAsync(new RetrievalQuery(
            "SQLite RAG",
            WorkspaceId: workspaceId.ToString(),
            MaxItems: 4));

        Assert.False(pack.UsedEmbeddingSearch);
        Assert.Contains(pack.Items, item => item.Kind == RetrievalContextItemKind.Memory);
        Assert.Contains(pack.Items, item => item.Kind == RetrievalContextItemKind.KnowledgeChunk);
        Assert.All(pack.Items, item => Assert.False(string.IsNullOrWhiteSpace(item.Source.SourceType)));
        Assert.Contains(pack.Items, item => item.SourceLabel == "docs/rag.md");
    }

    [Fact]
    public async Task Retrieval_UsesVectorRankingWhenAvailable()
    {
        var memoryRepository = await CreateMemoryRepositoryAsync("retrieval-vector-memory.db");
        var knowledge = await CreateKnowledgeRepositoryAsync("retrieval-vector-knowledge.db");
        var vectorIndex = new InMemoryVectorIndex(4);
        var workspaceId = WorkspaceId.NewId();
        await StoreChunkAsync(knowledge, workspaceId, "alpha.md", "alpha alpha alpha");
        await vectorIndex.UpsertAsync(new VectorDocument(
            "chunk-alpha",
            (await new FakeEmbeddingProvider().EmbedAsync(new EmbeddingRequest(["alpha"]))).Vectors[0],
            "alpha alpha alpha",
            MemoryScope.Workspace,
            WorkspaceId: workspaceId.ToString(),
            SourceKind: KnowledgeSourceType.WorkspaceFile.ToString(),
            SourceId: "chunk-alpha",
            Metadata: new Dictionary<string, string>
            {
                ["relative_path"] = "alpha.md",
                ["content_hash"] = "hash-alpha",
                ["document_id"] = "doc-alpha"
            }));
        var retrieval = new RetrievalService(
            memoryRepository,
            knowledge,
            new FakeEmbeddingProvider(),
            vectorIndex);

        var pack = await retrieval.RetrieveAsync(new RetrievalQuery(
            "alpha",
            WorkspaceId: workspaceId.ToString(),
            MaxItems: 2));

        var item = Assert.Single(pack.Items);
        Assert.True(pack.UsedEmbeddingSearch);
        Assert.Equal("vector", item.MatchType);
        Assert.Equal("alpha.md", item.SourceLabel);
        Assert.Equal("hash-alpha", item.ContentHash);
    }

    [Fact]
    public async Task Retrieval_BoundsCharactersAndExcludesSensitiveByDefault()
    {
        var memoryRepository = await CreateMemoryRepositoryAsync("retrieval-bounds.db");
        await memoryRepository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            new string('a', 1000),
            MemoryKind.ProjectFact,
            MemoryScope.Global));
        await memoryRepository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            "Sensitive local note",
            MemoryKind.ProjectFact,
            MemoryScope.Global) with
        {
            Sensitivity = MemorySensitivity.Sensitive
        });
        var retrieval = new RetrievalService(memoryRepository);

        var pack = await retrieval.RetrieveAsync(new RetrievalQuery(
            "a Sensitive",
            MaxItems: 10,
            MaxCharacters: 120));

        Assert.True(pack.Items.Sum(item => item.Text.Length) <= 120);
        Assert.DoesNotContain(pack.Items, item => item.Text.Contains("Sensitive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Retrieval_DeduplicatesTraceIds()
    {
        var memoryRepository = await CreateMemoryRepositoryAsync("retrieval-dedupe-memory.db");
        var vectorIndex = new InMemoryVectorIndex(4);
        var provider = new FakeEmbeddingProvider();
        var vector = (await provider.EmbedAsync(new EmbeddingRequest(["alpha"]))).Vectors[0];
        await vectorIndex.UpsertAsync(new VectorDocument(
            "dup-a",
            vector,
            "duplicate text",
            MemoryScope.Workspace,
            WorkspaceId: "ws",
            SourceKind: KnowledgeSourceType.WorkspaceFile.ToString(),
            SourceId: "same",
            Metadata: new Dictionary<string, string> { ["relative_path"] = "a.md" }));
        await vectorIndex.UpsertAsync(new VectorDocument(
            "dup-b",
            vector,
            "duplicate text",
            MemoryScope.Workspace,
            WorkspaceId: "ws",
            SourceKind: KnowledgeSourceType.WorkspaceFile.ToString(),
            SourceId: "same",
            Metadata: new Dictionary<string, string> { ["relative_path"] = "a.md" }));
        var retrieval = new RetrievalService(
            memoryRepository,
            embeddingProvider: provider,
            vectorIndex: vectorIndex);

        var pack = await retrieval.RetrieveAsync(new RetrievalQuery(
            "alpha",
            WorkspaceId: "ws",
            MaxItems: 10));

        Assert.Single(pack.Items);
    }

    [Fact]
    public async Task ChatContextRetrievalService_RequiresExplicitCallerPath()
    {
        var repository = await CreateMemoryRepositoryAsync("chat-context.db");
        await repository.CreateAsync(MemoryRepositoryTests.CreateMemory("Explicit context only."));
        var chatRetrieval = new ChatContextRetrievalService(new RetrievalService(repository));

        var pack = await chatRetrieval.BuildContextPackAsync("context");

        Assert.Single(pack.Items);
    }

    private async Task<SqliteMemoryRepository> CreateMemoryRepositoryAsync(string fileName)
    {
        var repository = new SqliteMemoryRepository(Path.Combine(_directory, fileName));
        await repository.InitializeAsync();
        return repository;
    }

    private async Task<SqliteKnowledgeRepository> CreateKnowledgeRepositoryAsync(string fileName)
    {
        var repository = new SqliteKnowledgeRepository(Path.Combine(_directory, fileName));
        await repository.InitializeAsync();
        return repository;
    }

    private static async Task StoreChunkAsync(
        IKnowledgeRepository repository,
        WorkspaceId workspaceId,
        string relativePath,
        string text)
    {
        var source = new KnowledgeSource(
            KnowledgeSourceType.WorkspaceFile,
            DateTimeOffset.UtcNow,
            workspaceId,
            relativePath);
        var result = KnowledgeChunker.ChunkText(
            $"{workspaceId}:{relativePath}",
            relativePath,
            text,
            source);
        await repository.UpsertDocumentAsync(
            result.Document with { State = DocumentIndexState.Indexed },
            result.Chunks);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
