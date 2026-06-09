namespace PersonalAI.Core.Chat;

public interface IChatProvider
{
    string ProviderName { get; }

    IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}