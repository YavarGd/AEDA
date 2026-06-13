namespace PersonalAI.Core.Permissions;

public static class PermissionDialogDecisionMapper
{
    public static PermissionResponse Map(
        PermissionRequest request,
        PermissionDialogOutcome outcome,
        string? summary = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return outcome switch
        {
            PermissionDialogOutcome.AllowOnce =>
                PermissionResponse.AllowOnce(request, summary),
            PermissionDialogOutcome.AllowForTask =>
                PermissionResponse.AllowForTask(request, summary),
            PermissionDialogOutcome.CancelTask =>
                PermissionResponse.CancelTask(request, summary),
            _ => PermissionResponse.Deny(
                request,
                summary ?? "The approval dialog did not explicitly approve the request.")
        };
    }
}
