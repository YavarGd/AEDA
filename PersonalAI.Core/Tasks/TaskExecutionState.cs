namespace PersonalAI.Core.Tasks;

public enum TaskExecutionState
{
    Created,
    Running,
    WaitingForPermission,
    Completed,
    Cancelled,
    Failed
}
