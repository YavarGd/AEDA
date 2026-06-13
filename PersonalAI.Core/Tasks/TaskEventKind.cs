namespace PersonalAI.Core.Tasks;

public enum TaskEventKind
{
    TaskCreated,
    TaskStarted,
    TaskStatusChanged,
    PermissionRequested,
    PermissionGranted,
    PermissionDenied,
    ToolRequested,
    ToolStarted,
    ToolCompleted,
    ToolCancelled,
    ToolTimedOut,
    ToolFailed,
    TaskCompleted,
    TaskCancelled,
    TaskFailed
}
