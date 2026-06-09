namespace PersonalAI.Core.Chat;

public interface IConversationRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        CancellationToken cancellationToken = default);

    Task<Conversation?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default);

    Task<Conversation> CreateConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default);

    Task UpdateConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default);

    Task<StoredChatMessage> AddMessageAsync(
        StoredChatMessage message,
        CancellationToken cancellationToken = default);
}
