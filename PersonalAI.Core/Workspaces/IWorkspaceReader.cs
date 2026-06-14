namespace PersonalAI.Core.Workspaces;

public interface IWorkspaceReader
{
    WorkspaceDescriptor GetWorkspace(WorkspaceId workspaceId);

    IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
        WorkspaceId workspaceId,
        string relativePath,
        int maxEntries,
        bool includeHidden,
        CancellationToken cancellationToken = default);

    WorkspaceTextFile ReadTextFile(
        WorkspaceId workspaceId,
        string relativePath,
        int maxCharacters,
        CancellationToken cancellationToken = default);

    WorkspaceSearchResult SearchText(
        WorkspaceId workspaceId,
        string query,
        string relativeDirectory,
        string? filePattern,
        bool matchCase,
        int maxResults,
        CancellationToken cancellationToken = default);
}
