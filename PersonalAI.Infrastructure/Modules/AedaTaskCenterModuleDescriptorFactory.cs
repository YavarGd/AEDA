using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public static class AedaTaskCenterModuleDescriptorFactory
{
    private static readonly BackendCapability[] RequiredCapabilities =
    [
        BackendCapability.TaskRuntime,
        BackendCapability.DurableTaskHistory,
        BackendCapability.AedaTaskCenter,
        BackendCapability.ActivityTimeline
    ];

    public static AedaModuleDescriptor Create(IBackendCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var moduleCapabilities = new[]
        {
            FromBackend("task_center", "Task Center", BackendCapability.AedaTaskCenter, capabilities),
            FromBackend("activity_timeline", "Activity timeline", BackendCapability.ActivityTimeline, capabilities),
            FromBackend("approval_inbox", "Approval inbox", BackendCapability.ApprovalInbox, capabilities),
            FromBackend("artifact_links", "Artifact links", BackendCapability.TaskArtifactLinks, capabilities),
            FromBackend("module_task_summaries", "Module task summaries", BackendCapability.ModuleTaskSummaries, capabilities),
            FromBackend("durable_task_history", "Durable task history", BackendCapability.DurableTaskHistory, capabilities)
        };

        var requiredStatuses = RequiredCapabilities
            .Select(capabilities.GetStatus)
            .ToArray();
        var availableRequiredCount = requiredStatuses.Count(status => status.IsAvailable);
        var status = availableRequiredCount == requiredStatuses.Length
            ? AedaModuleStatus.Available
            : availableRequiredCount > 0
                ? AedaModuleStatus.PartiallyAvailable
                : AedaModuleStatus.Unavailable;

        return new AedaModuleDescriptor(
            AedaModuleId.TaskCenter,
            AedaModuleKind.TaskCenter,
            "Task Center",
            "Shared view of active work, approvals, failures, recent history, and task artifacts.",
            "\uE9D9",
            status,
            moduleCapabilities,
            new AedaModuleRoute("aeda-task-center", "AedaTaskCenterViewModel"),
            status == AedaModuleStatus.Unavailable
                ? "task_center_required_capabilities_unavailable"
                : null,
            SortOrder: 10);
    }

    private static AedaModuleCapability FromBackend(
        string id,
        string displayName,
        BackendCapability backendCapability,
        IBackendCapabilityRegistry capabilities)
    {
        var status = capabilities.GetStatus(backendCapability);
        return new AedaModuleCapability(
            id,
            displayName,
            status.IsAvailable
                ? AedaModuleCapabilityState.Available
                : AedaModuleCapabilityState.Unavailable,
            status.SafeReasonCode,
            backendCapability);
    }
}
