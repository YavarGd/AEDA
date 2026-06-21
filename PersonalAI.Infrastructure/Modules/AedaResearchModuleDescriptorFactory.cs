using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public static class AedaResearchModuleDescriptorFactory
{
    private static readonly BackendCapability[] RequiredCapabilities =
    [
        BackendCapability.AedaResearchModule,
        BackendCapability.ResearchDashboard,
        BackendCapability.ClaimExtraction,
        BackendCapability.EvidenceTracking,
        BackendCapability.VerificationReport,
        BackendCapability.CitationReport,
        BackendCapability.LocalEvidenceRetrieval,
        BackendCapability.AreYouSureReview
    ];

    public static AedaModuleDescriptor Create(IBackendCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var moduleCapabilities = new[]
        {
            FromBackend("claim_extraction", "Claim extraction", BackendCapability.ClaimExtraction, capabilities),
            FromBackend("evidence_tracking", "Evidence tracking", BackendCapability.EvidenceTracking, capabilities),
            FromBackend("source_attribution", "Source attribution", BackendCapability.CitationReport, capabilities),
            FromBackend("citation_report", "Citation report", BackendCapability.CitationReport, capabilities),
            FromBackend("local_rag_evidence", "Local RAG evidence", BackendCapability.LocalEvidenceRetrieval, capabilities),
            FromBackend("memory_evidence", "Memory evidence", BackendCapability.LocalEvidenceRetrieval, capabilities),
            FromBackend("verification_report", "Verification report", BackendCapability.VerificationReport, capabilities),
            FromBackend("confidence_assessment", "Confidence assessment", BackendCapability.VerificationReport, capabilities),
            FromBackend("are_you_sure_review", "Are you sure review", BackendCapability.AreYouSureReview, capabilities),
            Unavailable("browser_automation", "Browser automation", "browser_automation_unavailable", BackendCapability.BrowserResearchAgent),
            Unavailable("autonomous_web_research", "Autonomous web research", "autonomous_web_research_unavailable", BackendCapability.BrowserResearchAgent),
            Unavailable("form_submission", "Form submission", "form_submission_unavailable"),
            Unavailable("cloud_search", "Cloud search", "cloud_search_disabled", BackendCapability.ExternalSearchProvider),
            Unavailable("paid_search_provider", "Paid search provider", "paid_search_provider_unconfigured", BackendCapability.ExternalSearchProvider),
            Unavailable("web_scraping", "Web scraping", "web_scraping_unavailable")
        };

        var requiredStatuses = RequiredCapabilities
            .Select(capabilities.GetStatus)
            .ToArray();
        var availableRequiredCount = requiredStatuses.Count(status => status.IsAvailable);
        var status = availableRequiredCount == requiredStatuses.Length
            ? AedaModuleStatus.Available
            : availableRequiredCount > 0
                ? AedaModuleStatus.PartiallyAvailable
                : AedaModuleStatus.Unavailable;
        var unavailableReason = status == AedaModuleStatus.Unavailable
            ? "aeda_research_required_capabilities_unavailable"
            : null;

        return new AedaModuleDescriptor(
            AedaModuleId.Research,
            AedaModuleKind.Research,
            "AEDA Research",
            "Verify claims with local evidence, citations, and conservative confidence.",
            "\uE721",
            status,
            moduleCapabilities,
            new AedaModuleRoute("aeda-research", "AedaResearchModuleViewModel"),
            unavailableReason,
            SortOrder: 40);
    }

    private static AedaModuleCapability FromBackend(
        string id,
        string displayName,
        BackendCapability backendCapability,
        IBackendCapabilityRegistry capabilities)
    {
        var status = capabilities.GetStatus(backendCapability);
        return new AedaModuleCapability(
            id,
            displayName,
            status.IsAvailable
                ? AedaModuleCapabilityState.Available
                : AedaModuleCapabilityState.Unavailable,
            status.SafeReasonCode,
            backendCapability);
    }

    private static AedaModuleCapability Unavailable(
        string id,
        string displayName,
        string reason,
        BackendCapability? backendCapability = null) =>
        new(
            id,
            displayName,
            AedaModuleCapabilityState.Unavailable,
            reason,
            backendCapability);
}
