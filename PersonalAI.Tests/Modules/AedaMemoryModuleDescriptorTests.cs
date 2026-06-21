using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Tests.Modules;

public sealed class AedaMemoryModuleDescriptorTests
{
    [Fact]
    public void Descriptor_IsAvailableWhenRequiredBackendCapabilitiesExist()
    {
        var registry = CreateCapabilities(
            hasMemoryRepository: true,
            hasAedaMemoryModule: true,
            retrievalEnabled: true);

        var descriptor = AedaMemoryModuleDescriptorFactory.Create(registry);

        Assert.Equal(AedaModuleId.Memory, descriptor.Id);
        Assert.Equal(AedaModuleStatus.Available, descriptor.Status);
        Assert.Equal("Project brain. Manage memories and indexed knowledge.", descriptor.ShortDescription);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "memory_search" &&
            capability.State == AedaModuleCapabilityState.Available &&
            capability.BackendCapability == BackendCapability.MemorySearch);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "automatic_memory" &&
            capability.State == AedaModuleCapabilityState.Unavailable &&
            capability.SafeReasonCode == "automatic_memory_disabled");
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "hidden_capture" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "cloud_memory_sync" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "cloud_embeddings" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
    }

    [Fact]
    public void Descriptor_IsPartiallyAvailableWhenOptionalRagPiecesAreDisabled()
    {
        var registry = CreateCapabilities(
            hasMemoryRepository: true,
            hasAedaMemoryModule: true,
            retrievalEnabled: false);

        var descriptor = AedaMemoryModuleDescriptorFactory.Create(registry);

        Assert.Equal(AedaModuleStatus.Available, descriptor.Status);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "retrieval_preview" &&
            capability.State == AedaModuleCapabilityState.Unavailable &&
            capability.SafeReasonCode == "retrieval_preview_unavailable");
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "vector_search" &&
            capability.State == AedaModuleCapabilityState.Unavailable);
    }

    [Fact]
    public void Descriptor_IsUnavailableWhenMemoryRepositoryIsMissing()
    {
        var descriptor = AedaMemoryModuleDescriptorFactory.Create(
            CreateCapabilities(
                hasMemoryRepository: false,
                hasAedaMemoryModule: true,
                retrievalEnabled: true));

        Assert.Equal(AedaModuleStatus.Unavailable, descriptor.Status);
        Assert.Equal("aeda_memory_required_capabilities_unavailable", descriptor.SafeUnavailableReason);
    }

    private static BackendCapabilityRegistry CreateCapabilities(
        bool hasMemoryRepository,
        bool hasAedaMemoryModule,
        bool retrievalEnabled) =>
        BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: hasMemoryRepository,
            retrievalEnabled: retrievalEnabled,
            hasAedaModules: true,
            hasAedaMemoryModule: hasAedaMemoryModule,
            hasModuleDashboard: true,
            hasModuleRouting: true);
}
