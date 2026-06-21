using PersonalAI.Core.Memory;
using PersonalAI.Core.Research;

namespace PersonalAI.Infrastructure.Research;

public sealed class LocalRagEvidenceProvider(IRetrievalService retrievalService) : IEvidenceProvider
{
    private const int MaxExcerptCharacters = 700;

    public EvidenceProviderStatus GetStatus() =>
        new(
            "local_rag",
            "Local memory and indexed knowledge",
            EvidenceSourceType.Memory,
            IsLocal: true,
            IsEnabled: true,
            "local_rag_available");

    public async Task<IReadOnlyList<EvidenceItem>> SearchAsync(
        EvidenceSearchRequest request,
        IReadOnlyList<ResearchClaim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(claims);

        if (string.IsNullOrWhiteSpace(request.Query) || claims.Count == 0)
        {
            return [];
        }

        var maxItems = Math.Clamp(request.MaxItems, 1, 20);
        var maxCharacters = Math.Clamp(request.MaxCharacters, 1, 20_000);
        var pack = await retrievalService.RetrieveAsync(
            new RetrievalQuery(
                request.Query.Trim(),
                ProjectId: request.Scope.ProjectId,
                WorkspaceId: request.Scope.WorkspaceId,
                IncludeSensitive: request.Scope.IncludeSensitive,
                MaxItems: maxItems,
                MaxCharacters: maxCharacters),
            cancellationToken).ConfigureAwait(false);

        var items = new List<EvidenceItem>();
        foreach (var retrieved in pack.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var excerpt = Bound(retrieved.Text, MaxExcerptCharacters);
            if (string.IsNullOrWhiteSpace(excerpt))
            {
                continue;
            }

            var claim = claims
                .OrderByDescending(candidate => ScoreOverlap(candidate.Text, excerpt))
                .ThenBy(candidate => candidate.Id.Value, StringComparer.Ordinal)
                .First();
            var overlap = ScoreOverlap(claim.Text, excerpt);
            items.Add(new EvidenceItem(
                EvidenceItemId.NewId(),
                claim.Id.Value,
                excerpt,
                ToSource(retrieved),
                retrieved.Score,
                SupportsClaim: overlap > 0,
                ContradictsClaim: LooksContradictory(claim.Text, excerpt),
                DateTimeOffset.UtcNow));
        }

        return items
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source.Label, StringComparer.Ordinal)
            .Take(maxItems)
            .ToArray();
    }

    private static EvidenceSource ToSource(RetrievalContextItem item) =>
        new(
            item.TraceId ?? item.ContentHash ?? item.Source.SourceType,
            MapSourceType(item),
            Bound(item.SourceLabel ?? CreateSourceLabel(item.Source), 160),
            item.Source.TimestampUtc,
            IsLocal: true,
            MapQuality(item.Confidence),
            NormalizeRelativePath(item.Source.RelativeFilePath),
            SafeTraceId(item.TraceId),
            item.ContentHash);

    private static EvidenceSourceType MapSourceType(RetrievalContextItem item) =>
        item.Kind switch
        {
            RetrievalContextItemKind.KnowledgeChunk => EvidenceSourceType.WorkspaceChunk,
            RetrievalContextItemKind.TaskOutcome => EvidenceSourceType.TaskOutcome,
            _ when item.Source.SourceType.Contains("project", StringComparison.OrdinalIgnoreCase) =>
                EvidenceSourceType.ProjectMemory,
            _ => EvidenceSourceType.Memory
        };

    private static EvidenceQuality MapQuality(MemoryConfidence confidence) =>
        confidence switch
        {
            MemoryConfidence.High => EvidenceQuality.High,
            MemoryConfidence.Low => EvidenceQuality.Low,
            _ => EvidenceQuality.Medium
        };

    private static string CreateSourceLabel(MemorySource source) =>
        source.RelativeFilePath ?? source.ProjectId ?? source.WorkspaceId?.ToString() ?? source.SourceType;

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

    internal static int ScoreOverlap(string claim, string excerpt)
    {
        var terms = claim
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(term => term.Trim('.', ',', ';', ':', '!', '?', '"', '\'').ToLowerInvariant())
            .Where(term => term.Length >= 4)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return terms.Count(term => excerpt.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool LooksContradictory(string claim, string excerpt)
    {
        var lowerClaim = claim.ToLowerInvariant();
        var lowerExcerpt = excerpt.ToLowerInvariant();
        return ContainsNegation(lowerClaim) != ContainsNegation(lowerExcerpt) &&
            ScoreOverlap(claim, excerpt) > 0;
    }

    private static bool ContainsNegation(string text) =>
        text.Contains(" not ", StringComparison.Ordinal) ||
        text.Contains(" no ", StringComparison.Ordinal) ||
        text.Contains(" never ", StringComparison.Ordinal) ||
        text.Contains("false", StringComparison.Ordinal) ||
        text.Contains("unsupported", StringComparison.Ordinal);

    private static string? SafeTraceId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static string Bound(string value, int maxCharacters)
    {
        var singleLine = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
        return singleLine.Length <= maxCharacters ? singleLine : singleLine[..maxCharacters];
    }
}

public sealed class FakeEvidenceProvider(
    IReadOnlyList<EvidenceItem>? evidence = null,
    bool enabled = true) : IEvidenceProvider
{
    public EvidenceProviderStatus GetStatus() =>
        new(
            "fake_manual",
            "Fake manual evidence",
            EvidenceSourceType.Manual,
            IsLocal: true,
            enabled,
            enabled ? "fake_evidence_available" : "fake_evidence_disabled");

    public Task<IReadOnlyList<EvidenceItem>> SearchAsync(
        EvidenceSearchRequest request,
        IReadOnlyList<ResearchClaim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled)
        {
            return Task.FromResult<IReadOnlyList<EvidenceItem>>([]);
        }

        var maxItems = Math.Clamp(request.MaxItems, 1, 20);
        var claimIds = claims.Select(claim => claim.Id.Value).ToHashSet(StringComparer.Ordinal);
        var selected = (evidence ?? [])
            .Where(item => claimIds.Count == 0 || claimIds.Contains(item.ClaimId))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source.Label, StringComparer.Ordinal)
            .Take(maxItems)
            .ToArray();
        return Task.FromResult<IReadOnlyList<EvidenceItem>>(selected);
    }
}

public sealed class DisabledExternalSearchEvidenceProvider : IEvidenceProvider
{
    public EvidenceProviderStatus GetStatus() =>
        new(
            "external_search",
            "External search provider",
            EvidenceSourceType.ExternalSearch,
            IsLocal: false,
            IsEnabled: false,
            "external_search_unconfigured");

    public Task<IReadOnlyList<EvidenceItem>> SearchAsync(
        EvidenceSearchRequest request,
        IReadOnlyList<ResearchClaim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<EvidenceItem>>([]);
    }
}
