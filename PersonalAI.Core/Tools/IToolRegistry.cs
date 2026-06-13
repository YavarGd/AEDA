namespace PersonalAI.Core.Tools;

public interface IToolRegistry
{
    IReadOnlyCollection<ToolDescriptor> Descriptors { get; }

    void Register(ITypedTool tool);

    bool TryGetTool(ToolId toolId, out ITypedTool tool);

    ITypedTool GetRequiredTool(ToolId toolId);
}
