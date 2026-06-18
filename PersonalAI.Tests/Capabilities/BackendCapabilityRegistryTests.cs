using PersonalAI.Core.Capabilities;

namespace PersonalAI.Tests.Capabilities;

public sealed class BackendCapabilityRegistryTests
{
    [Fact]
    public void Registry_ReportsAvailableAndUnavailableCapabilities()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: true,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true);

        Assert.True(registry.GetStatus(BackendCapability.TaskRuntime).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.VoiceInput).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.VoiceOutput).IsAvailable);
        Assert.True(registry.GetStatus(BackendCapability.StructuredToolCalls).IsAvailable);
        Assert.False(registry.GetStatus(BackendCapability.WebResearch).IsAvailable);
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
