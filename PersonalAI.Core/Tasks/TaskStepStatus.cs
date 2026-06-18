namespace PersonalAI.Core.Tasks;

public enum TaskStepStatus
{
    Created,
    Running,
    WaitingForApproval,
    Cancelled,
    Completed,
    Failed
}
