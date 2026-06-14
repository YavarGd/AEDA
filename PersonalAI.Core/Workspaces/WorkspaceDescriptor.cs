namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceDescriptor(
    WorkspaceId Id,
    string DisplayName,
    string CanonicalRootPath,
    DateTimeOffset RegisteredAtUtc,
    WorkspaceAccessPolicy Policy,
    string? Source = null);
