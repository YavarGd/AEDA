namespace PersonalAI.Core.Chat;

public sealed record Conversation(
    Guid Id,
    string Title,
    string Model,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ConversationStatus Status);
