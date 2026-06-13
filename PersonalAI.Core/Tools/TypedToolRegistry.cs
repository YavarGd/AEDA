namespace PersonalAI.Core.Tools;

public sealed class TypedToolRegistry : IToolRegistry
{
    private readonly Dictionary<ToolId, ITypedTool> _tools = [];

    public IReadOnlyCollection<ToolDescriptor> Descriptors =>
        _tools.Values.Select(tool => tool.Descriptor).ToArray();

    public void Register(ITypedTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (!_tools.TryAdd(tool.Descriptor.Id, tool))
        {
            throw new InvalidOperationException(
                $"Tool '{tool.Descriptor.Id}' is already registered.");
        }
    }

    public bool TryGetTool(ToolId toolId, out ITypedTool tool) =>
        _tools.TryGetValue(toolId, out tool!);

    public ITypedTool GetRequiredTool(ToolId toolId) =>
        TryGetTool(toolId, out var tool)
            ? tool
            : throw new KeyNotFoundException($"Tool '{toolId}' is not registered.");
}
