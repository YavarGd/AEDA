namespace PersonalAI.Core.Tasks;

public enum TaskCancellationReason
{
    UserRequested,
    PermissionDenied,
    ApprovalDenied,
    Timeout,
    Shutdown,
    Unknown
}
