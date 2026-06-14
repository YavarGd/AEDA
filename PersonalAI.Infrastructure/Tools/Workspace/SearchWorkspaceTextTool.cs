using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Infrastructure.Tools.Workspace;

public sealed class SearchWorkspaceTextTool(
    IWorkspaceReader reader,
    IWorkspacePathResolver resolver,
    WorkspaceToolOptions options)
    : WorkspaceToolBase<SearchWorkspaceTextInput, SearchWorkspaceTextOutput>(
        reader,
        resolver,
        options)
{
    public static ToolId Id { get; } = new("workspace.text.search");

    public override ToolDescriptor Descriptor { get; } =
        ToolDescriptor.Create<SearchWorkspaceTextInput, SearchWorkspaceTextOutput>(
            Id,
            "Search workspace text",
            "Performs bounded literal text search in an approved workspace.",
            requiredPermissions: [ToolPermission.ReadWorkspace],
            requiresApproval: true,
            category: "Workspace",
            isReadOnly: true,
            outputMayContainSensitiveData: true);

    public override ValueTask<ToolValidationResult> ValidateAsync(
        SearchWorkspaceTextInput input,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(input.Query) ||
                input.Query.Length > Options.MaxSearchQueryLength)
            {
                return ValueTask.FromResult(ToolValidationResult.Failure(
                    "invalid_search_query",
                    "Search query was empty or too long."));
            }

            if (input.MaxResults <= 0 ||
                input.MaxResults > Options.MaxSearchResults)
            {
                return ValueTask.FromResult(ToolValidationResult.Failure(
                    "search_limit_exceeded",
                    "Search result limit was outside the allowed range."));
            }

            FileSystemWorkspaceReader.ValidateFilePattern(input.FilePattern);
            _ = Resolver.Resolve(
                input.WorkspaceId,
                input.RelativeDirectory,
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
        var typed = (SearchWorkspaceTextInput)input;
        var path = Resolver.Resolve(
            typed.WorkspaceId,
            typed.RelativeDirectory,
            WorkspacePathKind.Directory);
        return ValueTask.FromResult<IReadOnlyList<PermissionRequirement>>(
            [CreateReadRequirement(
                ToolPermission.ReadWorkspace,
                path,
                $"{path.Workspace.DisplayName}/{path.RelativePath}",
                $"Search text under '{path.RelativePath}' in workspace '{path.Workspace.DisplayName}'.")]);
    }

    public override ValueTask<SearchWorkspaceTextOutput> ExecuteAsync(
        SearchWorkspaceTextInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var result = Reader.SearchText(
            input.WorkspaceId,
            input.Query,
            input.RelativeDirectory,
            input.FilePattern,
            input.MatchCase,
            input.MaxResults,
            cancellationToken);
        return ValueTask.FromResult(new SearchWorkspaceTextOutput(
            result.Query,
            result.RelativeDirectory,
            result.Matches,
            result.IsTruncated,
            result.FilesScanned,
            result.FilesSkipped));
    }
}
