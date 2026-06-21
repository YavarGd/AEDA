using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public static class AedaMemoryModuleDescriptorFactory
{
    private static readonly BackendCapability[] RequiredCapabilities =
    [
        BackendCapability.Memory,
        BackendCapability.ExplicitMemory,
        BackendCapability.MemoryDashboard,
        BackendCapability.MemorySearch,
        BackendCapability.MemorySourceAttribution
    ];

    public static AedaModuleDescriptor Create(IBackendCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var moduleCapabilities = new[]
        {
            FromBackend("explicit_memory", "Explicit memory", BackendCapability.ExplicitMemory, capabilities),
            FromBackend("project_memory", "Project memory", BackendCapability.ProjectMemory, capabilities),
            FromBackend("task_outcome_memory", "Task outcome memory", BackendCapability.TaskOutcomeMemory, capabilities),
            FromBackend("memory_search", "Memory search", BackendCapability.MemorySearch, capabilities),
            FromBackend("memory_edit", "Memory edit", BackendCapability.MemoryEdit, capabilities),
            FromBackend("memory_archive", "Memory archive", BackendCapability.MemoryEdit, capabilities),
            FromBackend("memory_delete", "Memory delete", BackendCapability.MemoryDelete, capabilities),
            FromBackend("source_attribution", "Source attribution", BackendCapability.MemorySourceAttribution, capabilities),
            FromBackend("retrieval", "Retrieval", BackendCapability.Retrieval, capabilities),
            FromBackend("retrieval_preview", "Retrieval preview", BackendCapability.RetrievalPreview, capabilities),
            FromBackend("workspace_indexing", "Workspace indexing", BackendCapability.WorkspaceIndexing, capabilities),
            FromBackend("knowledge_documents", "Knowledge documents", BackendCapability.KnowledgeDocumentBrowser, capabilities),
            FromBackend("vector_search", "Vector search", BackendCapability.VectorSearch, capabilities),
            FromBackend("embeddings", "Embeddings", BackendCapability.Embeddings, capabilities),
            FromBackend("local_only_rag", "Local-only RAG", BackendCapability.LocalOnlyRag, capabilities),
            Unavailable("automatic_memory", "Automatic memory", "automatic_memory_disabled"),
            Unavailable("hidden_capture", "Hidden capture", "hidden_capture_unavailable"),
            Unavailable("cloud_memory_sync", "Cloud memory sync", "cloud_memory_sync_unavailable"),
            Unavailable("always_on_capture", "Always-on capture", "always_on_capture_unavailable"),
            Unavailable("cloud_embeddings", "Cloud embeddings", "cloud_embeddings_unavailable")
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
            ? "aeda_memory_required_capabilities_unavailable"
            : null;

        return new AedaModuleDescriptor(
            AedaModuleId.Memory,
            AedaModuleKind.Memory,
            "AEDA Memory",
            "Project brain. Manage memories and indexed knowledge.",
            "\uE8F1",
            status,
            moduleCapabilities,
            new AedaModuleRoute("aeda-memory", "AedaMemoryModuleViewModel"),
            unavailableReason,
            SortOrder: 30);
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
        string reason) =>
        new(
            id,
            displayName,
            AedaModuleCapabilityState.Unavailable,
            reason);
}
