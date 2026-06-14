using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;

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

        var workingMessages = messages.ToList();
        var tools = GetWorkspaceToolDefinitions();
        var totalToolCalls = 0;

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
                var requested = CreateToolCallPersistencePayload(toolCall);
                await AddMessageAsync(
                    conversationId,
                    ChatRole.Tool,
                    requested,
                    CancellationToken.None);

                yield return new ChatChunk(
                    string.Empty,
                    false,
                    [],
                    CreateActivityMessage(toolCall, running: true));

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
                    CreateActivityMessage(toolCall, running: false, payload));

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
            .Select(ChatToolDefinitionFactory.Create)
            .ToArray();
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

        if (!_toolRegistry.TryGetTool(toolId, out var tool) ||
            !ExposedWorkspaceToolIds.Contains(toolId))
        {
            return CreateSafeFailure(
                toolCall,
                "tool_not_available",
                "The requested workspace tool is not available.");
        }

        object? input;

        try
        {
            input = JsonSerializer.Deserialize(
                toolCall.Arguments.GetRawText(),
                tool.Descriptor.InputType,
                ChatToolJson.SerializerOptions);
        }
        catch (Exception exception) when (exception is JsonException ||
                                         exception is NotSupportedException ||
                                         exception is ArgumentException)
        {
            return CreateSafeFailure(
                toolCall,
                "invalid_tool_arguments",
                "The tool arguments were invalid.");
        }

        ToolResult result;

        try
        {
            result = await _toolRuntime.InvokeAsync(
                taskId,
                new ToolInvocation(toolId, input),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return CreateSafeFailure(
                toolCall,
                "tool_runtime_failed",
                "Workspace tool execution failed unexpectedly.");
        }

        return CreatePayload(toolCall, result);
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

    private static string CreateActivityMessage(
        ChatToolCall toolCall,
        bool running,
        ChatToolResultPayload? result = null)
    {
        if (running)
        {
            return $"Running workspace tool: {toolCall.Name}.";
        }

        if (result is null)
        {
            return $"Workspace tool finished: {toolCall.Name}.";
        }

        if (result.Status == ToolExecutionStatus.PermissionDenied.ToString())
        {
            return "Permission denied.";
        }

        if (result.Status == ToolExecutionStatus.Cancelled.ToString())
        {
            return "Workspace tool cancelled.";
        }

        return result.IsSuccess
            ? result.IsTruncated
                ? $"Workspace tool completed with truncated results: {toolCall.Name}."
                : $"Workspace tool completed: {toolCall.Name}."
            : result.SafeErrorMessage ?? "Workspace tool failed.";
    }

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
