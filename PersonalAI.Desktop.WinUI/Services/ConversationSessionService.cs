using PersonalAI.Core.Chat;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ConversationSessionService(
    IConversationRepository conversationRepository,
    ChatSessionService chatSession)
{
    public Task<IReadOnlyList<Conversation>> LoadConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        return conversationRepository.ListConversationsAsync(cancellationToken);
    }

    public Task<Conversation?> LoadConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return conversationRepository.GetConversationAsync(
            conversationId,
            cancellationToken);
    }

    public Task<IReadOnlyList<StoredChatMessage>> LoadMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return conversationRepository.ListMessagesAsync(
            conversationId,
            cancellationToken);
    }

    public Task<Conversation> CreateConversationAsync(
        string firstPrompt,
        string model,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(),
            ConversationTitleGenerator.CreateTitle(firstPrompt),
            model,
            now,
            now,
            ConversationStatus.Active);

        return conversationRepository.CreateConversationAsync(
            conversation,
            cancellationToken);
    }

    public Task AddMessageAsync(
        Guid conversationId,
        ChatRole role,
        string content,
        CancellationToken cancellationToken = default)
    {
        var message = new StoredChatMessage(
            Guid.NewGuid(),
            conversationId,
            role,
            content,
            DateTimeOffset.UtcNow);

        return conversationRepository.AddMessageAsync(message, cancellationToken);
    }

    public async Task<Conversation> UpdateConversationAsync(
        Conversation conversation,
        ConversationStatus status,
        string model,
        CancellationToken cancellationToken = default)
    {
        var updated = conversation with
        {
            Model = model,
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await conversationRepository.UpdateConversationAsync(
            updated,
            cancellationToken);

        return updated;
    }

    public IAsyncEnumerable<ChatChunk> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        return chatSession.StreamAsync(model, messages, cancellationToken);
    }
}
