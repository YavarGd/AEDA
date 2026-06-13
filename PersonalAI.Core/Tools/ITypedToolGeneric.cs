namespace PersonalAI.Core.Tools;

public interface ITypedTool<TInput, TOutput> : ITypedTool
{
    ValueTask<ToolValidationResult> ValidateAsync(
        TInput input,
        CancellationToken cancellationToken = default);

    ValueTask<TOutput> ExecuteAsync(
        TInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default);
}
