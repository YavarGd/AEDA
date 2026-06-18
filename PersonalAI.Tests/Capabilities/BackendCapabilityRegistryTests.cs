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
}
