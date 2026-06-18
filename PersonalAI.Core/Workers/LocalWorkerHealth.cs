namespace PersonalAI.Core.Workers;

public sealed record LocalWorkerHealth(
    string WorkerId,
    LocalWorkerStatus Status,
    string? SafeErrorCode = null);
