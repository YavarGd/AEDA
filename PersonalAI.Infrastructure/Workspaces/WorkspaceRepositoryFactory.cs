using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Persistence;

namespace PersonalAI.Infrastructure.Workspaces;

public static class WorkspaceRepositoryFactory
{
    public static IWorkspaceRepository CreateDefaultRepository()
    {
        return new SqliteWorkspaceRepository(
            ConversationDatabasePaths.GetDefaultDatabasePath());
    }
}
