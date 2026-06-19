using PersonalAI.Core.Memory;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;

namespace PersonalAI.Infrastructure.Memory;

public sealed class RoutedEmbeddingProvider(
    IProviderFactory providerFactory,
    IApplicationSettingsService settingsService,
    bool includesWorkspaceContent = false,
    bool includesMemoryContext = false) : IEmbeddingProvider
{
    public EmbeddingModelInfo ModelInfo
    {
        get
        {
            var selected = TrySelectProvider();
            return selected is null
                ? new EmbeddingModelInfo(
                    "routing",
                    "unconfigured",
                    1,
                    8192,
                    SupportsBatch: true)
                : selected.Value.Provider.ModelInfo;
        }
    }

    public EmbeddingProviderHealth GetStatus()
    {
        var selected = TrySelectProvider();
        return selected is null
            ? new EmbeddingProviderHealth(
                EmbeddingProviderStatus.Unavailable,
                "embedding_provider_unavailable")
            : selected.Value.Provider.GetStatus();
    }

    public async Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        var settings = settingsService.Current;
        var catalog = providerFactory.CreateCatalog(settings);
        var policy = new LocalFirstModelRoutingPolicy(catalog.Registry);
        var decision = await policy.SelectAsync(new ModelRoutingPolicyRequest(
            ModelCapability.Embeddings,
            settings.ProviderRouting.LocalOnlyMode,
            settings.ProviderRouting.AllowRemoteChat,
            settings.ProviderRouting.AllowRemoteEmbeddings,
            settings.ProviderRouting.AllowRemoteWithWorkspaceContext,
            settings.ProviderRouting.AllowRemoteWithMemoryContext,
            settings.ProviderRouting.AllowRemoteWithScreenshots,
            settings.ProviderRouting.AllowRemoteWithClipboardOrAppContext,
            IncludesWorkspaceContent: includesWorkspaceContent,
            IncludesMemoryContext: includesMemoryContext,
            IncludesScreenshot: false,
            IncludesClipboardOrAppContext: false,
            ProviderOverride: new ProviderId(settings.ProviderRouting.SelectedEmbeddingProvider),
            ModelOverride: string.IsNullOrWhiteSpace(request.Model)
                ? null
                : new ModelId(request.Model)));

        if (!decision.IsAllowed || decision.Provider is null)
        {
            throw new InvalidOperationException(decision.SafeReasonCode);
        }

        if (!catalog.EmbeddingProviders.TryGetValue(
                decision.Provider.Id,
                out var provider))
        {
            throw new InvalidOperationException("embedding_provider_unavailable");
        }

        return await provider.EmbedAsync(
            request with { Model = decision.Model?.ModelId.Value ?? request.Model },
            cancellationToken);
    }

    private (ProviderId Id, IEmbeddingProvider Provider)? TrySelectProvider()
    {
        var settings = settingsService.Current;
        var catalog = providerFactory.CreateCatalog(settings);
        var providerId = new ProviderId(settings.ProviderRouting.SelectedEmbeddingProvider);
        return catalog.EmbeddingProviders.TryGetValue(providerId, out var provider)
            ? (providerId, provider)
            : null;
    }
}
