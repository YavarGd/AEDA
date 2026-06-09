namespace PersonalAI.Core.Chat;

public sealed record ChatRequest(
    string Model,
    IReadOnlyList<ChatMessage> Messages);