using System.Net.Http.Headers;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Diagnostics;

namespace PersonalAI.Providers.Ollama;

public sealed class OllamaChatProvider :
    IChatProvider,
    IToolCallingChatProvider,
    IChatModelCatalog
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
                            .ToArray(),
                    ToolCalls = message.ToolCalls.Count == 0
                        ? null
                        : message.ToolCalls
                            .Select(call => new OllamaToolCall
                            {
                                Function = new OllamaToolCallFunction
                                {
                                    Name = call.Name,
                                    Arguments = call.Arguments
                                }
                            })
                            .ToArray()
                })
                .ToArray(),
            Tools = request.Tools.Count == 0
                ? null
                : request.Tools
                    .Select(tool => new OllamaToolDefinition
                    {
                        Function = new OllamaToolFunctionDefinition
                        {
                            Name = tool.Name,
                            Description = tool.Description,
                            Parameters = tool.Parameters
                        }
                    })
                    .ToArray()
        };

        var json = JsonSerializer.Serialize(ollamaRequest, JsonOptions);
        LogOutgoingRequest(request, json);

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
            throw new HttpRequestException(
                $"Ollama returned HTTP {(int)response.StatusCode}.");
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
                    "Ollama returned invalid JSON.",
                    exception);
            }

            if (chunk is null)
            {
                continue;
            }

            var content = chunk.Message?.Content ?? string.Empty;
            var toolCalls = chunk.Message?.ToolCalls is null
                ? []
                : ConvertToolCalls(chunk.Message.ToolCalls);
            LogIncomingChunk(content, toolCalls, chunk.Done);

            yield return new ChatChunk(
                content,
                chunk.Done,
                toolCalls);

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

    private static IReadOnlyList<ChatToolCall> ConvertToolCalls(
        IReadOnlyList<OllamaToolCall> toolCalls)
    {
        var converted = new List<ChatToolCall>();

        foreach (var toolCall in toolCalls)
        {
            var name = toolCall.Function?.Name?.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException(
                    "Ollama returned a tool call without a function name.");
            }

            var arguments = NormalizeArguments(toolCall.Function?.Arguments);
            converted.Add(new ChatToolCall(
                string.IsNullOrWhiteSpace(toolCall.Id)
                    ? Guid.NewGuid().ToString("N")
                    : toolCall.Id,
                name,
                arguments));
        }

        return converted;
    }

    private static JsonElement NormalizeArguments(JsonElement? arguments)
    {
        if (arguments is null)
        {
            using var emptyDocument = JsonDocument.Parse("{}");
            return emptyDocument.RootElement.Clone();
        }

        var element = arguments.Value;

        if (element.ValueKind == JsonValueKind.String)
        {
            var raw = element.GetString();

            if (string.IsNullOrWhiteSpace(raw))
            {
                using var emptyDocument = JsonDocument.Parse("{}");
                return emptyDocument.RootElement.Clone();
            }

            try
            {
                using var parsed = JsonDocument.Parse(raw);
                return parsed.RootElement.Clone();
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException(
                    "Ollama returned invalid tool-call arguments.",
                    exception);
            }
        }

        if (element.ValueKind is JsonValueKind.Object)
        {
            return element.Clone();
        }

        throw new InvalidDataException(
            "Ollama returned invalid tool-call arguments.");
    }

    [Conditional("DEBUG")]
    private static void LogOutgoingRequest(ChatRequest request, string serializedJson)
    {
        using var document = JsonDocument.Parse(serializedJson);
        var root = document.RootElement;
        var serializedTools = root.TryGetProperty("tools", out var toolsElement) &&
            toolsElement.ValueKind == JsonValueKind.Array
                ? toolsElement
                : default;

        SafeDebugDiagnostics.WriteLine(
            $"OllamaRequest build={CreateBuildMarker()} model={request.Model} messageRoles={string.Join(",", request.Messages.Select(message => message.Role.ToString()))} toolCount={request.Tools.Count} workspaceInstructionPresent={ContainsWorkspaceInstruction(request.Messages)}");
        SafeDebugDiagnostics.WriteLine(
            $"OllamaRequest advertisedTools={string.Join(",", request.Tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal))}");

        if (serializedTools.ValueKind == JsonValueKind.Array)
        {
            foreach (var tool in serializedTools.EnumerateArray())
            {
                if (!tool.TryGetProperty("function", out var function) ||
                    function.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var toolName = function.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString() ?? string.Empty
                    : string.Empty;
                var parameters = function.TryGetProperty("parameters", out var parameterElement)
                    ? parameterElement
                    : default;
                var properties = GetSchemaPropertyNames(parameters);
                var required = GetSchemaRequiredNames(parameters);
                SafeDebugDiagnostics.WriteLine(
                    $"OllamaRequest tool={toolName} properties={FormatNames(properties)} required={FormatNames(required)} workspaceIdPresent={ContainsText(parameters, "workspaceId")}");
            }
        }
    }

    [Conditional("DEBUG")]
    private static void LogIncomingChunk(
        string content,
        IReadOnlyList<ChatToolCall> toolCalls,
        bool done)
    {
        SafeDebugDiagnostics.WriteLine(
            $"OllamaResponse containsText={!string.IsNullOrWhiteSpace(content)} containsStructuredToolCalls={toolCalls.Count > 0} structuredToolNames={string.Join(",", toolCalls.Select(call => call.Name).OrderBy(name => name, StringComparer.Ordinal))} done={done}");

        foreach (var toolCall in toolCalls.OrderBy(call => call.Name, StringComparer.Ordinal))
        {
            SafeDebugDiagnostics.WriteLine(
                $"OllamaResponse tool={toolCall.Name} argumentFields={FormatNames(GetArgumentFieldNames(toolCall.Arguments))}");
        }
    }

    private static bool ContainsWorkspaceInstruction(IReadOnlyList<ChatMessage> messages) =>
        messages.Any(message =>
            message.Role == ChatRole.System &&
            message.Content.Contains(
                "Workspace identifiers are application-managed",
                StringComparison.Ordinal));

    private static string CreateBuildMarker()
    {
        var assembly = typeof(OllamaChatProvider).Assembly;
        var version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        var location = assembly.Location;
        var timestamp = string.IsNullOrWhiteSpace(location) || !File.Exists(location)
            ? "unknown"
            : File.GetLastWriteTimeUtc(location).ToString("O");
        return $"{assembly.GetName().Name}/{version}/{timestamp}";
    }

    private static string FormatNames(IEnumerable<string> names) =>
        string.Join(", ", names.OrderBy(name => name, StringComparer.Ordinal));

    private static IReadOnlyList<string> GetArgumentFieldNames(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return arguments
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    private static IReadOnlyList<string> GetSchemaPropertyNames(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return properties
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    private static IReadOnlyList<string> GetSchemaRequiredNames(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object ||
            !schema.TryGetProperty("required", out var required) ||
            required.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return required
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .OfType<string>()
            .ToArray();
    }

    private static bool ContainsText(JsonElement element, string value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        value,
                        StringComparison.OrdinalIgnoreCase) ||
                    ContainsText(property.Value, value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (ContainsText(item, value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            return string.Equals(
                element.GetString(),
                value,
                StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private sealed class OllamaChatRequest
    {
        [JsonPropertyName("model")]
        public required string Model { get; init; }

        [JsonPropertyName("messages")]
        public required OllamaMessage[] Messages { get; init; }

        [JsonPropertyName("stream")]
        public bool Stream { get; init; }

        [JsonPropertyName("tools")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OllamaToolDefinition[]? Tools { get; init; }
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

        [JsonPropertyName("tool_calls")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public OllamaToolCall[]? ToolCalls { get; init; }
    }

    private sealed class OllamaToolDefinition
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "function";

        [JsonPropertyName("function")]
        public required OllamaToolFunctionDefinition Function { get; init; }
    }

    private sealed class OllamaToolFunctionDefinition
    {
        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("description")]
        public required string Description { get; init; }

        [JsonPropertyName("parameters")]
        public required JsonElement Parameters { get; init; }
    }

    private sealed class OllamaToolCall
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("function")]
        public OllamaToolCallFunction? Function { get; init; }
    }

    private sealed class OllamaToolCallFunction
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("arguments")]
        public JsonElement? Arguments { get; init; }
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
