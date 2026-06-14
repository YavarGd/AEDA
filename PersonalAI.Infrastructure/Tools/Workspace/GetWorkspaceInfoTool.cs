using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Tools.Workspace;

public sealed class GetWorkspaceInfoTool(
    IWorkspaceReader reader,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options)
    : WorkspaceToolBase<GetWorkspaceInfoInput, GetWorkspaceInfoOutput>(
        reader,
        resolver,
        options)
{
    public static ToolId Id { get; } = new("workspace.info.get");

    public override ToolDescriptor Descriptor { get; } =
        ToolDescriptor.Create<GetWorkspaceInfoInput, GetWorkspaceInfoOutput>(
            Id,
            "Get workspace info",
            "Reads safe metadata for an approved workspace.",
            requiredPermissions: [ToolPermission.ReadWorkspace],
            requiresApproval: true,
            category: "Workspace",
            isReadOnly: true);

    public override ValueTask<ToolValidationResult> ValidateAsync(
        GetWorkspaceInfoInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _ = Reader.GetWorkspace(input.WorkspaceId);
            return ValueTask.FromResult(ToolValidationResult.Success);
        }
        catch (WorkspaceAccessException exception)
        {
            return ValueTask.FromResult(ValidationFailure(exception));
        }
    }

    public override ValueTask<IReadOnlyList<PermissionRequirement>>
        GetPermissionRequirementsAsync(
            object input,
            CancellationToken cancellationToken = default)
    {
        var typed = (GetWorkspaceInfoInput)input;
        var path = Resolver.Resolve(typed.WorkspaceId, ".", WorkspacePathKind.Directory);
        return ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
            [CreateReadRequirement(
                ToolPermission.ReadWorkspace,
                path,
                path.Workspace.DisplayName,
                $"Read metadata for workspace '{path.Workspace.DisplayName}'.")]);
    }

    public override ValueTask<GetWorkspaceInfoOutput> ExecuteAsync(
        GetWorkspaceInfoInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var workspace = Reader.GetWorkspace(input.WorkspaceId);
        var entries = Reader.ListDirectory(
            input.WorkspaceId,
            ".",
            Math.Min(Options.DefaultDirectoryEntries, Options.MaxDirectoryEntries),
            includeHidden: false,
            cancellationToken);

        return ValueTask.FromResult(new GetWorkspaceInfoOutput(
            workspace.Id,
            workspace.DisplayName,
            workspace.CanonicalRootPath,
            workspace.Policy.IsReadOnly,
            entries.Count(entry => entry.Type == WorkspaceEntryType.File),
            entries.Count(entry => entry.Type == WorkspaceEntryType.Directory)));
    }
}
