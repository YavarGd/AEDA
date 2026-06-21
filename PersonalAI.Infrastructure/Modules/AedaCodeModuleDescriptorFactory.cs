using PersonalAI.Core.Capabilities;
using PersonalAI.Core.Modules;

namespace PersonalAI.Infrastructure.Modules;

public static class AedaCodeModuleDescriptorFactory
{
    private static readonly BackendCapability[] RequiredCapabilities =
    [
        BackendCapability.WorkspaceRead,
        BackendCapability.CodeContextRead,
        BackendCapability.CodeChangePlanning,
        BackendCapability.PatchProposal
    ];

    public static AedaModuleDescriptor Create(IBackendCapabilityRegistry capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        var moduleCapabilities = new[]
        {
            FromBackend("workspace_read", "Workspace read", BackendCapability.WorkspaceRead, capabilities),
            FromBackend("workspace_search", "Workspace search", BackendCapability.CodeContextRead, capabilities),
            FromBackend("code_context_read", "Code context read", BackendCapability.CodeContextRead, capabilities),
            FromBackend("code_change_planning", "Code change planning", BackendCapability.CodeChangePlanning, capabilities),
            FromBackend("patch_proposal", "Patch proposal", BackendCapability.PatchProposal, capabilities),
            FromBackend("patch_review", "Patch review", BackendCapability.PatchReview, capabilities),
            FromBackend("patch_apply", "Approval-gated patch apply", BackendCapability.PatchApply, capabilities),
            FromBackend("patch_rollback", "Patch rollback", BackendCapability.PatchRollback, capabilities),
            FromBackend("controlled_validation", "Approval-gated validation", BackendCapability.ControlledValidation, capabilities),
            FromBackend("test_execution", "Allowlisted test execution", BackendCapability.TestExecution, capabilities),
            FromBackend("task_timeline", "Task timeline", BackendCapability.CodeTaskTimeline, capabilities),
            FromBackend("module_dashboard", "Module dashboard", BackendCapability.ModuleDashboard, capabilities),
            FromBackend("approval_required", "Approval checkpoints", BackendCapability.PatchApply, capabilities),
            FromBackend("backup_and_rollback", "Backup and rollback", BackendCapability.PatchRollback, capabilities),
            Deferred("git_mutation", "Git mutation", "git_mutation_deferred", BackendCapability.GitMutation),
            Deferred("free_form_shell", "Free-form shell", "free_form_shell_deferred", BackendCapability.ShellExecution),
            Deferred("autonomous_coding", "Autonomous coding loops", "autonomous_coding_deferred"),
            Deferred("package_install", "Package installation", "package_install_deferred"),
            Deferred("browser_automation", "Browser automation", "browser_automation_deferred")
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
        var unavailableReason = status == AedaModuleStatus.Unavailable
            ? "aeda_code_required_capabilities_unavailable"
            : null;

        return new AedaModuleDescriptor(
            AedaModuleId.Code,
            AedaModuleKind.Code,
            "AEDA Code",
            "Inspect code context, prepare patch proposals, apply approved changes, and run allowlisted validation.",
            "\uE943",
            status,
            moduleCapabilities,
            new AedaModuleRoute("aeda-code", "AedaCodeModuleViewModel"),
            unavailableReason,
            SortOrder: 20);
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

    private static AedaModuleCapability Deferred(
        string id,
        string displayName,
        string reason,
        BackendCapability? backendCapability = null) =>
        new(
            id,
            displayName,
            AedaModuleCapabilityState.Deferred,
            reason,
            backendCapability);
}
