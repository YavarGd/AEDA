namespace PersonalAI.Core.Permissions;

public interface IPermissionBroker
{
    ValueTask<PermissionResponse> RequestPermissionAsync(
        PermissionRequest request,
        CancellationToken cancellationToken = default);
}
