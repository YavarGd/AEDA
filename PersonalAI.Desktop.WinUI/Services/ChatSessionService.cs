using PersonalAI.Core.Chat;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ChatSessionService
{
    private readonly IChatProvider? _chatProvider;
    private readonly IProviderFactory? _providerFactory;
    private readonly IApplicationSettingsService? _settingsService;
    private readonly IContextPrivacyFilter _contextPrivacyFilter;

    public ChatSessionService(IChatProvider chatProvider)
    {
        _chatProvider = chatProvider;
        _contextPrivacyFilter = new ContextPrivacyFilter();
    }

    public ChatSessionService(
        IProviderFactory providerFactory,
        IApplicationSettingsService settingsService,
        IContextPrivacyFilter? contextPrivacyFilter = null)
    {
        _providerFactory = providerFactory;
        _settingsService = settingsService;
        _contextPrivacyFilter = contextPrivacyFilter ?? new ContextPrivacyFilter();
    }

    public string ProviderName => _chatProvider?.ProviderName ??
        _settingsService?.Current.ProviderRouting.SelectedChatProvider ??
        "unknown";

    public bool SupportsToolCalls
    {
        get
        {
            if (_chatProvider is not null)
            {
                return _chatProvider is IToolCallingChatProvider;
            }

            var route = TryGetSelectedProvider();
            return route.Provider is not null &&
                route.ChatProvider is IToolCallingChatProvider;
        }
    }

    public IAsyncEnumerable<ChatChunk> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        return StreamAsync(model, messages, [], cancellationToken);
    }

    public IAsyncEnumerable<ChatChunk> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        CancellationToken cancellationToken)
    {
        return StreamCoreAsync(model, messages, tools, cancellationToken);
    }

    public bool SupportsToolCallsFor(string model)
    {
        if (_chatProvider is not null)
        {
            return _chatProvider is IToolCallingChatProvider &&
                ChatModelCapabilityService.SupportsTools(model);
        }

        var route = TryGetSelectedProvider();
        if (route.Provider is null || route.ChatProvider is not IToolCallingChatProvider)
        {
            return false;
        }

        var descriptor = route.Provider.Models.FirstOrDefault(item =>
            item.ModelId.Value.Equals(model, StringComparison.OrdinalIgnoreCase));
        return descriptor?.Capabilities.HasFlag(ModelCapability.SupportsTools) == true ||
            descriptor?.Capabilities.HasFlag(ModelCapability.StructuredToolCalls) == true;
    }

    private async IAsyncEnumerable<ChatChunk> StreamCoreAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        if (_chatProvider is not null)
        {
            await foreach (var chunk in _chatProvider.StreamAsync(
                               new ChatRequest(model, messages, tools),
                               cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        var route = await SelectRouteAsync(
            model,
            messages,
            tools.Count > 0,
            cancellationToken);
        var request = new ChatRequest(route.Model.ModelId.Value, route.Messages, tools);

        await foreach (var chunk in route.Provider.StreamAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }

    private async Task<SelectedChatRoute> SelectRouteAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        bool toolsRequested,
        CancellationToken cancellationToken)
    {
        if (_providerFactory is null || _settingsService is null)
        {
            throw new InvalidOperationException("chat_provider_unavailable");
        }

        var settings = _settingsService.Current;
        var catalog = _providerFactory.CreateCatalog(settings);
        var policy = new LocalFirstModelRoutingPolicy(catalog.Registry);
        var sensitivity = DetectSensitivity(messages);
        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Chat,
            settings.ProviderRouting.LocalOnlyMode,
            settings.ProviderRouting.AllowRemoteChat,
            settings.ProviderRouting.AllowRemoteEmbeddings,
            settings.ProviderRouting.AllowRemoteWithWorkspaceContext,
            settings.ProviderRouting.AllowRemoteWithMemoryContext,
            settings.ProviderRouting.AllowRemoteWithScreenshots,
            settings.ProviderRouting.AllowRemoteWithClipboardOrAppContext,
            sensitivity.IncludesWorkspaceContent,
            sensitivity.IncludesMemoryContext,
            sensitivity.IncludesScreenshot,
            sensitivity.IncludesClipboardOrAppContext,
            ProviderOverride: new ProviderId(settings.ProviderRouting.SelectedChatProvider),
            ModelOverride: new ModelId(model)));

        if (!decision.IsAllowed || decision.Provider is null || decision.Model is null)
        {
            throw new InvalidOperationException(decision.SafeReasonCode);
        }

        if (!catalog.ChatProviders.TryGetValue(
                decision.Provider.Id,
                out var provider))
        {
            throw new InvalidOperationException("chat_provider_unavailable");
        }

        if (toolsRequested &&
            !decision.Model.Capabilities.HasFlag(ModelCapability.StructuredToolCalls) &&
            !decision.Model.Capabilities.HasFlag(ModelCapability.SupportsTools))
        {
            throw new InvalidOperationException("provider_tools_unavailable");
        }

        var filtered = await _contextPrivacyFilter.FilterAsync(
            new ContextPrivacyFilterRequest(
                decision.Provider,
                messages,
                settings.ProviderRouting.AllowRemoteWithWorkspaceContext,
                settings.ProviderRouting.AllowRemoteWithMemoryContext,
                settings.ProviderRouting.AllowRemoteWithScreenshots,
                settings.ProviderRouting.AllowRemoteWithClipboardOrAppContext),
            cancellationToken);

        return new SelectedChatRoute(
            provider,
            decision.Provider,
            decision.Model,
            filtered.Messages);
    }

    private (ProviderProfile? Provider, IChatProvider? ChatProvider) TryGetSelectedProvider()
    {
        if (_providerFactory is null || _settingsService is null)
        {
            return (null, null);
        }

        var catalog = _providerFactory.CreateCatalog(_settingsService.Current);
        var providerId = new ProviderId(
            _settingsService.Current.ProviderRouting.SelectedChatProvider);
        return catalog.Registry.TryGetProvider(providerId, out var provider) &&
            catalog.ChatProviders.TryGetValue(providerId, out var chatProvider)
            ? (provider, chatProvider)
            : (null, null);
    }

    private static ChatContextSensitivity DetectSensitivity(
        IReadOnlyList<ChatMessage> messages)
    {
        var allText = string.Join('\n', messages.Select(message => message.Content));
        return new ChatContextSensitivity(
            IncludesWorkspaceContent:
                Contains(allText, "Attached context: VsCodeEditor") ||
                Contains(allText, "workspace context") ||
                Contains(allText, "Workspace identifiers are application-managed"),
            IncludesMemoryContext:
                Contains(allText, "[memory]") ||
                Contains(allText, "memory context") ||
                Contains(allText, "retrieval context") ||
                Contains(allText, "rag context"),
            IncludesScreenshot:
                messages.Any(message => message.Images.Count > 0) ||
                Contains(allText, "Attached context: Screenshot"),
            IncludesClipboardOrAppContext:
                Contains(allText, "Attached context: Clipboard") ||
                Contains(allText, "Attached context: ApplicationWindow") ||
                Contains(allText, "clipboard context") ||
                Contains(allText, "active window"));
    }

    private static bool Contains(string value, string signal) =>
        value.Contains(signal, StringComparison.OrdinalIgnoreCase);

    private sealed record SelectedChatRoute(
        IChatProvider Provider,
        ProviderProfile Profile,
        ModelDescriptor Model,
        IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatContextSensitivity(
        bool IncludesWorkspaceContent,
        bool IncludesMemoryContext,
        bool IncludesScreenshot,
        bool IncludesClipboardOrAppContext);
}
