using System.Security.Cryptography;
using System.Text;
using PersonalAI.Core.Editor;

namespace PersonalAI.Core.Context;

public static class AttachedContextFactory
{
    private const int PreviewLength = 180;

    public static AttachedContextItem FromClipboardText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException(
                "Clipboard text must not be empty.",
                nameof(text));
        }

        var normalized = text.Trim();
        return new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.Clipboard,
            "Clipboard",
            "Clipboard text",
            CreatePreview(normalized),
            normalized,
            new Dictionary<string, string>
            {
                ["characters"] = normalized.Length.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            },
            DateTimeOffset.UtcNow,
            $"clipboard:{Hash(normalized)}");
    }

    public static AttachedContextItem FromActiveApplicationContext(
        ActiveApplicationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var title = FirstNonEmpty(
            context.WindowTitle,
            context.ProcessName,
            "Active application");
        var source = FirstNonEmpty(context.ProcessName, "Application");
        var payload = ContextFormatter.FormatPromptBlock(context);
        var metadata = new Dictionary<string, string>();

        AddMetadata(metadata, "processName", context.ProcessName);
        AddMetadata(metadata, "windowTitle", context.WindowTitle);
        AddMetadata(metadata, "processId", context.ProcessId?.ToString(
            System.Globalization.CultureInfo.InvariantCulture));
        AddMetadata(metadata, "executablePath", context.ExecutablePath);

        return new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.ApplicationWindow,
            source,
            title,
            CreatePreview(payload),
            payload,
            metadata,
            context.CapturedAtUtc,
            $"app:{context.ProcessId}:{context.ProcessName}:{context.WindowTitle}");
    }

    public static AttachedContextItem FromEditorContext(
        EditorContextEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var context = envelope.Context;
        var title = FirstNonEmpty(
            context?.FileName,
            context?.RelativeWorkspacePath,
            "VS Code editor context");
        var source = FirstNonEmpty(context?.WorkspaceFolderName, "VS Code");
        var payload = EditorContextPromptComposer.FormatPromptBlock(envelope);
        var metadata = new Dictionary<string, string>
        {
            ["command"] = envelope.Command,
            ["requestId"] = envelope.RequestId
        };

        AddMetadata(metadata, "fileName", context?.FileName);
        AddMetadata(metadata, "language", context?.LanguageId);
        AddMetadata(metadata, "workspace", context?.WorkspaceFolderName);
        AddMetadata(metadata, "relativePath", context?.RelativeWorkspacePath);

        return new AttachedContextItem(
            Guid.NewGuid(),
            AttachedContextType.VsCodeEditor,
            source,
            title,
            CreatePreview(EditorContextPromptComposer.FormatPreview(envelope)),
            payload,
            metadata,
            context?.TimestampUtc ?? DateTimeOffset.UtcNow,
            $"vscode:{context?.WorkspaceFolderPath}:{context?.FullActiveFilePath}:{context?.Selection}:{Hash(context?.SelectedText ?? string.Empty)}");
    }

    public static string CreatePreview(string text)
    {
        var singleLine = text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();

        if (singleLine.Length <= PreviewLength)
        {
            return singleLine;
        }

        return singleLine[..PreviewLength] + "...";
    }

    private static void AddMetadata(
        IDictionary<string, string> metadata,
        string key,
        string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();
    }

    private static string Hash(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes);
    }
}
