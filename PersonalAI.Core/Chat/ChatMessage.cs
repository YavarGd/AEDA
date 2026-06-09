namespace PersonalAI.Core.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ChatMessage(
    ChatRole Role,
    string Content);