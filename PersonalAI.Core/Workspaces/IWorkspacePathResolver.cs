namespace PersonalAI.Core.Workspaces;

public interface IWorkspacePathResolver
{
    WorkspacePath Resolve(
        WorkspaceId workspaceId,
        string relativePath,
        WorkspacePathKind expectedKind = WorkspacePathKind.Any);
}
