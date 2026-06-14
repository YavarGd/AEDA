using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Tools.Workspace;

public abstract class WorkspaceToolBase<TInput, TOutput>(
    IWorkspaceReader reader,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options)
    : TypedToolBase<TInput, TOutput>, IInvocationPermissionProvider
{
    protected IWorkspaceReader Reader { get; } = reader;

    protected IWorkspacePathResolver Resolver { get; } = resolver;

    protected WorkspaceToolOptions Options { get; } = options;

    public abstract ValueTask<IReadOnlyList<PermissionRequirement>>
        GetPermissionRequirementsAsync(
            object input,
            CancellationToken cancellationToken = default);

    protected static ToolValidationResult ValidationFailure(
        WorkspaceAccessException exception) =>
        ToolValidationResult.Failure(
            exception.SafeErrorCode,
            exception.SafeErrorMessage);

    protected PermissionRequirement CreateReadRequirement(
        ToolPermission permission,
        WorkspacePath path,
        string userReadableScope,
        string explanation) =>
        new(
            permission,
            PermissionAccessMode.Read,
            path.NormalizedResourceScope,
            userReadableScope,
            explanation,
            LeavesMachine: false,
            ChangesState: false,
            IsReadOnly: true);
}
