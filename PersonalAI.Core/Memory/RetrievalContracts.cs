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
    MemoryConfidence Confidence = MemoryConfidence.Medium);

public sealed record RetrievalQuery(
    string Text,
    string? ProjectId = null,
    string? WorkspaceId = null,
    bool IncludeSensitive = false,
    int MaxItems = 10);

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

public sealed class RetrievalService(IMemoryRepository memoryRepository)
    : IRetrievalService
{
    public async Task<RetrievalContextPack> RetrieveAsync(
        RetrievalQuery query,
        CancellationToken cancellationToken = default)
    {
        var limit = Math.Clamp(query.MaxItems, 1, 50);
        var results = await memoryRepository.SearchAsync(
            new MemorySearchQuery(
                query.Text,
                ProjectId: query.ProjectId,
                WorkspaceId: query.WorkspaceId is null
                    ? null
                    : new Workspaces.WorkspaceId(query.WorkspaceId),
                IncludeSensitive: query.IncludeSensitive,
                Limit: limit + 1),
            cancellationToken);

        var items = results
            .Take(limit)
            .Select(result => new RetrievalContextItem(
                result.Memory.Kind == MemoryKind.TaskOutcome
                    ? RetrievalContextItemKind.TaskOutcome
                    : RetrievalContextItemKind.Memory,
                result.Memory.Text,
                result.Score,
                result.Source,
                result.Memory.Confidence))
            .ToArray();

        return new RetrievalContextPack(
            items,
            UsedEmbeddingSearch: false,
            IsTruncated: results.Count > limit);
    }
}
