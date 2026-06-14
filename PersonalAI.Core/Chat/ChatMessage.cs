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
        : this(role, content, images, [], null, null)
    {
    }

    public ChatMessage(
        ChatRole role,
        string content,
        IReadOnlyList<ChatImage> images,
        IReadOnlyList<ChatToolCall> toolCalls,
        string? toolCallId,
        string? toolName)
    {
        Role = role;
        Content = content;
        Images = images;
        ToolCalls = toolCalls;
        ToolCallId = toolCallId;
        ToolName = toolName;
    }

    public ChatRole Role { get; }

    public string Content { get; }

    public IReadOnlyList<ChatImage> Images { get; }

    public IReadOnlyList<ChatToolCall> ToolCalls { get; }

    public string? ToolCallId { get; }

    public string? ToolName { get; }
}
