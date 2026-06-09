using System.Text;

namespace PersonalAI.Core.Context;

public static class ContextFormatter
{
    public static string FormatPreview(ActiveApplicationContext context)
    {
        var builder = new StringBuilder();

        AppendIfPresent(builder, "Application", context.ProcessName);
        AppendIfPresent(builder, "Window", context.WindowTitle);
        AppendIfPresent(builder, "Executable", context.ExecutablePath);

        if (!string.IsNullOrWhiteSpace(context.CapturedSelectedText))
        {
            AppendIfPresent(builder, "Text", context.CapturedSelectedText);
        }

        if (!string.IsNullOrWhiteSpace(context.ScreenshotPath))
        {
            AppendIfPresent(builder, "Screenshot", context.ScreenshotPath);
        }

        if (builder.Length == 0)
        {
            builder.Append("No active application details were available.");
        }

        return builder.ToString();
    }

    public static string FormatPromptBlock(ActiveApplicationContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Attached active-window context");
        builder.AppendLine("---");
        AppendIfPresent(builder, "Application", context.ProcessName);
        AppendIfPresent(builder, "Window title", context.WindowTitle);
        AppendIfPresent(builder, "Executable path", context.ExecutablePath);

        if (!string.IsNullOrWhiteSpace(context.CapturedSelectedText))
        {
            builder.AppendLine("Selected or clipboard text:");
            builder.AppendLine(context.CapturedSelectedText);
        }

        builder.AppendLine("---");
        builder.AppendLine("Use this context only if it is relevant to the user's request.");

        return builder.ToString();
    }

    private static void AppendIfPresent(
        StringBuilder builder,
        string label,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        builder.Append(label);
        builder.Append(": ");
        builder.AppendLine(value.Trim());
    }
}
