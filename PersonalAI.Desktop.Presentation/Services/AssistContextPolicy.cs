using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.Presentation.Services;

public static class AssistContextPolicy
{
    public static bool IsMeaningful(
        AttachedContextItem? context,
        DateTimeOffset now)
    {
        if (context is null ||
            context.Type is not (AttachedContextType.ApplicationWindow or
                AttachedContextType.VsCodeEditor) ||
            context.CreatedAtUtc < now.AddMinutes(-2) ||
            context.CreatedAtUtc > now.AddMinutes(1) ||
            !context.Metadata.TryGetValue("selectedTextCharacters", out var value))
        {
            return false;
        }

        return int.TryParse(value, out var characters) && characters > 0;
    }

    public static bool MatchesForeground(
        AttachedContextItem context,
        ActiveWindowReference? foreground)
    {
        if (context.Type != AttachedContextType.VsCodeEditor || foreground is null)
        {
            return false;
        }

        var processName = PrivacyExclusionMatcher.NormalizeProcessName(
            foreground.ProcessName);
        if (!processName.Equals("Code", StringComparison.OrdinalIgnoreCase) &&
            !processName.StartsWith("Code - ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(foreground.WindowTitle))
        {
            return true;
        }

        var candidates = new[]
        {
            context.Metadata.GetValueOrDefault("fileName"),
            context.Metadata.GetValueOrDefault("workspace")
        }.Where(value => !string.IsNullOrWhiteSpace(value));

        return !candidates.Any() || candidates.Any(value =>
            foreground.WindowTitle.Contains(value!, StringComparison.OrdinalIgnoreCase));
    }
}
