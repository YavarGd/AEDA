using PersonalAI.Core.Memory;

namespace PersonalAI.Infrastructure.Memory;

public sealed class InMemoryVectorIndex(int dimension) : IVectorIndex
{
    private readonly Dictionary<string, VectorDocument> _documents =
        new(StringComparer.Ordinal);
    private bool _rebuildRequired;

    public Task UpsertAsync(
        VectorDocument document,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDimension(document.Vector);
        _documents[document.Id] = document;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _documents.Remove(documentId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDimension(query.Vector);
        var topK = Math.Clamp(query.TopK, 1, 100);
        var results = _documents.Values
            .Where(document => query.Scope is null || document.Scope == query.Scope)
            .Where(document => query.ProjectId is null || document.ProjectId == query.ProjectId)
            .Where(document => query.WorkspaceId is null || document.WorkspaceId == query.WorkspaceId)
            .Select(document => new VectorSearchResult(
                document,
                CosineSimilarity(query.Vector.Values, document.Vector.Values)))
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Document.Id, StringComparer.Ordinal)
            .Take(topK)
            .ToArray();

        return Task.FromResult<IReadOnlyList<VectorSearchResult>>(results);
    }

    public Task<VectorIndexStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new VectorIndexStatus(
            IsAvailable: true,
            _documents.Count,
            dimension,
            _rebuildRequired));
    }

    public Task MarkRebuildRequiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rebuildRequired = true;
        return Task.CompletedTask;
    }

    private void ValidateDimension(EmbeddingVector vector)
    {
        if (vector.Dimension != dimension)
        {
            throw new ArgumentException(
                "Vector dimension did not match the index.",
                nameof(vector));
        }
    }

    private static double CosineSimilarity(
        IReadOnlyList<float> left,
        IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        if (leftMagnitude == 0 || rightMagnitude == 0)
        {
            return 0;
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
