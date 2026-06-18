namespace PersonalAI.Core.Tasks;

public sealed record TaskCheckpoint(
    Guid Id,
    TaskId TaskId,
    string Title,
    string Body,
    DateTimeOffset CreatedAtUtc)
{
    public static TaskCheckpoint Create(TaskId taskId, string title, string body) =>
        new(
            Guid.NewGuid(),
            taskId,
            TaskEventMetadata.SanitizeSummary(title),
            TaskEventMetadata.SanitizeSummary(body),
            DateTimeOffset.UtcNow);
}
