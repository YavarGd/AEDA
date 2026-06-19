namespace PersonalAI.Core.Providers;

public enum RoutingContextSensitivity
{
    Normal,
    Sensitive
}

public sealed record ModelRoutingPolicyRequest(
    ModelCapability RequiredCapability,
    bool LocalOnlyMode,
    bool AllowRemoteChat,
    bool AllowRemoteEmbeddings,
    bool AllowRemoteWorkspaceContext,
    bool AllowRemoteMemoryContext,
    bool AllowRemoteScreenshots,
    bool AllowRemoteClipboardOrAppContext,
    bool IncludesWorkspaceContent,
    bool IncludesMemoryContext,
    bool IncludesScreenshot,
    bool IncludesClipboardOrAppContext,
    RoutingContextSensitivity Sensitivity = RoutingContextSensitivity.Normal,
    ProviderId? ProviderOverride = null,
    ModelId? ModelOverride = null);

public sealed record ModelRoutingPolicyDecision(
    bool IsAllowed,
    ProviderProfile? Provider,
    ModelDescriptor? Model,
    string SafeReasonCode,
    bool RequiresExplicitApproval,
    bool MustStripWorkspaceContext,
    bool MustStripMemoryContext,
    bool MustStripScreenshots,
    bool MustStripClipboardOrAppContext);

public interface IModelRoutingPolicy
{
    Task<ModelRoutingPolicyDecision> SelectAsync(
        ModelRoutingPolicyRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class LocalFirstModelRoutingPolicy(IProviderRegistry providerRegistry)
    : IModelRoutingPolicy
{
    public async Task<ModelRoutingPolicyDecision> SelectAsync(
        ModelRoutingPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var providers = providerRegistry.ListProviders()
            .Where(provider => provider.IsEnabled)
            .ToArray();
        if (request.ProviderOverride is not null)
        {
            providers = providers
                .Where(provider => provider.Id == request.ProviderOverride.Value)
                .ToArray();
        }

        foreach (var provider in providers.OrderBy(provider => provider.IsLocal ? 0 : 1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = SelectModel(provider, request);
            if (model is null)
            {
                continue;
            }

            var health = await providerRegistry.GetHealthAsync(
                provider.Id,
                cancellationToken);
            if (health.Status != ProviderStatus.Available)
            {
                continue;
            }

            var remoteDecision = EvaluateRemotePolicy(provider, request);
            if (!remoteDecision.IsAllowed)
            {
                return remoteDecision with
                {
                    Provider = provider,
                    Model = model
                };
            }

            return remoteDecision with
            {
                IsAllowed = true,
                Provider = provider,
                Model = model,
                SafeReasonCode = "provider_selected"
            };
        }

        return new ModelRoutingPolicyDecision(
            IsAllowed: false,
            Provider: null,
            Model: null,
            SafeReasonCode: "provider_capability_unavailable",
            RequiresExplicitApproval: false,
            MustStripWorkspaceContext: false,
            MustStripMemoryContext: false,
            MustStripScreenshots: false,
            MustStripClipboardOrAppContext: false);
    }

    private static ModelDescriptor? SelectModel(
        ProviderProfile provider,
        ModelRoutingPolicyRequest request)
    {
        var models = provider.Models
            .Where(model => model.Capabilities.HasFlag(request.RequiredCapability))
            .ToArray();

        if (request.ModelOverride is not null)
        {
            return models.FirstOrDefault(model =>
                model.ModelId.Value.Equals(
                    request.ModelOverride.Value.Value,
                    StringComparison.OrdinalIgnoreCase));
        }

        return models.FirstOrDefault();
    }

    private static ModelRoutingPolicyDecision EvaluateRemotePolicy(
        ProviderProfile provider,
        ModelRoutingPolicyRequest request)
    {
        var isRemote = provider.IsRemote ||
            provider.Endpoint.Origin == ProviderEndpointOrigin.PrivateNetwork;
        var isEmbedding = request.RequiredCapability.HasFlag(ModelCapability.Embeddings);
        var remoteAllowed = !isRemote ||
            !request.LocalOnlyMode &&
            (isEmbedding ? request.AllowRemoteEmbeddings : request.AllowRemoteChat);

        if (!remoteAllowed)
        {
            return Deny("remote_provider_disabled");
        }

        if (isRemote && provider.Endpoint.Origin == ProviderEndpointOrigin.PrivateNetwork)
        {
            return Deny("private_network_provider_requires_approval") with
            {
                RequiresExplicitApproval = true
            };
        }

        if (isRemote && request.IncludesWorkspaceContent &&
            !request.AllowRemoteWorkspaceContext)
        {
            return Deny("remote_workspace_context_denied") with
            {
                MustStripWorkspaceContext = true
            };
        }

        if (isRemote && request.IncludesMemoryContext &&
            !request.AllowRemoteMemoryContext)
        {
            return Deny("remote_memory_context_denied") with
            {
                MustStripMemoryContext = true
            };
        }

        if (isRemote && request.IncludesScreenshot &&
            !request.AllowRemoteScreenshots)
        {
            return Deny("remote_screenshot_context_denied") with
            {
                MustStripScreenshots = true
            };
        }

        if (isRemote && request.IncludesClipboardOrAppContext &&
            !request.AllowRemoteClipboardOrAppContext)
        {
            return Deny("remote_clipboard_app_context_denied") with
            {
                MustStripClipboardOrAppContext = true
            };
        }

        return new ModelRoutingPolicyDecision(
            true,
            null,
            null,
            "provider_selected",
            RequiresExplicitApproval: false,
            MustStripWorkspaceContext: false,
            MustStripMemoryContext: false,
            MustStripScreenshots: false,
            MustStripClipboardOrAppContext: false);
    }

    private static ModelRoutingPolicyDecision Deny(string safeReasonCode) =>
        new(
            IsAllowed: false,
            Provider: null,
            Model: null,
            SafeReasonCode: safeReasonCode,
            RequiresExplicitApproval: false,
            MustStripWorkspaceContext: false,
            MustStripMemoryContext: false,
            MustStripScreenshots: false,
            MustStripClipboardOrAppContext: false);
}
