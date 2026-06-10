namespace PersonalAI.Core.Chat;

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool
}

public sealed record ChatMessage
{
    public ChatMessage(ChatRole role, string content)
        : this(role, content, [])
    {
    }

    public ChatMessage(
        ChatRole role,
        string content,
        IReadOnlyList<ChatImage> images)
    {
        Role = role;
        Content = content;
        Images = images;
    }

    public ChatRole Role { get; }

    public string Content { get; }

    public IReadOnlyList<ChatImage> Images { get; }
}
