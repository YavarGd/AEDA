namespace PersonalAI.Core.Providers;

public interface IProviderRegistry
{
    IReadOnlyList<ProviderProfile> ListProviders();

    bool TryGetProvider(ProviderId providerId, out ProviderProfile provider);

    IReadOnlyList<ModelDescriptor> ListModels(ProviderId providerId);

    ModelDescriptor? FindModel(ProviderId providerId, ModelId modelId);

    Task<ProviderHealth> GetHealthAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default);
}

public interface IProviderHealthProbe
{
    Task<ProviderHealth> CheckAsync(
        ProviderProfile provider,
        CancellationToken cancellationToken = default);
}

public sealed class StaticProviderRegistry : IProviderRegistry
{
    private readonly Dictionary<ProviderId, ProviderProfile> _providers;
    private readonly Dictionary<ProviderId, IProviderHealthProbe> _healthProbes;

    public StaticProviderRegistry(
        IEnumerable<ProviderProfile> providers,
        IReadOnlyDictionary<ProviderId, IProviderHealthProbe>? healthProbes = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers
            .GroupBy(provider => provider.Id)
            .ToDictionary(
                group => group.Key,
                group => group.Last());
        _healthProbes = healthProbes is null
            ? []
            : new Dictionary<ProviderId, IProviderHealthProbe>(healthProbes);
    }

    public IReadOnlyList<ProviderProfile> ListProviders() =>
        _providers.Values
            .OrderBy(provider => provider.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool TryGetProvider(ProviderId providerId, out ProviderProfile provider) =>
        _providers.TryGetValue(providerId, out provider!);

    public IReadOnlyList<ModelDescriptor> ListModels(ProviderId providerId) =>
        _providers.TryGetValue(providerId, out var provider)
            ? provider.Models
            : [];

    public ModelDescriptor? FindModel(ProviderId providerId, ModelId modelId) =>
        ListModels(providerId).FirstOrDefault(model =>
            model.ModelId.Value.Equals(modelId.Value, StringComparison.OrdinalIgnoreCase));

    public async Task<ProviderHealth> GetHealthAsync(
        ProviderId providerId,
        CancellationToken cancellationToken = default)
    {
        if (!_providers.TryGetValue(providerId, out var provider))
        {
            return new ProviderHealth(ProviderStatus.Unavailable, "provider_not_configured");
        }

        if (!provider.IsEnabled)
        {
            return new ProviderHealth(ProviderStatus.Disabled, "provider_disabled");
        }

        if (!provider.Endpoint.IsUsable)
        {
            return new ProviderHealth(
                ProviderStatus.Unconfigured,
                provider.Endpoint.SafeReasonCode ?? "provider_endpoint_invalid");
        }

        if (!_healthProbes.TryGetValue(providerId, out var probe))
        {
            return ProviderHealth.Available;
        }

        try
        {
            return await probe.CheckAsync(provider, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new ProviderHealth(ProviderStatus.Unavailable, "provider_health_check_failed");
        }
    }
}
