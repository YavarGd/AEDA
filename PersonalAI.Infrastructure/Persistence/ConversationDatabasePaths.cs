namespace PersonalAI.Infrastructure.Persistence;

public static class ConversationDatabasePaths
{
    public static string GetDefaultDatabasePath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppData, "PersonalAI", "personalai.db");
    }
}
