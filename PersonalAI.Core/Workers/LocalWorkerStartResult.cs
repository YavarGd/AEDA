namespace PersonalAI.Core.Workers;

public sealed record LocalWorkerStartResult(
    string WorkerId,
    LocalWorkerStatus Status,
    string? SafeErrorCode = null)
{
    public bool IsSuccess => Status == LocalWorkerStatus.Running;
}
