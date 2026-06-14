namespace PersonalAI.Core.Workspaces;

public sealed record PersistedWorkspace(
    WorkspaceId Id,
    string DisplayName,
    string CanonicalRootPath,
    string Source,
    DateTimeOffset AddedAtUtc,
    DateTimeOffset? LastValidatedAtUtc,
    WorkspaceRegistrationStatus Status,
    string? SafeStatusCode,
    bool IsReadOnly,
    DateTimeOffset? RemovedAtUtc = null)
{
    public bool IsActive => RemovedAtUtc is null;
}
