namespace PersonalAI.Core.Permissions;

public enum PermissionDialogOutcome
{
    AllowOnce,
    AllowForTask,
    CancelTask,
    Deny,
    Dismissed,
    Unavailable,
    Error,
    Unknown
}
