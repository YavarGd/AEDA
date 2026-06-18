namespace PersonalAI.Core.Memory;

public sealed record EmbeddingRequest(
    IReadOnlyList<string> Inputs,
    string? Model = null,
    int? Dimensions = null);

public sealed record EmbeddingVector(
    IReadOnlyList<float> Values)
{
    public int Dimension => Values.Count;
}

public sealed record EmbeddingResult(
    IReadOnlyList<EmbeddingVector> Vectors,
    string Model,
    string ProviderId);

public sealed record EmbeddingModelInfo(
    string ProviderId,
    string Model,
    int Dimension,
    int MaxInputCharacters,
    bool SupportsBatch,
    int MaxBatchSize = 16);

public enum EmbeddingProviderStatus
{
    Available,
    Unconfigured,
    Unavailable
}

public sealed record EmbeddingProviderHealth(
    EmbeddingProviderStatus Status,
    string? SafeReasonCode = null);

public interface IEmbeddingProvider
{
    EmbeddingModelInfo ModelInfo { get; }

    EmbeddingProviderHealth GetStatus();

    Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default);
}
