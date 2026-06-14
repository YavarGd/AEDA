namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceFileInfo(
    string RelativePath,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string Extension,
    bool IsHidden);
