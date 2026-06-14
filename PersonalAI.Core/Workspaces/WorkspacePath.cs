namespace PersonalAI.Core.Workspaces;

public sealed record WorkspacePath(
    WorkspaceDescriptor Workspace,
    string RelativePath,
    string FullPath,
    string NormalizedResourceScope);
