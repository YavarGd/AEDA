namespace PersonalAI.Core.Tools;

public sealed record ToolValidationResult(
    bool IsValid,
    string? SafeErrorCode = null,
    string? SafeErrorMessage = null)
{
    public static ToolValidationResult Success { get; } = new(true);

    public static ToolValidationResult Failure(
        string safeErrorCode,
        string safeErrorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(safeErrorCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeErrorMessage);

        return new ToolValidationResult(
            false,
            safeErrorCode,
            safeErrorMessage);
    }
}
