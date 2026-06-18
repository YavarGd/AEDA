namespace PersonalAI.Core.Workflows;

public interface IWorkflowManifestLoader
{
    ValueTask<WorkflowManifestDiscoveryResult> DiscoverAsync(
        CancellationToken cancellationToken = default);
}
