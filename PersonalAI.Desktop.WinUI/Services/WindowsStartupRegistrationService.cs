using Microsoft.Win32;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class WindowsStartupRegistrationService : IStartupRegistrationService
{
    private const string RunKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PersonalAI.WinUI";

    public bool IsSupported => GetExecutablePath() is not null;

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value &&
            !string.IsNullOrWhiteSpace(value);
    }

    public StartupRegistrationResult SetEnabled(bool enabled)
    {
        var executablePath = GetExecutablePath();

        if (executablePath is null)
        {
            return new StartupRegistrationResult(
                false,
                "Launch at sign-in is unavailable for this development host.");
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                key.SetValue(ValueName, $"\"{executablePath}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return new StartupRegistrationResult(
                true,
                enabled
                    ? "PersonalAI will launch at Windows sign-in."
                    : "PersonalAI will not launch at Windows sign-in.");
        }
        catch (UnauthorizedAccessException exception)
        {
            return new StartupRegistrationResult(false, exception.Message);
        }
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;

        if (string.IsNullOrWhiteSpace(path) ||
            path.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return path;
    }
}
