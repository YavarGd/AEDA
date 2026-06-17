namespace PersonalAI.Core.Settings;

using PersonalAI.Core.Chat;

public static class ApplicationSettingsValidator
{
    public static ApplicationSettings Normalize(ApplicationSettings? settings)
    {
        var value = settings ?? ApplicationSettings.CreateDefault();
        var defaults = ApplicationSettings.CreateDefault();

        var normalizedVision = NormalizeVision(value.Vision ?? defaults.Vision);

        return value with
        {
            SchemaVersion = ApplicationSettings.CurrentSchemaVersion,
            General = value.General ?? defaults.General,
            Appearance = value.Appearance ?? defaults.Appearance,
            Models = NormalizeModels(value.Models ?? defaults.Models, normalizedVision),
            Hotkey = HotkeySettingsValidator.Normalize(value.Hotkey ?? defaults.Hotkey),
            Window = value.Window ?? defaults.Window,
            Context = NormalizeContext(value.Context ?? defaults.Context),
            Privacy = NormalizePrivacy(value.Privacy ?? defaults.Privacy),
            Vision = normalizedVision
        };
    }

    public static ContextSettings NormalizeContext(ContextSettings settings)
    {
        return settings with
        {
            MaxTotalTextContextCharacters = Clamp(
                settings.MaxTotalTextContextCharacters,
                1_000,
                100_000),
            MaxIndividualClipboardCharacters = Clamp(
                settings.MaxIndividualClipboardCharacters,
                500,
                50_000),
            MaxAttachedContextItems = Clamp(settings.MaxAttachedContextItems, 1, 20),
            ScreenshotMaxPayloadBytes = Clamp(
                settings.ScreenshotMaxPayloadBytes,
                256 * 1024,
                20 * 1024 * 1024),
            ScreenshotThumbnailMaxEdge = Clamp(
                settings.ScreenshotThumbnailMaxEdge,
                64,
                1024)
        };
    }

    public static PrivacySettings NormalizePrivacy(PrivacySettings settings)
    {
        var excluded = settings.ExcludedApplications
            .Select(item => item with
            {
                ProcessName = PrivacyExclusionMatcher.NormalizeProcessName(
                    item.ProcessName)
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.ProcessName))
            .GroupBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        return settings with { ExcludedApplications = excluded };
    }

    public static VisionSettings NormalizeVision(VisionSettings settings)
    {
        var patterns = settings.UserModelPatterns
            .Select(VisionModelCapabilityRegistry.NormalizePattern)
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return settings with { UserModelPatterns = patterns };
    }

    private static ModelSettings NormalizeModels(
        ModelSettings settings,
        VisionSettings visionSettings)
    {
        var assignments = (settings.Assignments ??
                ModelRoutingSettings.CreateDefaultAssignments())
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.Model))
            .Select(assignment => assignment with { Model = assignment.Model.Trim() })
            .Select(assignment =>
                assignment.Category == ModelRoutingCategory.Vision &&
                !VisionModelCapabilityRegistry.SupportsImages(
                    assignment.Model,
                    visionSettings)
                    ? assignment with { Model = ModelRoutingSettings.DefaultModel }
                    : assignment)
            .GroupBy(assignment => assignment.Category)
            .Select(group => group.First())
            .ToList();

        foreach (var defaultAssignment in
                 ModelRoutingSettings.CreateDefaultAssignments())
        {
            if (assignments.All(assignment =>
                    assignment.Category != defaultAssignment.Category))
            {
                assignments.Add(defaultAssignment);
            }
        }

        return settings with { Assignments = assignments };
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Min(maximum, Math.Max(minimum, value));
    }
}
