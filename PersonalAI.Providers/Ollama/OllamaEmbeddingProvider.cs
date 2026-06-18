using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Memory;

namespace PersonalAI.Providers.Ollama;

public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly TimeSpan _requestTimeout;

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        string model,
        int dimension,
        int maxInputCharacters = 8192,
        int maxBatchSize = 16,
        TimeSpan? requestTimeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dimension));
        }

        _httpClient = httpClient;
        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri("http://localhost:11434");
        }

        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        ModelInfo = new EmbeddingModelInfo(
            "ollama",
            model.Trim(),
            dimension,
            Math.Clamp(maxInputCharacters, 100, 100_000),
            SupportsBatch: true,
            Math.Clamp(maxBatchSize, 1, 128));
    }

    public EmbeddingModelInfo ModelInfo { get; }

    public EmbeddingProviderHealth GetStatus()
    {
        if (string.IsNullOrWhiteSpace(ModelInfo.Model))
        {
            return new EmbeddingProviderHealth(
                EmbeddingProviderStatus.Unconfigured,
                "embedding_model_unconfigured");
        }

        return new EmbeddingProviderHealth(EmbeddingProviderStatus.Available);
    }

    public async Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var timeout = new CancellationTokenSource(_requestTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            var payload = new OllamaEmbedRequest
            {
                Model = string.IsNullOrWhiteSpace(request.Model)
                    ? ModelInfo.Model
                    : request.Model.Trim(),
                Input = request.Inputs.Count == 1
                    ? request.Inputs[0]
                    : request.Inputs.ToArray(),
                Truncate = false,
                Dimensions = request.Dimensions
            };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "/api/embed")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateSafeFailure(response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token);
            var body = await JsonSerializer.DeserializeAsync<OllamaEmbedResponse>(
                stream,
                JsonOptions,
                linked.Token);
            if (body?.Embeddings is null ||
                body.Embeddings.Length != request.Inputs.Count ||
                body.Embeddings.Length == 0)
            {
                throw SafeFailure("ollama_embedding_malformed_response");
            }

            var vectors = body.Embeddings.Select(ConvertVector).ToArray();
            return new EmbeddingResult(
                vectors,
                string.IsNullOrWhiteSpace(body.Model) ? ModelInfo.Model : body.Model,
                ModelInfo.ProviderId);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw SafeFailure("ollama_embedding_timeout");
        }
        catch (JsonException)
        {
            throw SafeFailure("ollama_embedding_malformed_response");
        }
        catch (HttpRequestException)
        {
            throw SafeFailure("ollama_unavailable");
        }
    }

    private void ValidateRequest(EmbeddingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Inputs.Count == 0 ||
            request.Inputs.Count > ModelInfo.MaxBatchSize)
        {
            throw SafeFailure("embedding_batch_size_invalid");
        }

        if (request.Inputs.Any(input =>
                string.IsNullOrWhiteSpace(input) ||
                input.Length > ModelInfo.MaxInputCharacters))
        {
            throw SafeFailure("embedding_input_invalid");
        }
    }

    private EmbeddingVector ConvertVector(double[] values)
    {
        if (values.Length != ModelInfo.Dimension)
        {
            throw SafeFailure("embedding_dimension_mismatch");
        }

        return new EmbeddingVector(values.Select(value => (float)value).ToArray());
    }

    private static InvalidOperationException CreateSafeFailure(HttpStatusCode statusCode)
    {
        var code = statusCode switch
        {
            HttpStatusCode.NotFound => "ollama_embedding_model_unavailable",
            HttpStatusCode.BadRequest => "ollama_embedding_request_rejected",
            _ => "ollama_embedding_failed"
        };

        return SafeFailure(code);
    }

    private static InvalidOperationException SafeFailure(string safeErrorCode) =>
        new(safeErrorCode);

    private sealed class OllamaEmbedRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required object Input { get; init; }

        [JsonPropertyName("truncate")]
        public bool Truncate { get; init; }

        [JsonPropertyName("dimensions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Dimensions { get; init; }
    }

    private sealed class OllamaEmbedResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("embeddings")]
        public double[][]? Embeddings { get; init; }
    }
}
