using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Providers;

namespace PersonalAI.Providers.OpenAICompatible;

public sealed class OpenAICompatibleEmbeddingProvider : IEmbeddingProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly ISecretStore _secretStore;
    private readonly OpenAICompatibleEmbeddingOptions _options;

    public OpenAICompatibleEmbeddingProvider(
        HttpClient httpClient,
        ISecretStore secretStore,
        OpenAICompatibleEmbeddingOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(secretStore);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Model);
        if (options.Dimension <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        _httpClient = httpClient;
        _secretStore = secretStore;
        _options = options;
        _httpClient.BaseAddress ??= options.BaseUri;
        _httpClient.Timeout = Timeout.InfiniteTimeSpan;
        ModelInfo = new EmbeddingModelInfo(
            "openai-compatible",
            options.Model.Trim(),
            options.Dimension,
            Math.Clamp(options.MaxInputCharacters, 100, 100_000),
            SupportsBatch: true,
            Math.Clamp(options.MaxBatchSize, 1, 2048));
    }

    public EmbeddingModelInfo ModelInfo { get; }

    public EmbeddingProviderHealth GetStatus() =>
        string.IsNullOrWhiteSpace(ModelInfo.Model)
            ? new EmbeddingProviderHealth(
                EmbeddingProviderStatus.Unconfigured,
                "embedding_model_unconfigured")
            : new EmbeddingProviderHealth(EmbeddingProviderStatus.Available);

    public async Task<EmbeddingResult> EmbedAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        using var timeout = new CancellationTokenSource(
            _options.RequestTimeout ?? TimeSpan.FromSeconds(30));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);

        try
        {
            using var httpRequest = await CreateRequestAsync(request, linked.Token);
            using var response = await _httpClient.SendAsync(
                httpRequest,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateSafeFailure(response.StatusCode);
            }

            await using var stream =
                await response.Content.ReadAsStreamAsync(linked.Token);
            var body = await JsonSerializer.DeserializeAsync<EmbeddingResponse>(
                stream,
                JsonOptions,
                linked.Token);
            if (body?.Data is null || body.Data.Length != request.Inputs.Count)
            {
                throw SafeFailure("openai_compatible_embedding_malformed_response");
            }

            var vectors = body.Data
                .OrderBy(item => item.Index)
                .Select(item => ConvertVector(item.Embedding))
                .ToArray();
            return new EmbeddingResult(
                vectors,
                string.IsNullOrWhiteSpace(body.Model) ? ModelInfo.Model : body.Model,
                ModelInfo.ProviderId);
        }
        catch (OperationCanceledException) when (
            timeout.IsCancellationRequested &&
            !cancellationToken.IsCancellationRequested)
        {
            throw SafeFailure("openai_compatible_embedding_timeout");
        }
        catch (JsonException)
        {
            throw SafeFailure("openai_compatible_embedding_malformed_response");
        }
        catch (HttpRequestException)
        {
            throw SafeFailure("openai_compatible_provider_unavailable");
        }
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        var payload = new EmbeddingRequestBody
        {
            Model = string.IsNullOrWhiteSpace(request.Model)
                ? ModelInfo.Model
                : request.Model.Trim(),
            Input = request.Inputs.Count == 1
                ? request.Inputs[0]
                : request.Inputs.ToArray(),
            Dimensions = request.Dimensions
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "embeddings")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(_options.SecretReference))
        {
            var secret = await _secretStore.GetAsync(
                _options.SecretReference,
                cancellationToken);
            if (secret is null || string.IsNullOrWhiteSpace(secret.Value))
            {
                throw SafeFailure("openai_compatible_secret_missing");
            }

            httpRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                secret.Value);
        }

        return httpRequest;
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

    private EmbeddingVector ConvertVector(double[]? values)
    {
        if (values is null)
        {
            throw SafeFailure("openai_compatible_embedding_malformed_response");
        }

        if (values.Length != ModelInfo.Dimension)
        {
            throw SafeFailure("embedding_dimension_mismatch");
        }

        return new EmbeddingVector(values.Select(value => (float)value).ToArray());
    }

    private static InvalidOperationException CreateSafeFailure(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.TooManyRequests => SafeFailure("openai_compatible_rate_limited"),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                SafeFailure("openai_compatible_auth_failed"),
            HttpStatusCode.NotFound => SafeFailure("openai_compatible_model_unavailable"),
            _ => SafeFailure("openai_compatible_embedding_failed")
        };

    private static InvalidOperationException SafeFailure(string safeErrorCode) =>
        new(safeErrorCode);

    private sealed class EmbeddingRequestBody
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("input")]
        public required object Input { get; init; }

        [JsonPropertyName("dimensions")]
        public int? Dimensions { get; init; }
    }

    private sealed class EmbeddingResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("data")]
        public EmbeddingData[]? Data { get; init; }
    }

    private sealed class EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public double[]? Embedding { get; init; }
    }
}
