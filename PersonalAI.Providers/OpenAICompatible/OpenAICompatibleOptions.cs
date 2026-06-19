namespace PersonalAI.Providers.OpenAICompatible;

public sealed record OpenAICompatibleChatOptions(
    Uri BaseUri,
    string Model,
    string? SecretReference = null,
    bool UseStreaming = true,
    TimeSpan? RequestTimeout = null);

public sealed record OpenAICompatibleEmbeddingOptions(
    Uri BaseUri,
    string Model,
    int Dimension,
    string? SecretReference = null,
    int MaxInputCharacters = 8192,
    int MaxBatchSize = 128,
    TimeSpan? RequestTimeout = null);
