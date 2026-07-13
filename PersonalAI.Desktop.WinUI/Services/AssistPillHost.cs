using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Providers;
using PersonalAI.Desktop.WinUI.Models;

namespace PersonalAI.Desktop.WinUI.Services;

public interface IAssistPillHost
{
    Task<AttachedContextItem?> CaptureContextAsync(CancellationToken cancellationToken);

    Task<AssistGenerationResult> GenerateAsync(
        string prompt,
        AttachedContextItem? context,
        Action<string> reportChunk,
        CancellationToken cancellationToken);

    Task CopyTextAsync(string text, CancellationToken cancellationToken);

    Task OpenInAedaAsync();
}

public sealed record AssistGenerationResult(
    ChatStatus Status,
    string? SafeErrorMessage = null);

public sealed class AssistPillHost(
    ConversationSessionService conversationSession,
    IApplicationSettingsService settingsService,
    IChatModelRouter modelRouter,
    Func<CancellationToken, Task<ProviderHealth>> checkProviderHealthAsync,
    Func<CancellationToken, Task<IReadOnlyList<string>>> listModelsAsync,
    ActiveWindowContextService contextService,
    Func<AttachedContextItem?> getExplicitContext,
    IClipboardWriter clipboardWriter,
    Func<Guid?, Task> openConversationAsync) : IAssistPillHost
{
    private Guid? _conversationId;

    public async Task<AttachedContextItem?> CaptureContextAsync(
        CancellationToken cancellationToken)
    {
        var explicitContext = getExplicitContext();
        if (AssistContextPolicy.IsMeaningful(explicitContext, DateTimeOffset.UtcNow))
        {
            return explicitContext;
        }

        var captured = await contextService.CaptureAsync(cancellationToken);
        return AssistContextPolicy.IsMeaningful(captured, DateTimeOffset.UtcNow)
            ? captured
            : null;
    }

    public async Task<AssistGenerationResult> GenerateAsync(
        string prompt,
        AttachedContextItem? context,
        Action<string> reportChunk,
        CancellationToken cancellationToken)
    {
        var settings = settingsService.Current;
        var selectedProvider = settings.ProviderRouting.ProviderProfiles
            .LastOrDefault(profile => profile.Id.Equals(
                settings.ProviderRouting.SelectedChatProvider,
                StringComparison.OrdinalIgnoreCase));
        var unavailableMessage = selectedProvider?.Kind == ProviderKind.Ollama
            ? "Ollama is not running."
            : "The configured chat provider is unavailable.";
        ProviderHealth health;
        try
        {
            health = await checkProviderHealthAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            health = new ProviderHealth(ProviderStatus.Unavailable);
        }

        if (health.Status != ProviderStatus.Available)
        {
            return new AssistGenerationResult(
                ChatStatus.Failed,
                unavailableMessage);
        }

        IReadOnlyList<string> models;
        try
        {
            models = await listModelsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new AssistGenerationResult(
                ChatStatus.Failed,
                unavailableMessage);
        }

        if (models.Count == 0)
        {
            return new AssistGenerationResult(
                ChatStatus.Failed,
                "No chat model is available. Check AEDA settings.");
        }

        IReadOnlyList<AttachedContextItem> contexts =
            context is null ? [] : [context];
        var decision = await modelRouter.SelectModelAsync(
            new ModelRoutingRequest(
                prompt,
                contexts
                    .Select(item => new AttachedContextSignal(
                        item.Type.ToString(),
                        item.Images.Count > 0))
                    .ToArray(),
                models,
                settings.Models.Assignments),
            cancellationToken);
        if (decision.IsCapabilityBlocked ||
            !models.Contains(decision.SelectedModel, StringComparer.OrdinalIgnoreCase))
        {
            return new AssistGenerationResult(
                ChatStatus.Failed,
                "The selected chat model is unavailable.");
        }

        var routedPrompt = string.IsNullOrWhiteSpace(decision.RoutedPrompt)
            ? prompt
            : decision.RoutedPrompt.Trim();
        var messages = AttachedContextPromptComposer.Compose(
            [],
            routedPrompt,
            contexts,
            settings.Context.MaxIndividualClipboardCharacters,
            settings.Context.MaxTotalTextContextCharacters);
        var result = await conversationSession.GenerateNewConversationTurnAsync(
            routedPrompt,
            decision.SelectedModel,
            messages,
            reportChunk,
            cancellationToken);
        _conversationId = result.ConversationId;

        return result.Status == ChatStatus.Failed
            ? new AssistGenerationResult(
                ChatStatus.Failed,
                "The response could not be completed. Check the provider connection.")
            : new AssistGenerationResult(result.Status);
    }

    public Task CopyTextAsync(string text, CancellationToken cancellationToken) =>
        clipboardWriter.CopyTextAsync(text, cancellationToken);

    public Task OpenInAedaAsync() => openConversationAsync(_conversationId);
}

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
