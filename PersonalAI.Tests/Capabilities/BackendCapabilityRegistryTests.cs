using PersonalAI.Core.Capabilities;

namespace PersonalAI.Tests.Capabilities;

public sealed class BackendCapabilityRegistryTests
{
    [Fact]
    public void Registry_ReportsAvailableAndUnavailableCapabilities()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: true,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true,
            hasAudioPlaybackService: true);

        Assert.True(registry.GetStatus(BackendCapability.TaskRuntime).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.DurableTaskHistory).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.VoiceInput).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.VoiceOutput).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.StructuredToolCalls).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.WebResearch).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.Memory).IsAvailable);
    }

    [Fact]
    public void VoiceCapabilities_RequireProviderAndAudioServices()
    {
        var unconfigured = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true);
        var configured = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: true,
            hasTextToSpeechProvider: true,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true,
            hasAudioCaptureService: true,
            hasAudioPlaybackService: true);

        Assert.False(unconfigured.GetStatus(BackendCapability.VoiceInput).IsAvailable);
        Assert.False(unconfigured.GetStatus(BackendCapability.VoiceOutput).IsAvailable);
        Assert.Equal(
            "voice_input_unavailable",
            unconfigured.GetStatus(BackendCapability.VoiceInput).SafeReasonCode);
        Assert.True(configured.GetStatus(BackendCapability.PushToTalk).IsAvailable);
        Assert.True(configured.GetStatus(BackendCapability.SpeakResponse).IsAvailable);
    }

    [Fact]
    public void UnknownOrUnsetCapability_ReturnsUnavailableSafely()
    {
        var registry = new BackendCapabilityRegistry([]);

        var status = registry.GetStatus(BackendCapability.CodePatchProposal);

        Assert.False(status.IsAvailable);
        Assert.Equal("capability_unavailable", status.SafeReasonCode);
    }

    [Fact]
    public void MemoryAndRagCapabilities_ReportConfiguredAndUnconfiguredStates()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: true,
            explicitMemoryEnabled: true,
            projectMemoryEnabled: true,
            taskOutcomeMemoryEnabled: true,
            retrievalEnabled: true,
            workspaceIndexingEnabled: false,
            hasEmbeddingProvider: false,
            hasVectorIndex: false);

        Assert.True(registry.GetStatus(BackendCapability.Memory).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.ExplicitMemory).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.ProjectMemory).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.TaskOutcomeMemory).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.Retrieval).IsAvailable);
        Assert.Equal(
            "retrieval_text_search_only",
            registry.GetStatus(BackendCapability.Retrieval).SafeReasonCode);
        Assert.False(registry.GetStatus(BackendCapability.WorkspaceIndexing).IsAvailable);
        Assert.Equal(
            "workspace_indexing_disabled",
            registry.GetStatus(BackendCapability.WorkspaceIndexing).SafeReasonCode);
        Assert.False(registry.GetStatus(BackendCapability.Embeddings).IsAvailable);
        Assert.Equal(
            "embeddings_unconfigured",
            registry.GetStatus(BackendCapability.Embeddings).SafeReasonCode);
        Assert.False(registry.GetStatus(BackendCapability.VectorSearch).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.SemanticRetrieval).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.LocalOnlyRag).IsAvailable);
    }

    [Fact]
    public void EmbeddingAndVectorCapabilities_AvailableWithConfiguredProviders()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true,
            hasMemoryRepository: true,
            workspaceIndexingEnabled: true,
            hasEmbeddingProvider: true,
            hasVectorIndex: true);

        Assert.True(registry.GetStatus(BackendCapability.WorkspaceIndexing).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.Embeddings).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.VectorSearch).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.SemanticRetrieval).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.LocalOnlyRag).IsAvailable);
    }
}
