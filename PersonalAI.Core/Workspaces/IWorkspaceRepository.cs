namespace PersonalAI.Core.Workspaces;

public interface IWorkspaceRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
        bool includeRemoved = false,
        CancellationToken cancellationToken = default);

    Task<PersistedWorkspace?> GetAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<PersistedWorkspace?> FindActiveByCanonicalRootAsync(
        string canonicalRootPath,
        CancellationToken cancellationToken = default);

    Task UpsertAsync(
        PersistedWorkspace workspace,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        WorkspaceId workspaceId,
        DateTimeOffset removedAtUtc,
        CancellationToken cancellationToken = default);
}
