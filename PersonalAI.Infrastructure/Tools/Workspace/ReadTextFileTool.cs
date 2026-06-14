using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Tools.Workspace;

public sealed class ReadTextFileTool(
    IWorkspaceReader reader,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options)
    : WorkspaceToolBase<ReadTextFileInput, ReadTextFileOutput>(
        reader,
        resolver,
        options)
{
    public static ToolId Id { get; } = new("workspace.file.read_text");

    public override ToolDescriptor Descriptor { get; } =
        ToolDescriptor.Create<ReadTextFileInput, ReadTextFileOutput>(
            Id,
            "Read workspace text file",
            "Reads bounded text content from an approved workspace file.",
            requiredPermissions: [ToolPermission.ReadWorkspace],
            requiresApproval: true,
            category: "Workspace",
            isReadOnly: true,
            outputMayContainSensitiveData: true);

    public override ValueTask<ToolValidationResult> ValidateAsync(
        ReadTextFileInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (input.MaxCharacters <= 0 ||
                input.MaxCharacters > Options.MaxReadCharacters)
            {
                return ValueTask.FromResult(ToolValidationResult.Failure(
                    "request_limit_too_high",
                    "Read character limit was outside the allowed range."));
            }

            _ = Resolver.Resolve(
                input.WorkspaceId,
                input.RelativePath,
                WorkspacePathKind.File);
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
        var typed = (ReadTextFileInput)input;
        var path = Resolver.Resolve(
            typed.WorkspaceId,
            typed.RelativePath,
            WorkspacePathKind.File);
        return ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
            [CreateReadRequirement(
                ToolPermission.ReadWorkspace,
                path,
                $"{path.Workspace.DisplayName}/{path.RelativePath}",
                $"Read file '{path.RelativePath}' in workspace '{path.Workspace.DisplayName}'.")]);
    }

    public override ValueTask<ReadTextFileOutput> ExecuteAsync(
        ReadTextFileInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var text = Reader.ReadTextFile(
            input.WorkspaceId,
            input.RelativePath,
            input.MaxCharacters,
            cancellationToken);
        return ValueTask.FromResult(new ReadTextFileOutput(
            text.RelativePath,
            text.Content,
            text.EncodingName,
            text.FileSizeBytes,
            text.IsTruncated,
            text.HadDecodingErrors));
    }
}
