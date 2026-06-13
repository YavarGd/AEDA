using PersonalAI.Core.Tools;

namespace PersonalAI.Core.Tasks;

public sealed record TaskEvent
{
    private TaskEvent(
        Guid eventId,
        TaskId taskId,
        DateTimeOffset timestampUtc,
        TaskEventKind kind,
        string summary,
        TaskExecutionState? state,
        ToolId? toolId,
        int? progressPercent,
        string? progressLabel,
        IReadOnlyDictionary<string, string>? safeMetadata,
        string? safeErrorCode,
        string? safeErrorMessage)
    {
        EventId = eventId;
        TaskId = taskId;
        TimestampUtc = timestampUtc;
        Kind = kind;
        Summary = summary;
        State = state;
        ToolId = toolId;
        ProgressPercent = progressPercent;
        ProgressLabel = progressLabel;
        SafeMetadata = safeMetadata;
        SafeErrorCode = safeErrorCode;
        SafeErrorMessage = safeErrorMessage;
    }

    public Guid EventId { get; }

    public TaskId TaskId { get; }

    public DateTimeOffset TimestampUtc { get; }

    public TaskEventKind Kind { get; }

    public string Summary { get; }

    public TaskExecutionState? State { get; }

    public ToolId? ToolId { get; }

    public int? ProgressPercent { get; }

    public string? ProgressLabel { get; }

    public IReadOnlyDictionary<string, string>? SafeMetadata { get; }

    public string? SafeErrorCode { get; }

    public string? SafeErrorMessage { get; }

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
            TaskEventMetadata.SanitizeSummary(summary),
            state,
            toolId,
            progressPercent,
            TaskEventMetadata.SanitizeProgressLabel(progressLabel),
            TaskEventMetadata.Normalize(safeMetadata),
            TaskEventMetadata.SanitizeErrorCode(safeErrorCode),
            safeErrorMessage is null
                ? null
                : TaskEventMetadata.SanitizeSummary(safeErrorMessage));
    }
}
