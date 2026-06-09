namespace PersonalAI.Core.Editor;

public sealed record EditorContextEnvelope(
    int ProtocolVersion,
    string RequestId,
    ContextSource Source,
    string Command,
    string? UserPrompt,
    EditorContext? Context);
