namespace PersonalAI.Core.Workspaces;

public interface IWorkspaceRegistry
{
    WorkspaceDescriptor Register(
        string rootPath,
        string? displayName = null,
        string? source = null);

    WorkspaceDescriptor Register(
        WorkspaceId workspaceId,
        string rootPath,
        string? displayName = null,
        string? source = null);

    bool TryGet(WorkspaceId workspaceId, out WorkspaceDescriptor workspace);

    IReadOnlyList<WorkspaceDescriptor> List();

    bool Remove(WorkspaceId workspaceId);
}
