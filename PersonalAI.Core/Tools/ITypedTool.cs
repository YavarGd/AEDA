namespace PersonalAI.Core.Tools;

public interface ITypedTool
{
    ToolDescriptor Descriptor { get; }

    ValueTask<ToolValidationResult> ValidateAsync(
        object? input,
        CancellationToken cancellationToken = default);

    ValueTask<ToolResult> ExecuteAsync(
        ToolInvocation invocation,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default);
}
