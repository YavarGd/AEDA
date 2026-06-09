namespace PersonalAI.Core.Chat;

public sealed record ChatChunk(
    string Content,
    bool IsComplete);