using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Research;
using PersonalAI.Core.Tasks;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Infrastructure.Research;

public sealed class AedaResearchModuleService(
    IClaimExtractionService claimExtraction,
    IEnumerable<IEvidenceProvider> evidenceProviders,
    IVerificationReportRepository repository,
    IBackendCapabilityRegistry capabilities,
    ITaskRuntime? taskRuntime = null) : IAedaResearchModuleService
{
    private const int RecentReportLimit = 8;

    private readonly IReadOnlyList<IEvidenceProvider> _evidenceProviders =
        evidenceProviders?.ToArray() ?? throw new ArgumentNullException(nameof(evidenceProviders));

    public Task<AedaModuleDescriptor> GetDescriptorAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AedaResearchModuleDescriptorFactory.Create(capabilities));
    }

    public async Task<AedaResearchDashboardModel> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        var descriptor = await GetDescriptorAsync(cancellationToken).ConfigureAwait(false);
        var reports = await repository.ListRecentAsync(RecentReportLimit, cancellationToken)
            .ConfigureAwait(false);
        return new AedaResearchDashboardModel(
            descriptor,
            "Local evidence only. Remote search is disabled by default.",
            descriptor.Capabilities
                .Where(capability => capability.State == AedaModuleCapabilityState.Available)
                .Take(8)
                .Select(capability => capability.DisplayName)
                .ToArray(),
            _evidenceProviders.Select(provider => provider.GetStatus()).ToArray(),
            reports,
            "Research module ready.");
    }

    public Task<IReadOnlyList<ResearchClaim>> ExtractClaimsAsync(
        string text,
        int maxClaims = 8,
        CancellationToken cancellationToken = default) =>
        claimExtraction.ExtractClaimsAsync(
            new ClaimExtractionRequest(text, Math.Clamp(maxClaims, 1, 20)),
            cancellationToken);

    public async Task<IReadOnlyList<EvidenceItem>> SearchLocalEvidenceAsync(
        string query,
        ResearchScope? scope = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var boundedScope = NormalizeScope(scope);
        var claims = await ExtractClaimsAsync(query, boundedScope.MaxClaims, cancellationToken)
            .ConfigureAwait(false);
        return await GatherEvidenceAsync(query, boundedScope, claims, localOnly: true, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<VerificationReport> VerifyWithLocalEvidenceAsync(
        VerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var taskRun = taskRuntime is null
            ? null
            : await taskRuntime.StartTaskAsync(
                "AEDA Research verification",
                "aeda_research",
                cancellationToken: cancellationToken).ConfigureAwait(false);

        try
        {
            await AppendTaskEventAsync(taskRun?.Id, TaskEventKind.ResearchVerificationRequested, "Research verification requested.", cancellationToken)
                .ConfigureAwait(false);
            var scope = NormalizeScope(request.Scope) with
            {
                LocalEvidenceOnly = true,
                AllowRemoteSearch = false
            };
            var claims = await ExtractClaimsAsync(request.Text, scope.MaxClaims, cancellationToken)
                .ConfigureAwait(false);
            await AppendTaskEventAsync(taskRun?.Id, TaskEventKind.ResearchClaimsExtracted, $"{claims.Count} claim(s) extracted.", cancellationToken)
                .ConfigureAwait(false);
            var evidence = await GatherEvidenceAsync(request.Text, scope, claims, localOnly: true, cancellationToken)
                .ConfigureAwait(false);
            await AppendTaskEventAsync(taskRun?.Id, TaskEventKind.ResearchEvidenceGathered, $"{evidence.Count} evidence item(s) gathered.", cancellationToken)
                .ConfigureAwait(false);
            var report = CreateReport(request.Text, scope, claims, evidence);
            await repository.SaveAsync(report, cancellationToken).ConfigureAwait(false);
            await AppendTaskEventAsync(taskRun?.Id, TaskEventKind.ResearchReportCreated, "Research report created.", cancellationToken)
                .ConfigureAwait(false);
            if (taskRun is not null && taskRuntime is not null)
            {
                await taskRuntime.CompleteTaskAsync(taskRun.Id, cancellationToken).ConfigureAwait(false);
            }

            return report;
        }
        catch (OperationCanceledException)
        {
            if (taskRun is not null && taskRuntime is not null)
            {
                await taskRuntime.CancelTaskAsync(taskRun.Id, TaskCancellationReason.UserRequested, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            if (taskRun is not null && taskRuntime is not null)
            {
                await taskRuntime.FailTaskAsync(taskRun.Id, "research_verification_failed", CancellationToken.None)
                    .ConfigureAwait(false);
            }

            return CreateFailedReport(request.Text, NormalizeScope(request.Scope), "research_verification_failed");
        }
    }

    public Task<VerificationReport?> GetReportAsync(
        VerificationReportId reportId,
        CancellationToken cancellationToken = default) =>
        repository.GetAsync(reportId, cancellationToken);

    public Task<IReadOnlyList<VerificationReport>> ListReportsAsync(
        int limit = 20,
        CancellationToken cancellationToken = default) =>
        repository.ListRecentAsync(Math.Clamp(limit, 1, 100), cancellationToken);

    public Task<ResearchSuggestion> CreateAreYouSureSuggestionAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(AreYouSureReviewSuggester.Suggest(text));
    }

    private async Task<IReadOnlyList<EvidenceItem>> GatherEvidenceAsync(
        string query,
        ResearchScope scope,
        IReadOnlyList<ResearchClaim> claims,
        bool localOnly,
        CancellationToken cancellationToken)
    {
        if (claims.Count == 0)
        {
            return [];
        }

        var providers = _evidenceProviders
            .Where(provider =>
            {
                var status = provider.GetStatus();
                return status.IsEnabled && (!localOnly || status.IsLocal);
            })
            .ToArray();
        var evidence = new List<EvidenceItem>();
        foreach (var provider in providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = Math.Max(1, scope.MaxEvidenceItems - evidence.Count);
            var items = await provider.SearchAsync(
                new EvidenceSearchRequest(
                    query,
                    scope,
                    remaining,
                    scope.MaxEvidenceCharacters),
                claims,
                cancellationToken).ConfigureAwait(false);
            evidence.AddRange(items.Where(HasRequiredAttribution));
            if (evidence.Count >= scope.MaxEvidenceItems)
            {
                break;
            }
        }

        return evidence
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Source.Label, StringComparer.Ordinal)
            .Take(scope.MaxEvidenceItems)
            .ToArray();
    }

    private static VerificationReport CreateReport(
        string text,
        ResearchScope scope,
        IReadOnlyList<ResearchClaim> claims,
        IReadOnlyList<EvidenceItem> evidence)
    {
        var findings = claims.Select(claim => CreateFinding(claim, evidence)).ToArray();
        var overall = CombineVerdicts(findings.Select(finding => finding.Verdict).ToArray());
        var confidence = CombineConfidence(findings.Select(finding => finding.Confidence).ToArray(), evidence.Count);
        var now = DateTimeOffset.UtcNow;
        return new VerificationReport(
            VerificationReportId.NewId(),
            new ResearchQuestion(Bound(text, 1000), scope.WorkspaceId, scope.ProjectId, now),
            claims,
            evidence,
            findings,
            overall,
            confidence,
            VerificationReportStatus.Completed,
            LocalEvidenceOnly: true,
            RemoteSearchWasUsed: false,
            now,
            now,
            findings.SelectMany(finding => finding.UnresolvedGaps).Distinct(StringComparer.Ordinal).ToArray());
    }

    private static VerificationReport CreateFailedReport(
        string text,
        ResearchScope scope,
        string safeReasonCode)
    {
        var now = DateTimeOffset.UtcNow;
        return new VerificationReport(
            VerificationReportId.NewId(),
            new ResearchQuestion(Bound(text, 1000), scope.WorkspaceId, scope.ProjectId, now),
            [],
            [],
            [],
            VerificationVerdict.Unresolved,
            VerificationConfidence.Low,
            VerificationReportStatus.Failed,
            LocalEvidenceOnly: true,
            RemoteSearchWasUsed: false,
            now,
            now,
            [safeReasonCode]);
    }

    private static VerificationFinding CreateFinding(
        ResearchClaim claim,
        IReadOnlyList<EvidenceItem> allEvidence)
    {
        if (claim.Kind is ResearchClaimKind.SubjectiveOpinion or ResearchClaimKind.Unverifiable)
        {
            return new VerificationFinding(
                claim.Id,
                VerificationVerdict.NotVerifiable,
                VerificationConfidence.Low,
                [],
                "Claim is not suitable for evidence verification.",
                ["Claim needs rephrasing as a factual statement."]);
        }

        var evidence = allEvidence
            .Where(item => item.ClaimId == claim.Id.Value)
            .OrderByDescending(item => item.Score)
            .ToArray();
        if (evidence.Length == 0)
        {
            return new VerificationFinding(
                claim.Id,
                VerificationVerdict.Unresolved,
                VerificationConfidence.Low,
                [],
                "No supporting or contradicting local evidence was found.",
                ["No local evidence matched this claim."]);
        }

        var contradictions = evidence.Count(item => item.ContradictsClaim);
        var supports = evidence.Count(item => item.SupportsClaim && !item.ContradictsClaim);
        var verdict = contradictions > 0 && supports == 0
            ? VerificationVerdict.Contradicted
            : contradictions > 0 && supports > 0
                ? VerificationVerdict.PartiallySupported
                : supports > 0
                    ? VerificationVerdict.Supported
                    : VerificationVerdict.NeedsMoreEvidence;
        var citations = evidence
            .Where(item => item.SupportsClaim || item.ContradictsClaim)
            .Take(4)
            .Select((item, index) => new CitationReference(
                $"C{index + 1}",
                item.Id,
                item.Source.Label,
                item.Source.RelativePath,
                item.Source.TraceId))
            .ToArray();
        var confidence = citations.Length >= 2 && verdict == VerificationVerdict.Supported
            ? VerificationConfidence.Medium
            : VerificationConfidence.Low;
        return new VerificationFinding(
            claim.Id,
            verdict,
            confidence,
            citations,
            CreateFindingSummary(verdict),
            verdict is VerificationVerdict.Supported or VerificationVerdict.Contradicted
                ? []
                : ["More independent evidence is needed."]);
    }

    private static string CreateFindingSummary(VerificationVerdict verdict) =>
        verdict switch
        {
            VerificationVerdict.Supported => "Local evidence supports the claim.",
            VerificationVerdict.PartiallySupported => "Local evidence is mixed.",
            VerificationVerdict.Contradicted => "Local evidence contradicts the claim.",
            VerificationVerdict.NeedsMoreEvidence => "Evidence was relevant but insufficient.",
            VerificationVerdict.NotVerifiable => "Claim is not verifiable as written.",
            _ => "Local evidence is insufficient."
        };

    private static VerificationVerdict CombineVerdicts(IReadOnlyList<VerificationVerdict> verdicts)
    {
        if (verdicts.Count == 0)
        {
            return VerificationVerdict.Unresolved;
        }

        if (verdicts.Any(verdict => verdict == VerificationVerdict.Contradicted))
        {
            return VerificationVerdict.Contradicted;
        }

        if (verdicts.All(verdict => verdict == VerificationVerdict.Supported))
        {
            return VerificationVerdict.Supported;
        }

        if (verdicts.Any(verdict => verdict == VerificationVerdict.Supported))
        {
            return VerificationVerdict.PartiallySupported;
        }

        if (verdicts.All(verdict => verdict == VerificationVerdict.NotVerifiable))
        {
            return VerificationVerdict.NotVerifiable;
        }

        return VerificationVerdict.Unresolved;
    }

    private static VerificationConfidence CombineConfidence(
        IReadOnlyList<VerificationConfidence> confidences,
        int evidenceCount)
    {
        if (evidenceCount == 0 || confidences.Count == 0)
        {
            return VerificationConfidence.Low;
        }

        return confidences.All(confidence => confidence == VerificationConfidence.Medium)
            ? VerificationConfidence.Medium
            : VerificationConfidence.Low;
    }

    private static ResearchScope NormalizeScope(ResearchScope? scope)
    {
        var value = scope ?? new ResearchScope();
        return value with
        {
            IncludeSensitive = false,
            AllowRemoteSearch = false,
            MaxClaims = Math.Clamp(value.MaxClaims, 1, 20),
            MaxEvidenceItems = Math.Clamp(value.MaxEvidenceItems, 1, 30),
            MaxEvidenceCharacters = Math.Clamp(value.MaxEvidenceCharacters, 1, 20_000)
        };
    }

    private static bool HasRequiredAttribution(EvidenceItem item) =>
        !string.IsNullOrWhiteSpace(item.Excerpt) &&
        !string.IsNullOrWhiteSpace(item.Source.SourceId) &&
        !string.IsNullOrWhiteSpace(item.Source.Label);

    private async Task AppendTaskEventAsync(
        TaskId? taskId,
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (taskId is null || taskRuntime is null)
        {
            return;
        }

        await taskRuntime.AppendEventAsync(taskId.Value, kind, summary, cancellationToken)
            .ConfigureAwait(false);
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

public sealed class InMemoryVerificationReportRepository : IVerificationReportRepository
{
    private readonly List<VerificationReport> _reports = [];

    public Task SaveAsync(
        VerificationReport report,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(report);
        _reports.RemoveAll(existing => existing.Id == report.Id);
        _reports.Add(report);
        return Task.CompletedTask;
    }

    public Task<VerificationReport?> GetAsync(
        VerificationReportId reportId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_reports.FirstOrDefault(report => report.Id == reportId));
    }

    public Task<IReadOnlyList<VerificationReport>> ListRecentAsync(
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var selected = _reports
            .OrderByDescending(report => report.UpdatedAtUtc)
            .ThenBy(report => report.Id.Value, StringComparer.Ordinal)
            .Take(Math.Clamp(limit, 1, 100))
            .ToArray();
        return Task.FromResult<IReadOnlyList<VerificationReport>>(selected);
    }
}

public static class AreYouSureReviewSuggester
{
    private static readonly string[] Triggers =
    [
        "are you sure",
        "verify this",
        "fact check",
        "fact-check",
        "check this claim",
        "is this true"
    ];

    public static ResearchSuggestion Suggest(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ResearchSuggestion(false, string.Empty, AutoLaunch: false);
        }

        var normalized = text.Trim().ToLowerInvariant();
        var shouldSuggest = Triggers.Any(trigger => normalized.Contains(trigger, StringComparison.Ordinal));
        return shouldSuggest
            ? new ResearchSuggestion(
                true,
                "Open in AEDA Research to verify with local evidence.",
                AutoLaunch: false)
            : new ResearchSuggestion(false, string.Empty, AutoLaunch: false);
    }
}
