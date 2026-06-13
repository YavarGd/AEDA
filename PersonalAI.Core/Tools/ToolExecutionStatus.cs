namespace PersonalAI.Core.Tools;

public enum ToolExecutionStatus
{
    Succeeded,
    ValidationFailed,
    PermissionDenied,
    Cancelled,
    TimedOut,
    ToolFailed,
    UnhandledFailure
}
