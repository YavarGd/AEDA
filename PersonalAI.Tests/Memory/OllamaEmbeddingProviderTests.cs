using System.Net;
using System.Text;
using PersonalAI.Core.Memory;
using PersonalAI.Providers.Ollama;

namespace PersonalAI.Tests.Memory;

public sealed class OllamaEmbeddingProviderTests
{
    [Fact]
    public async Task EmbedAsync_SucceedsForSingleAndBatchInputs()
    {
        var handler = new StubHttpHandler(
            (_, _) => JsonResponse("""
                {"model":"nomic-embed-text","embeddings":[[1,0,0],[0,1,0]]}
                """));
        var provider = CreateProvider(handler);

        var result = await provider.EmbedAsync(
            new EmbeddingRequest(["alpha", "beta"]));

        Assert.Equal("ollama", result.ProviderId);
        Assert.Equal("nomic-embed-text", result.Model);
        Assert.Equal(2, result.Vectors.Count);
        Assert.Equal(3, result.Vectors[0].Dimension);
        Assert.Equal("/api/embed", handler.RequestPaths.Single());
        Assert.Contains("\"input\":[\"alpha\",\"beta\"]", handler.RequestBodies.Single(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ollama_embedding_model_unavailable")]
    [InlineData(HttpStatusCode.BadGateway, "ollama_embedding_failed")]
    public async Task EmbedAsync_NormalizesHttpFailures(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var provider = CreateProvider(new StubHttpHandler(
            (_, _) => new HttpResponseMessage(statusCode)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["alpha"])));

        Assert.Equal(expectedCode, exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_NormalizesMalformedResponse()
    {
        var provider = CreateProvider(new StubHttpHandler(
            (_, _) => JsonResponse("""{"model":"m","embeddings":[]}""")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["alpha"])));

        Assert.Equal("ollama_embedding_malformed_response", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_RejectsDimensionMismatch()
    {
        var provider = CreateProvider(new StubHttpHandler(
            (_, _) => JsonResponse("""{"model":"m","embeddings":[[1,2]]}""")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["alpha"])));

        Assert.Equal("embedding_dimension_mismatch", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_HonorsCancellation()
    {
        var provider = CreateProvider(new StubHttpHandler(
            async (_, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                return JsonResponse("""{"model":"m","embeddings":[[1,0,0]]}""");
            }));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["alpha"]), cts.Token));
    }

    [Fact]
    public async Task EmbedAsync_NormalizesTimeout()
    {
        var provider = CreateProvider(
            new StubHttpHandler(
                async (_, token) =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), token);
                    return JsonResponse("""{"model":"m","embeddings":[[1,0,0]]}""");
                }),
            timeout: TimeSpan.FromMilliseconds(20));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest(["alpha"])));

        Assert.Equal("ollama_embedding_timeout", exception.Message);
    }

    [Fact]
    public async Task EmbedAsync_RejectsInvalidInputSafely()
    {
        var provider = CreateProvider(new StubHttpHandler(
            (_, _) => JsonResponse("""{"model":"m","embeddings":[[1,0,0]]}""")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EmbedAsync(new EmbeddingRequest([new string('a', 200)])));

        Assert.Equal("embedding_input_invalid", exception.Message);
    }

    private static OllamaEmbeddingProvider CreateProvider(
        HttpMessageHandler handler,
        TimeSpan? timeout = null)
    {
        return new OllamaEmbeddingProvider(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost:11434") },
            "nomic-embed-text",
            dimension: 3,
            maxInputCharacters: 10,
            maxBatchSize: 4,
            requestTimeout: timeout);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

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

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestPaths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);
            RequestBodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return await handler(request, cancellationToken);
        }
    }
}
