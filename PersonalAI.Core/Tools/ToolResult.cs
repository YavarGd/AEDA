namespace PersonalAI.Core.Tools;

public sealed record ToolResult(
    ToolId ToolId,
    ToolExecutionStatus Status,
    object? Output,
    string Summary,
    TimeSpan Duration,
    string? SafeErrorCode = null,
    string? SafeErrorMessage = null)
{
    public bool IsSuccess => Status == ToolExecutionStatus.Succeeded;

    public static ToolResult Success<TOutput>(
        ToolId toolId,
        TOutput output,
        string summary,
        TimeSpan duration) =>
        new(toolId, ToolExecutionStatus.Succeeded, output, summary, duration);

    public static ToolResult Failure(
        ToolId toolId,
        ToolExecutionStatus status,
        string summary,
        TimeSpan duration,
        string safeErrorCode,
        string safeErrorMessage) =>
        new(toolId, status, null, summary, duration, safeErrorCode, safeErrorMessage);
}
