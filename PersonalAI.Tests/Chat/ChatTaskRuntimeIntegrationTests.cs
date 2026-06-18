using System.Text.Json;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Tools.Workspace;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Infrastructure.Tasks;
using PersonalAI.Infrastructure.Tools;
using PersonalAI.Infrastructure.Tools.Workspace;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Chat;

public sealed class ChatTaskRuntimeIntegrationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "PersonalAI.ChatTaskTests",
        Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;
    private readonly WorkspaceRegistry _workspaceRegistry = new();
    private readonly TypedToolRegistry _toolRegistry = new();
    private readonly FakeConversationRepository _repository = new();
    private readonly FakeToolProvider _provider = new();
    private readonly WorkspaceDescriptor _workspace;

    public ChatTaskRuntimeIntegrationTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "README.md"), "hello needle");
        _databasePath = Path.Combine(_root, "tasks.db");
        _workspace = _workspaceRegistry.Register(_root, "Task workspace", "test");
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
    public async Task ChatTurn_CreatesDurableTaskAndCompletes()
    {
        var (service, store, _) = await CreateServiceAsync(
            new QueuePermissionBroker(PermissionDecision.AllowOnce));
        var conversationId = Guid.NewGuid();

        var taskId = await service.StartChatTaskAsync(
            conversationId,
            "List files in the workspace",
            "qwen3",
            CancellationToken.None);
        _provider.Enqueue([new ChatChunk("Done.", true)]);

        var chunks = await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            conversationId,
            taskId,
            "qwen3",
            [new ChatMessage(ChatRole.User, "List files in the workspace")],
            CancellationToken.None));
        await service.CompleteChatTaskAsync(
            taskId,
            string.Concat(chunks.Select(chunk => chunk.Content)),
            CancellationToken.None);

        var record = await store.GetTaskRunAsync(taskId);
        var latest = await store.GetLatestTaskRunForConversationAsync(conversationId);

        Assert.NotNull(record);
        Assert.Equal(TaskRunStatus.Completed, record.TaskRun.Status);
        Assert.Equal("chat", record.TaskRun.Source);
        Assert.Equal(conversationId, record.TaskRun.ConversationId);
        Assert.Equal("qwen3", record.TaskRun.Model);
        Assert.Equal("Fake", record.TaskRun.Provider);
        Assert.NotNull(latest);
        Assert.Equal(taskId, latest.Id);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.TaskStarted);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.MessageEmitted);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.TaskCompleted);
    }

    [Fact]
    public async Task WorkspaceToolFlow_PersistsPermissionToolEventsAndSafeMetadata()
    {
        var approvalStore = new InMemoryApprovalCheckpointStore();
        var broker = new QueuePermissionBroker(PermissionDecision.AllowForTask);
        var (service, store, _) = await CreateServiceAsync(broker, approvalStore);
        var conversationId = Guid.NewGuid();
        var taskId = await service.StartChatTaskAsync(
            conversationId,
            "Search for needle",
            "qwen3",
            CancellationToken.None);
        var call = CreateToolCall(
            "call-search",
            SearchWorkspaceTextTool.Id.ToString(),
            new
            {
                workspaceId = _workspace.Id.ToString(),
                query = "needle",
                relativeDirectory = "."
            });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Found it.", true)]);

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            conversationId,
            taskId,
            "qwen3",
            [new ChatMessage(ChatRole.User, "Search for needle")],
            CancellationToken.None));
        await service.CompleteChatTaskAsync(taskId, "Found it.", CancellationToken.None);

        var record = await store.GetTaskRunAsync(taskId);
        Assert.NotNull(record);
        Assert.Collection(
            record.Events.Where(item =>
                item.Kind is TaskEventKind.ToolRequested or
                    TaskEventKind.PermissionRequested or
                    TaskEventKind.PermissionGranted or
                    TaskEventKind.ToolStarted or
                    TaskEventKind.ToolCompleted),
            first => Assert.Equal(TaskEventKind.ToolRequested, first.Kind),
            second => Assert.Equal(TaskEventKind.PermissionRequested, second.Kind),
            third => Assert.Equal(TaskEventKind.PermissionGranted, third.Kind),
            fourth => Assert.Equal(TaskEventKind.ToolStarted, fourth.Kind),
            fifth =>
            {
                Assert.Equal(TaskEventKind.ToolCompleted, fifth.Kind);
                Assert.Equal("1", fifth.SafeMetadata!["resultCount"]);
                Assert.Equal("needle", fifth.SafeMetadata!["query"]);
            });
        Assert.DoesNotContain(record.Events, item =>
            item.Summary.Contains(_workspace.Id.ToString(), StringComparison.OrdinalIgnoreCase));

        var reusableDecision = await approvalStore.FindReusableDecisionAsync(
            new ApprovalScope(
                taskId,
                ApprovalKind.WorkspacePermission,
                broker.Requests.Single().ResourceScope!));
        Assert.NotNull(reusableDecision);
        Assert.True(reusableDecision.IsAllowed);
    }

    [Fact]
    public async Task PermissionDenied_IsControlledOutcomeAndTaskCanComplete()
    {
        var (service, store, _) = await CreateServiceAsync(
            new QueuePermissionBroker(PermissionDecision.Deny));
        var conversationId = Guid.NewGuid();
        var taskId = await service.StartChatTaskAsync(
            conversationId,
            "Read README",
            "qwen3",
            CancellationToken.None);
        var call = CreateToolCall(
            "call-read",
            ReadTextFileTool.Id.ToString(),
            new { workspaceId = _workspace.Id.ToString(), relativePath = "README.md" });
        _provider.Enqueue([new ChatChunk("", true, [call])]);
        _provider.Enqueue([new ChatChunk("Permission was denied.", true)]);

        await CollectAsync(service.StreamWithWorkspaceToolsAsync(
            conversationId,
            taskId,
            "qwen3",
            [new ChatMessage(ChatRole.User, "Read README")],
            CancellationToken.None));
        await service.CompleteChatTaskAsync(
            taskId,
            "Permission was denied.",
            CancellationToken.None);

        var record = await store.GetTaskRunAsync(taskId);
        Assert.NotNull(record);
        Assert.Equal(TaskRunStatus.Completed, record.TaskRun.Status);
        Assert.Contains(record.Events, item => item.Kind == TaskEventKind.PermissionDenied);
        Assert.DoesNotContain(record.Events, item => item.Kind == TaskEventKind.TaskFailed);
    }

    [Fact]
    public async Task Cancellation_MarksTaskCancelledWithoutDuplicateTerminalEvents()
    {
        var (service, store, _) = await CreateServiceAsync(
            new QueuePermissionBroker(PermissionDecision.AllowOnce));
        var conversationId = Guid.NewGuid();
        var taskId = await service.StartChatTaskAsync(
            conversationId,
            "Cancel me",
            "qwen3",
            CancellationToken.None);

        await service.CancelChatTaskAsync(taskId, CancellationToken.None);
        await service.FailChatTaskAsync(
            taskId,
            "should_not_duplicate",
            CancellationToken.None);

        var record = await store.GetTaskRunAsync(taskId);
        Assert.NotNull(record);
        Assert.Equal(TaskRunStatus.Cancelled, record.TaskRun.Status);
        Assert.Equal(1, record.Events.Count(item => item.Kind == TaskEventKind.TaskCancelled));
        Assert.DoesNotContain(record.Events, item => item.Kind == TaskEventKind.TaskFailed);
    }

    private async Task<(ConversationSessionService Service, SqliteTaskEventStore Store, TaskRuntime Runtime)>
        CreateServiceAsync(
            IPermissionBroker broker,
            IApprovalCheckpointStore? approvalStore = null)
    {
        var store = new SqliteTaskEventStore(_databasePath);
        await store.InitializeAsync();
        var bus = new DurableTaskEventBus(new TaskEventBus(), store);
        var runtime = new TaskRuntime(store, bus);
        var toolRuntime = new TypedToolRuntime(
            _toolRegistry,
            bus,
            broker,
            approvalCheckpointStore: approvalStore);
        var service = new ConversationSessionService(
            _repository,
            new ChatSessionService(_provider),
            _toolRegistry,
            toolRuntime,
            _workspaceRegistry,
            runtime);
        return (service, store, runtime);
    }

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

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FakeToolProvider : IToolCallingChatProvider
    {
        private readonly Queue<IReadOnlyList<ChatChunk>> _responses = [];

        public string ProviderName => "Fake";

        public void Enqueue(IReadOnlyList<ChatChunk> chunks) =>
            _responses.Enqueue(chunks);

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            var chunks = _responses.Dequeue();
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return chunk;
            }
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
            return ValueTask.FromResult(decision switch
            {
                PermissionDecision.AllowForTask => PermissionResponse.AllowForTask(request),
                PermissionDecision.Deny => PermissionResponse.Deny(request, "Denied."),
                PermissionDecision.CancelTask => PermissionResponse.CancelTask(request),
                _ => PermissionResponse.AllowOnce(request)
            });
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
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
            Task.FromResult<IReadOnlyList<StoredChatMessage>>([]);

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult(message);
    }
}
