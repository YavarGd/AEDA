namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceStatusPresentation(
    string State,
    string Label,
    string Message,
    string Symbol);

public static class WorkspaceStatusPresentationMapper
{
    public static WorkspaceStatusPresentation Map(PersistedWorkspace workspace)
    {
        if (workspace.RemovedAtUtc is not null ||
            workspace.Status == WorkspaceRegistrationStatus.Removed)
        {
            return new WorkspaceStatusPresentation(
                "Removed",
                "Removed",
                "This workspace has been removed.",
                "Removed");
        }

        return workspace.Status switch
        {
            WorkspaceRegistrationStatus.Available => new WorkspaceStatusPresentation(
                "Available",
                "Available",
                "Workspace is available.",
                "Available"),
            WorkspaceRegistrationStatus.Missing or
                WorkspaceRegistrationStatus.AccessDenied => new WorkspaceStatusPresentation(
                    "Unavailable",
                    "Unavailable",
                    "The folder no longer exists or cannot be accessed.",
                    "Unavailable"),
            WorkspaceRegistrationStatus.UnsafeReparsePoint or
                WorkspaceRegistrationStatus.NeedsReview => new WorkspaceStatusPresentation(
                    "Invalid",
                    "Invalid",
                    "The workspace path is invalid or unsafe.",
                    "Invalid"),
            WorkspaceRegistrationStatus.ValidationFailed
                when workspace.SafeStatusCode == "workspace_runtime_registration_failed" =>
                new WorkspaceStatusPresentation(
                    "Runtime registration failed",
                    "Runtime registration failed",
                    "Runtime access could not be registered. Try revalidating.",
                    "Runtime issue"),
            WorkspaceRegistrationStatus.ValidationFailed => new WorkspaceStatusPresentation(
                "Invalid",
                "Invalid",
                "Workspace validation failed.",
                "Invalid"),
            _ => new WorkspaceStatusPresentation(
                "Invalid",
                "Invalid",
                "Workspace validation failed.",
                "Invalid")
        };
    }
}
