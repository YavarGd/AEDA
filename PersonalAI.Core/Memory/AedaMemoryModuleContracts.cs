using PersonalAI.Core.Modules;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public sealed record AedaMemoryDashboardModel(
    int TotalMemoryCount,
    IReadOnlyDictionary<string, int> CountsByKind,
    IReadOnlyDictionary<string, int> CountsByScope,
    IReadOnlyList<AedaMemoryRecordSummary> RecentMemories,
    IReadOnlyList<AedaMemoryRecordSummary> RecentTaskOutcomes,
    IReadOnlyList<AedaKnowledgeDocumentSummary> RecentDocuments,
    int IndexedDocumentCount,
    int IndexedChunkCount,
    AedaMemoryPolicySummary Policy,
    AedaMemoryPrivacyStatus Privacy,
    bool RetrievalAvailable,
    bool EmbeddingsConfigured,
    bool VectorSearchConfigured,
    string SafeStatusMessage);

public sealed record AedaMemoryRecordSummary(
    string Id,
    AedaMemoryKindBadge Kind,
    AedaMemoryScopeBadge Scope,
    string PreviewText,
    string Visibility,
    string SensitivityStatus,
    string SourceLabel,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaMemoryRecordDetail(
    string Id,
    AedaMemoryKindBadge Kind,
    AedaMemoryScopeBadge Scope,
    string Text,
    string Visibility,
    string SensitivityStatus,
    string Confidence,
    AedaMemorySourceSummary Source,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaMemorySourceSummary(
    string SourceType,
    string DisplayName,
    string? RelativePath,
    string? Excerpt,
    DateTimeOffset TimestampUtc);

public sealed record AedaKnowledgeDocumentSummary(
    string Id,
    string Title,
    string SourceType,
    string? WorkspaceId,
    string? RelativePath,
    string State,
    string TraceId,
    int ChunkCount,
    DateTimeOffset UpdatedAtUtc,
    string? SafeStatusCode);

public sealed record AedaKnowledgeChunkSummary(
    string Id,
    int Ordinal,
    string PreviewText,
    string ContentHash,
    AedaMemorySourceSummary Source,
    DateTimeOffset UpdatedAtUtc);

public sealed record AedaMemoryPolicySummary(
    bool MemoryEnabled,
    bool ExplicitMemoryEnabled,
    bool AutomaticMemoryEnabled,
    bool ProjectMemoryEnabled,
    bool TaskOutcomeMemoryEnabled,
    bool SensitiveMemoryRequiresApproval,
    bool LocalOnly,
    int RetentionDays,
    int ExclusionRuleCount);

public sealed record AedaMemoryPrivacyStatus(
    string LocalOnlyStatus,
    string AutomaticMemoryStatus,
    string SensitiveMemoryStatus,
    string SourceTextStatus,
    IReadOnlyList<string> ExclusionSummaries);

public sealed record AedaRetrievalPreviewItem(
    string Kind,
    string PreviewText,
    double Score,
    string SourceLabel,
    string MatchType,
    string? TraceId,
    string? ContentHash);

public sealed record AedaMemoryKindBadge(
    string Id,
    string Label);

public sealed record AedaMemoryScopeBadge(
    string Id,
    string Label);

public sealed record AedaMemoryCreateRequest(
    MemoryKind Kind,
    MemoryScope Scope,
    string Text,
    string SourceReason,
    MemorySensitivity Sensitivity = MemorySensitivity.Normal,
    bool SensitiveApproved = false,
    string? ProjectId = null,
    WorkspaceId? WorkspaceId = null);

public sealed record AedaMemoryUpdateRequest(
    MemoryId MemoryId,
    string Text,
    MemoryKind Kind,
    MemoryScope Scope,
    MemorySensitivity Sensitivity,
    bool SensitiveApproved = false);

public sealed record AedaMemoryOperationResult(
    bool Succeeded,
    AedaMemoryRecordDetail? Memory = null,
    string? SafeReasonCode = null);

public interface IAedaMemoryModuleService
{
    Task<AedaModuleDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default);

    Task<AedaMemoryDashboardModel> GetDashboardAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaMemoryRecordSummary>> ListMemoriesAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaMemoryRecordSummary>> SearchMemoriesAsync(
        string text,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryRecordDetail?> GetMemoryDetailAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> CreateExplicitMemoryAsync(
        AedaMemoryCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> CreateProjectFactAsync(
        AedaMemoryCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> UpdateMemoryAsync(
        AedaMemoryUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> ArchiveMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> DeleteMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryOperationResult> RestoreArchivedMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaMemorySourceSummary>> ListMemorySourcesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaMemoryRecordSummary>> ListMemoriesBySourceTypeAsync(
        string sourceType,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaKnowledgeDocumentSummary>> ListIndexedDocumentsAsync(
        string? workspaceId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaKnowledgeChunkSummary>> ListChunksForDocumentAsync(
        string documentId,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaKnowledgeChunkSummary>> SearchIndexedKnowledgeAsync(
        string text,
        string? workspaceId = null,
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AedaRetrievalPreviewItem>> PreviewRetrievalAsync(
        string text,
        int limit = 6,
        CancellationToken cancellationToken = default);

    Task<AedaMemoryPolicySummary> GetPolicyStatusAsync(
        CancellationToken cancellationToken = default);
}
