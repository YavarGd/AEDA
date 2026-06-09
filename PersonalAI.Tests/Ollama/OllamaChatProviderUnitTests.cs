using System.Net;
using System.Text;
using PersonalAI.Core.Chat;
using PersonalAI.Providers.Ollama;

namespace PersonalAI.Tests.Ollama;

public sealed class OllamaChatProviderUnitTests
{
    [Fact]
    public async Task StreamAsync_StreamsChunksFromOllamaLines()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"role":"assistant","content":"Hello"},"done":false}
                    {"message":{"role":"assistant","content":" world"},"done":true}

                    """,
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);
        var request = CreateRequest();
        var chunks = new List<ChatChunk>();

        await foreach (var chunk in provider.StreamAsync(request))
        {
            chunks.Add(chunk);
        }

        Assert.Collection(
            chunks,
            chunk =>
            {
                Assert.Equal("Hello", chunk.Content);
                Assert.False(chunk.IsComplete);
            },
            chunk =>
            {
                Assert.Equal(" world", chunk.Content);
                Assert.True(chunk.IsComplete);
            });
    }

    [Fact]
    public async Task StreamAsync_ThrowsForInvalidJsonLine()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{broken json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(
            async () =>
            {
                await foreach (var _ in provider.StreamAsync(CreateRequest()))
                {
                }
            });

        Assert.Contains("invalid JSON", exception.Message);
    }

    [Fact]
    public async Task StreamAsync_ThrowsForMissingModel()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)));
        var provider = new OllamaChatProvider(httpClient);

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () =>
            {
                var request = new ChatRequest(
                    "",
                    [new ChatMessage(ChatRole.User, "Hello")]);

                await foreach (var _ in provider.StreamAsync(request))
                {
                }
            });

        Assert.Contains("model name", exception.Message);
    }

    private static ChatRequest CreateRequest()
    {
        return new ChatRequest(
            "gemma4",
            [new ChatMessage(ChatRole.User, "Hello")]);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(send(request));
        }
    }
}
