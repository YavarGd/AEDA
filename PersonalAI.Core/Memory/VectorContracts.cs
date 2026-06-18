namespace PersonalAI.Core.Memory;

public sealed record VectorDocument(
    string Id,
    EmbeddingVector Vector,
    string Text,
    MemoryScope Scope,
    string? ProjectId = null,
    string? WorkspaceId = null,
    string? SourceKind = null,
    string? SourceId = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record VectorSearchQuery(
    EmbeddingVector Vector,
    int TopK,
    MemoryScope? Scope = null,
    string? ProjectId = null,
    string? WorkspaceId = null,
    string? SourceKind = null);

public sealed record VectorSearchResult(
    VectorDocument Document,
    double Score);

public sealed record VectorIndexStatus(
    bool IsAvailable,
    int DocumentCount,
    int Dimension,
    bool RebuildRequired = false,
    string? SafeReasonCode = null);

public interface IVectorIndex
{
    Task UpsertAsync(
        VectorDocument document,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchQuery query,
        CancellationToken cancellationToken = default);

    Task<VectorIndexStatus> GetStatusAsync(
        CancellationToken cancellationToken = default);

    Task MarkRebuildRequiredAsync(
        CancellationToken cancellationToken = default);
}
