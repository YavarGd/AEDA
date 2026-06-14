using System.Text.Json;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Infrastructure.Tools;
using PersonalAI.Infrastructure.Tools.Workspace;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Chat;

public sealed class WorkspaceToolConversationSessionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.ToolChatTests",
        Guid.NewGuid().ToString("N"));
    private readonly WorkspaceRegistry _workspaceRegistry = new();
    private readonly TypedToolRegistry _toolRegistry = new();
    private readonly FakeConversationRepository _repository = new();
    private readonly FakeToolRuntime _runtime = new();
    private readonly FakeToolProvider _provider = new();
    private readonly WorkspaceDescriptor _workspace;

    public WorkspaceToolConversationSessionTests()
    {
        Directory.CreateDirectory(_root);
        _workspace = _workspaceRegistry.Register(_root, "Tool test", "test");
        var options = new WorkspaceToolOptions();
        var resolver = new WorkspacePathResolver(_workspaceRegistry);
        var reader = new FileSystemWorkspaceReader(
            _workspaceRegistry,
            resolver,
            options);
        _toolRegistry.Register(new GetWorkspaceInfoTool(reader, resolver, options));
        _toolRegistry.Register(new ListDirectoryTool(reader, resolver, options));
        _toolRegistry.Register(new ReadTextFileTool(reader, resolver, options));
        _toolRegistry.Register(new SearchWorkspaceTextTool(reader, resolver, options));
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_DoesNotAdvertiseToolsForNonToolModel()
    {
        _provider.Enqueue([new ChatChunk("plain", true)]);
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "embedding-only-model",
            [new ChatMessage(ChatRole.User, "List files")],
            CancellationToken.None));

        Assert.Equal("plain", Assert.Single(chunks).Content);
        Assert.Empty(Assert.Single(_provider.Requests).Tools);
        Assert.Empty(_runtime.Invocations);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_ExecutesToolAndFeedsResultBack()
    {
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("The workspace has files.", true)]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ListDirectoryTool.Id,
            new ListDirectoryOutput(".", [], IsTruncated: false),
            "Listed directory.",
            TimeSpan.FromMilliseconds(1)));
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List files")],
            CancellationToken.None));

        Assert.Contains(chunks, chunk => chunk.ActivityMessage?.Contains("Running workspace tool") == true);
        Assert.Contains(chunks, chunk => chunk.Content == "The workspace has files.");
        var invocation = Assert.Single(_runtime.Invocations);
        Assert.Equal(ListDirectoryTool.Id, invocation.ToolId);
        Assert.IsType<ListDirectoryInput>(invocation.Input);
        Assert.Equal(2, _provider.Requests.Count);
        Assert.Equal(ChatRole.Tool, _provider.Requests[1].Messages.Last().Role);
        Assert.Contains(_repository.Messages, message => message.Role == ChatRole.Tool &&
            message.Content.Contains("\"kind\":\"tool_call\"", StringComparison.Ordinal));
        Assert.Contains(_repository.Messages, message => message.Role == ChatRole.Tool &&
            message.Content.Contains("\"status\":\"Succeeded\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_MultipleRoundsExecuteInOrder()
    {
        var first = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        var second = CreateToolCall(
            "call-2",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "README.md" });
        _provider.Enqueue([new ChatChunk("", true, [first])]);
        _provider.Enqueue([new ChatChunk("", true, [second])]);
        _provider.Enqueue([new ChatChunk("Done.", true)]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ListDirectoryTool.Id,
            new ListDirectoryOutput(".", [], IsTruncated: false),
            "Listed directory.",
            TimeSpan.Zero));
        _runtime.Results.Enqueue(ToolResult.Success(
            ReadTextFileTool.Id,
            new ReadTextFileOutput("README.md", "hello", "utf-8", 5, false, false),
            "Read file.",
            TimeSpan.Zero));
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List then read")],
            CancellationToken.None));

        Assert.Equal(
            [ListDirectoryTool.Id, ReadTextFileTool.Id],
            _runtime.Invocations.Select(invocation => invocation.ToolId).ToArray());
        Assert.Contains(chunks, chunk => chunk.Content == "Done.");
        Assert.Equal(3, _provider.Requests.Count);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_MultipleCallsInOneResponseExecuteSequentially()
    {
        var first = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        var second = CreateToolCall(
            "call-2",
            SearchWorkspaceTextTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), query = "needle" });
        _provider.Enqueue([new ChatChunk("", true, [first, second])]);
        _provider.Enqueue([new ChatChunk("Done.", true)]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ListDirectoryTool.Id,
            new ListDirectoryOutput(".", [], IsTruncated: false),
            "Listed directory.",
            TimeSpan.Zero));
        _runtime.Results.Enqueue(ToolResult.Success(
            SearchWorkspaceTextTool.Id,
            new SearchWorkspaceTextOutput("needle", ".", [], false, 1, 0),
            "Searched workspace.",
            TimeSpan.Zero));
        var service = CreateService();

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List and search")],
            CancellationToken.None));

        Assert.Equal(
            [ListDirectoryTool.Id, SearchWorkspaceTextTool.Id],
            _runtime.Invocations.Select(invocation => invocation.ToolId).ToArray());
        var toolCallIds = _provider.Requests[1].Messages
                .Where(message => message.Role == ChatRole.Tool)
                .Select(message => message.ToolCallId)
                .ToArray();
        Assert.Collection(
            toolCallIds,
            first => Assert.Equal("call-1", first),
            second => Assert.Equal("call-2", second));
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_UnknownToolReturnsSafeResultWithoutRuntime()
    {
        var call = CreateToolCall("call-1", "workspace.file.write", new { path = "README.md" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("I could not use that tool.", true)]);
        var service = CreateService();

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Write a file")],
            CancellationToken.None));

        Assert.Empty(_runtime.Invocations);
        var toolMessage = _provider.Requests[1].Messages.Last();
        Assert.Equal(ChatRole.Tool, toolMessage.Role);
        Assert.Contains("tool_not_available", toolMessage.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_UnknownWorkspaceIdRejectedSafely()
    {
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = "missing-workspace", relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Could not list it.", true)]);
        var service = CreateService(CreateRuntime(new QueuePermissionBroker(
            PermissionDecision.AllowOnce)));

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List missing workspace")],
            CancellationToken.None));

        var content = _provider.Requests[1].Messages.Last().Content;
        Assert.Contains("workspace_not_found", content, StringComparison.Ordinal);
        Assert.DoesNotContain(_root, content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_RemovedWorkspaceIdRejectedSafely()
    {
        var otherRoot = Path.Combine(_root, "other");
        Directory.CreateDirectory(otherRoot);
        _ = _workspaceRegistry.Register(otherRoot, "Other workspace", "test");
        _workspaceRegistry.Remove(_workspace.Id);
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Could not list it.", true)]);
        var service = CreateService(CreateRuntime(new QueuePermissionBroker(
            PermissionDecision.AllowOnce)));

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List removed workspace")],
            CancellationToken.None));

        Assert.Contains(
            "workspace_not_found",
            _provider.Requests[1].Messages.Last().Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_UnavailableWorkspaceDoesNotEnableTools()
    {
        _workspaceRegistry.Remove(_workspace.Id);
        _provider.Enqueue([new ChatChunk("normal chat", true)]);
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List files")],
            CancellationToken.None));

        Assert.Equal("normal chat", Assert.Single(chunks).Content);
        Assert.Empty(Assert.Single(_provider.Requests).Tools);
        Assert.Empty(_runtime.Invocations);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_RelativeTraversalRejectedThroughRuntime()
    {
        var call = CreateToolCall(
            "call-1",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "../outside.txt" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("No traversal.", true)]);
        var service = CreateService(CreateRuntime(new QueuePermissionBroker(
            PermissionDecision.AllowOnce)));

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Read outside")],
            CancellationToken.None));

        Assert.Contains(
            "path_outside_workspace",
            _provider.Requests[1].Messages.Last().Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_ReparsePointRejectedThroughRuntimeWhenSupported()
    {
        var outside = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var link = Path.Combine(_root, "link.txt");

        try
        {
            File.WriteAllText(outside, "outside");
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is IOException ||
            exception is UnauthorizedAccessException ||
            exception is PlatformNotSupportedException)
        {
            return;
        }

        var call = CreateToolCall(
            "call-1",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "link.txt" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("No link.", true)]);
        var service = CreateService(CreateRuntime(new QueuePermissionBroker(
            PermissionDecision.AllowOnce)));

        try
        {
            await CollectAsync(service.StreamWithWorkspaceToolsAsync(
                Guid.NewGuid(),
                TaskId.NewId(),
                "qwen3",
                [new ChatMessage(ChatRole.User, "Read link")],
                CancellationToken.None));

            Assert.Contains(
                "reparse_point_rejected",
                _provider.Requests[1].Messages.Last().Content,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(link);
            File.Delete(outside);
        }
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_ApprovalForWorkspaceADoesNotAuthorizeWorkspaceB()
    {
        var otherRoot = Path.Combine(_root, "other");
        Directory.CreateDirectory(otherRoot);
        var other = _workspaceRegistry.Register(otherRoot, "Other workspace", "test");
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce);
        var service = CreateService(CreateRuntime(broker));
        var first = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        var second = CreateToolCall(
            "call-2",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = other.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [first, second])]);
        _provider.Enqueue([new ChatChunk("Done.", true)]);

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List both")],
            CancellationToken.None));

        Assert.Equal(2, broker.Requests.Count);
        Assert.NotEqual(
            broker.Requests[0].ResourceScope,
            broker.Requests[1].ResourceScope);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_ApprovalForOneResourceDoesNotAuthorizeBroaderResource()
    {
        File.WriteAllText(Path.Combine(_root, "a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "b.txt"), "b");
        var broker = new QueuePermissionBroker(
            PermissionDecision.AllowForTask,
            PermissionDecision.AllowOnce);
        var service = CreateService(CreateRuntime(broker));
        var first = CreateToolCall(
            "call-1",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "a.txt" });
        var second = CreateToolCall(
            "call-2",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "b.txt" });
        _provider.Enqueue([new ChatChunk("", true, [first, second])]);
        _provider.Enqueue([new ChatChunk("Done.", true)]);

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Read two files")],
            CancellationToken.None));

        Assert.Equal(2, broker.Requests.Count);
        Assert.NotEqual(
            broker.Requests[0].ResourceScope,
            broker.Requests[1].ResourceScope);
        Assert.EndsWith(":A.TXT", broker.Requests[0].ResourceScope);
        Assert.EndsWith(":B.TXT", broker.Requests[1].ResourceScope);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_MalformedArgumentsDoNotReachRuntime()
    {
        using var document = JsonDocument.Parse("[]");
        var call = new ChatToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            document.RootElement.Clone());
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Bad arguments.", true)]);
        var service = CreateService();

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List files")],
            CancellationToken.None));

        Assert.Empty(_runtime.Invocations);
        Assert.Contains(
            "invalid_tool_arguments",
            _provider.Requests[1].Messages.Last().Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_DeniedToolResultIsFedBackSafely()
    {
        var call = CreateToolCall(
            "call-1",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "secret.txt" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Permission was denied.", true)]);
        _runtime.Results.Enqueue(ToolResult.Failure(
            ReadTextFileTool.Id,
            ToolExecutionStatus.PermissionDenied,
            "Tool permission was denied.",
            TimeSpan.Zero,
            "permission_denied",
            "The action was not approved."));
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Read secret.txt")],
            CancellationToken.None));

        Assert.Contains(chunks, chunk => chunk.ActivityMessage == "Permission denied.");
        Assert.DoesNotContain(
            "Exception",
            _provider.Requests[1].Messages.Last().Content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_RuntimeExceptionIsNormalizedEverywhere()
    {
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Recovered.", true)]);
        _runtime.ExceptionToThrow = new InvalidOperationException(
            "raw secret framework path C:\\secret\\token.txt");
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List")],
            CancellationToken.None));

        var modelVisibleResult = _provider.Requests[1].Messages.Last().Content;
        var persisted = string.Join('\n', _repository.Messages.Select(message => message.Content));
        var activity = string.Join('\n', chunks.Select(chunk => chunk.ActivityMessage));
        Assert.Contains("tool_runtime_failed", modelVisibleResult, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", modelVisibleResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", persisted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", activity, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_TruncatesLargeResultsAsValidStructuredData()
    {
        var call = CreateToolCall(
            "call-1",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "large.txt" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Summarized.", true)]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ReadTextFileTool.Id,
            new ReadTextFileOutput(
                "large.txt",
                new string('x', 40_000),
                "utf-8",
                40_000,
                false,
                false),
            "Read file.",
            TimeSpan.Zero));
        var service = CreateService();

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Read large")],
            CancellationToken.None));

        using var document = JsonDocument.Parse(_provider.Requests[1].Messages.Last().Content);
        var root = document.RootElement;
        Assert.True(root.GetProperty("isTruncated").GetBoolean());
        var content = root.GetProperty("output").GetProperty("content").GetString();
        Assert.NotNull(content);
        Assert.True(content.Length <= 32_000);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_CancelledToolStopsTurn()
    {
        var call = CreateToolCall(
            "call-1",
            SearchWorkspaceTextTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), query = "needle" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _runtime.Results.Enqueue(ToolResult.Failure(
            SearchWorkspaceTextTool.Id,
            ToolExecutionStatus.Cancelled,
            "Tool invocation was cancelled.",
            TimeSpan.Zero,
            "cancelled",
            "The task was cancelled."));
        var service = CreateService();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await CollectAsync(service.StreamWithWorkspaceToolsAsync(
                Guid.NewGuid(),
                TaskId.NewId(),
                "qwen3",
                [new ChatMessage(ChatRole.User, "Search")],
                CancellationToken.None));
        });

        Assert.Single(_runtime.Invocations);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_CancellationAfterToolResultPreventsNextRound()
    {
        using var cancellation = new CancellationTokenSource();
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ListDirectoryTool.Id,
            new ListDirectoryOutput(".", [], IsTruncated: false),
            "Listed directory.",
            TimeSpan.Zero));
        _runtime.AfterInvoke = () => cancellation.Cancel();
        var service = CreateService();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await CollectAsync(service.StreamWithWorkspaceToolsAsync(
                Guid.NewGuid(),
                TaskId.NewId(),
                "qwen3",
                [new ChatMessage(ChatRole.User, "List")],
                cancellation.Token));
        });

        Assert.Single(_provider.Requests);
        Assert.Single(_runtime.Invocations);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_CancellationDuringPermissionDoesNotDisplayFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var broker = new CancellingPermissionBroker(cancellation);
        var service = CreateService(CreateRuntime(broker));
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await CollectAsync(service.StreamWithWorkspaceToolsAsync(
                Guid.NewGuid(),
                TaskId.NewId(),
                "qwen3",
                [new ChatMessage(ChatRole.User, "List")],
                cancellation.Token));
        });

        var persisted = string.Join('\n', _repository.Messages.Select(message => message.Content));
        Assert.DoesNotContain("failed", persisted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_StopsAtToolCallLimit()
    {
        var calls = Enumerable.Range(0, 9)
            .Select(index => CreateToolCall(
                $"call-{index}",
                ListDirectoryTool.Id.ToString(),
                new { workspaceId = _workspace.Id.ToString(), relativePath = "." }))
            .ToArray();
        _provider.Enqueue([new ChatChunk("", true, calls)]);
        for (var i = 0; i < 8; i++)
        {
            _runtime.Results.Enqueue(ToolResult.Success(
                ListDirectoryTool.Id,
                new ListDirectoryOutput(".", [], IsTruncated: false),
                "Listed directory.",
                TimeSpan.Zero));
        }

        var service = CreateService();
        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List")],
            CancellationToken.None));

        Assert.Equal(8, _runtime.Invocations.Count);
        Assert.Contains(chunks, chunk =>
            chunk.ActivityMessage == "Workspace tool-call limit reached for this response.");
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_StopsAtRoundLimit()
    {
        for (var i = 0; i < 4; i++)
        {
            _provider.Enqueue([
                new ChatChunk(
                    "",
                    true,
                    [
                        CreateToolCall(
                            $"call-{i}",
                            ListDirectoryTool.Id.ToString(),
                            new { workspaceId = _workspace.Id.ToString(), relativePath = "." })
                    ])
            ]);
            _runtime.Results.Enqueue(ToolResult.Success(
                ListDirectoryTool.Id,
                new ListDirectoryOutput(".", [], IsTruncated: false),
                "Listed directory.",
                TimeSpan.Zero));
        }

        var service = CreateService();
        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "Keep listing")],
            CancellationToken.None));

        Assert.Equal(4, _provider.Requests.Count);
        Assert.Contains(
            chunks,
            chunk => chunk.Content == "I stopped because the workspace tool round limit was reached.");
    }

    [Fact]
    public async Task StreamWithWorkspaceToolsAsync_FinalAssistantTextIsSeparateFromActivity()
    {
        var call = CreateToolCall(
            "call-1",
            ListDirectoryTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "." });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Final answer.", true)]);
        _runtime.Results.Enqueue(ToolResult.Success(
            ListDirectoryTool.Id,
            new ListDirectoryOutput(".", [], IsTruncated: false),
            "Listed directory.",
            TimeSpan.Zero));
        var service = CreateService();

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            Guid.NewGuid(),
            TaskId.NewId(),
            "qwen3",
            [new ChatMessage(ChatRole.User, "List")],
            CancellationToken.None));

        Assert.Contains(chunks, chunk => chunk.Content == "Final answer." &&
            string.IsNullOrWhiteSpace(chunk.ActivityMessage));
        Assert.Contains(chunks, chunk => string.IsNullOrEmpty(chunk.Content) &&
            !string.IsNullOrWhiteSpace(chunk.ActivityMessage));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private ConversationSessionService CreateService() =>
        CreateService(_runtime);

    private ConversationSessionService CreateService(ITypedToolRuntime runtime) =>
        new(
            _repository,
            new ChatSessionService(_provider),
            _toolRegistry,
            runtime,
            _workspaceRegistry);

    private TypedToolRuntime CreateRuntime(IPermissionBroker broker) =>
        new(_toolRegistry, new TaskEventBus(), broker);

    private static ChatToolCall CreateToolCall(
        string id,
        string name,
        object arguments)
    {
        var element = JsonSerializer.SerializeToElement(
            arguments,
            ChatToolJson.SerializerOptions);
        return new ChatToolCall(id, name, element);
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

    private sealed class FakeToolProvider : IToolCallingChatProvider
    {
        private readonly Queue<IReadOnlyList<ChatChunk>> _responses = [];

        public string ProviderName => "Fake";

        public List<ChatRequest> Requests { get; } = [];

        public void Enqueue(IReadOnlyList<ChatChunk> chunks) =>
            _responses.Enqueue(chunks);

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var chunks = _responses.Dequeue();

            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
        }
    }

    private sealed class FakeToolRuntime : ITypedToolRuntime
    {
        public Queue<ToolResult> Results { get; } = [];

        public List<ToolInvocation> Invocations { get; } = [];

        public Exception? ExceptionToThrow { get; set; }

        public Action? AfterInvoke { get; set; }

        public ValueTask<ToolResult> InvokeAsync(
            TaskId taskId,
            ToolInvocation invocation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Invocations.Add(invocation);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            var result = Results.Dequeue();
            AfterInvoke?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class QueuePermissionBroker(params PermissionDecision[] decisions)
        : IPermissionBroker
    {
        private readonly Queue<PermissionDecision> _decisions = new(decisions);

        public List<PermissionRequest> Requests { get; } = [];

        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            var decision = _decisions.Count == 0
                ? PermissionDecision.AllowOnce
                : _decisions.Dequeue();
            var response = decision switch
            {
                PermissionDecision.AllowForTask =>
                    PermissionResponse.AllowForTask(request),
                PermissionDecision.Deny =>
                    PermissionResponse.Deny(request, "Denied."),
                PermissionDecision.CancelTask =>
                    PermissionResponse.CancelTask(request, "Cancelled."),
                _ => PermissionResponse.AllowOnce(request)
            };

            return ValueTask.FromResult(response);
        }
    }

    private sealed class CancellingPermissionBroker(CancellationTokenSource cancellation)
        : IPermissionBroker
    {
        public ValueTask<PermissionResponse> RequestPermissionAsync(
            PermissionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(PermissionResponse.CancelTask(request));
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public List<StoredChatMessage> Messages { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Conversation>>([]);

        public Task<Conversation?> GetConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Conversation?>(null);

        public Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredChatMessage>>(Messages);

        public Task<Conversation> CreateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(conversation);

        public Task UpdateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<StoredChatMessage> AddMessageAsync(
            StoredChatMessage message,
            CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return Task.FromResult(message);
        }
    }
}
