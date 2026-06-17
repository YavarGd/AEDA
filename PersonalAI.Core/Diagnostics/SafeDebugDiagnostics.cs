using System.Diagnostics;

namespace PersonalAI.Core.Diagnostics;

public static class SafeDebugDiagnostics
{
    public static string LogPath =>
        Path.Combine(
            Path.GetTempPath(),
            "PersonalAI",
            "workspace-tool-diagnostics.log");

    [Conditional("DEBUG")]
    public static void WriteLine(string message)
    {
        Debug.WriteLine(message);

        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(
                LogPath,
                $"{DateTimeOffset.UtcNow:O} {message}{Environment.NewLine}");
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is NotSupportedException ||
            exception is ArgumentException)
        {
        }
    }
}
