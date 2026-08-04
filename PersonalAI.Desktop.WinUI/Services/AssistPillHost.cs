using PersonalAI.Core.Chat;
using PersonalAI.Core.Context;
using PersonalAI.Core.Settings;
using PersonalAI.Core.Providers;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Context;
using PersonalAI.Infrastructure.ScreenCapture;

namespace PersonalAI.Desktop.WinUI.Services;

public interface IAssistPillHost
{
    string? LastCaptureFailureMessage { get; }

    Task<AttachedContextItem?> CaptureContextAsync(CancellationToken cancellationToken);

    Task<AttachedContextItem?> CaptureScreenTextAsync(CancellationToken cancellationToken);

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
    IActiveWindowContextService contextService,
    IScreenTextCaptureService screenTextCaptureService,
    Func<AttachedContextItem?> getExplicitContext,
    IClipboardWriter clipboardWriter,
    Func<Guid?, Task> openConversationAsync) : IAssistPillHost
{
    private Guid? _conversationId;

    public string? LastCaptureFailureMessage { get; private set; }

    public async Task<AttachedContextItem?> CaptureContextAsync(
        CancellationToken cancellationToken)
    {
        var explicitContext = getExplicitContext();
        var captured = await contextService.CaptureAsync(explicitContext, cancellationToken);
        LastCaptureFailureMessage = contextService.LastCaptureResult?.FailureReason switch
        {
            SelectedTextCaptureFailure.PrivacyBlocked =>
                "Selection capture is disabled for this application.",
            SelectedTextCaptureFailure.ElevatedTarget =>
                "Selection capture is unavailable for elevated applications.",
            SelectedTextCaptureFailure.ClipboardRestoreFailed =>
                "Clipboard restoration failed; your clipboard may have changed.",
            _ => null
        };
        return AssistContextPolicy.IsMeaningful(captured, DateTimeOffset.UtcNow)
            ? captured
            : null;
    }

    public async Task<AttachedContextItem?> CaptureScreenTextAsync(
        CancellationToken cancellationToken)
    {
        var result = await screenTextCaptureService.CaptureAsync(
            settingsService.Current.Context.MaxIndividualClipboardCharacters,
            settingsService.Current.Context.ScreenshotMaxPayloadBytes,
            cancellationToken);
        LastCaptureFailureMessage = result.Message;
        if (result.Status != ScreenTextCaptureStatus.Success ||
            string.IsNullOrWhiteSpace(result.Text))
        {
            return null;
        }

        var context = AttachedContextFactory.FromActiveApplicationContext(new(
            null,
            null,
            "Screen text",
            null,
            "Selected screen area",
            result.Text,
            null,
            null,
            DateTimeOffset.UtcNow));
        var metadata = context.Metadata.ToDictionary(pair => pair.Key, pair => pair.Value);
        metadata["captureSource"] = "screenOcr";
        return context with
        {
            SourceName = "Screen text",
            DisplayTitle = "Selected screen text",
            Metadata = metadata
        };
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
