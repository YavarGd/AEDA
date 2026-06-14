namespace PersonalAI.Core.Workspaces;

public interface IWorkspaceRegistrationService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
        CancellationToken cancellationToken = default);

    Task<PersistedWorkspace> RegisterAsync(
        string rootPath,
        string displayName,
        string source,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<PersistedWorkspace?> UpdateDisplayNameAsync(
        WorkspaceId workspaceId,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<PersistedWorkspace?> RevalidateAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task RevalidateAllAsync(CancellationToken cancellationToken = default);
}
