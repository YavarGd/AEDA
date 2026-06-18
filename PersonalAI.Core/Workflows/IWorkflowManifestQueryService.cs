using PersonalAI.Core.Capabilities;

namespace PersonalAI.Core.Workflows;

public interface IWorkflowManifestQueryService
{
    ValueTask<IReadOnlyList<WorkflowManifest>> ListInstalledAsync(
        CancellationToken cancellationToken = default);

    ValueTask<WorkflowManifest?> GetAsync(
        string id,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<BackendCapabilityStatus>> ValidateRequiredCapabilitiesAsync(
        WorkflowManifest manifest,
        CancellationToken cancellationToken = default);
}
