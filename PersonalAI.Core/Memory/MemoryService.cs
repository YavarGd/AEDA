using System.Text.RegularExpressions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public sealed record SaveMemoryRequest(
    MemoryKind Kind,
    MemoryScope Scope,
    string Text,
    MemorySource Source,
    bool IsExplicit,
    bool SensitiveApproved = false,
    MemorySensitivity Sensitivity = MemorySensitivity.Normal,
    MemoryConfidence Confidence = MemoryConfidence.Medium,
    string? ProjectId = null,
    WorkspaceId? WorkspaceId = null,
    Guid? ConversationId = null,
    TaskId? TaskRunId = null);

public sealed record SaveMemoryResult(
    bool Succeeded,
    MemoryRecord? Memory,
    string? SafeReasonCode = null);

public interface IMemoryService
{
    Task<SaveMemoryResult> SaveExplicitMemoryAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    Task<SaveMemoryResult> SaveProjectFactAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    Task<SaveMemoryResult> SaveTaskOutcomeAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    Task<SaveMemoryResult> SaveCorrectionAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task ArchiveAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class MemoryService(
    IMemoryRepository repository,
    IMemoryPolicyEvaluator policyEvaluator,
    MemoryPolicy policy) : IMemoryService
{
    public Task<SaveMemoryResult> SaveExplicitMemoryAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            request with { IsExplicit = true },
            MemoryKind.ExplicitUserPreference,
            cancellationToken);

    public Task<SaveMemoryResult> SaveProjectFactAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default) =>
        SaveAsync(request, MemoryKind.ProjectFact, cancellationToken);

    public Task<SaveMemoryResult> SaveTaskOutcomeAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default) =>
        SaveAsync(
            request with { IsExplicit = true },
            MemoryKind.TaskOutcome,
            cancellationToken);

    public Task<SaveMemoryResult> SaveCorrectionAsync(
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default) =>
        SaveAsync(request with { IsExplicit = true }, MemoryKind.Correction, cancellationToken);

    public Task DeleteAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default) =>
        repository.DeleteAsync(memoryId, cancellationToken);

    public Task ArchiveAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default) =>
        repository.ArchiveAsync(memoryId, DateTimeOffset.UtcNow, cancellationToken);

    public Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default) =>
        repository.SearchAsync(query, cancellationToken);

    private async Task<SaveMemoryResult> SaveAsync(
        SaveMemoryRequest request,
        MemoryKind expectedKind,
        CancellationToken cancellationToken)
    {
        var sourceValidation = ValidateSource(request.Source);
        if (sourceValidation is not null)
        {
            return new SaveMemoryResult(false, null, sourceValidation);
        }

        if (ContainsPrivateReasoning(request.Text))
        {
            return new SaveMemoryResult(false, null, "private_reasoning_rejected");
        }

        if (LooksLikeSecret(request.Text))
        {
            return new SaveMemoryResult(false, null, "secret_memory_rejected");
        }

        var sensitivity = request.Sensitivity;
        var decision = policyEvaluator.CanWrite(
            policy,
            expectedKind,
            request.Scope,
            sensitivity,
            request.IsExplicit,
            request.WorkspaceId,
            request.ProjectId);
        if (decision.Kind == MemoryWriteDecisionKind.RequiresApproval &&
            request.SensitiveApproved)
        {
            decision = MemoryWriteDecision.Allowed;
        }

        if (!decision.IsAllowed)
        {
            return new SaveMemoryResult(false, null, decision.SafeReasonCode);
        }

        var now = DateTimeOffset.UtcNow;
        var source = request.Source with
        {
            TimestampUtc = request.Source.TimestampUtc.ToUniversalTime(),
            Excerpt = Bound(request.Source.Excerpt, MemorySource.MaxExcerptCharacters)
        };
        var memory = new MemoryRecord(
            MemoryId.NewId(),
            expectedKind,
            request.Scope,
            Bound(request.Text.Trim(), 4000),
            source,
            now,
            now,
            request.Confidence,
            Sensitivity: sensitivity,
            RetentionPolicy: new MemoryRetentionPolicy(policy.RetentionDays, true),
            ProjectId: request.ProjectId,
            WorkspaceId: request.WorkspaceId,
            ConversationId: request.ConversationId,
            TaskRunId: request.TaskRunId);

        return new SaveMemoryResult(
            true,
            await repository.CreateAsync(memory, cancellationToken));
    }

    public static string? ValidateSource(MemorySource source)
    {
        if (string.IsNullOrWhiteSpace(source.SourceType))
        {
            return "memory_source_required";
        }

        if (source.TimestampUtc == default)
        {
            return "memory_source_timestamp_required";
        }

        if (source.IsSystemSafePlaceholder)
        {
            return null;
        }

        var hasAttribution =
            source.ConversationId is not null ||
            source.TaskRunId is not null ||
            source.WorkspaceId is not null ||
            !string.IsNullOrWhiteSpace(source.ProjectId) ||
            !string.IsNullOrWhiteSpace(source.RelativeFilePath) ||
            !string.IsNullOrWhiteSpace(source.DocumentId) ||
            !string.IsNullOrWhiteSpace(source.ChunkId) ||
            string.Equals(source.SourceType, "explicit_user_save", StringComparison.OrdinalIgnoreCase);

        return hasAttribution ? null : "memory_source_attribution_required";
    }

    private static string Bound(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static bool ContainsPrivateReasoning(string text) =>
        text.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("private reasoning", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecret(string text) =>
        Regex.IsMatch(
            text,
            @"(?i)(api[_-]?key|password|secret|token)\s*[:=]\s*\S{8,}");
}
