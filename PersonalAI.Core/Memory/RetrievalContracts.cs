namespace PersonalAI.Core.Memory;

public enum RetrievalContextItemKind
{
    Memory,
    KnowledgeChunk,
    TaskOutcome
}

public sealed record RetrievalContextItem(
    RetrievalContextItemKind Kind,
    string Text,
    double Score,
    MemorySource Source,
    MemoryConfidence Confidence = MemoryConfidence.Medium,
    string? SourceLabel = null,
    string? MatchType = null,
    string? TraceId = null,
    string? ContentHash = null);

public sealed record RetrievalQuery(
    string Text,
    string? ProjectId = null,
    string? WorkspaceId = null,
    bool IncludeSensitive = false,
    int MaxItems = 10,
    int MaxCharacters = 8000);

public sealed record RetrievalContextPack(
    IReadOnlyList<RetrievalContextItem> Items,
    bool UsedEmbeddingSearch,
    bool IsTruncated);

public interface IRetrievalService
{
    Task<RetrievalContextPack> RetrieveAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class RetrievalService(
    IMemoryRepository memoryRepository,
    IKnowledgeRepository? knowledgeRepository = null,
    IEmbeddingProvider? embeddingProvider = null,
    IVectorIndex? vectorIndex = null)
    : IRetrievalService
{
    public async Task<RetrievalContextPack> RetrieveAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(query.MaxItems, 1, 50);
        var maxCharacters = Math.Clamp(query.MaxCharacters, 1, 50_000);
        var candidates = new List<RetrievalContextItem>();
        var memoryResults = await memoryRepository.SearchAsync(
            new MemorySearchQuery(
                query.Text,
                ProjectId: query.ProjectId,
                WorkspaceId: query.WorkspaceId is null
                    ? null
                    : new Workspaces.WorkspaceId(query.WorkspaceId),
                IncludeSensitive: query.IncludeSensitive,
                Limit: limit * 2),
            cancellationToken);

        candidates.AddRange(memoryResults.Select(result => new RetrievalContextItem(
                result.Memory.Kind == MemoryKind.TaskOutcome
                    ? RetrievalContextItemKind.TaskOutcome
                    : RetrievalContextItemKind.Memory,
                result.Memory.Text,
                result.Score,
                result.Source,
                result.Memory.Confidence,
                CreateMemorySourceLabel(result.Memory),
                "memory_text",
                result.Memory.Id.ToString())));

        var usedEmbeddingSearch = false;
        if (embeddingProvider is not null &&
            vectorIndex is not null &&
            embeddingProvider.GetStatus().Status == EmbeddingProviderStatus.Available)
        {
            var embedding = await embeddingProvider.EmbedAsync(
                new EmbeddingRequest([query.Text]),
                cancellationToken);
            var vectorResults = await vectorIndex.SearchAsync(
                new VectorSearchQuery(
                    embedding.Vectors[0],
                    limit * 2,
                    Scope: MemoryScope.Workspace,
                    ProjectId: query.ProjectId,
                    WorkspaceId: query.WorkspaceId,
                    SourceKind: KnowledgeSourceType.WorkspaceFile.ToString()),
                cancellationToken);
            usedEmbeddingSearch = true;
            candidates.AddRange(vectorResults.Select(result =>
            {
                var relativePath = TryGetMetadata(result.Document, "relative_path");
                var contentHash = TryGetMetadata(result.Document, "content_hash");
                var source = new MemorySource(
                    "workspace_chunk",
                    DateTimeOffset.UtcNow,
                    WorkspaceId: result.Document.WorkspaceId is null
                        ? null
                        : new Workspaces.WorkspaceId(result.Document.WorkspaceId),
                    RelativeFilePath: relativePath,
                    DocumentId: TryGetMetadata(result.Document, "document_id"),
                    ChunkId: result.Document.SourceId,
                    Excerpt: Bound(result.Document.Text, MemorySource.MaxExcerptCharacters),
                    Confidence: MemoryConfidence.Medium);
                return new RetrievalContextItem(
                    RetrievalContextItemKind.KnowledgeChunk,
                    result.Document.Text,
                    result.Score,
                    source,
                    MemoryConfidence.Medium,
                    relativePath ?? "Workspace chunk",
                    "vector",
                    result.Document.SourceId ?? result.Document.Id,
                    contentHash);
            }));
        }
        else if (knowledgeRepository is not null)
        {
            var chunks = await knowledgeRepository.SearchChunksAsync(
                query.Text,
                query.WorkspaceId,
                limit * 2,
                cancellationToken);
            if (chunks.Count == 0)
            {
                var chunkCandidates = new Dictionary<string, KnowledgeChunk>(StringComparer.Ordinal);
                foreach (var term in query.Text.Split(
                             ' ',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var chunk in await knowledgeRepository.SearchChunksAsync(
                                 term,
                                 query.WorkspaceId,
                                 limit * 2,
                                 cancellationToken))
                    {
                        chunkCandidates.TryAdd(chunk.Id, chunk);
                    }
                }

                chunks = chunkCandidates.Values.ToArray();
            }

            candidates.AddRange(chunks.Select(chunk => new RetrievalContextItem(
                RetrievalContextItemKind.KnowledgeChunk,
                chunk.Text,
                ScoreText(chunk.Text, query.Text),
                new MemorySource(
                    "workspace_chunk",
                    chunk.Source.TimestampUtc,
                    WorkspaceId: chunk.Source.WorkspaceId,
                    RelativeFilePath: chunk.Source.RelativePath,
                    DocumentId: chunk.DocumentId,
                    ChunkId: chunk.Id,
                    Excerpt: Bound(chunk.Text, MemorySource.MaxExcerptCharacters),
                    Confidence: MemoryConfidence.Medium),
                MemoryConfidence.Medium,
                chunk.Source.RelativePath ?? "Workspace chunk",
                "chunk_text",
                chunk.Id,
                chunk.ContentHash)));
        }

        var deduped = candidates
            .Where(item => !ContainsPrivateReasoning(item.Text))
            .GroupBy(item => item.TraceId ?? $"{item.Kind}:{item.Source.SourceType}:{item.Text}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.SourceLabel, StringComparer.Ordinal)
            .ToList();

        var selected = new List<RetrievalContextItem>();
        var characters = 0;
        foreach (var item in deduped)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (selected.Count >= limit)
            {
                break;
            }

            var remaining = maxCharacters - characters;
            if (remaining <= 0)
            {
                break;
            }

            var text = Bound(item.Text, remaining);
            if (text.Length == 0)
            {
                continue;
            }

            selected.Add(item with { Text = text });
            characters += text.Length;
        }

        return new RetrievalContextPack(
            selected,
            usedEmbeddingSearch,
            IsTruncated: deduped.Count > selected.Count);
    }

    private static string? CreateMemorySourceLabel(MemoryRecord memory) =>
        memory.Source.RelativeFilePath ??
        memory.ProjectId ??
        memory.WorkspaceId?.ToString() ??
        memory.Kind.ToString();

    private static string? TryGetMetadata(VectorDocument document, string key) =>
        document.Metadata is not null &&
        document.Metadata.TryGetValue(key, out var value) &&
        !string.IsNullOrWhiteSpace(value)
            ? value
            : null;

    private static double ScoreText(string text, string query)
    {
        var terms = query.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return 1;
        }

        return terms.Count(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string Bound(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || maxLength <= 0)
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool ContainsPrivateReasoning(string text) =>
        text.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("private reasoning", StringComparison.OrdinalIgnoreCase);
}
