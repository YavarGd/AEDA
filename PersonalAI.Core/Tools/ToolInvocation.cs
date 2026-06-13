namespace PersonalAI.Core.Tools;

public sealed record ToolInvocation(
    ToolId ToolId,
    object? Input)
{
    public static ToolInvocation Create<TInput>(ToolId toolId, TInput input) =>
        new(toolId, input);
}
