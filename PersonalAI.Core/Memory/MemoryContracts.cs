using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public readonly record struct MemoryId(string Value)
{
    public static MemoryId NewId() => new(Guid.NewGuid().ToString("N"));

    public override string ToString() => Value;
}

public enum MemoryKind
{
    ExplicitUserPreference,
    ProjectFact,
    TaskOutcome,
    ConversationSummary,
    DocumentFact,
    WorkflowNote,
    Correction,
    Exclusion
}

public enum MemoryScope
{
    Global,
    Project,
    Workspace,
    Conversation,
    Task
}

public enum MemoryVisibility
{
    Active,
    Archived,
    Disabled
}

public enum MemoryConfidence
{
    Low,
    Medium,
    High
}

public enum MemorySensitivity
{
    Normal,
    Sensitive,
    Secret
}

public sealed record MemoryRetentionPolicy(
    int? RetentionDays,
    bool ArchiveOnExpiry)
{
    public static MemoryRetentionPolicy Default { get; } = new(null, false);
}

public sealed record MemorySource(
    string SourceType,
    DateTimeOffset TimestampUtc,
    Guid? ConversationId = null,
    TaskId? TaskRunId = null,
    WorkspaceId? WorkspaceId = null,
    string? ProjectId = null,
    string? RelativeFilePath = null,
    string? DocumentId = null,
    string? ChunkId = null,
    string? Excerpt = null,
    MemoryConfidence Confidence = MemoryConfidence.Medium)
{
    public const int MaxExcerptCharacters = 500;

    public bool IsSystemSafePlaceholder =>
        string.Equals(SourceType, "system_placeholder", StringComparison.OrdinalIgnoreCase);
}

public sealed record MemoryRecord(
    MemoryId Id,
    MemoryKind Kind,
    MemoryScope Scope,
    string Text,
    MemorySource Source,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    MemoryConfidence Confidence,
    MemoryVisibility Visibility = MemoryVisibility.Active,
    MemorySensitivity Sensitivity = MemorySensitivity.Normal,
    MemoryRetentionPolicy? RetentionPolicy = null,
    string? ProjectId = null,
    WorkspaceId? WorkspaceId = null,
    Guid? ConversationId = null,
    TaskId? TaskRunId = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public bool IsSearchable => Visibility == MemoryVisibility.Active;
}

public sealed record MemorySearchQuery(
    string? Text = null,
    MemoryScope? Scope = null,
    MemoryKind? Kind = null,
    string? ProjectId = null,
    WorkspaceId? WorkspaceId = null,
    Guid? ConversationId = null,
    TaskId? TaskRunId = null,
    bool IncludeArchived = false,
    bool IncludeSensitive = false,
    int Limit = 20);

public sealed record MemorySearchResult(
    MemoryRecord Memory,
    double Score,
    MemorySource Source);

public interface IMemoryRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<MemoryRecord> CreateAsync(
        MemoryRecord memory,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        MemoryRecord memory,
        CancellationToken cancellationToken = default);

    Task ArchiveAsync(
        MemoryId memoryId,
        DateTimeOffset archivedAtUtc,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<MemoryRecord?> GetAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemoryRecord>> ListAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default);
}
