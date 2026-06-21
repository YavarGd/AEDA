using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Infrastructure.Memory;

public sealed class AedaMemoryModuleService(
    IMemoryRepository memoryRepository,
    IMemoryService memoryService,
    IBackendCapabilityRegistry capabilities,
    MemoryPolicy policy,
    IKnowledgeRepository? knowledgeRepository = null,
    IRetrievalService? retrievalService = null) : IAedaMemoryModuleService
{
    private const int PreviewCharacters = 280;
    private const int DetailCharacters = 4000;
    private const int SourceExcerptCharacters = 500;
    private const int DashboardLimit = 8;

    public Task<AedaModuleDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AedaMemoryModuleDescriptorFactory.Create(capabilities));
    }

    public async Task<AedaMemoryDashboardModel> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await memoryRepository.ListAsync(
            new MemorySearchQuery(IncludeArchived: true, IncludeSensitive: false, Limit: 200),
            cancellationToken).ConfigureAwait(false);
        var documents = knowledgeRepository is null
            ? []
            : await knowledgeRepository.ListDocumentsAsync(limit: 100, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

        var recent = records
            .Where(memory => memory.Visibility == MemoryVisibility.Active)
            .OrderByDescending(memory => memory.UpdatedAtUtc)
            .ThenBy(memory => memory.Id.Value, StringComparer.Ordinal)
            .Take(DashboardLimit)
            .Select(ToSummary)
            .ToArray();
        var taskOutcomes = records
            .Where(memory => memory.Kind == MemoryKind.TaskOutcome)
            .OrderByDescending(memory => memory.UpdatedAtUtc)
            .ThenBy(memory => memory.Id.Value, StringComparer.Ordinal)
            .Take(DashboardLimit)
            .Select(ToSummary)
            .ToArray();
        var documentSummaries = new List<AedaKnowledgeDocumentSummary>();
        var chunkCount = 0;
        foreach (var document in documents
                     .OrderByDescending(document => document.UpdatedAtUtc)
                     .ThenBy(document => document.Id, StringComparer.Ordinal)
                     .Take(DashboardLimit))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunks = knowledgeRepository is null
                ? []
                : await knowledgeRepository.ListChunksAsync(document.Id, 500, cancellationToken)
                    .ConfigureAwait(false);
            chunkCount += chunks.Count;
            documentSummaries.Add(ToDocumentSummary(document, chunks.Count));
        }

        return new AedaMemoryDashboardModel(
            records.Count,
            CountBy(records, memory => memory.Kind.ToString()),
            CountBy(records, memory => memory.Scope.ToString()),
            recent,
            taskOutcomes,
            documentSummaries,
            documents.Count,
            chunkCount,
            CreatePolicySummary(),
            CreatePrivacyStatus(),
            capabilities.GetStatus(BackendCapability.RetrievalPreview).IsAvailable,
            capabilities.GetStatus(BackendCapability.Embeddings).IsAvailable,
            capabilities.GetStatus(BackendCapability.VectorSearch).IsAvailable,
            "Memory dashboard loaded.");
    }

    public async Task<IReadOnlyList<AedaMemoryRecordSummary>> ListMemoriesAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var records = await memoryRepository.ListAsync(
            query with { Limit = Math.Clamp(query.Limit, 1, 100) },
            cancellationToken).ConfigureAwait(false);
        return records
            .OrderByDescending(memory => memory.UpdatedAtUtc)
            .ThenBy(memory => memory.Id.Value, StringComparer.Ordinal)
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<AedaMemoryRecordSummary>> SearchMemoriesAsync(
        string text,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var results = await memoryRepository.SearchAsync(
            new MemorySearchQuery(
                Text: text.Trim(),
                IncludeSensitive: false,
                Limit: Math.Clamp(limit, 1, 50)),
            cancellationToken).ConfigureAwait(false);
        return results
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Memory.UpdatedAtUtc)
            .ThenBy(result => result.Memory.Id.Value, StringComparer.Ordinal)
            .Select(result => ToSummary(result.Memory))
            .ToArray();
    }

    public async Task<AedaMemoryRecordDetail?> GetMemoryDetailAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        var memory = await memoryRepository.GetAsync(memoryId, cancellationToken)
            .ConfigureAwait(false);
        return memory is null ? null : ToDetail(memory);
    }

    public async Task<AedaMemoryOperationResult> CreateExplicitMemoryAsync(
        AedaMemoryCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateRequest(request);
        if (validation is not null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: validation);
        }

        var result = await memoryService.SaveExplicitMemoryAsync(
            CreateSaveRequest(request, isExplicit: true),
            cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result);
    }

    public async Task<AedaMemoryOperationResult> CreateProjectFactAsync(
        AedaMemoryCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateRequest(request);
        if (validation is not null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: validation);
        }

        var result = await memoryService.SaveProjectFactAsync(
            CreateSaveRequest(request with { Kind = MemoryKind.ProjectFact }, isExplicit: true),
            cancellationToken).ConfigureAwait(false);
        return ToOperationResult(result);
    }

    public async Task<AedaMemoryOperationResult> UpdateMemoryAsync(
        AedaMemoryUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "memory_text_required");
        }

        if (ContainsPrivateReasoning(request.Text))
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "private_reasoning_rejected");
        }

        if (LooksLikeSecret(request.Text))
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "secret_memory_rejected");
        }

        if (request.Sensitivity != MemorySensitivity.Normal && !request.SensitiveApproved)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "sensitive_memory_requires_approval");
        }

        var existing = await memoryRepository.GetAsync(request.MemoryId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "memory_not_found");
        }

        var updated = existing with
        {
            Kind = request.Kind,
            Scope = request.Scope,
            Text = Bound(request.Text.Trim(), DetailCharacters),
            Sensitivity = request.Sensitivity,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await memoryRepository.UpdateAsync(updated, cancellationToken).ConfigureAwait(false);
        return new AedaMemoryOperationResult(true, ToDetail(updated));
    }

    public async Task<AedaMemoryOperationResult> ArchiveMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        var existing = await memoryRepository.GetAsync(memoryId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "memory_not_found");
        }

        await memoryService.ArchiveAsync(memoryId, cancellationToken).ConfigureAwait(false);
        var archived = existing with
        {
            Visibility = MemoryVisibility.Archived,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        return new AedaMemoryOperationResult(true, ToDetail(archived));
    }

    public async Task<AedaMemoryOperationResult> DeleteMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        var existing = await memoryRepository.GetAsync(memoryId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "memory_not_found");
        }

        await memoryService.DeleteAsync(memoryId, cancellationToken).ConfigureAwait(false);
        return new AedaMemoryOperationResult(true);
    }

    public async Task<AedaMemoryOperationResult> RestoreArchivedMemoryAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        var existing = await memoryRepository.GetAsync(memoryId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            return new AedaMemoryOperationResult(false, SafeReasonCode: "memory_not_found");
        }

        var restored = existing with
        {
            Visibility = MemoryVisibility.Active,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        await memoryRepository.UpdateAsync(restored, cancellationToken).ConfigureAwait(false);
        return new AedaMemoryOperationResult(true, ToDetail(restored));
    }

    public async Task<IReadOnlyList<AedaMemorySourceSummary>> ListMemorySourcesAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var records = await memoryRepository.ListAsync(
            new MemorySearchQuery(IncludeArchived: true, IncludeSensitive: false, Limit: Math.Clamp(limit, 1, 100)),
            cancellationToken).ConfigureAwait(false);
        return records
            .OrderByDescending(memory => memory.UpdatedAtUtc)
            .ThenBy(memory => memory.Id.Value, StringComparer.Ordinal)
            .Select(memory => ToSourceSummary(memory.Source))
            .ToArray();
    }

    public async Task<IReadOnlyList<AedaMemoryRecordSummary>> ListMemoriesBySourceTypeAsync(
        string sourceType,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceType))
        {
            return [];
        }

        var records = await memoryRepository.ListAsync(
            new MemorySearchQuery(IncludeArchived: true, IncludeSensitive: false, Limit: 200),
            cancellationToken).ConfigureAwait(false);
        return records
            .Where(memory => memory.Source.SourceType.Equals(sourceType.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(memory => memory.UpdatedAtUtc)
            .ThenBy(memory => memory.Id.Value, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .Select(ToSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<AedaKnowledgeDocumentSummary>> ListIndexedDocumentsAsync(
        string? workspaceId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeRepository is null)
        {
            return [];
        }

        var documents = await knowledgeRepository.ListDocumentsAsync(
            workspaceId,
            Math.Clamp(limit, 1, 200),
            cancellationToken).ConfigureAwait(false);
        var summaries = new List<AedaKnowledgeDocumentSummary>();
        foreach (var document in documents
                     .OrderByDescending(document => document.UpdatedAtUtc)
                     .ThenBy(document => document.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunks = await knowledgeRepository.ListChunksAsync(document.Id, 500, cancellationToken)
                .ConfigureAwait(false);
            summaries.Add(ToDocumentSummary(document, chunks.Count));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<AedaKnowledgeChunkSummary>> ListChunksForDocumentAsync(
        string documentId,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeRepository is null || string.IsNullOrWhiteSpace(documentId))
        {
            return [];
        }

        var chunks = await knowledgeRepository.ListChunksAsync(
            documentId.Trim(),
            Math.Clamp(limit, 1, 50),
            cancellationToken).ConfigureAwait(false);
        return chunks
            .OrderBy(chunk => chunk.Ordinal)
            .ThenBy(chunk => chunk.Id, StringComparer.Ordinal)
            .Select(ToChunkSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<AedaKnowledgeChunkSummary>> SearchIndexedKnowledgeAsync(
        string text,
        string? workspaceId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        if (knowledgeRepository is null || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var chunks = await knowledgeRepository.SearchChunksAsync(
            text.Trim(),
            workspaceId,
            Math.Clamp(limit, 1, 50),
            cancellationToken).ConfigureAwait(false);
        return chunks
            .OrderByDescending(chunk => chunk.UpdatedAtUtc)
            .ThenBy(chunk => chunk.Ordinal)
            .ThenBy(chunk => chunk.Id, StringComparer.Ordinal)
            .Select(ToChunkSummary)
            .ToArray();
    }

    public async Task<IReadOnlyList<AedaRetrievalPreviewItem>> PreviewRetrievalAsync(
        string text,
        int limit = 6,
        CancellationToken cancellationToken = default)
    {
        if (retrievalService is null || string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        try
        {
            var pack = await retrievalService.RetrieveAsync(
                new RetrievalQuery(
                    text.Trim(),
                    IncludeSensitive: false,
                    MaxItems: Math.Clamp(limit, 1, 10),
                    MaxCharacters: 2500),
                cancellationToken).ConfigureAwait(false);
            return pack.Items
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.SourceLabel, StringComparer.Ordinal)
                .Select(item => new AedaRetrievalPreviewItem(
                    item.Kind.ToString(),
                    Bound(item.Text, PreviewCharacters),
                    item.Score,
                    item.SourceLabel ?? CreateSourceLabel(item.Source),
                    item.MatchType ?? "retrieval",
                    SafeTraceId(item.TraceId),
                    item.ContentHash))
                .ToArray();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is ArgumentException ||
            exception is IOException)
        {
            return [];
        }
    }

    public Task<AedaMemoryPolicySummary> GetPolicyStatusAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreatePolicySummary());
    }

    private SaveMemoryRequest CreateSaveRequest(
        AedaMemoryCreateRequest request,
        bool isExplicit) =>
        new(
            request.Kind,
            request.Scope,
            request.Text.Trim(),
            new MemorySource(
                NormalizeSourceType(request.SourceReason),
                DateTimeOffset.UtcNow,
                WorkspaceId: request.WorkspaceId,
                ProjectId: request.ProjectId,
                Excerpt: Bound(request.SourceReason, SourceExcerptCharacters),
                Confidence: MemoryConfidence.High),
            isExplicit,
            request.SensitiveApproved,
            request.Sensitivity,
            MemoryConfidence.High,
            request.ProjectId,
            request.WorkspaceId);

    private static string? ValidateCreateRequest(AedaMemoryCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return "memory_text_required";
        }

        if (string.IsNullOrWhiteSpace(request.SourceReason))
        {
            return "memory_source_required";
        }

        if (request.Sensitivity != MemorySensitivity.Normal && !request.SensitiveApproved)
        {
            return "sensitive_memory_requires_approval";
        }

        return null;
    }

    private AedaMemoryOperationResult ToOperationResult(SaveMemoryResult result) =>
        result.Succeeded && result.Memory is not null
            ? new AedaMemoryOperationResult(true, ToDetail(result.Memory))
            : new AedaMemoryOperationResult(false, SafeReasonCode: result.SafeReasonCode ?? "memory_operation_failed");

    private static AedaMemoryRecordSummary ToSummary(MemoryRecord memory) =>
        new(
            memory.Id.Value,
            ToKindBadge(memory.Kind),
            ToScopeBadge(memory.Scope),
            Bound(memory.Text, PreviewCharacters),
            memory.Visibility.ToString(),
            ToSensitivityStatus(memory.Sensitivity),
            CreateSourceLabel(memory.Source),
            memory.UpdatedAtUtc);

    private static AedaMemoryRecordDetail ToDetail(MemoryRecord memory) =>
        new(
            memory.Id.Value,
            ToKindBadge(memory.Kind),
            ToScopeBadge(memory.Scope),
            Bound(memory.Text, DetailCharacters),
            memory.Visibility.ToString(),
            ToSensitivityStatus(memory.Sensitivity),
            memory.Confidence.ToString(),
            ToSourceSummary(memory.Source),
            memory.CreatedAtUtc,
            memory.UpdatedAtUtc);

    private static AedaMemorySourceSummary ToSourceSummary(MemorySource source) =>
        new(
            source.SourceType,
            CreateSourceLabel(source),
            NormalizeRelativePath(source.RelativeFilePath),
            Bound(source.Excerpt, SourceExcerptCharacters),
            source.TimestampUtc);

    private static AedaKnowledgeDocumentSummary ToDocumentSummary(
        KnowledgeDocument document,
        int chunkCount) =>
        new(
            document.Id,
            Bound(document.Title, 160),
            document.Source.Type.ToString(),
            document.Source.WorkspaceId?.Value,
            NormalizeRelativePath(document.Source.RelativePath),
            document.State.ToString(),
            SafeTraceId(document.ContentHash) ?? string.Empty,
            chunkCount,
            document.UpdatedAtUtc,
            document.SafeStatusCode);

    private static AedaKnowledgeChunkSummary ToChunkSummary(KnowledgeChunk chunk) =>
        new(
            chunk.Id,
            chunk.Ordinal,
            Bound(chunk.Text, PreviewCharacters),
            chunk.ContentHash,
            new AedaMemorySourceSummary(
                chunk.Source.Type.ToString(),
                chunk.Source.RelativePath ?? chunk.Source.Type.ToString(),
                NormalizeRelativePath(chunk.Source.RelativePath),
                Bound(chunk.Text, SourceExcerptCharacters),
                chunk.Source.TimestampUtc),
            chunk.UpdatedAtUtc);

    private AedaMemoryPolicySummary CreatePolicySummary() =>
        new(
            policy.MemoryEnabled,
            policy.ExplicitMemoryEnabled,
            policy.AutomaticMemoryEnabled,
            policy.ProjectMemoryEnabled,
            policy.TaskOutcomeMemoryEnabled,
            policy.SensitiveMemoryRequiresApproval,
            policy.LocalOnly,
            policy.RetentionDays,
            policy.ExclusionRules.Count(rule => rule.IsEnabled));

    private AedaMemoryPrivacyStatus CreatePrivacyStatus() =>
        new(
            policy.LocalOnly ? "Local only" : "Remote use restricted by provider policy",
            policy.AutomaticMemoryEnabled ? "Automatic memory enabled" : "Automatic memory disabled",
            policy.SensitiveMemoryRequiresApproval ? "Sensitive memory requires approval" : "Sensitive memory normal policy",
            policy.AllowSourceText ? "Bounded source excerpts allowed" : "Source excerpts disabled",
            policy.ExclusionRules
                .Where(rule => rule.IsEnabled)
                .OrderBy(rule => rule.Pattern, StringComparer.OrdinalIgnoreCase)
                .Take(10)
                .Select(rule => Bound(rule.Pattern, 80))
                .ToArray());

    private static IReadOnlyDictionary<string, int> CountBy(
        IEnumerable<MemoryRecord> records,
        Func<MemoryRecord, string> selector) =>
        records
            .GroupBy(selector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static AedaMemoryKindBadge ToKindBadge(MemoryKind kind) =>
        new(kind.ToString(), SplitWords(kind.ToString()));

    private static AedaMemoryScopeBadge ToScopeBadge(MemoryScope scope) =>
        new(scope.ToString(), SplitWords(scope.ToString()));

    private static string ToSensitivityStatus(MemorySensitivity sensitivity) =>
        sensitivity == MemorySensitivity.Normal
            ? "Normal"
            : "Protected";

    private static string CreateSourceLabel(MemorySource source) =>
        source.SourceType switch
        {
            "explicit_user_save" => "Explicit user save",
            "project_note" => "Project note",
            "task_outcome_request" => "Task outcome",
            "conversation_summary" => "Conversation summary",
            "workspace_document" => "Workspace document",
            "workspace_chunk" => "Indexed chunk",
            _ => SplitWords(source.SourceType)
        };

    private static string NormalizeSourceType(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Contains("project", StringComparison.OrdinalIgnoreCase)
            ? "project_note"
            : "explicit_user_save";
    }

    private static string? NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return Path.IsPathRooted(trimmed)
            ? Path.GetFileName(trimmed)
            : trimmed.Replace('\\', '/');
    }

    private static string? SafeTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 16 ? trimmed : trimmed[..16];
    }

    private static string Bound(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var singleLine = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength];
    }

    private static string SplitWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = new List<char>(value.Length + 8);
        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            if (current is '_' or '-')
            {
                result.Add(' ');
                continue;
            }

            if (index > 0 && char.IsUpper(current) && char.IsLower(value[index - 1]))
            {
                result.Add(' ');
            }

            result.Add(current);
        }

        return new string(result.ToArray());
    }

    private static bool ContainsPrivateReasoning(string text) =>
        text.Contains("chain-of-thought", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("private reasoning", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSecret(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            text,
            @"(?i)(api[_-]?key|password|secret|token)\s*[:=]\s*\S{8,}");
}
