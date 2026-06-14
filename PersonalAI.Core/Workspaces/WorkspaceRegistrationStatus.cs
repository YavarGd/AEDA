namespace PersonalAI.Core.Workspaces;

public enum WorkspaceRegistrationStatus
{
    Available,
    Missing,
    AccessDenied,
    UnsafeReparsePoint,
    NeedsReview,
    ValidationFailed,
    Removed
}
