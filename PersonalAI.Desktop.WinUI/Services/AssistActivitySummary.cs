using PersonalAI.Core.Context;

namespace PersonalAI.Desktop.WinUI.Services;

public static class AssistActivitySummary
{
    public static string CreateSafeSummary(
        AssistContextEnvelope? context,
        string action)
    {
        if (context is null || !context.HasContext)
        {
            return $"{action} (no context)";
        }

        var appLabel = string.IsNullOrWhiteSpace(context.ApplicationLabel)
            ? "unknown"
            : context.ApplicationLabel;

        var textInfo = context.SelectedTextLength > 0
            ? $" ({context.SelectedTextLength} chars)"
            : string.Empty;

        return $"{action} [{context.ContextKind}] from {appLabel}{textInfo}";
    }
}
