namespace PersonalAI.Core.Tasks;

public enum TaskEventKind
{
    TaskCreated,
    TaskStarted,
    TaskStatusChanged,
    StepStarted,
    PermissionRequested,
    PermissionGranted,
    PermissionDenied,
    ToolRequested,
    ToolStarted,
    ToolCompleted,
    ToolCancelled,
    ToolTimedOut,
    ToolFailed,
    ApprovalRequested,
    ApprovalGranted,
    ApprovalDenied,
    ArtifactCreated,
    MessageEmitted,
    TaskPaused,
    TaskResumed,
    TaskCompleted,
    TaskCancelled,
    TaskFailed
}
