namespace PersonalAI.Core.Tasks;

public sealed record TaskArtifact(
    Guid Id,
    string Name,
    string Kind,
    string? SafeUri,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyDictionary<string, string>? SafeMetadata = null)
{
    public static TaskArtifact Create(
        string name,
        string kind,
        string? safeUri = null,
        IReadOnlyDictionary<string, string>? safeMetadata = null) =>
        new(
            Guid.NewGuid(),
            TaskEventMetadata.SanitizeSummary(name),
            TaskEventMetadata.SanitizeSummary(kind),
            safeUri is null ? null : TaskEventMetadata.SanitizeSummary(safeUri),
            DateTimeOffset.UtcNow,
            TaskEventMetadata.Normalize(safeMetadata));
}
