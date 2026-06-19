using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Providers;

namespace PersonalAI.Providers.OpenAICompatible;

public sealed class OpenAICompatibleChatProvider : IChatProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly OpenAICompatibleChatOptions _options;

    public OpenAICompatibleChatProvider(
        HttpClient httpClient,
        ISecretStore secretStore,
        OpenAICompatibleChatOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);

        _httpClient = httpClient;
        _secretStore = secretStore;
        _options = options;
        _httpClient.BaseAddress ??= options.BaseUri;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public string ProviderName => "OpenAI-compatible";

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var timeout = new CancellationTokenSource(
            _options.RequestTimeout ?? TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        HttpResponseMessage response;
        try
        {
            using var httpRequest = await CreateRequestAsync(
                request,
                _options.UseStreaming,
                linked.Token);
            response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw SafeFailure("openai_compatible_chat_timeout");
        }
        catch (HttpRequestException)
        {
            throw SafeFailure("openai_compatible_provider_unavailable");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw CreateSafeFailure(response.StatusCode);
            }

            if (_options.UseStreaming)
            {
                await foreach (var chunk in ReadStreamingAsync(response, linked.Token))
                {
                    yield return chunk;
                }
            }
            else
            {
                yield return await ReadNonStreamingAsync(response, linked.Token);
            }
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        ChatRequest request,
        bool stream,
        CancellationToken cancellationToken)
    {
        var payload = new OpenAIChatRequest
        {
            Model = string.IsNullOrWhiteSpace(request.Model)
                ? _options.Model
                : request.Model.Trim(),
            Stream = stream,
            Messages = request.Messages.Select(message => new OpenAIMessage
            {
                Role = ConvertRole(message.Role),
                Content = message.Content
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue(stream
                ? "text/event-stream"
                : "application/json"));

        var secret = await ResolveSecretAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(secret))
        {
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                secret);
        }

        return httpRequest;
    }

    private async Task<string?> ResolveSecretAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.SecretReference))
        {
            return null;
        }

        var secret = await _secretStore.GetAsync(
            _options.SecretReference,
            cancellationToken);
        if (secret is null || string.IsNullOrWhiteSpace(secret.Value))
        {
            throw SafeFailure("openai_compatible_secret_missing");
        }

        return secret.Value;
    }

    private static async IAsyncEnumerable<ChatChunk> ReadStreamingAsync(
        HttpResponseMessage response,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith(':'))
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data.Equals("[DONE]", StringComparison.Ordinal))
            {
                yield return new ChatChunk(string.Empty, true);
                yield break;
            }

            OpenAIChatResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<OpenAIChatResponse>(
                    data,
                    JsonOptions);
            }
            catch (JsonException)
            {
                throw SafeFailure("openai_compatible_chat_malformed_response");
            }

            var choice = parsed?.Choices?.FirstOrDefault();
            if (choice is null)
            {
                throw SafeFailure("openai_compatible_chat_malformed_response");
            }

            var content = choice.Delta?.Content ?? string.Empty;
            var complete = !string.IsNullOrWhiteSpace(choice.FinishReason);
            yield return new ChatChunk(content, complete);
        }
    }

    private static async Task<ChatChunk> ReadNonStreamingAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            var parsed = await JsonSerializer.DeserializeAsync<OpenAIChatResponse>(
                stream,
                JsonOptions,
                cancellationToken);
            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (content is null)
            {
                throw SafeFailure("openai_compatible_chat_malformed_response");
            }

            return new ChatChunk(content, true);
        }
        catch (JsonException)
        {
            throw SafeFailure("openai_compatible_chat_malformed_response");
        }
    }

    private static void ValidateRequest(ChatRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException("A model name is required.", nameof(request));
        }

        if (request.Messages.Count == 0)
        {
            throw new ArgumentException(
                "At least one chat message is required.",
                nameof(request));
        }

        if (request.Tools.Count > 0)
        {
            throw SafeFailure("openai_compatible_tools_deferred");
        }
    }

    private static string ConvertRole(ChatRole role) =>
        role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(nameof(role))
        };

    private static InvalidOperationException CreateSafeFailure(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.TooManyRequests => SafeFailure("openai_compatible_rate_limited"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                SafeFailure("openai_compatible_auth_failed"),
            HttpStatusCode.NotFound => SafeFailure("openai_compatible_model_unavailable"),
            _ => SafeFailure("openai_compatible_chat_failed")
        };

    private static InvalidOperationException SafeFailure(string safeErrorCode) =>
        new(safeErrorCode);

    private sealed class OpenAIChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required OpenAIMessage[] Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }
    }

    private sealed class OpenAIMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }
    }

    private sealed class OpenAIChatResponse
    {
        [JsonPropertyName("choices")]
        public OpenAIChoice[]? Choices { get; init; }
    }

    private sealed class OpenAIChoice
    {
        [JsonPropertyName("delta")]
        public OpenAIMessageDelta? Delta { get; init; }

        [JsonPropertyName("message")]
        public OpenAIMessageDelta? Message { get; init; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; init; }
    }

    private sealed class OpenAIMessageDelta
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }
}
