namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceSearchResult(
    string Query,
    string RelativeDirectory,
    IReadOnlyList<WorkspaceSearchMatch> Matches,
    bool IsTruncated,
    int FilesScanned,
    int FilesSkipped);
