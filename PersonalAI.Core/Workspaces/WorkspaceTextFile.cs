namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceTextFile(
    string RelativePath,
    string Content,
    string EncodingName,
    long FileSizeBytes,
    bool IsTruncated,
    bool HadDecodingErrors);
