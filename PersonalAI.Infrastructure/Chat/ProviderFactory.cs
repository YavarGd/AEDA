using PersonalAI.Core.Chat;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;
using PersonalAI.Providers.Ollama;
using PersonalAI.Providers.OpenAICompatible;

namespace PersonalAI.Infrastructure.Chat;

public sealed class ProviderFactory(
    Func<HttpClient>? httpClientFactory = null,
    ISecretStore? secretStore = null) : IProviderFactory
{
    private const string DefaultCodingModel = "qwen2.5-coder:7b";

    private readonly Func<HttpClient> _httpClientFactory =
        httpClientFactory ?? (() => new HttpClient());
    private readonly ISecretStore _secretStore =
        secretStore ?? new PersonalAI.Infrastructure.Settings.DpapiSecretStore();

    public RuntimeProviderCatalog CreateCatalog(ApplicationSettings settings)
    {
        settings = ApplicationSettingsValidator.Normalize(settings);
        var profiles = settings.ProviderRouting.ProviderProfiles.Count == 0
            ? ProviderRoutingSettings.Default.ProviderProfiles
            : settings.ProviderRouting.ProviderProfiles;
        var runtimeProfiles = new List<ProviderProfile>();
        var chatProviders = new Dictionary<ProviderId, IChatProvider>();
        var embeddingProviders = new Dictionary<ProviderId, IEmbeddingProvider>();

        foreach (var profile in profiles)
        {
            var endpoint = ProviderEndpointClassifier.Classify(profile.EndpointUrl);
            var providerId = new ProviderId(profile.Id);
            var enabled = profile.IsEnabled && endpoint.IsUsable;
            var models = CreateModelDescriptors(
                providerId,
                profile,
                endpoint,
                profile.Kind == ProviderKind.Ollama
                    ? settings.Models.Assignments.Select(assignment => assignment.Model)
                        .Concat([DefaultCodingModel])
                    : null);
            var runtimeProfile = new ProviderProfile(
                providerId,
                profile.Kind,
                profile.DisplayName,
                endpoint,
                enabled,
                profile.ChatModel,
                profile.EmbeddingModel,
                profile.SecretReference,
                models);
            runtimeProfiles.Add(runtimeProfile);

            if (!enabled)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(profile.ChatModel))
            {
                var chatProvider = CreateChatProvider(profile, endpoint);
                if (chatProvider is not null)
                {
                    chatProviders[providerId] = chatProvider;
                }
            }

            if (!string.IsNullOrWhiteSpace(profile.EmbeddingModel))
            {
                var embeddingProvider = CreateEmbeddingProvider(profile, endpoint);
                if (embeddingProvider is not null)
                {
                    embeddingProviders[providerId] = embeddingProvider;
                }
            }
        }

        if (runtimeProfiles.All(profile => profile.Id != ProviderId.Ollama))
        {
            var endpoint = ProviderEndpointClassifier.Classify("http://localhost:11434");
            var provider = CreateDefaultOllamaProfile(endpoint);
            runtimeProfiles.Insert(0, provider);
            chatProviders[ProviderId.Ollama] = new OllamaChatProvider(_httpClientFactory());
        }

        return new RuntimeProviderCatalog(
            new StaticProviderRegistry(runtimeProfiles),
            chatProviders,
            embeddingProviders);
    }

    private IChatProvider? CreateChatProvider(
        ProviderProfileSetting profile,
        ProviderEndpoint endpoint)
    {
        if (!endpoint.IsUsable || endpoint.BaseUri is null ||
            string.IsNullOrWhiteSpace(profile.ChatModel))
        {
            return null;
        }

        var client = _httpClientFactory();
        client.BaseAddress = endpoint.BaseUri;
        return profile.Kind switch
        {
            ProviderKind.Ollama => new OllamaChatProvider(client),
            ProviderKind.OpenAICompatible or
                ProviderKind.LocalGateway or
                ProviderKind.CloudGateway => new OpenAICompatibleChatProvider(
                    client,
                    _secretStore,
                    new OpenAICompatibleChatOptions(
                        EnsureTrailingSlash(endpoint.BaseUri),
                        profile.ChatModel,
                        profile.SecretReference,
                        UseStreaming: true)),
            _ => null
        };
    }

    private IEmbeddingProvider? CreateEmbeddingProvider(
        ProviderProfileSetting profile,
        ProviderEndpoint endpoint)
    {
        if (!endpoint.IsUsable || endpoint.BaseUri is null ||
            string.IsNullOrWhiteSpace(profile.EmbeddingModel))
        {
            return null;
        }

        var client = _httpClientFactory();
        client.BaseAddress = endpoint.BaseUri;
        return profile.Kind switch
        {
            ProviderKind.Ollama => new OllamaEmbeddingProvider(
                client,
                profile.EmbeddingModel,
                dimension: 768),
            ProviderKind.OpenAICompatible or
                ProviderKind.LocalGateway or
                ProviderKind.CloudGateway => new OpenAICompatibleEmbeddingProvider(
                    client,
                    _secretStore,
                    new OpenAICompatibleEmbeddingOptions(
                        EnsureTrailingSlash(endpoint.BaseUri),
                        profile.EmbeddingModel,
                        Dimension: 1536,
                        SecretReference: profile.SecretReference)),
            _ => null
        };
    }

    private static IReadOnlyList<ModelDescriptor> CreateModelDescriptors(
        ProviderId providerId,
        ProviderProfileSetting profile,
        ProviderEndpoint endpoint,
        IEnumerable<string>? additionalChatModels = null)
    {
        var models = new List<ModelDescriptor>();
        var isLocal = profile.Kind is ProviderKind.Ollama or ProviderKind.TestFake ||
            endpoint.Origin == ProviderEndpointOrigin.Local;
        var isRemote = !isLocal;
        var safety = new ModelSafetyProfile(
            IsLocalOnly: isLocal,
            IsRemote: isRemote,
            AllowsWorkspaceContext: isLocal,
            AllowsMemoryContext: isLocal,
            AllowsScreenshots: isLocal,
            AllowsClipboardOrAppContext: isLocal);

        var chatModels = new[] { profile.ChatModel }
            .Concat(additionalChatModels ?? [])
            .Where(model => !string.IsNullOrWhiteSpace(model))
            .Select(model => model!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var chatModel in chatModels)
        {
            var capabilities = ModelCapability.Chat |
                ModelCapability.StreamingChat |
                ModelCapability.SupportsSystemPrompt |
                (isLocal ? ModelCapability.LocalOnly : ModelCapability.Remote);
            if (profile.Kind == ProviderKind.Ollama)
            {
                capabilities |= ModelCapability.StructuredToolCalls |
                    ModelCapability.SupportsTools;
            }

            models.Add(new ModelDescriptor(
                providerId,
                new ModelId(chatModel),
                capabilities,
                safety,
                chatModel));
        }

        if (!string.IsNullOrWhiteSpace(profile.EmbeddingModel))
        {
            models.Add(new ModelDescriptor(
                providerId,
                new ModelId(profile.EmbeddingModel),
                ModelCapability.Embeddings |
                    (isLocal ? ModelCapability.LocalOnly : ModelCapability.Remote),
                safety,
                profile.EmbeddingModel));
        }

        return models;
    }

    private static ProviderProfile CreateDefaultOllamaProfile(
        ProviderEndpoint endpoint)
    {
        var setting = ProviderRoutingSettings.Default.ProviderProfiles[0];
        return new ProviderProfile(
            ProviderId.Ollama,
            ProviderKind.Ollama,
            "Ollama",
            endpoint,
            IsEnabled: true,
            setting.ChatModel,
            setting.EmbeddingModel,
            SecretReference: null,
            CreateModelDescriptors(ProviderId.Ollama, setting, endpoint));
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(value + "/");
    }
}
