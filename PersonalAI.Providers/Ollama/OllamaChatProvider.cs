using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Chat;

namespace PersonalAI.Providers.Ollama;

public sealed class OllamaChatProvider : IChatProvider, IChatModelCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public string ProviderName => "Ollama";

    public OllamaChatProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri("http://localhost:11434");
        }

        // Local models can take a long time for large prompts.
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
    }

    public async IAsyncEnumerable<ChatChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw new ArgumentException(
                "A model name is required.",
                nameof(request));
        }

        if (request.Messages.Count == 0)
        {
            throw new ArgumentException(
                "At least one chat message is required.",
                nameof(request));
        }

        var ollamaRequest = new OllamaChatRequest
        {
            Model = request.Model,
            Stream = true,
            Messages = request.Messages
                .Select(message => new OllamaMessage
                {
                    Role = ConvertRole(message.Role),
                    Content = message.Content,
                    Images = message.Images.Count == 0
                        ? null
                        : message.Images
                            .Select(image => image.Base64Data)
                            .ToArray()
                })
                .ToArray()
        };

        var json = JsonSerializer.Serialize(ollamaRequest, JsonOptions);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/chat");

        httpRequest.Content = new StringContent(
            json,
            Encoding.UTF8,
            "application/json");

        httpRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Ollama returned {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}: {errorBody}");
        }

        await using var responseStream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        using var reader = new StreamReader(responseStream);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            OllamaChatResponse? chunk;

            try
            {
                chunk = JsonSerializer.Deserialize<OllamaChatResponse>(
                    line,
                    JsonOptions);
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    $"Ollama returned invalid JSON: {line}",
                    exception);
            }

            if (chunk is null)
            {
                continue;
            }

            var content = chunk.Message?.Content ?? string.Empty;

            yield return new ChatChunk(
                Content: content,
                IsComplete: chunk.Done);

            if (chunk.Done)
            {
                yield break;
            }
        }
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            "/api/tags",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(
                cancellationToken);

            throw new HttpRequestException(
                $"Ollama returned {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        var tags = await JsonSerializer.DeserializeAsync<OllamaTagsResponse>(
            stream,
            JsonOptions,
            cancellationToken);

        return tags?.Models
            .Select(model => model.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    private static string ConvertRole(ChatRole role)
    {
        return role switch
        {
            ChatRole.System => "system",
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "tool",
            _ => throw new ArgumentOutOfRangeException(
                nameof(role),
                role,
                "Unsupported chat role.")
        };
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required OllamaMessage[] Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }
    }

    private sealed class OllamaMessage
    {
        [JsonPropertyName("role")]
        public required string Role { get; init; }

        [JsonPropertyName("content")]
        public required string Content { get; init; }

        [JsonPropertyName("images")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string[]? Images { get; init; }
    }

    private sealed class OllamaChatResponse
    {
        [JsonPropertyName("message")]
        public OllamaMessage? Message { get; init; }

        [JsonPropertyName("done")]
        public bool Done { get; init; }

        [JsonPropertyName("done_reason")]
        public string? DoneReason { get; init; }
    }

    private sealed class OllamaTagsResponse
    {
        [JsonPropertyName("models")]
        public OllamaModelTag[] Models { get; init; } = [];
    }

    private sealed class OllamaModelTag
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;
    }
}
