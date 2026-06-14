using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Tools;

public interface IWorkspacePermissionInvalidator
{
    void InvalidateWorkspacePermissions(WorkspaceId workspaceId);
}
