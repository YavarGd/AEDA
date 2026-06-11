namespace PersonalAI.Core.Settings;

public static class VisionModelCapabilityRegistry
{
    public static readonly IReadOnlyList<string> BuiltInPatterns =
    [
        "llava",
        "bakllava",
        "moondream",
        "llama3.2-vision",
        "llama3.2vision",
        "minicpm-v",
        "gemma3",
        "gemma4"
    ];

    public static bool SupportsImages(
        string? model,
        VisionSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        var normalizedModel = NormalizeModelName(model);
        var patterns = BuiltInPatterns
            .Concat(settings?.UserModelPatterns ?? [])
            .Select(NormalizePattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern));

        return patterns.Any(pattern => MatchesPattern(normalizedModel, pattern));
    }

    public static string NormalizePattern(string? pattern)
    {
        return string.IsNullOrWhiteSpace(pattern)
            ? string.Empty
            : NormalizeModelName(pattern);
    }

    private static bool MatchesPattern(string model, string pattern)
    {
        if (model.Equals(pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return model.StartsWith(pattern + ":", StringComparison.OrdinalIgnoreCase) ||
            model.StartsWith(pattern + "-", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("-" + pattern + "-", StringComparison.OrdinalIgnoreCase) ||
            model.Contains("/" + pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeModelName(string model)
    {
        return model.Trim();
    }
}
