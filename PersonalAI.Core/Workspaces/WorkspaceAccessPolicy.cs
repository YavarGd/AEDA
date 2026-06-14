namespace PersonalAI.Core.Workspaces;

public sealed record WorkspaceAccessPolicy(bool IsReadOnly)
{
    public static WorkspaceAccessPolicy ReadOnly { get; } = new(true);
}
