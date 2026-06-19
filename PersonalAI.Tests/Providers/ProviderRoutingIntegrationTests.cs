using System.Net;
using System.Text;
using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Providers;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Infrastructure.Chat;
using PersonalAI.Infrastructure.Memory;
using PersonalAI.Infrastructure.Settings;

namespace PersonalAI.Tests.Providers;

public sealed class ProviderRoutingIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ProviderFactory_MaterializesDefaultOllamaProfile()
    {
        var catalog = new ProviderFactory(
            () => new HttpClient(new StubHttpHandler((_, _) => JsonResponse("{}"))),
            new InMemorySecretStore()).CreateCatalog(ApplicationSettings.CreateDefault());

        Assert.True(catalog.Registry.TryGetProvider(ProviderId.Ollama, out var profile));
        Assert.True(profile.IsEnabled);
        Assert.True(catalog.ChatProviders.ContainsKey(ProviderId.Ollama));
    }

    [Fact]
    public void ProviderFactory_MaterializesLocalOpenAICompatibleGateway()
    {
        var settings = CreateSettings(
            providerId: "gateway",
            endpoint: "http://127.0.0.1:3001/v1",
            selectedChatProvider: "gateway",
            localOnly: true);

        var catalog = new ProviderFactory(
            () => new HttpClient(new StubHttpHandler((_, _) => JsonResponse("{}"))),
            new InMemorySecretStore()).CreateCatalog(settings);

        Assert.True(catalog.Registry.TryGetProvider(new ProviderId("gateway"), out var profile));
        Assert.Equal(ProviderEndpointOrigin.Local, profile.Endpoint.Origin);
        Assert.True(catalog.ChatProviders.ContainsKey(new ProviderId("gateway")));
    }

    [Fact]
    public void SettingsValidation_DisablesInvalidAndCredentialUrls()
    {
        var settings = ApplicationSettings.CreateDefault() with
        {
            ProviderRouting = ProviderRoutingSettings.Default with
            {
                ProviderProfiles =
                [
                    new("bad", ProviderKind.OpenAICompatible, "Bad", "not-url", true, "m", null, null),
                    new("cred", ProviderKind.OpenAICompatible, "Cred", "https://u:p@example.com/v1", true, "m", null, null)
                ]
            }
        };

        var normalized = ApplicationSettingsValidator.Normalize(settings);

        Assert.All(
            normalized.ProviderRouting.ProviderProfiles.Where(profile => profile.Id != "ollama"),
            profile =>
            Assert.False(profile.IsEnabled));
    }

    [Fact]
    public void SettingsValidation_StripsRawApiKeyReferences()
    {
        var settings = CreateSettings(secretReference: "sk-secret-value");

        var normalized = ApplicationSettingsValidator.Normalize(settings);
        var json = JsonSerializer.Serialize(normalized);

        Assert.Null(normalized.ProviderRouting.ProviderProfiles[0].SecretReference);
        Assert.DoesNotContain("sk-secret-value", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DpapiSecretStore_ReturnsNullForCorruptedPayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var store = new DpapiSecretStore(_root);
        await store.SetAsync("provider/key", new SecretValue("secret-value"));
        var file = Assert.Single(Directory.GetFiles(_root));
        await File.WriteAllTextAsync(file, "corrupted");

        Assert.Null(await store.GetAsync("provider/key"));
    }

    [Fact]
    public async Task ChatRouting_LocalGatewayStreamsSuccessfully()
    {
        var handler = new StubHttpHandler((_, _) => TextResponse(
            """
            data: {"choices":[{"delta":{"content":"hello"},"finish_reason":"stop"}]}

            data: [DONE]

            """,
            "text/event-stream"));
        var service = CreateChatService(
            CreateSettings(
                providerId: "gateway",
                endpoint: "http://127.0.0.1:3001/v1",
                selectedChatProvider: "gateway"),
            handler);

        var chunks = await CollectAsync(service.StreamAsync(
            "chat-model",
            [new ChatMessage(ChatRole.User, "hi")],
            CancellationToken.None));

        Assert.Contains(chunks, chunk => chunk.Content == "hello");
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ChatRouting_RemoteDeniedByDefault()
    {
        var service = CreateChatService(
            CreateSettings(
                providerId: "remote",
                endpoint: "https://api.example.com/v1",
                selectedChatProvider: "remote",
                localOnly: true),
            new StubHttpHandler((_, _) => JsonResponse("{}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.StreamAsync(
                               "chat-model",
                               [new ChatMessage(ChatRole.User, "hi")],
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("remote_provider_disabled", exception.Message);
    }

    [Fact]
    public async Task ChatRouting_RemoteWorkspaceContextDenied()
    {
        var service = CreateChatService(
            CreateSettings(
                providerId: "remote",
                endpoint: "https://api.example.com/v1",
                selectedChatProvider: "remote",
                localOnly: false,
                allowRemoteChat: true),
            new StubHttpHandler((_, _) => JsonResponse("{}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.StreamAsync(
                               "chat-model",
                               [new ChatMessage(ChatRole.System, "Attached context: VsCodeEditor\nsecret")],
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("remote_workspace_context_denied", exception.Message);
    }

    [Fact]
    public async Task ChatRouting_OpenAICompatibleProviderDoesNotReceiveTools()
    {
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var service = CreateChatService(
            CreateSettings(
                providerId: "gateway",
                endpoint: "http://127.0.0.1:3001/v1",
                selectedChatProvider: "gateway"),
            new StubHttpHandler((_, _) => JsonResponse("{}")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in service.StreamAsync(
                               "chat-model",
                               [new ChatMessage(ChatRole.User, "tool please")],
                               [new ChatToolDefinition("workspace.file.read_text", "read", schema.RootElement.Clone())],
                               CancellationToken.None))
            {
            }
        });

        Assert.Equal("provider_tools_unavailable", exception.Message);
    }

    [Fact]
    public async Task RoutedEmbeddingProvider_LocalGatewayEmbeds()
    {
        var vector = string.Join(",", Enumerable.Repeat("0", 1536));
        var handler = new StubHttpHandler((_, _) => JsonResponse(
            $$"""{"model":"embed-model","data":[{"index":0,"embedding":[{{vector}}]}]}"""));
        var provider = CreateEmbeddingProvider(
            CreateSettings(
                providerId: "gateway",
                endpoint: "http://127.0.0.1:3001/v1",
                selectedEmbeddingProvider: "gateway",
                embeddingModel: "embed-model"),
            handler);

        var result = await provider.EmbedAsync(new EmbeddingRequest(["text"]));

        Assert.Single(result.Vectors);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task RoutedEmbeddingProvider_RemoteWorkspaceEmbeddingDenied()
    {
        var provider = CreateEmbeddingProvider(
            CreateSettings(
                providerId: "remote",
                endpoint: "https://api.example.com/v1",
                selectedEmbeddingProvider: "remote",
                localOnly: false,
                allowRemoteEmbeddings: true,
                embeddingModel: "embed-model"),
            new StubHttpHandler((_, _) => JsonResponse("{}")),
            includesWorkspaceContent: true);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["workspace secret"])));

        Assert.Equal("remote_workspace_context_denied", exception.Message);
    }

    private static ChatSessionService CreateChatService(
        ApplicationSettings settings,
        StubHttpHandler handler) =>
        new(
            new ProviderFactory(
                () => new HttpClient(handler),
                new InMemorySecretStore()),
            new FakeSettingsService(settings));

    private static RoutedEmbeddingProvider CreateEmbeddingProvider(
        ApplicationSettings settings,
        StubHttpHandler handler,
        bool includesWorkspaceContent = false) =>
        new(
            new ProviderFactory(
                () => new HttpClient(handler),
                new InMemorySecretStore()),
            new FakeSettingsService(settings),
            includesWorkspaceContent);

    private static ApplicationSettings CreateSettings(
        string providerId = "gateway",
        string endpoint = "http://127.0.0.1:3001/v1",
        string selectedChatProvider = "gateway",
        string selectedEmbeddingProvider = "ollama",
        bool localOnly = true,
        bool allowRemoteChat = false,
        bool allowRemoteEmbeddings = false,
        string chatModel = "chat-model",
        string? embeddingModel = null,
        string? secretReference = null) =>
        ApplicationSettingsValidator.Normalize(ApplicationSettings.CreateDefault() with
        {
            ProviderRouting = ProviderRoutingSettings.Default with
            {
                ProviderProfiles =
                [
                    new(
                        providerId,
                        ProviderKind.OpenAICompatible,
                        providerId,
                        endpoint,
                        IsEnabled: true,
                        ChatModel: chatModel,
                        EmbeddingModel: embeddingModel,
                        SecretReference: secretReference)
                ],
                SelectedChatProvider = selectedChatProvider,
                SelectedEmbeddingProvider = selectedEmbeddingProvider,
                LocalOnlyMode = localOnly,
                AllowRemoteChat = allowRemoteChat,
                AllowRemoteEmbeddings = allowRemoteEmbeddings
            }
        });

    private static HttpResponseMessage JsonResponse(string json) =>
        TextResponse(json, "application/json");

    private static HttpResponseMessage TextResponse(string text, string mediaType) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(text, Encoding.UTF8, mediaType)
        };

    private static async Task<List<ChatChunk>> CollectAsync(
        IAsyncEnumerable<ChatChunk> chunks)
    {
        var collected = new List<ChatChunk>();
        await foreach (var chunk in chunks)
        {
            collected.Add(chunk);
        }

        return collected;
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeSettingsService(ApplicationSettings settings)
        : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; private set; } = settings;

        public string SettingsPath => string.Empty;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            Current = ApplicationSettingsValidator.Normalize(settings);
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = ApplicationSettings.CreateDefault();
            return Task.CompletedTask;
        }
    }

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(handler(request, cancellationToken));
        }
    }
}
