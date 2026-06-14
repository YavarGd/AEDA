namespace PersonalAI.Core.Chat;

public sealed record ChatRequest
{
    public ChatRequest(string Model, IReadOnlyList<ChatMessage> Messages)
        : this(Model, Messages, [])
    {
    }

    public ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        IReadOnlyList<ChatToolDefinition> Tools)
    {
        this.Model = Model;
        this.Messages = Messages;
        this.Tools = Tools;
    }

    public string Model { get; }

    public IReadOnlyList<ChatMessage> Messages { get; }

    public IReadOnlyList<ChatToolDefinition> Tools { get; }
}
