using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Settings;

public static class ApplicationSettingsPaths
{
    public static string GetDefaultSettingsPath()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppData, "PersonalAI", "settings.json");
    }
}
