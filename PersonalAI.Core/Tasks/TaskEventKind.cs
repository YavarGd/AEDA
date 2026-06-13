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
    ToolFailed,
    TaskCompleted,
    TaskCancelled,
    TaskFailed
}
