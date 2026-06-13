namespace PersonalAI.Core.Permissions;

public sealed class DenyingPermissionBroker : IPermissionBroker
{
    public ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        return ValueTask.FromResult(PermissionResponse.Deny(
            request,
            "No permission broker is available."));
    }
}
