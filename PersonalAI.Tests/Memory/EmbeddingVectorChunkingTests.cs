using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class EmbeddingVectorChunkingTests
{
    [Fact]
    public async Task FakeEmbeddingProvider_EmbedsSingleAndBatchDeterministically()
    {
        var provider = new FakeEmbeddingProvider();

        var single = await provider.EmbedAsync(new EmbeddingRequest(["alpha"]));
        var batch = await provider.EmbedAsync(new EmbeddingRequest(["alpha", "beta"]));

        Assert.Equal(4, provider.ModelInfo.Dimension);
        Assert.Equal(4, single.Vectors[0].Dimension);
        Assert.Equal(single.Vectors[0].Values, batch.Vectors[0].Values);
        Assert.Equal(2, batch.Vectors.Count);
    }

    [Fact]
    public async Task FakeEmbeddingProvider_EnforcesMaxInputAndCancellation()
    {
        var provider = new FakeEmbeddingProvider(maxInputCharacters: 5);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["too long"])));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["ok"]), cts.Token));
    }

    [Fact]
    public async Task InMemoryVectorIndex_UpsertsDeletesFiltersAndBoundsResults()
    {
        var index = new InMemoryVectorIndex(dimension: 3);
        await index.UpsertAsync(new VectorDocument(
            "a",
            new EmbeddingVector([1, 0, 0]),
            "alpha",
            MemoryScope.Workspace,
            WorkspaceId: "ws1"));
        await index.UpsertAsync(new VectorDocument(
            "b",
            new EmbeddingVector([0.9f, 0.1f, 0]),
            "beta",
            MemoryScope.Workspace,
            WorkspaceId: "ws1"));
        await index.UpsertAsync(new VectorDocument(
            "c",
            new EmbeddingVector([0, 1, 0]),
            "gamma",
            MemoryScope.Project,
            WorkspaceId: "ws2"));

        var results = await index.SearchAsync(new VectorSearchQuery(
            new EmbeddingVector([1, 0, 0]),
            TopK: 1,
            Scope: MemoryScope.Workspace,
            WorkspaceId: "ws1"));

        var result = Assert.Single(results);
        Assert.Equal("a", result.Document.Id);

        await index.DeleteAsync("a");
        results = await index.SearchAsync(new VectorSearchQuery(
            new EmbeddingVector([1, 0, 0]),
            TopK: 10,
            Scope: MemoryScope.Workspace,
            WorkspaceId: "ws1"));
        Assert.Single(results);
        Assert.Equal("b", results[0].Document.Id);
    }

    [Fact]
    public async Task InMemoryVectorIndex_RejectsDimensionMismatchAndHonorsCancellation()
    {
        var index = new InMemoryVectorIndex(dimension: 3);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => index.UpsertAsync(
            new VectorDocument(
                "bad",
                new EmbeddingVector([1, 2]),
                "bad",
                MemoryScope.Global)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => index.GetStatusAsync(cts.Token));
    }

    [Fact]
    public void KnowledgeChunker_CreatesDeterministicChunksAndMetadata()
    {
        var source = new KnowledgeSource(
            KnowledgeSourceType.WorkspaceFile,
            DateTimeOffset.UtcNow,
            WorkspaceId.NewId(),
            "src/App.cs");
        var text = string.Join(' ', Enumerable.Repeat("alpha beta gamma", 80));

        var first = KnowledgeChunker.ChunkText(
            "doc1",
            "App.cs",
            text,
            source,
            new ChunkingOptions(120, 10, 100));
        var second = KnowledgeChunker.ChunkText(
            "doc1",
            "App.cs",
            text,
            source,
            new ChunkingOptions(120, 10, 100));
        var small = KnowledgeChunker.ChunkText("doc2", "small", "short", source);
        var binary = KnowledgeChunker.ChunkText("doc3", "bin", "abc\0def", source);

        Assert.False(first.IsSkipped);
        Assert.True(first.Chunks.Count > 1);
        Assert.All(first.Chunks, chunk => Assert.True(chunk.Text.Length <= 120));
        Assert.Equal(first.Document.ContentHash, second.Document.ContentHash);
        Assert.Equal(first.Chunks.Select(chunk => chunk.ContentHash), second.Chunks.Select(chunk => chunk.ContentHash));
        Assert.Single(small.Chunks);
        Assert.True(binary.IsSkipped);
        Assert.Equal(source, first.Chunks[0].Source);
    }
}

internal sealed class FakeEmbeddingProvider(
    int dimension = 4,
    int maxInputCharacters = 1000,
    EmbeddingProviderStatus status = EmbeddingProviderStatus.Available)
    : IEmbeddingProvider
{
    public EmbeddingModelInfo ModelInfo { get; } = new(
        "fake",
        "fake-embedding",
        dimension,
        maxInputCharacters,
        SupportsBatch: true);

    public EmbeddingProviderHealth GetStatus() => new(status);

    public Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (status != EmbeddingProviderStatus.Available)
        {
            throw new InvalidOperationException("embedding_provider_unavailable");
        }

        foreach (var input in request.Inputs)
        {
            if (input.Length > ModelInfo.MaxInputCharacters)
            {
                throw new InvalidOperationException("embedding_input_too_large");
            }
        }

        var vectors = request.Inputs
            .Select(input =>
            {
                var values = new float[ModelInfo.Dimension];
                foreach (var character in input)
                {
                    values[character % values.Length] += 1;
                }

                return new EmbeddingVector(values);
            })
            .ToArray();

        return Task.FromResult(new EmbeddingResult(
            vectors,
            ModelInfo.Model,
            ModelInfo.ProviderId));
    }
}
