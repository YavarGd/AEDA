using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Tools.Workspace;

public sealed class ListDirectoryTool(
    IWorkspaceReader reader,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options)
    : WorkspaceToolBase<ListDirectoryInput, ListDirectoryOutput>(
        reader,
        resolver,
        options)
{
    public static ToolId Id { get; } = new("workspace.directory.list");

    public override ToolDescriptor Descriptor { get; } =
        ToolDescriptor.Create<ListDirectoryInput, ListDirectoryOutput>(
            Id,
            "List workspace directory",
            "Lists immediate entries in an approved workspace directory.",
            requiredPermissions: [ToolPermission.ReadWorkspace],
            requiresApproval: true,
            category: "Workspace",
            isReadOnly: true);

    public override ValueTask<ToolValidationResult> ValidateAsync(
        ListDirectoryInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (input.MaxEntries <= 0 || input.MaxEntries > Options.MaxDirectoryEntries)
            {
                return ValueTask.FromResult(ToolValidationResult.Failure(
                    "request_limit_too_high",
                    "Directory entry limit was outside the allowed range."));
            }

            _ = Resolver.Resolve(
                input.WorkspaceId,
                input.RelativePath,
                WorkspacePathKind.Directory);
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
        var typed = (ListDirectoryInput)input;
        var path = Resolver.Resolve(
            typed.WorkspaceId,
            typed.RelativePath,
            WorkspacePathKind.Directory);
        return ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
            [CreateReadRequirement(
                ToolPermission.ReadWorkspace,
                path,
                $"{path.Workspace.DisplayName}/{path.RelativePath}",
                $"List directory '{path.RelativePath}' in workspace '{path.Workspace.DisplayName}'.")]);
    }

    public override ValueTask<ListDirectoryOutput> ExecuteAsync(
        ListDirectoryInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var entries = Reader.ListDirectory(
            input.WorkspaceId,
            input.RelativePath,
            input.MaxEntries,
            input.IncludeHidden,
            cancellationToken);

        return ValueTask.FromResult(new ListDirectoryOutput(
            WorkspacePathResolver.NormalizeDisplayRelativePath(input.RelativePath),
            entries.Take(input.MaxEntries).ToArray(),
            entries.Count >= input.MaxEntries));
    }
}
