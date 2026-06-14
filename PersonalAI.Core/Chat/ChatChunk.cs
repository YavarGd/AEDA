namespace PersonalAI.Core.Chat;

public sealed record ChatChunk
{
    public ChatChunk(string content, bool isComplete)
        : this(content, isComplete, [], null)
    {
    }

    public ChatChunk(
        string content,
        bool isComplete,
        IReadOnlyList<ChatToolCall> toolCalls,
        string? activityMessage = null)
    {
        Content = content;
        IsComplete = isComplete;
        ToolCalls = toolCalls;
        ActivityMessage = activityMessage;
    }

    public string Content { get; }

    public bool IsComplete { get; }

    public IReadOnlyList<ChatToolCall> ToolCalls { get; }

    public string? ActivityMessage { get; }
}
