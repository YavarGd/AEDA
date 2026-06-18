namespace PersonalAI.Core.Tasks;

public sealed record TaskRun(
    TaskId Id,
    string Title,
    TaskRunStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? SafeErrorCode = null,
    string Source = "unknown",
    Guid? ConversationId = null,
    string? Model = null,
    string? Provider = null)
{
    public static TaskRun Create(
        string title,
        string source = "unknown",
        Guid? conversationId = null,
        string? model = null,
        string? provider = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new TaskRun(
            TaskId.NewId(),
            TaskEventMetadata.SanitizeSummary(title),
            TaskRunStatus.Created,
            now,
            now,
            Source: TaskEventMetadata.SanitizeSummary(source),
            ConversationId: conversationId,
            Model: string.IsNullOrWhiteSpace(model)
                ? null
                : TaskEventMetadata.SanitizeSummary(model),
            Provider: string.IsNullOrWhiteSpace(provider)
                ? null
                : TaskEventMetadata.SanitizeSummary(provider));
    }
}
