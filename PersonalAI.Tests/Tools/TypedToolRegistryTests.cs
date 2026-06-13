using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Reference;

namespace PersonalAI.Tests.Tools;

public sealed class TypedToolRegistryTests
{
    [Fact]
    public void Register_StoresDescriptorAndTool()
    {
        var registry = new TypedToolRegistry();
        var tool = new GetCurrentUtcTimeTool();

        registry.Register(tool);

        Assert.True(registry.TryGetTool(GetCurrentUtcTimeTool.Id, out var registered));
        Assert.Same(tool, registered);
        var descriptor = Assert.Single(registry.Descriptors);
        Assert.Equal(GetCurrentUtcTimeTool.Id, descriptor.Id);
        Assert.Equal(typeof(GetCurrentUtcTimeInput), descriptor.InputType);
        Assert.Equal(typeof(GetCurrentUtcTimeOutput), descriptor.OutputType);
    }

    [Fact]
    public void Register_RejectsDuplicateToolIds()
    {
        var registry = new TypedToolRegistry();
        registry.Register(new GetCurrentUtcTimeTool());

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new GetCurrentUtcTimeTool()));
    }

    [Fact]
    public async Task TypedToolBase_RejectsWrongInputType()
    {
        var tool = (ITypedTool)new GetCurrentUtcTimeTool();

        var validation = await tool.ValidateAsync("wrong");

        Assert.False(validation.IsValid);
        Assert.Equal("input_type_mismatch", validation.SafeErrorCode);
    }
}
