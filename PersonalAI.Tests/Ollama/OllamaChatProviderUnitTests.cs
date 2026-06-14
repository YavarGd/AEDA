using System.Net;
using System.Text;
using System.Text.Json;
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
        Assert.DoesNotContain("{broken", exception.Message, StringComparison.Ordinal);
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

    [Fact]
    public async Task StreamAsync_OmitsImagesForTextOnlyRequest()
    {
        string? body = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return CreateOkResponse();
        }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        await foreach (var _ in provider.StreamAsync(CreateRequest()))
        {
        }

        Assert.NotNull(body);
        Assert.DoesNotContain("\"images\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_SerializesImageExactlyOnce()
    {
        string? body = null;
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return CreateOkResponse();
        }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);
        var request = new ChatRequest(
            "llava",
            [
                new ChatMessage(
                    ChatRole.User,
                    "describe",
                    [new ChatImage("image/png", "aW1hZ2U=")])
            ]);

        await foreach (var _ in provider.StreamAsync(request))
        {
        }

        using var document = JsonDocument.Parse(body!);
        var images = document.RootElement
            .GetProperty("messages")[0]
            .GetProperty("images");

        var image = Assert.Single(images.EnumerateArray());
        Assert.Equal("aW1hZ2U=", image.GetString());
    }

    [Fact]
    public async Task StreamAsync_SerializesToolDefinitions()
    {
        string? body = null;
        using var schema = JsonDocument.Parse(
            """
            {"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}
            """);
        using var httpClient = new HttpClient(new StubHttpMessageHandler(request =>
        {
            body = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return CreateOkResponse();
        }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);
        var request = new ChatRequest(
            "qwen3",
            [new ChatMessage(ChatRole.User, "List files")],
            [
                new ChatToolDefinition(
                    "workspace.directory.list",
                    "List a workspace directory.",
                    schema.RootElement.Clone())
            ]);

        await foreach (var _ in provider.StreamAsync(request))
        {
        }

        using var document = JsonDocument.Parse(body!);
        var tool = Assert.Single(document.RootElement.GetProperty("tools").EnumerateArray());
        Assert.Equal("function", tool.GetProperty("type").GetString());
        Assert.Equal(
            "workspace.directory.list",
            tool.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal(
            "object",
            tool.GetProperty("function").GetProperty("parameters").GetProperty("type").GetString());
    }

    [Fact]
    public async Task StreamAsync_ParsesStructuredToolCall()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"role":"assistant","content":"","tool_calls":[{"id":"abc","function":{"name":"workspace.directory.list","arguments":{"workspaceId":"ws1","relativePath":"."}}}]},"done":true}
                    """,
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        var chunk = Assert.Single(await CollectAsync(provider.StreamAsync(
            new ChatRequest(
                "qwen3",
                [new ChatMessage(ChatRole.User, "List files")]))));
        var toolCall = Assert.Single(chunk.ToolCalls);
        Assert.Equal("abc", toolCall.Id);
        Assert.Equal("workspace.directory.list", toolCall.Name);
        Assert.Equal("ws1", toolCall.Arguments.GetProperty("workspaceId").GetString());
    }

    [Fact]
    public async Task StreamAsync_ParsesMultipleToolCalls()
    {
        using var httpClient = CreateClient(
            """
            {"message":{"role":"assistant","content":"","tool_calls":[{"id":"first","function":{"name":"workspace.directory.list","arguments":{"workspaceId":"ws1"}}},{"id":"second","function":{"name":"workspace.text.search","arguments":{"workspaceId":"ws1","query":"needle"}}}]},"done":true}
            """);
        var provider = new OllamaChatProvider(httpClient);

        var chunk = Assert.Single(await CollectAsync(provider.StreamAsync(CreateRequest())));

        Assert.Collection(
            chunk.ToolCalls,
            first => Assert.Equal("first", first.Id),
            second => Assert.Equal("second", second.Id));
    }

    [Fact]
    public async Task StreamAsync_PreservesTextPlusToolCalls()
    {
        using var httpClient = CreateClient(
            """
            {"message":{"role":"assistant","content":"I'll inspect that.","tool_calls":[{"id":"abc","function":{"name":"workspace.directory.list","arguments":{"workspaceId":"ws1"}}}]},"done":true}
            """);
        var provider = new OllamaChatProvider(httpClient);

        var chunk = Assert.Single(await CollectAsync(provider.StreamAsync(CreateRequest())));

        Assert.Equal("I'll inspect that.", chunk.Content);
        Assert.Single(chunk.ToolCalls);
        Assert.True(chunk.IsComplete);
    }

    [Fact]
    public async Task StreamAsync_ParsesStringEncodedToolArguments()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"workspace.text.search","arguments":"{\"workspaceId\":\"ws1\",\"query\":\"needle\"}"}}]},"done":true}
                    """,
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        var chunk = Assert.Single(await CollectAsync(provider.StreamAsync(CreateRequest())));
        var toolCall = Assert.Single(chunk.ToolCalls);
        Assert.Equal("workspace.text.search", toolCall.Name);
        Assert.Equal("needle", toolCall.Arguments.GetProperty("query").GetString());
    }

    [Fact]
    public async Task StreamAsync_GeneratesIdWhenToolCallIdIsMissing()
    {
        using var httpClient = CreateClient(
            """
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"workspace.text.search","arguments":{"workspaceId":"ws1","query":"needle"}}}]},"done":true}
            """);
        var provider = new OllamaChatProvider(httpClient);

        var toolCall = Assert.Single(
            Assert.Single(await CollectAsync(provider.StreamAsync(CreateRequest()))).ToolCalls);

        Assert.False(string.IsNullOrWhiteSpace(toolCall.Id));
        Assert.Equal(32, toolCall.Id.Length);
    }

    [Theory]
    [InlineData("""{"message":{"role":"assistant","tool_calls":[{"function":{"arguments":{}}}]},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","tool_calls":[{"function":{"name":"","arguments":{}}}]},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","tool_calls":[{"function":{"name":"workspace.text.search","arguments":[]}}]},"done":true}""")]
    [InlineData("""{"message":{"role":"assistant","tool_calls":[{"function":{"name":"workspace.text.search","arguments":"not-json"}}]},"done":true}""")]
    public async Task StreamAsync_ThrowsForMalformedToolCalls(string responseLine)
    {
        using var httpClient = CreateClient(responseLine);
        var provider = new OllamaChatProvider(httpClient);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateRequest()))
            {
            }
        });

        Assert.DoesNotContain("not-json", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamAsync_ThrowsForToolCallWithoutName()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {"message":{"role":"assistant","tool_calls":[{"function":{"arguments":{}}}]},"done":true}
                    """,
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var provider = new OllamaChatProvider(httpClient);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateRequest()))
            {
            }
        });
    }

    [Fact]
    public async Task StreamAsync_StopsAtDoneChunk()
    {
        using var httpClient = CreateClient(
            """
            {"message":{"role":"assistant","content":"done"},"done":true}
            {"message":{"role":"assistant","content":"ignored"},"done":false}
            """);
        var provider = new OllamaChatProvider(httpClient);

        var chunks = await CollectAsync(provider.StreamAsync(CreateRequest()));

        Assert.Equal("done", Assert.Single(chunks).Content);
    }

    [Fact]
    public async Task StreamAsync_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var httpClient = new HttpClient(new CancellationAwareHandler())
        {
            BaseAddress = new Uri("http://localhost:11434")
        };
        var provider = new OllamaChatProvider(httpClient);

        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateRequest(), cancellation.Token))
            {
            }
        });
    }

    [Fact]
    public async Task StreamAsync_NonSuccessHttpDoesNotLeakResponseBody()
    {
        using var httpClient = new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("raw secret provider body")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };
        var provider = new OllamaChatProvider(httpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var _ in provider.StreamAsync(CreateRequest()))
            {
            }
        });

        Assert.Contains("HTTP 500", exception.Message);
        Assert.DoesNotContain("secret", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ChatRequest CreateRequest()
    {
        return new ChatRequest(
            "gemma4",
            [new ChatMessage(ChatRole.User, "Hello")]);
    }

    private static HttpResponseMessage CreateOkResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "{\"message\":{\"role\":\"assistant\",\"content\":\"ok\"},\"done\":true}",
                Encoding.UTF8,
                "application/json")
        };
    }

    private static HttpClient CreateClient(string responseLines)
    {
        return new HttpClient(new StubHttpMessageHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseLines,
                    Encoding.UTF8,
                    "application/json")
            }))
        {
            BaseAddress = new Uri("http://localhost:11434")
        };
    }

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

    private sealed class CancellationAwareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
