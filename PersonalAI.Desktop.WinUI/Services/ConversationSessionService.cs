using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Diagnostics;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Desktop.WinUI.Services;

public sealed class ConversationSessionService
{
    private const int MaxToolRoundsPerTurn = 4;
    private const int MaxToolCallsPerTurn = 8;
    private const int MaxToolResultCharacters = 32_000;

    private static readonly HashSet<ToolId> ExposedWorkspaceToolIds =
    [
        GetWorkspaceInfoToolIds.GetWorkspaceInfo,
        GetWorkspaceInfoToolIds.ListDirectory,
        GetWorkspaceInfoToolIds.ReadTextFile,
        GetWorkspaceInfoToolIds.SearchWorkspaceText
    ];

    private readonly IConversationRepository _conversationRepository;
    private readonly ChatSessionService _chatSession;
    private readonly IToolRegistry? _toolRegistry;
    private readonly ITypedToolRuntime? _toolRuntime;
    private readonly IWorkspaceRegistry? _workspaceRegistry;

    public ConversationSessionService(
        IConversationRepository conversationRepository,
        ChatSessionService chatSession)
        : this(conversationRepository, chatSession, null, null, null)
    {
    }

    public ConversationSessionService(
        IConversationRepository conversationRepository,
        ChatSessionService chatSession,
        IToolRegistry? toolRegistry,
        ITypedToolRuntime? toolRuntime,
        IWorkspaceRegistry? workspaceRegistry)
    {
        _conversationRepository = conversationRepository;
        _chatSession = chatSession;
        _toolRegistry = toolRegistry;
        _toolRuntime = toolRuntime;
        _workspaceRegistry = workspaceRegistry;
    }

    public Task<IReadOnlyList<Conversation>> LoadConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        return _conversationRepository.ListConversationsAsync(cancellationToken);
    }

    public Task<Conversation?> LoadConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return _conversationRepository.GetConversationAsync(
            conversationId,
            cancellationToken);
    }

    public Task<IReadOnlyList<StoredChatMessage>> LoadMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        return _conversationRepository.ListMessagesAsync(
            conversationId,
            cancellationToken);
    }

    public Task<Conversation> CreateConversationAsync(
        string firstPrompt,
        string model,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(),
            ConversationTitleGenerator.CreateTitle(firstPrompt),
            model,
            now,
            now,
            ConversationStatus.Active);

        return _conversationRepository.CreateConversationAsync(
            conversation,
            cancellationToken);
    }

    public Task AddMessageAsync(
        Guid conversationId,
        ChatRole role,
        string content,
        CancellationToken cancellationToken = default)
    {
        var message = new StoredChatMessage(
            Guid.NewGuid(),
            conversationId,
            role,
            content,
            DateTimeOffset.UtcNow);

        return _conversationRepository.AddMessageAsync(message, cancellationToken);
    }

    public async Task<Conversation> UpdateConversationAsync(
        Conversation conversation,
        ConversationStatus status,
        string model,
        CancellationToken cancellationToken = default)
    {
        var updated = conversation with
        {
            Model = model,
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _conversationRepository.UpdateConversationAsync(
            updated,
            cancellationToken);

        return updated;
    }

    public IAsyncEnumerable<ChatChunk> StreamAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken)
    {
        return _chatSession.StreamAsync(model, messages, cancellationToken);
    }

    public bool CanUseWorkspaceTools(string model) =>
        _toolRegistry is not null &&
        _toolRuntime is not null &&
        _workspaceRegistry is not null &&
        _chatSession.SupportsToolCalls &&
        ChatModelCapabilityService.SupportsTools(model) &&
        _workspaceRegistry.List().Count > 0 &&
        GetWorkspaceToolDefinitions().Count > 0;

    public string GetWorkspaceToolAvailabilityMessage(string model)
    {
        if (_toolRegistry is null || _toolRuntime is null)
        {
            return "Workspace tools are unavailable.";
        }

        if (!_chatSession.SupportsToolCalls)
        {
            return "The current provider does not support workspace tools.";
        }

        if (!ChatModelCapabilityService.SupportsTools(model))
        {
            return "This model does not support workspace tools.";
        }

        if (_workspaceRegistry is null || _workspaceRegistry.List().Count == 0)
        {
            return "No available workspace is registered.";
        }

        return GetWorkspaceToolDefinitions().Count == 0
            ? "Workspace tools are unavailable."
            : "Workspace tools available.";
    }

    public async IAsyncEnumerable<ChatChunk> StreamWithWorkspaceToolsAsync(
        Guid conversationId,
        TaskId taskId,
        string model,
        IReadOnlyList<ChatMessage> messages,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken)
    {
        if (!CanUseWorkspaceTools(model))
        {
            await foreach (var chunk in StreamAsync(model, messages, cancellationToken))
            {
                yield return chunk;
            }

            yield break;
        }

        var workingMessages = AddWorkspaceToolInstruction(messages);
        var tools = GetWorkspaceToolDefinitions();
        var totalToolCalls = 0;
        LogWorkspaceToolRequestShape(model, workingMessages, tools);

        for (var round = 0; round < MaxToolRoundsPerTurn; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toolCalls = new List<ChatToolCall>();

            await foreach (var chunk in _chatSession.StreamAsync(
                               model,
                               workingMessages,
                               tools,
                               cancellationToken))
            {
                if (chunk.ToolCalls.Count > 0)
                {
                    toolCalls.AddRange(chunk.ToolCalls);
                }

                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    yield return chunk;
                }
            }

            if (toolCalls.Count == 0)
            {
                yield break;
            }

            workingMessages.Add(new ChatMessage(
                ChatRole.Assistant,
                string.Empty,
                [],
                toolCalls,
                null,
                null));

            foreach (var toolCall in toolCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                LogToolStage(
                    "Tool call received",
                    toolCall.Name,
                    toolCall.Arguments,
                    SafeWorkspaceIdentifierPresent(toolCall.Arguments));

                if (totalToolCalls >= MaxToolCallsPerTurn)
                {
                    var limitMessage =
                        "Workspace tool-call limit reached for this response.";
                    yield return new ChatChunk(
                        string.Empty,
                        false,
                        [],
                        limitMessage);
                    workingMessages.Add(CreateToolResultMessage(
                        toolCall,
                        CreateLimitPayload(toolCall, limitMessage)));
                    yield break;
                }

                totalToolCalls++;
                var displayToolCall = CreateDisplayToolCall(toolCall);
                var requested = CreateToolCallPersistencePayload(displayToolCall);
                await AddMessageAsync(
                    conversationId,
                    ChatRole.Tool,
                    requested,
                    CancellationToken.None);

                yield return new ChatChunk(
                    string.Empty,
                    false,
                    [],
                    CreateRequestedActivityMessage(displayToolCall));

                var payload = await ExecuteToolCallAsync(
                    taskId,
                    toolCall,
                    cancellationToken);

                var persistedResult = JsonSerializer.Serialize(
                    payload,
                    ChatToolJson.SerializerOptions);
                await AddMessageAsync(
                    conversationId,
                    ChatRole.Tool,
                    persistedResult,
                    CancellationToken.None);

                yield return new ChatChunk(
                    string.Empty,
                    false,
                    [],
                    CreateCompletedActivityMessage(displayToolCall, payload));

                if (payload.Status == ToolExecutionStatus.Cancelled.ToString())
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                workingMessages.Add(CreateToolResultMessage(toolCall, payload));
            }
        }

        yield return new ChatChunk(
            "I stopped because the workspace tool round limit was reached.",
            true);
    }

    private IReadOnlyList<ChatToolDefinition> GetWorkspaceToolDefinitions()
    {
        if (_toolRegistry is null)
        {
            return [];
        }

        return _toolRegistry.Descriptors
            .Where(descriptor =>
                ExposedWorkspaceToolIds.Contains(descriptor.Id) &&
                descriptor.IsReadOnly &&
                !descriptor.ChangesState &&
                !descriptor.LeavesMachine)
            .Select(CreateWorkspaceToolDefinition)
            .ToArray();
    }

    private List<ChatMessage> AddWorkspaceToolInstruction(
        IReadOnlyList<ChatMessage> messages)
    {
        if (_workspaceRegistry?.List().Count != 1)
        {
            return messages.ToList();
        }

        return
        [
            ..messages,
            new ChatMessage(
                ChatRole.System,
                "Workspace identifiers are application-managed. Do not ask the user to provide workspace IDs. Use workspace tools with relative paths only; the app will bind the active workspace.")
        ];
    }

    private ChatToolDefinition CreateWorkspaceToolDefinition(ToolDescriptor descriptor)
    {
        var definition = ChatToolDefinitionFactory.Create(descriptor);
        if (_workspaceRegistry?.List().Count != 1)
        {
            return definition;
        }

        using var document = JsonDocument.Parse(definition.Parameters.GetRawText());
        var root = JsonNode.Parse(document.RootElement.GetRawText())?.AsObject();
        if (root is null ||
            !root.TryGetPropertyValue("properties", out var propertiesNode) ||
            propertiesNode is not JsonObject properties)
        {
            return definition;
        }

        properties.Remove("workspaceId");

        if (root.TryGetPropertyValue("required", out var requiredNode) &&
            requiredNode is JsonArray required)
        {
            for (var index = required.Count - 1; index >= 0; index--)
            {
                if (required[index]?.GetValue<string>() == "workspaceId")
                {
                    required.RemoveAt(index);
                }
            }
        }

        using var shapedDocument = JsonDocument.Parse(root.ToJsonString());
        return definition with
        {
            Parameters = shapedDocument.RootElement.Clone()
        };
    }

    private async Task<ChatToolResultPayload> ExecuteToolCallAsync(
        TaskId taskId,
        ChatToolCall toolCall,
        CancellationToken cancellationToken)
    {
        if (_toolRegistry is null || _toolRuntime is null)
        {
            return CreateSafeFailure(
                toolCall,
                "tool_runtime_unavailable",
                "Workspace tools are unavailable.");
        }

        var toolId = new ToolId(toolCall.Name);
        LogToolStage(
            "Tool name resolved",
            toolCall.Name,
            toolCall.Arguments,
            SafeWorkspaceIdentifierPresent(toolCall.Arguments));

        if (!_toolRegistry.TryGetTool(toolId, out var tool) ||
            !ExposedWorkspaceToolIds.Contains(toolId))
        {
            return CreateSafeFailure(
                toolCall,
                "tool_not_available",
                "The requested workspace tool is not available.");
        }

        var normalizedArguments = NormalizeToolArguments(toolCall, out var argumentFailure);
        if (argumentFailure is not null)
        {
            return argumentFailure;
        }

        object? input;

        try
        {
            input = JsonSerializer.Deserialize(
                normalizedArguments.GetRawText(),
                tool.Descriptor.InputType,
                ChatToolJson.SerializerOptions);
            LogToolStage(
                "Arguments deserialized",
                toolCall.Name,
                normalizedArguments,
                SafeWorkspaceIdentifierPresent(normalizedArguments));
        }
        catch (Exception exception) when (exception is JsonException ||
                                         exception is NotSupportedException ||
                                         exception is ArgumentException)
        {
            LogToolStage(
                "Arguments deserialization failed",
                toolCall.Name,
                toolCall.Arguments,
                SafeWorkspaceIdentifierPresent(toolCall.Arguments),
                "invalid_tool_arguments",
                exception);
            return CreateSafeFailure(
                toolCall,
                "invalid_tool_arguments",
                "The tool arguments were invalid.");
        }

        ToolResult result;

        try
        {
            LogToolStage(
                "Typed runtime invoked",
                toolCall.Name,
                normalizedArguments,
                SafeWorkspaceIdentifierPresent(normalizedArguments));
            result = await _toolRuntime.InvokeAsync(
                taskId,
                new ToolInvocation(toolId, input),
                cancellationToken);
            LogToolStage(
                "Tool result returned",
                toolCall.Name,
                normalizedArguments,
                SafeWorkspaceIdentifierPresent(normalizedArguments),
                result.SafeErrorCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            LogToolStage(
                "Typed runtime failed",
                toolCall.Name,
                normalizedArguments,
                SafeWorkspaceIdentifierPresent(normalizedArguments),
                "tool_runtime_failed");
            return CreateSafeFailure(
                toolCall,
                "tool_runtime_failed",
                "Workspace tool execution failed unexpectedly.");
        }

        return CreatePayload(toolCall, result);
    }

    private JsonElement NormalizeToolArguments(
        ChatToolCall toolCall,
        out ChatToolResultPayload? failure)
    {
        failure = null;

        if (toolCall.Arguments.ValueKind != JsonValueKind.Object)
        {
            failure = CreateSafeFailure(
                toolCall,
                "invalid_tool_arguments",
                "The tool arguments were invalid.");
            return toolCall.Arguments;
        }

        var node = JsonNode.Parse(toolCall.Arguments.GetRawText())?.AsObject();
        if (node is null)
        {
            failure = CreateSafeFailure(
                toolCall,
                "invalid_tool_arguments",
                "The tool arguments were invalid.");
            return toolCall.Arguments;
        }

        var workspaceId = GetStringProperty(node, "workspaceId");
        LogToolStage(
            "Workspace ID extracted",
            toolCall.Name,
            toolCall.Arguments,
            !string.IsNullOrWhiteSpace(workspaceId));

        var workspaces = _workspaceRegistry?.List() ?? [];
        if (workspaces.Count == 0)
        {
            failure = CreateSafeFailure(
                toolCall,
                "workspace_unavailable",
                "No available workspace is registered.");
            return toolCall.Arguments;
        }

        if (workspaces.Count == 1 &&
            (string.IsNullOrWhiteSpace(workspaceId) ||
             !LooksLikeWorkspaceId(workspaceId)))
        {
            var workspace = workspaces[0];
            node["workspaceId"] = workspace.Id.ToString();
            LogToolStage(
                "Workspace resolved from runtime registry",
                toolCall.Name,
                toolCall.Arguments,
                safeWorkspaceIdentifierPresent: true);
        }
        else if (workspaces.Count == 1 &&
                 _workspaceRegistry?.TryGet(new WorkspaceId(workspaceId!), out _) == true)
        {
            LogToolStage(
                "Workspace resolved from runtime registry",
                toolCall.Name,
                toolCall.Arguments,
                safeWorkspaceIdentifierPresent: true);
        }
        else if (string.IsNullOrWhiteSpace(workspaceId))
        {
            failure = CreateSafeFailure(
                toolCall,
                "workspace_ambiguous",
                "Choose a workspace before using workspace tools.");
            return toolCall.Arguments;
        }
        else if (_workspaceRegistry?.TryGet(new WorkspaceId(workspaceId), out _) == true)
        {
            LogToolStage(
                "Workspace resolved from runtime registry",
                toolCall.Name,
                toolCall.Arguments,
                safeWorkspaceIdentifierPresent: true);
        }
        else
        {
            LogToolStage(
                "Workspace resolution failed",
                toolCall.Name,
                toolCall.Arguments,
                safeWorkspaceIdentifierPresent: true,
                "workspace_not_found");
        }

        NormalizeRelativePathAlias(toolCall.Name, node);

        using var document = JsonDocument.Parse(
            node.ToJsonString(ChatToolJson.SerializerOptions));
        return document.RootElement.Clone();
    }

    private static void NormalizeRelativePathAlias(
        string toolName,
        JsonObject arguments)
    {
        var path = GetStringProperty(arguments, "path");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        switch (toolName)
        {
            case "workspace.directory.list":
            case "workspace.file.read_text":
                if (!arguments.ContainsKey("relativePath"))
                {
                    arguments["relativePath"] = path;
                }

                break;
            case "workspace.text.search":
                if (!arguments.ContainsKey("relativeDirectory"))
                {
                    arguments["relativeDirectory"] = path;
                }

                break;
        }
    }

    private static string? GetStringProperty(JsonObject arguments, string name) =>
        arguments.TryGetPropertyValue(name, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text)
            ? text
            : null;

    private static bool LooksLikeWorkspaceId(string value) =>
        Guid.TryParseExact(value, "N", out _) ||
        Guid.TryParseExact(value, "D", out _);

    private ChatToolCall CreateDisplayToolCall(ChatToolCall toolCall)
    {
        var normalizedArguments = NormalizeToolArguments(
            toolCall,
            out var failure);
        return failure is null
            ? toolCall with { Arguments = normalizedArguments }
            : toolCall;
    }

    private static ChatMessage CreateToolResultMessage(
        ChatToolCall toolCall,
        ChatToolResultPayload payload)
    {
        var content = JsonSerializer.Serialize(payload, ChatToolJson.SerializerOptions);
        return new ChatMessage(
            ChatRole.Tool,
            content,
            [],
            [],
            toolCall.Id,
            toolCall.Name);
    }

    private static string CreateToolCallPersistencePayload(ChatToolCall toolCall)
    {
        var payload = new
        {
            kind = "tool_call",
            toolCallId = toolCall.Id,
            toolName = toolCall.Name,
            arguments = toolCall.Arguments
        };

        return JsonSerializer.Serialize(payload, ChatToolJson.SerializerOptions);
    }

    private static bool SafeWorkspaceIdentifierPresent(JsonElement arguments)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("workspaceId", out var workspaceId) &&
            workspaceId.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(workspaceId.GetString());
    }

    [Conditional("DEBUG")]
    private static void LogToolStage(
        string stage,
        string toolName,
        JsonElement arguments,
        bool safeWorkspaceIdentifierPresent,
        string? safeErrorCode = null,
        Exception? exception = null)
    {
        var fields = arguments.ValueKind == JsonValueKind.Object
            ? string.Join(
                ",",
                arguments.EnumerateObject()
                    .Select(property => property.Name)
                    .Order(StringComparer.Ordinal))
            : arguments.ValueKind.ToString();
        var exceptionText = exception is null
            ? string.Empty
            : $" exception={exception.GetType().Name} hresult=0x{exception.HResult:X8}";
        var safeErrorText = string.IsNullOrWhiteSpace(safeErrorCode)
            ? string.Empty
            : $" safeErrorCode={safeErrorCode}";

        SafeDebugDiagnostics.WriteLine(
            $"WorkspaceToolCall stage={stage} tool={toolName} workspaceIdPresent={safeWorkspaceIdentifierPresent} argumentFields={fields}{safeErrorText}{exceptionText}");
    }

    [Conditional("DEBUG")]
    private void LogWorkspaceToolRequestShape(
        string model,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<ChatToolDefinition> tools)
    {
        var workspaceCount = _workspaceRegistry?.List().Count ?? 0;
        SafeDebugDiagnostics.WriteLine(
            $"WorkspaceToolRequest build={CreateBuildMarker()} model={model} activeRuntimeWorkspaces={workspaceCount} singleWorkspaceMode={workspaceCount == 1}");
        SafeDebugDiagnostics.WriteLine(
            $"WorkspaceToolRequest messageRoles={string.Join(",", messages.Select(message => message.Role.ToString()))} workspaceInstructionPresent={ContainsWorkspaceInstruction(messages)}");
        SafeDebugDiagnostics.WriteLine(
            $"WorkspaceToolRequest advertisedTools={string.Join(",", tools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal))}");

        foreach (var tool in tools.OrderBy(tool => tool.Name, StringComparer.Ordinal))
        {
            var properties = GetSchemaPropertyNames(tool.Parameters);
            var required = GetSchemaRequiredNames(tool.Parameters);
            SafeDebugDiagnostics.WriteLine(
                $"WorkspaceToolRequest tool={tool.Name} properties={FormatNames(properties)} required={FormatNames(required)} workspaceIdPresent={ContainsText(tool.Parameters, "workspaceId")}");
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
        var assembly = typeof(ConversationSessionService).Assembly;
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

    private static ChatToolResultPayload CreatePayload(
        ChatToolCall toolCall,
        ToolResult result)
    {
        var outputWasTruncated = false;
        JsonElement? output = result.Output is null
            ? null
            : JsonSerializer.SerializeToElement(
                CreateModelVisibleOutput(result.Output, out outputWasTruncated),
                ChatToolJson.SerializerOptions);
        var isTruncated = result.Output is not null &&
            (WasOutputTruncated(result.Output) || outputWasTruncated);

        return new ChatToolResultPayload(
            toolCall.Id,
            toolCall.Name,
            result.IsSuccess,
            result.Status.ToString(),
            result.Summary,
            result.SafeErrorCode,
            result.SafeErrorMessage,
            output,
            isTruncated);
    }

    private static object CreateModelVisibleOutput(object output, out bool truncated)
    {
        truncated = false;

        if (output is ReadTextFileOutput read &&
            read.Content.Length > MaxToolResultCharacters)
        {
            truncated = true;
            return read with
            {
                Content = read.Content[..MaxToolResultCharacters],
                IsTruncated = true
            };
        }

        var json = JsonSerializer.Serialize(output, ChatToolJson.SerializerOptions);
        if (json.Length <= MaxToolResultCharacters)
        {
            return output;
        }

        truncated = true;
        return new
        {
            message = "Tool output was too large and was truncated.",
            outputPreview = json[..MaxToolResultCharacters]
        };
    }

    private static bool WasOutputTruncated(object output) =>
        output switch
        {
            ReadTextFileOutput read => read.IsTruncated ||
                read.Content.Length > MaxToolResultCharacters,
            SearchWorkspaceTextOutput search => search.IsTruncated,
            ListDirectoryOutput list => list.IsTruncated,
            _ => false
        };

    private static ChatToolResultPayload CreateSafeFailure(
        ChatToolCall toolCall,
        string safeErrorCode,
        string safeErrorMessage) =>
        new(
            toolCall.Id,
            toolCall.Name,
            false,
            ToolExecutionStatus.ValidationFailed.ToString(),
            safeErrorMessage,
            safeErrorCode,
            safeErrorMessage,
            null,
            false);

    private static ChatToolResultPayload CreateLimitPayload(
        ChatToolCall toolCall,
        string message) =>
        new(
            toolCall.Id,
            toolCall.Name,
            false,
            ToolExecutionStatus.ValidationFailed.ToString(),
            message,
            "tool_call_limit_reached",
            message,
            null,
            false);

    private string CreateRequestedActivityMessage(ChatToolCall toolCall) =>
        ToolPresentationMapper.RequestedActivity(
            toolCall,
            _workspaceRegistry);

    private string CreateCompletedActivityMessage(
        ChatToolCall toolCall,
        ChatToolResultPayload result) =>
        ToolPresentationMapper.CompletedActivity(
            toolCall,
            result,
            _workspaceRegistry);

    private static class GetWorkspaceInfoToolIds
    {
        public static readonly ToolId GetWorkspaceInfo =
            new("workspace.info.get");
        public static readonly ToolId ListDirectory =
            new("workspace.directory.list");
        public static readonly ToolId ReadTextFile =
            new("workspace.file.read_text");
        public static readonly ToolId SearchWorkspaceText =
            new("workspace.text.search");
    }
}
