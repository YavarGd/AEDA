namespace PersonalAI.Core.Settings;

public static class PrivacyExclusionMatcher
{
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
