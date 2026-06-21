using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Research;
using PersonalAI.Infrastructure.Modules;
using PersonalAI.Infrastructure.Research;

namespace PersonalAI.Tests.Research;

public sealed class AedaResearchFoundationTests
{
    [Fact]
    public void Descriptor_ExposesResearchCapabilitiesAndKeepsUnsafeCapabilitiesUnavailable()
    {
        var descriptor = AedaResearchModuleDescriptorFactory.Create(CreateCapabilities(retrievalEnabled: true));

        Assert.Equal(AedaModuleId.Research, descriptor.Id);
        Assert.Equal(AedaModuleStatus.Available, descriptor.Status);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "claim_extraction" &&
            capability.State == AedaModuleCapabilityState.Available);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "local_rag_evidence" &&
            capability.State == AedaModuleCapabilityState.Available);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "browser_automation" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "cloud_search" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
    }

    [Fact]
    public void Descriptor_IsPartiallyAvailableWhenLocalRetrievalIsUnavailable()
    {
        var descriptor = AedaResearchModuleDescriptorFactory.Create(CreateCapabilities(retrievalEnabled: false));

        Assert.Equal(AedaModuleStatus.PartiallyAvailable, descriptor.Status);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "local_rag_evidence" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
    }

    [Fact]
    public async Task ClaimExtraction_ClassifiesAndBoundsClaims()
    {
        var service = new DeterministicClaimExtractionService();

        var claims = await service.ExtractClaimsAsync(new ClaimExtractionRequest(
            "SQLite is used for local RAG. Today the app is fast. I think it is beautiful.",
            MaxClaims: 2));

        Assert.Equal(2, claims.Count);
        Assert.Equal(ResearchClaimKind.Factual, claims[0].Kind);
        Assert.Equal(ResearchClaimKind.CurrentOrTimeSensitive, claims[1].Kind);
        Assert.All(claims, claim => Assert.True(claim.Text.Length <= 500));
    }

    [Fact]
    public async Task LocalRagEvidenceProvider_ReturnsBoundedAttributedEvidence()
    {
        var provider = new LocalRagEvidenceProvider(new FakeRetrievalService(
            new RetrievalContextPack(
                [
                    new RetrievalContextItem(
                        RetrievalContextItemKind.Memory,
                        new string('a', 1000),
                        4,
                        new MemorySource("project_note", DateTimeOffset.UtcNow, ProjectId: "p1"),
                        MemoryConfidence.High,
                        "Project note",
                        "memory_text",
                        "trace-1")
                ],
                UsedEmbeddingSearch: false,
                IsTruncated: false)));
        var claim = new ResearchClaim(
            ResearchClaimId.NewId(),
            "Project note has local evidence",
            ResearchClaimKind.Factual,
            DateTimeOffset.UtcNow);

        var evidence = await provider.SearchAsync(
            new EvidenceSearchRequest("local evidence", new ResearchScope(), MaxItems: 4),
            [claim]);

        var item = Assert.Single(evidence);
        Assert.Equal(claim.Id.Value, item.ClaimId);
        Assert.NotEmpty(item.Source.SourceId);
        Assert.Equal("Project note", item.Source.Label);
        Assert.True(item.Excerpt.Length <= 700);
        Assert.True(item.Source.IsLocal);
    }

    [Fact]
    public async Task VerificationService_CreatesSupportedReportWithCitations()
    {
        var claim = new ResearchClaim(
            ResearchClaimId.NewId(),
            "SQLite stores local reports",
            ResearchClaimKind.Factual,
            DateTimeOffset.UtcNow);
        var evidence = CreateEvidence(claim, "SQLite stores local reports in a local database.", supports: true, contradicts: false);
        var service = CreateService(
            new FixedClaimExtractionService([claim]),
            new FakeEvidenceProvider([evidence]));

        var report = await service.VerifyWithLocalEvidenceAsync(new VerificationRequest(
            claim.Text,
            new ResearchScope()));

        Assert.Equal(VerificationReportStatus.Completed, report.Status);
        Assert.Equal(VerificationVerdict.Supported, report.OverallVerdict);
        Assert.False(report.RemoteSearchWasUsed);
        Assert.True(report.LocalEvidenceOnly);
        Assert.NotEmpty(report.Findings.Single().Citations);
    }

    [Fact]
    public async Task VerificationService_CreatesContradictedAndUnresolvedReportsConservatively()
    {
        var contradictedClaim = new ResearchClaim(
            ResearchClaimId.NewId(),
            "Cloud search is enabled",
            ResearchClaimKind.Factual,
            DateTimeOffset.UtcNow);
        var contradictedService = CreateService(
            new FixedClaimExtractionService([contradictedClaim]),
            new FakeEvidenceProvider([
                CreateEvidence(contradictedClaim, "Cloud search is not enabled by default.", supports: true, contradicts: true)
            ]));

        var contradicted = await contradictedService.VerifyWithLocalEvidenceAsync(new VerificationRequest(
            contradictedClaim.Text,
            new ResearchScope()));

        Assert.Equal(VerificationVerdict.Contradicted, contradicted.OverallVerdict);
        Assert.Equal(VerificationConfidence.Low, contradicted.Confidence);

        var unresolvedClaim = new ResearchClaim(
            ResearchClaimId.NewId(),
            "The answer is fully verified",
            ResearchClaimKind.Factual,
            DateTimeOffset.UtcNow);
        var unresolvedService = CreateService(
            new FixedClaimExtractionService([unresolvedClaim]),
            new FakeEvidenceProvider([]));

        var unresolved = await unresolvedService.VerifyWithLocalEvidenceAsync(new VerificationRequest(
            unresolvedClaim.Text,
            new ResearchScope()));

        Assert.Equal(VerificationVerdict.Unresolved, unresolved.OverallVerdict);
        Assert.Empty(unresolved.Findings.Single().Citations);
        Assert.NotEmpty(unresolved.UnresolvedGaps);
    }

    [Theory]
    [InlineData("are you sure?")]
    [InlineData("verify this answer")]
    [InlineData("fact check this")]
    public void ModuleSuggestion_SuggestsResearchWithoutAutoLaunch(string text)
    {
        var suggestion = new ModuleSuggestionService().Suggest(text);

        Assert.True(suggestion.ShouldSuggest);
        Assert.Equal(AedaModuleId.Research.Value, suggestion.ModuleId);
        Assert.False(suggestion.AutoLaunch);
    }

    [Fact]
    public async Task Dashboard_ShowsProviderStatusAndRecentReports()
    {
        var claim = new ResearchClaim(
            ResearchClaimId.NewId(),
            "Local evidence exists",
            ResearchClaimKind.Factual,
            DateTimeOffset.UtcNow);
        var repository = new InMemoryVerificationReportRepository();
        var service = CreateService(
            new FixedClaimExtractionService([claim]),
            new FakeEvidenceProvider([CreateEvidence(claim, "Local evidence exists.", supports: true, contradicts: false)]),
            repository);
        await service.VerifyWithLocalEvidenceAsync(new VerificationRequest(claim.Text, new ResearchScope()));

        var dashboard = await service.GetDashboardAsync();

        Assert.Contains(dashboard.EvidenceProviders, provider => provider.ProviderId == "fake_manual");
        Assert.Single(dashboard.RecentReports);
        Assert.Contains("Local evidence only", dashboard.PrivacyStatus);
    }

    private static BackendCapabilityRegistry CreateCapabilities(bool retrievalEnabled) =>
        BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: true,
            retrievalEnabled: retrievalEnabled,
            hasAedaModules: true,
            hasAedaResearchModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true);

    private static AedaResearchModuleService CreateService(
        IClaimExtractionService claimExtraction,
        IEvidenceProvider provider,
        IVerificationReportRepository? repository = null) =>
        new(
            claimExtraction,
            [provider, new DisabledExternalSearchEvidenceProvider()],
            repository ?? new InMemoryVerificationReportRepository(),
            CreateCapabilities(retrievalEnabled: true));

    private static EvidenceItem CreateEvidence(
        ResearchClaim claim,
        string excerpt,
        bool supports,
        bool contradicts) =>
        new(
            EvidenceItemId.NewId(),
            claim.Id.Value,
            excerpt,
            new EvidenceSource(
                "source-1",
                EvidenceSourceType.Manual,
                "Manual fixture",
                DateTimeOffset.UtcNow,
                IsLocal: true,
                EvidenceQuality.High),
            10,
            supports,
            contradicts,
            DateTimeOffset.UtcNow);

    private sealed class FakeRetrievalService(RetrievalContextPack pack) : IRetrievalService
    {
        public Task<RetrievalContextPack> RetrieveAsync(
            RetrievalQuery query,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(query.IncludeSensitive);
            return Task.FromResult(pack);
        }
    }

    private sealed class FixedClaimExtractionService(IReadOnlyList<ResearchClaim> claims) : IClaimExtractionService
    {
        public Task<IReadOnlyList<ResearchClaim>> ExtractClaimsAsync(
            ClaimExtractionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(claims);
        }
    }
}
