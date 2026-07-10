namespace PersonalAI.Core.Settings;

public static class PrivacyExclusionMatcher
{
    private static readonly HashSet<string> Browsers = new(
        ["brave", "chrome", "firefox", "msedge", "opera"],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsExcluded(
        string? processName,
        IEnumerable<ExcludedApplicationSetting> exclusions)
    {
        var normalized = NormalizeProcessName(processName);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return exclusions.Any(exclusion =>
            exclusion.IsEnabled &&
            NormalizeProcessName(exclusion.ProcessName).Equals(
                normalized,
                StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsSensitiveWindow(
        string? processName,
        string? windowTitle,
        IEnumerable<ExcludedApplicationSetting> exclusions)
    {
        if (IsExcluded(processName, exclusions))
        {
            return true;
        }

        return Browsers.Contains(NormalizeProcessName(processName)) &&
            !string.IsNullOrWhiteSpace(windowTitle) &&
            (windowTitle.Contains("InPrivate", StringComparison.OrdinalIgnoreCase) ||
             windowTitle.Contains("Incognito", StringComparison.OrdinalIgnoreCase) ||
             windowTitle.Contains("Private Browsing", StringComparison.OrdinalIgnoreCase));
    }

    public static string NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return string.Empty;
        }

        var normalized = processName.Trim();

        if (normalized.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^4];
        }

        return normalized;
    }
}
