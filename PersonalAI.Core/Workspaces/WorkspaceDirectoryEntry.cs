namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceDirectoryEntry(
    string Name,
    string RelativePath,
    WorkspaceEntryType Type,
    long? SizeBytes,
    DateTimeOffset LastModifiedUtc,
    bool IsHidden,
    string Extension);
