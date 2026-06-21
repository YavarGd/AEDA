using PersonalAI.Core.Modules;

namespace PersonalAI.Core.Research;

public readonly record struct VerificationReportId(string Value)
{
    public override string ToString() => Value;

    public static VerificationReportId NewId() => new(Guid.NewGuid().ToString("N"));
}

public readonly record struct ResearchClaimId(string Value)
{
    public override string ToString() => Value;

    public static ResearchClaimId NewId() => new(Guid.NewGuid().ToString("N"));
}

public readonly record struct EvidenceItemId(string Value)
{
    public override string ToString() => Value;

    public static EvidenceItemId NewId() => new(Guid.NewGuid().ToString("N"));
}

public enum ResearchClaimKind
{
    Factual,
    CurrentOrTimeSensitive,
    CodeRelated,
    DocumentOrSourceRelated,
    SubjectiveOpinion,
    Unverifiable
}

public enum EvidenceSourceType
{
    Memory,
    ProjectMemory,
    WorkspaceChunk,
    KnowledgeDocument,
    TaskOutcome,
    Manual,
    ExternalSearch
}

public enum EvidenceQuality
{
    Unknown,
    Low,
    Medium,
    High
}

public enum VerificationVerdict
{
    Supported,
    PartiallySupported,
    Contradicted,
    Unresolved,
    NeedsMoreEvidence,
    NotVerifiable
}

public enum VerificationConfidence
{
    Low,
    Medium,
    High
}

public enum VerificationReportStatus
{
    Draft,
    Completed,
    Cancelled,
    Failed
}

public sealed record ResearchQuestion(
    string Text,
    string? WorkspaceId = null,
    string? ProjectId = null,
    DateTimeOffset? CreatedAtUtc = null);

public sealed record ResearchScope(
    bool LocalEvidenceOnly = true,
    bool IncludeSensitive = false,
    bool AllowRemoteSearch = false,
    int MaxClaims = 8,
    int MaxEvidenceItems = 12,
    int MaxEvidenceCharacters = 6000,
    string? WorkspaceId = null,
    string? ProjectId = null);

public sealed record ResearchClaim(
    ResearchClaimId Id,
    string Text,
    ResearchClaimKind Kind,
    DateTimeOffset CreatedAtUtc);

public sealed record EvidenceSource(
    string SourceId,
    EvidenceSourceType Type,
    string Label,
    DateTimeOffset TimestampUtc,
    bool IsLocal,
    EvidenceQuality Quality,
    string? RelativePath = null,
    string? TraceId = null,
    string? ContentHash = null);

public sealed record EvidenceItem(
    EvidenceItemId Id,
    string ClaimId,
    string Excerpt,
    EvidenceSource Source,
    double Score,
    bool SupportsClaim,
    bool ContradictsClaim,
    DateTimeOffset CreatedAtUtc);

public sealed record CitationReference(
    string CitationId,
    EvidenceItemId EvidenceItemId,
    string SourceLabel,
    string? RelativePath,
    string? TraceId);

public sealed record VerificationFinding(
    ResearchClaimId ClaimId,
    VerificationVerdict Verdict,
    VerificationConfidence Confidence,
    IReadOnlyList<CitationReference> Citations,
    string SafeSummary,
    IReadOnlyList<string> UnresolvedGaps);

public sealed record VerificationReport(
    VerificationReportId Id,
    ResearchQuestion Question,
    IReadOnlyList<ResearchClaim> Claims,
    IReadOnlyList<EvidenceItem> Evidence,
    IReadOnlyList<VerificationFinding> Findings,
    VerificationVerdict OverallVerdict,
    VerificationConfidence Confidence,
    VerificationReportStatus Status,
    bool LocalEvidenceOnly,
    bool RemoteSearchWasUsed,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<string> UnresolvedGaps);

public sealed record EvidenceProviderStatus(
    string ProviderId,
    string DisplayName,
    EvidenceSourceType SourceType,
    bool IsLocal,
    bool IsEnabled,
    string SafeStatusCode);

public sealed record EvidenceSearchRequest(
    string Query,
    ResearchScope Scope,
    int MaxItems = 8,
    int MaxCharacters = 4000);

public sealed record ClaimExtractionRequest(
    string Text,
    int MaxClaims = 8);

public sealed record AedaResearchDashboardModel(
    AedaModuleDescriptor Descriptor,
    string PrivacyStatus,
    IReadOnlyList<string> CapabilityBadges,
    IReadOnlyList<EvidenceProviderStatus> EvidenceProviders,
    IReadOnlyList<VerificationReport> RecentReports,
    string SafeStatusMessage);

public sealed record VerificationRequest(
    string Text,
    ResearchScope Scope);

public sealed record ResearchSuggestion(
    bool ShouldSuggest,
    string Message,
    bool AutoLaunch);

public interface IClaimExtractionService
{
    Task<IReadOnlyList<ResearchClaim>> ExtractClaimsAsync(
        ClaimExtractionRequest request,
        CancellationToken cancellationToken = default);
}

public interface IEvidenceProvider
{
    EvidenceProviderStatus GetStatus();

    Task<IReadOnlyList<EvidenceItem>> SearchAsync(
        EvidenceSearchRequest request,
        IReadOnlyList<ResearchClaim> claims,
        CancellationToken cancellationToken = default);
}

public interface IVerificationReportRepository
{
    Task SaveAsync(
        VerificationReport report,
        CancellationToken cancellationToken = default);

    Task<VerificationReport?> GetAsync(
        VerificationReportId reportId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VerificationReport>> ListRecentAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);
}

public interface IAedaResearchModuleService
{
    Task<AedaModuleDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default);

    Task<AedaResearchDashboardModel> GetDashboardAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ResearchClaim>> ExtractClaimsAsync(
        string text,
        int maxClaims = 8,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvidenceItem>> SearchLocalEvidenceAsync(
        string query,
        ResearchScope? scope = null,
        CancellationToken cancellationToken = default);

    Task<VerificationReport> VerifyWithLocalEvidenceAsync(
        VerificationRequest request,
        CancellationToken cancellationToken = default);

    Task<VerificationReport?> GetReportAsync(
        VerificationReportId reportId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VerificationReport>> ListReportsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default);

    Task<ResearchSuggestion> CreateAreYouSureSuggestionAsync(
        string text,
        CancellationToken cancellationToken = default);
}
