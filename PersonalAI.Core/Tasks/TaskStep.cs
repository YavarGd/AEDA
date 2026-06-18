namespace PersonalAI.Core.Tasks;

public sealed record TaskStep(
    Guid Id,
    TaskId TaskId,
    string Title,
    TaskStepStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc)
{
    public static TaskStep Create(TaskId taskId, string title)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskStep(
            Guid.NewGuid(),
            taskId,
            TaskEventMetadata.SanitizeSummary(title),
            TaskStepStatus.Created,
            now,
            now);
    }
}
