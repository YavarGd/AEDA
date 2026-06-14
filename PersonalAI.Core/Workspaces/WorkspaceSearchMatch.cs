namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceSearchMatch(
    string RelativeFilePath,
    int LineNumber,
    string LinePreview,
    int MatchStartIndex,
    int MatchLength);
