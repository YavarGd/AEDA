using PersonalAI.Core.Chat;
using PersonalAI.Infrastructure.Persistence;

namespace PersonalAI.Infrastructure.Chat;

public static class ConversationRepositoryFactory
{
    public static IConversationRepository CreateDefaultRepository()
    {
        return new SqliteConversationRepository(
            ConversationDatabasePaths.GetDefaultDatabasePath());
    }
}
