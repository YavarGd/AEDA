namespace PersonalAI.Core.Permissions;

public sealed record PermissionResponse(
    Guid RequestId,
    PermissionDecision Decision,
    DateTimeOffset RespondedAtUtc,
    string? Summary = null)
{
    public bool IsAllowed =>
        Decision is PermissionDecision.AllowOnce or PermissionDecision.AllowForTask;

    public static PermissionResponse AllowOnce(
        PermissionRequest request,
        string? summary = null) =>
        Create(request, PermissionDecision.AllowOnce, summary);

    public static PermissionResponse AllowForTask(
        PermissionRequest request,
        string? summary = null) =>
        Create(request, PermissionDecision.AllowForTask, summary);

    public static PermissionResponse Deny(
        PermissionRequest request,
        string? summary = null) =>
        Create(request, PermissionDecision.Deny, summary);

    public static PermissionResponse CancelTask(
        PermissionRequest request,
        string? summary = null) =>
        Create(request, PermissionDecision.CancelTask, summary);

    private static PermissionResponse Create(
        PermissionRequest request,
        PermissionDecision decision,
        string? summary)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PermissionResponse(
            request.RequestId,
            decision,
            DateTimeOffset.UtcNow,
            summary);
    }
}
