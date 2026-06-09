using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ChatSessionService(IChatProvider chatProvider)
{
    public IAsyncEnumerable<ChatChunk> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        var request = new ChatRequest(model, messages);
        return chatProvider.StreamAsync(request, cancellationToken);
    }
}
