using PersonalAI.Core.Capabilities;

namespace PersonalAI.Core.Workflows;

public sealed class WorkflowManifestQueryService(
    IWorkflowManifestLoader loader,
    IBackendCapabilityRegistry capabilityRegistry) : IWorkflowManifestQueryService
{
    public async ValueTask<IReadOnlyList<WorkflowManifest>> ListInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await loader.DiscoverAsync(cancellationToken);
        return result.Manifests;
    }

    public async ValueTask<WorkflowManifest?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var manifests = await ListInstalledAsync(cancellationToken);
        return manifests.FirstOrDefault(manifest =>
            string.Equals(manifest.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ValueTask<IReadOnlyList<BackendCapabilityStatus>> ValidateRequiredCapabilitiesAsync(
        WorkflowManifest manifest,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(manifest);

        var statuses = manifest.RequiredCapabilities
            .Select(ParseCapability)
            .Select(capability => capability is null
                ? new BackendCapabilityStatus(
                    BackendCapability.WorkflowManifests,
                    IsAvailable: false,
                    SafeReasonCode: "unsupported_required_capability")
                : capabilityRegistry.GetStatus(capability.Value))
            .ToArray();

        return ValueTask.FromResult<IReadOnlyList<BackendCapabilityStatus>>(statuses);
    }

    private static BackendCapability? ParseCapability(string value) =>
        Enum.TryParse<BackendCapability>(value, ignoreCase: true, out var capability)
            ? capability
            : null;
}
