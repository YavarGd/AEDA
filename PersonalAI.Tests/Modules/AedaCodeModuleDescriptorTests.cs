using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;
using PersonalAI.Infrastructure.Modules;

namespace PersonalAI.Tests.Modules;

public sealed class AedaCodeModuleDescriptorTests
{
    [Fact]
    public void Descriptor_IsAvailableWhenRequiredBackendCapabilitiesExist()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: true,
            hasDurableTaskHistory: true,
            hasWorkflowManifestLoader: true,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: true,
            hasStructuredToolRuntime: true,
            hasCodeContextRead: true,
            hasCodeChangePlanning: true,
            hasPatchProposal: true,
            hasPatchReview: true,
            hasPatchApply: true,
            hasPatchRollback: true,
            hasControlledValidation: true,
            hasAedaModules: true,
            hasAedaCodeModule: true,
            hasModuleDashboard: true,
            hasModuleRouting: true,
            hasCodeTaskTimeline: true);

        var descriptor = AedaCodeModuleDescriptorFactory.Create(registry);

        Assert.Equal(AedaModuleId.Code, descriptor.Id);
        Assert.Equal(AedaModuleStatus.Available, descriptor.Status);
        Assert.Null(descriptor.SafeUnavailableReason);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "patch_apply"
            && capability.State == AedaModuleCapabilityState.Available
            && capability.BackendCapability == BackendCapability.PatchApply);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "free_form_shell"
            && capability.State == AedaModuleCapabilityState.Deferred);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "autonomous_coding"
            && capability.State == AedaModuleCapabilityState.Deferred);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "git_mutation"
            && capability.State == AedaModuleCapabilityState.Deferred);
    }

    [Fact]
    public void Descriptor_IsPartiallyAvailableWhenOnlySomeRequiredCapabilitiesExist()
    {
        var registry = BackendCapabilityRegistry.CreateDefault(
            hasTaskRuntime: false,
            hasDurableTaskHistory: false,
            hasWorkflowManifestLoader: false,
            hasSpeechToTextProvider: false,
            hasTextToSpeechProvider: false,
            hasLocalWorkerSupervisor: false,
            hasStructuredToolRuntime: true,
            hasCodeContextRead: true);

        var descriptor = AedaCodeModuleDescriptorFactory.Create(registry);

        Assert.Equal(AedaModuleStatus.PartiallyAvailable, descriptor.Status);
        Assert.Contains(descriptor.Capabilities, capability =>
            capability.Id == "patch_proposal"
            && capability.State == AedaModuleCapabilityState.Unavailable
            && capability.SafeReasonCode == "patch_proposal_unavailable");
    }

    [Fact]
    public void Descriptor_IsUnavailableWhenRequiredCapabilitiesAreMissing()
    {
        var registry = new BackendCapabilityRegistry([]);

        var descriptor = AedaCodeModuleDescriptorFactory.Create(registry);

        Assert.Equal(AedaModuleStatus.Unavailable, descriptor.Status);
        Assert.Equal("aeda_code_required_capabilities_unavailable", descriptor.SafeUnavailableReason);
    }
}
