namespace PersonalAI.Core.Workflows;

public sealed record WorkflowManifestDiscoveryResult(
    IReadOnlyList<WorkflowManifest> Manifests,
    IReadOnlyList<string> SafeErrors);
