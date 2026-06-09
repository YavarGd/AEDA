namespace PersonalAI.Core.Chat;

public sealed record StoredChatMessage(
    Guid Id,
    Guid ConversationId,
    ChatRole Role,
    string Content,
    DateTimeOffset CreatedAtUtc);
