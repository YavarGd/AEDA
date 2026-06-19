using PersonalAI.Core.Chat;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Settings;

namespace PersonalAI.Core.Providers;

public sealed record RuntimeProviderCatalog(
    IProviderRegistry Registry,
    IReadOnlyDictionary<ProviderId, IChatProvider> ChatProviders,
    IReadOnlyDictionary<ProviderId, IEmbeddingProvider> EmbeddingProviders);

public interface IProviderFactory
{
    RuntimeProviderCatalog CreateCatalog(ApplicationSettings settings);
}
