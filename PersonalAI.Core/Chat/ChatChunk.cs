namespace PersonalAI.Core.Chat;

public sealed record ChatChunk
{
    public ChatChunk(string content, bool isComplete)
        : this(content, isComplete, [], null, null)
    {
    }

    public ChatChunk(
        string content,
        bool isComplete,
        IReadOnlyList<ChatToolCall> toolCalls,
        string? activityMessage = null,
        string? activityKey = null)
    {
        Content = content;
        IsComplete = isComplete;
        ToolCalls = toolCalls;
        ActivityMessage = activityMessage;
        ActivityKey = activityKey;
    }

    public string Content { get; }

    public bool IsComplete { get; }

    public IReadOnlyList<ChatToolCall> ToolCalls { get; }

    public string? ActivityMessage { get; }

    public string? ActivityKey { get; }
}
