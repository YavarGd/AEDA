using PersonalAI.Core.Tools;

namespace PersonalAI.Core.Tasks;

public sealed record TaskEvent(
    Guid EventId,
    TaskId TaskId,
    DateTimeOffset TimestampUtc,
    TaskEventKind Kind,
    string Summary,
    TaskExecutionState? State = null,
    ToolId? ToolId = null,
    int? ProgressPercent = null,
    string? ProgressLabel = null,
    IReadOnlyDictionary<string, string>? SafeMetadata = null,
    string? SafeErrorCode = null,
    string? SafeErrorMessage = null)
{
    public static TaskEvent Create(
        TaskId taskId,
        TaskEventKind kind,
        string summary,
        TaskExecutionState? state = null,
        ToolId? toolId = null,
        int? progressPercent = null,
        string? progressLabel = null,
        IReadOnlyDictionary<string, string>? safeMetadata = null,
        string? safeErrorCode = null,
        string? safeErrorMessage = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        if (progressPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(progressPercent),
                "Progress must be between 0 and 100.");
        }

        return new TaskEvent(
            Guid.NewGuid(),
            taskId,
            DateTimeOffset.UtcNow,
            kind,
            summary,
            state,
            toolId,
            progressPercent,
            progressLabel,
            safeMetadata,
            safeErrorCode,
            safeErrorMessage);
    }
}
