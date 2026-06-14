namespace PersonalAI.Core.Tools;

public interface IInvocationPermissionProvider
{
    ValueTask<IReadOnlyList<PermissionRequirement>> GetPermissionRequirementsAsync(
        object input,
        CancellationToken cancellationToken = default);
}
