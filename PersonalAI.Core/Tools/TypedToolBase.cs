namespace PersonalAI.Core.Tools;

public abstract class TypedToolBase<TInput, TOutput> : ITypedTool<TInput, TOutput>
{
    public abstract ToolDescriptor Descriptor { get; }

    public virtual ValueTask<ToolValidationResult> ValidateAsync(
        TInput input,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ToolValidationResult.Success);

    public abstract ValueTask<TOutput> ExecuteAsync(
        TInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default);

    async ValueTask<ToolValidationResult> ITypedTool.ValidateAsync(
        object? input,
        CancellationToken cancellationToken)
    {
        if (input is not TInput typedInput)
        {
            return ToolValidationResult.Failure(
                "input_type_mismatch",
                $"Expected input type {typeof(TInput).Name}.");
        }

        return await ValidateAsync(typedInput, cancellationToken);
    }

    async ValueTask<ToolResult> ITypedTool.ExecuteAsync(
        ToolInvocation invocation,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (invocation.Input is not TInput typedInput)
        {
            return ToolResult.Failure(
                Descriptor.Id,
                ToolExecutionStatus.ValidationFailed,
                "Tool input did not match the registered contract.",
                TimeSpan.Zero,
                "input_type_mismatch",
                $"Expected input type {typeof(TInput).Name}.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        var output = await ExecuteAsync(typedInput, context, cancellationToken);
        var duration = DateTimeOffset.UtcNow - startedAt;

        return ToolResult.Success(
            Descriptor.Id,
            output,
            "Tool completed successfully.",
            duration);
    }
}
