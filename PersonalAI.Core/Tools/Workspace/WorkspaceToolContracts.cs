using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Tools.Workspace;

public sealed record GetWorkspaceInfoInput(WorkspaceId WorkspaceId);

public sealed record GetWorkspaceInfoOutput(
    WorkspaceId WorkspaceId,
    string DisplayName,
    string RootDisplayPath,
    bool IsReadOnly,
    int? ImmediateFileCount,
    int? ImmediateDirectoryCount);

public sealed record ListDirectoryInput(
    WorkspaceId WorkspaceId,
    string RelativePath,
    int MaxEntries = 200,
    bool IncludeHidden = false);

public sealed record ListDirectoryOutput(
    string RelativePath,
    IReadOnlyList<WorkspaceDirectoryEntry> Entries,
    bool IsTruncated);

public sealed record ReadTextFileInput(
    WorkspaceId WorkspaceId,
    string RelativePath,
    int MaxCharacters = 100_000);

public sealed record ReadTextFileOutput(
    string RelativePath,
    string Content,
    string EncodingName,
    long FileSizeBytes,
    bool IsTruncated,
    bool HadDecodingErrors);

public sealed record SearchWorkspaceTextInput(
    WorkspaceId WorkspaceId,
    string Query,
    string RelativeDirectory = ".",
    string? FilePattern = null,
    bool MatchCase = false,
    int MaxResults = 100);

public sealed record SearchWorkspaceTextOutput(
    string Query,
    string RelativeDirectory,
    IReadOnlyList<WorkspaceSearchMatch> Matches,
    bool IsTruncated,
    int FilesScanned,
    int FilesSkipped);
