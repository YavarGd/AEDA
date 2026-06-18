namespace PersonalAI.Core.Tasks;

public sealed record TaskRun(
    TaskId Id,
    string Title,
    TaskRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SafeErrorCode = null)
{
    public static TaskRun Create(string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskRun(
            TaskId.NewId(),
            TaskEventMetadata.SanitizeSummary(title),
            TaskRunStatus.Created,
            now,
            now);
    }
}
