namespace PersonalAI.Core.Workspaces;

public sealed class WorkspaceAccessException(
    string safeErrorCode,
    string safeErrorMessage) : Exception(safeErrorMessage)
{
    public string SafeErrorCode { get; } = safeErrorCode;

    public string SafeErrorMessage { get; } = safeErrorMessage;
}
