using System.Net;
using System.Text;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Providers;
using PersonalAI.Providers.OpenAICompatible;

namespace PersonalAI.Tests.Providers;

public sealed class OpenAICompatibleProviderTests
{
    [Fact]
    public async Task ChatProvider_StreamsServerSentEvents()
    {
        var handler = new StubHttpHandler((_, _) => TextResponse(
            """
            data: {"choices":[{"delta":{"content":"Hello"},"finish_reason":null}]}

            data: {"choices":[{"delta":{"content":" world"},"finish_reason":"stop"}]}

            data: [DONE]

            """,
            "text/event-stream"));
        var provider = CreateChatProvider(handler, streaming: true);

        var chunks = await CollectAsync(provider.StreamAsync(CreateChatRequest()));

        Assert.Contains(chunks, chunk => chunk.Content == "Hello");
        Assert.Contains(chunks, chunk => chunk.Content == " world" && chunk.IsComplete);
        Assert.Equal("v1/chat/completions", handler.RequestPaths.Single());
    }

    [Fact]
    public async Task ChatProvider_NonStreamingSuccess()
    {
        var provider = CreateChatProvider(new StubHttpHandler((_, _) => JsonResponse(
            """{"choices":[{"message":{"content":"done"},"finish_reason":"stop"}]}""")),
            streaming: false);

        var chunk = Assert.Single(await CollectAsync(provider.StreamAsync(CreateChatRequest())));

        Assert.Equal("done", chunk.Content);
        Assert.True(chunk.IsComplete);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "openai_compatible_rate_limited")]
    [InlineData(HttpStatusCode.BadGateway, "openai_compatible_chat_failed")]
    public async Task ChatProvider_NormalizesHttpFailures(
        HttpStatusCode status,
        string expectedCode)
    {
        var provider = CreateChatProvider(new StubHttpHandler(
            (_, _) => new HttpResponseMessage(status)
            {
                Content = new StringContent("raw provider body sk-secret")
            }));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateChatRequest()))
            {
            }
        });

        Assert.Equal(expectedCode, exception.Message);
        Assert.DoesNotContain("sk-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ChatProvider_NormalizesMalformedStreamingResponse()
    {
        var provider = CreateChatProvider(new StubHttpHandler((_, _) =>
            TextResponse("data: {broken", "text/event-stream")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateChatRequest()))
            {
            }
        });

        Assert.Equal("openai_compatible_chat_malformed_response", exception.Message);
    }

    [Fact]
    public async Task ChatProvider_HonorsCancellation()
    {
        var provider = CreateChatProvider(new StubHttpHandler(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return JsonResponse("{}");
            }));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateChatRequest(), cts.Token))
            {
            }
        });
    }

    [Fact]
    public async Task ChatProvider_NormalizesTimeout()
    {
        var provider = CreateChatProvider(new StubHttpHandler(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return JsonResponse("{}");
            }),
            timeout: TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateChatRequest()))
            {
            }
        });

        Assert.Equal("openai_compatible_chat_timeout", exception.Message);
    }

    [Fact]
    public async Task ChatProvider_SendsApiKeyWithoutLeakingIt()
    {
        var secretStore = new InMemorySecretStore();
        await secretStore.SetAsync("secret-ref", new SecretValue("sk-secret"));
        var handler = new StubHttpHandler((_, _) => JsonResponse(
            """{"choices":[{"message":{"content":"done"}}]}"""));
        var provider = CreateChatProvider(
            handler,
            streaming: false,
            secretStore: secretStore,
            secretReference: "secret-ref");

        await foreach (var _ in provider.StreamAsync(CreateChatRequest()))
        {
        }

        Assert.Equal("Bearer", handler.AuthorizationScheme);
        Assert.Equal("sk-secret", handler.AuthorizationParameter);
    }

    [Fact]
    public async Task EmbeddingProvider_EmbedsBatch()
    {
        var handler = new StubHttpHandler((_, _) => JsonResponse(
            """{"model":"embed","data":[{"index":0,"embedding":[1,0,0]},{"index":1,"embedding":[0,1,0]}]}"""));
        var provider = CreateEmbeddingProvider(handler);

        var result = await provider.EmbedAsync(new EmbeddingRequest(["a", "b"]));

        Assert.Equal("openai-compatible", result.ProviderId);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(3, result.Vectors[0].Dimension);
        Assert.Equal("v1/embeddings", handler.RequestPaths.Single());
    }

    [Fact]
    public async Task EmbeddingProvider_NormalizesMalformedResponse()
    {
        var provider = CreateEmbeddingProvider(new StubHttpHandler(
            (_, _) => JsonResponse("""{"data":[]}""")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["a"])));

        Assert.Equal("openai_compatible_embedding_malformed_response", exception.Message);
    }

    [Fact]
    public async Task EmbeddingProvider_RejectsDimensionMismatch()
    {
        var provider = CreateEmbeddingProvider(new StubHttpHandler(
            (_, _) => JsonResponse("""{"data":[{"index":0,"embedding":[1,2]}]}""")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["a"])));

        Assert.Equal("embedding_dimension_mismatch", exception.Message);
    }

    [Fact]
    public async Task EmbeddingProvider_HonorsCancellation()
    {
        var provider = CreateEmbeddingProvider(new StubHttpHandler(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return JsonResponse("{}");
            }));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["a"]), cts.Token));
    }

    [Fact]
    public async Task EmbeddingProvider_NormalizesTimeout()
    {
        var provider = CreateEmbeddingProvider(
            new StubHttpHandler(
                async (_, token) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    return JsonResponse("{}");
                }),
            timeout: TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["a"])));

        Assert.Equal("openai_compatible_embedding_timeout", exception.Message);
    }

    private static OpenAICompatibleChatProvider CreateChatProvider(
        HttpMessageHandler handler,
        bool streaming = true,
        TimeSpan? timeout = null,
        ISecretStore? secretStore = null,
        string? secretReference = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:3001/v1/") },
            secretStore ?? new InMemorySecretStore(),
            new OpenAICompatibleChatOptions(
                new Uri("http://127.0.0.1:3001/v1/"),
                "model",
                secretReference,
                streaming,
                timeout));

    private static OpenAICompatibleEmbeddingProvider CreateEmbeddingProvider(
        HttpMessageHandler handler,
        TimeSpan? timeout = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:3001/v1/") },
            new InMemorySecretStore(),
            new OpenAICompatibleEmbeddingOptions(
                new Uri("http://127.0.0.1:3001/v1/"),
                "embed",
                Dimension: 3,
                RequestTimeout: timeout));

    private static ChatRequest CreateChatRequest() =>
        new("model", [new ChatMessage(ChatRole.User, "hello")]);

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

    private sealed class StubHttpHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public StubHttpHandler(
            Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
            : this((request, token) => Task.FromResult(handler(request, token)))
        {
        }

        public List<string> RequestPaths { get; } = [];

        public string? AuthorizationScheme { get; private set; }

        public string? AuthorizationParameter { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath.Trim('/') ?? string.Empty);
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            return await handler(request, cancellationToken);
        }
    }
}
