namespace PersonalAI.Core.Tasks;

public enum TaskRunStatus
{
    Created,
    Running,
    WaitingForApproval,
    Paused,
    Cancelling,
    Cancelled,
    Completed,
    Failed
}
