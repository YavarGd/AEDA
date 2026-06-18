namespace PersonalAI.Core.Workflows;

public sealed record WorkflowManifest(
    string Id,
    string Name,
    string Description,
    string Version,
    string? Author,
    IReadOnlyList<string> RequiredCapabilities,
    IReadOnlyList<string> RequiredTools,
    WorkflowRiskLevel RiskLevel,
    string? InputSchema,
    string? OutputSchema);
