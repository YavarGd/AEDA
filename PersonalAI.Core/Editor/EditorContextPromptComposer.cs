using System.Text;

namespace PersonalAI.Core.Editor;

public static class EditorContextPromptComposer
{
    public static string FormatPreview(EditorContextEnvelope envelope)
    {
        if (envelope.Context is null)
        {
            return "Source: VS Code";
        }

        var context = envelope.Context;
        var selectedLength = context.SelectedText?.Length ?? 0;

        var builder = new StringBuilder();
        builder.AppendLine("Source: VS Code");
        AppendIfPresent(builder, "File", context.FileName);
        AppendIfPresent(builder, "Language", context.LanguageId);
        builder.AppendLine($"Selected characters: {selectedLength}");
        builder.AppendLine($"Diagnostics: {context.Diagnostics.Count}");
        AppendIfPresent(builder, "Prompt", envelope.UserPrompt);

        return builder.ToString().TrimEnd();
    }

    public static string FormatPromptBlock(EditorContextEnvelope envelope)
    {
        if (envelope.Context is null)
        {
            return "VS Code requested PersonalAI to open.";
        }

        var context = envelope.Context;
        var builder = new StringBuilder();
        builder.AppendLine("Attached VS Code editor context");
        builder.AppendLine("---");
        AppendIfPresent(builder, "Command", envelope.Command);
        AppendIfPresent(builder, "User prompt", envelope.UserPrompt);
        AppendIfPresent(builder, "File name", context.FileName);
        AppendIfPresent(builder, "Language", context.LanguageId);
        AppendIfPresent(builder, "Relative path", context.RelativeWorkspacePath);
        AppendIfPresent(builder, "Workspace", context.WorkspaceFolderName);

        if (context.Selection is not null)
        {
            builder.AppendLine(
                $"Selection: {context.Selection.StartLine}:{context.Selection.StartCharacter} - {context.Selection.EndLine}:{context.Selection.EndCharacter}");
        }

        builder.AppendLine($"Document dirty: {context.IsDirty}");
        builder.AppendLine($"Diagnostics: {context.Diagnostics.Count}");

        foreach (var diagnostic in context.Diagnostics)
        {
            builder.Append("- ");
            builder.Append(diagnostic.Severity);
            builder.Append(": ");
            builder.AppendLine(diagnostic.Message);
        }

        if (!string.IsNullOrEmpty(context.SelectedText))
        {
            builder.AppendLine("--- Selected code ---");
            builder.AppendLine(context.SelectedText);
        }

        builder.AppendLine("---");
        builder.AppendLine("Use the editor context only if it is relevant to the user's request.");

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
