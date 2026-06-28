using System.Text;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Editor;
using PersonalAI.Core.Settings;
using PersonalAI.Infrastructure.Ipc;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class EditorCodeChatResponder(
    ChatSessionService chatSession,
    IApplicationSettingsService settingsService,
    Func<CancellationToken, Task<IReadOnlyList<string>>> listModelsAsync)
{
    public const int MaxPromptSelectedTextCharacters = 24_000;
    private const string PreferredCodingModel = "qwen2.5-coder:7b";

    public async Task<EditorContextHandlerResult> RespondAsync(
        EditorContextEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!IsSupportedCommand(envelope.Command))
        {
            return EditorContextHandlerResult.Failure(
                "Unsupported VS Code command.");
        }

        if (envelope.Context is null)
        {
            return EditorContextHandlerResult.Failure(
                "VS Code context is required.");
        }

        var selectedText = envelope.Context.SelectedText;
        if (string.IsNullOrWhiteSpace(selectedText))
        {
            return EditorContextHandlerResult.Failure(
                "Select code in VS Code before using this command.");
        }

        var messages = BuildMessages(envelope);
        var model = await SelectModelAsync(cancellationToken).ConfigureAwait(false);
        var answer = new StringBuilder();

        try
        {
            await foreach (var chunk in chatSession.StreamAsync(
                               model,
                               messages,
                               cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    answer.Append(chunk.Content);
                }
            }
        }
        catch (OperationCanceledException)
        {
            return EditorContextHandlerResult.Failure("Request cancelled.");
        }
        catch (Exception exception) when (IsSafeProviderFailure(exception))
        {
            return EditorContextHandlerResult.Failure(
                "PersonalAI could not reach an available local chat provider.");
        }

        var message = answer.ToString().Trim();
        return string.IsNullOrWhiteSpace(message)
            ? EditorContextHandlerResult.Failure(
                "The chat provider returned an empty response.")
            : EditorContextHandlerResult.Success(message);
    }

    private async Task<string> SelectModelAsync(CancellationToken cancellationToken)
    {
        var settings = settingsService.Current;
        var configuredCodingModel = settings.Models.Assignments
            .FirstOrDefault(assignment =>
                assignment.Category == ModelRoutingCategory.Coding)
            ?.Model;
        var configuredChatModel = settings.ProviderRouting.ProviderProfiles
            .FirstOrDefault(profile =>
                profile.Id.Equals(
                    settings.ProviderRouting.SelectedChatProvider,
                    StringComparison.OrdinalIgnoreCase))
            ?.ChatModel;
        var fallbackModel = FirstNonEmpty(
            configuredCodingModel,
            configuredChatModel,
            ModelRoutingSettings.DefaultModel);
        var installedModels = await listModelsAsync(cancellationToken)
            .ConfigureAwait(false);
        var configuredModels = settings.Models.Assignments
            .Select(assignment => assignment.Model)
            .Concat(settings.ProviderRouting.ProviderProfiles.Select(profile => profile.ChatModel))
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model!);
        var knownModels = installedModels
            .Concat(configuredModels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return knownModels.Any(model =>
            model.Equals(PreferredCodingModel, StringComparison.OrdinalIgnoreCase))
            ? PreferredCodingModel
            : fallbackModel;
    }

    private static IReadOnlyList<ChatMessage> BuildMessages(
        EditorContextEnvelope envelope)
    {
        return
        [
            new ChatMessage(ChatRole.System, """
                You are AEDA's read-only coding assistant for VS Code editor selections.
                You may explain, answer questions about, and review selected code.
                Do not modify files, do not propose patches, do not run tools, and do not claim to have changed the workspace.
                Keep answers specific to the provided selection and mention uncertainty when context is missing.
                """),
            new ChatMessage(ChatRole.User, BuildUserPrompt(envelope))
        ];
    }

    private static string BuildUserPrompt(EditorContextEnvelope envelope)
    {
        var context = envelope.Context!;
        var selectedText = BoundSelectedText(context.SelectedText ?? string.Empty);
        var builder = new StringBuilder();

        builder.AppendLine("Attached context: VsCodeEditor");
        builder.AppendLine("This is read-only workspace editor context.");
        AppendIfPresent(builder, "Command", envelope.Command);
        AppendIfPresent(builder, "File name", SafeLabel(context.FileName));
        AppendIfPresent(builder, "Relative path", SafeLabel(context.RelativeWorkspacePath));
        AppendIfPresent(builder, "Language", SafeLabel(context.LanguageId));

        if (selectedText.WasTruncated)
        {
            builder.AppendLine(
                $"Selected code was truncated to {MaxPromptSelectedTextCharacters} characters.");
        }

        builder.AppendLine();
        builder.AppendLine(CommandInstruction(envelope));
        builder.AppendLine();
        builder.AppendLine("Selected code:");
        builder.AppendLine("```" + SafeFenceLanguage(context.LanguageId));
        builder.AppendLine(selectedText.Text);
        builder.AppendLine("```");

        return builder.ToString();
    }

    private static string CommandInstruction(EditorContextEnvelope envelope)
    {
        if (envelope.Command.Equals(
                EditorContextCommands.ExplainSelection,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Explain what this selected code does. Be specific, but do not modify files.";
        }

        if (envelope.Command.Equals(
                EditorContextCommands.FindProblemsInSelection,
                StringComparison.OrdinalIgnoreCase))
        {
            return "Review this selected code for likely bugs, edge cases, or maintainability issues. Do not modify files.";
        }

        var question = string.IsNullOrWhiteSpace(envelope.UserPrompt)
            ? "Answer the user's question about this selected code."
            : envelope.UserPrompt.Trim();
        return "Answer this question about the selected code. Do not modify files.\nQuestion: " +
            question;
    }

    private static (string Text, bool WasTruncated) BoundSelectedText(string selectedText)
    {
        if (selectedText.Length <= MaxPromptSelectedTextCharacters)
        {
            return (selectedText, false);
        }

        return (selectedText[..MaxPromptSelectedTextCharacters], true);
    }

    private static bool IsSupportedCommand(string command) =>
        command.Equals(EditorContextCommands.AskAboutSelection, StringComparison.OrdinalIgnoreCase) ||
        command.Equals(EditorContextCommands.ExplainSelection, StringComparison.OrdinalIgnoreCase) ||
        command.Equals(EditorContextCommands.FindProblemsInSelection, StringComparison.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

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

    private static string? SafeLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return Path.GetFileName(trimmed);
        }

        return trimmed.Replace('\\', '/');
    }

    private static string SafeFenceLanguage(string? languageId)
    {
        if (string.IsNullOrWhiteSpace(languageId))
        {
            return string.Empty;
        }

        var safe = new string(languageId
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '#')
            .ToArray());
        return safe.Length == 0 ? string.Empty : safe;
    }

    private static bool IsSafeProviderFailure(Exception exception) =>
        exception is InvalidOperationException ||
        exception is HttpRequestException ||
        exception is TimeoutException ||
        exception is IOException;
}
