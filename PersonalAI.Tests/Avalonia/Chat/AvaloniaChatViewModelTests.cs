using System.Runtime.CompilerServices;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.Avalonia.ViewModels.Chat;
using PersonalAI.Infrastructure.Chat;

namespace PersonalAI.Tests.Avalonia.Chat;

public sealed class AvaloniaChatViewModelTests
{
    [Fact]
    public async Task Initialize_Search_And_Open_LoadPersistedConversation()
    {
        var repository = new MemoryRepository();
        var conversation = await repository.CreateConversationAsync(new Conversation(
            Guid.NewGuid(), "Stored question", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ConversationStatus.Completed));
        await repository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), conversation.Id, ChatRole.User, "hello", DateTimeOffset.UtcNow));
        var viewModel = Create(repository, []);

        await viewModel.InitializeAsync();
        viewModel.SearchText = "stored";
        await viewModel.RefreshConversationsAsync();
        await viewModel.OpenConversationAsync(conversation.Id);

        Assert.Equal(conversation.Id, Assert.Single(viewModel.Conversations).Id);
        Assert.Equal("hello", Assert.Single(viewModel.Messages).Content);
        Assert.Equal(ChatStatus.Completed, viewModel.Status);
    }

    [Fact]
    public async Task Send_Streams_And_ReopensCompletedConversation()
    {
        var repository = new MemoryRepository();
        AvaloniaChatViewModel? viewModel = null;
        var provider = new TestProvider
        {
            Chunks = [new ChatChunk("First ", false), new ChatChunk("answer", true)],
            AfterChunk = () => Assert.Equal("First ", viewModel!.Messages.Last().Content)
        };
        viewModel = Create(repository, provider);
        viewModel.Draft = "Question";

        await viewModel.SendAsync();

        var conversation = Assert.Single(repository.Conversations);
        Assert.Equal(ConversationStatus.Completed, conversation.Status);
        Assert.Equal("First answer", Assert.Single(repository.Messages, message => message.Role == ChatRole.Assistant).Content);
        var reopened = Create(repository, []);
        await reopened.OpenConversationAsync(conversation.Id);
        Assert.Equal("First answer", reopened.Messages.Last().Content);
    }

    [Fact]
    public async Task Cancel_PersistsPartialAssistantResponse()
    {
        var repository = new MemoryRepository();
        var provider = new TestProvider { Wait = true, Chunks = [new ChatChunk("partial", false)] };
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        var send = viewModel.SendAsync();
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Cancel();
        await send;

        Assert.Equal(ConversationStatus.Cancelled, Assert.Single(repository.Conversations).Status);
        Assert.Equal("partial", Assert.Single(repository.Messages, message => message.Role == ChatRole.Assistant).Content);
        var reopened = Create(repository, []);
        await reopened.OpenConversationAsync(repository.Conversations.Single().Id);
        Assert.Equal(ChatStatus.Cancelled, reopened.Status);
    }

    [Fact]
    public async Task Failure_UsesSafeText_AndPersistsError()
    {
        var repository = new MemoryRepository();
        var viewModel = Create(repository, new TestProvider { Fail = true, Chunks = [new ChatChunk("partial", false)] });
        viewModel.Draft = "Question";

        await viewModel.SendAsync();

        Assert.Equal(AvaloniaChatViewModel.SafeFailureText, viewModel.StatusMessage);
        Assert.Equal(ConversationStatus.Error, Assert.Single(repository.Conversations).Status);
        Assert.Equal("partial", Assert.Single(repository.Messages, message => message.Role == ChatRole.Assistant).Content);
        var reopened = Create(repository, []);
        await reopened.OpenConversationAsync(repository.Conversations.Single().Id);
        Assert.Equal(ChatStatus.Failed, reopened.Status);
        Assert.Equal(AvaloniaChatViewModel.SafeFailureText, reopened.StatusMessage);
    }

    [Fact]
    public async Task SecondTurn_DoesNotSendOrphanPersistedToolMessages()
    {
        var repository = new MemoryRepository();
        var conversation = await repository.CreateConversationAsync(new Conversation(
            Guid.NewGuid(), "First", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ConversationStatus.Completed));
        await repository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), conversation.Id, ChatRole.User, "First", DateTimeOffset.UtcNow));
        await repository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), conversation.Id, ChatRole.Tool, "orphan", DateTimeOffset.UtcNow));
        await repository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), conversation.Id, ChatRole.Assistant, "Answer", DateTimeOffset.UtcNow));
        var provider = new TestProvider { Chunks = [new ChatChunk("Next", true)] };
        var viewModel = Create(repository, provider);
        await viewModel.OpenConversationAsync(conversation.Id);
        viewModel.Draft = "Second";

        await viewModel.SendAsync();

        Assert.DoesNotContain(provider.LastRequest!.Messages, message => message.Role == ChatRole.Tool);
    }

    [Fact]
    public async Task CompletionUpdateFailure_DoesNotDuplicateAssistantResponse()
    {
        var repository = new MemoryRepository { FailNextUpdate = true };
        var viewModel = Create(repository, [new ChatChunk("partial", true)]);
        viewModel.Draft = "Question";

        await viewModel.SendAsync();

        Assert.Equal(ConversationStatus.Error, Assert.Single(repository.Conversations).Status);
        Assert.Single(repository.Messages, message => message.Role == ChatRole.Assistant);
    }

    [Fact]
    public void Lifecycle_ShowsMainWindowAfterAsyncComposition_AndSecondaryExits()
    {
        var source = File.ReadAllText(Find("PersonalAI.Desktop.Avalonia", "App.axaml.cs"));
        var startInitialization = source.IndexOf("_ = InitializeCompositionAsync(desktop, window);", StringComparison.Ordinal);
        var create = source.IndexOf("AvaloniaAppComposition.CreateAsync", StringComparison.Ordinal);
        var assignWindow = source.IndexOf("desktop.MainWindow = window;", StringComparison.Ordinal);
        var showWindow = source.IndexOf("window.Show();", StringComparison.Ordinal);

        Assert.True(startInitialization >= 0);
        Assert.True(create >= 0 && create < assignWindow && assignWindow < showWindow);
        Assert.DoesNotContain("override async void OnFrameworkInitializationCompleted", source);
        Assert.DoesNotContain("WindowsSingleInstanceService", source);
    }

    private static AvaloniaChatViewModel Create(MemoryRepository repository, IReadOnlyList<ChatChunk> chunks) =>
        Create(repository, new TestProvider { Chunks = chunks });

    private static AvaloniaChatViewModel Create(MemoryRepository repository, TestProvider provider) =>
        new(new ConversationSessionService(repository, new ChatSessionService(provider)), new TestSettings(), action => action());

    private static string Find(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException();
    }

    private sealed class TestProvider : IChatProvider
    {
        public string ProviderName => "test";
        public IReadOnlyList<ChatChunk> Chunks { get; init; } = [];
        public bool Wait { get; init; }
        public bool Fail { get; init; }
        public Action? AfterChunk { get; init; }
        public ChatRequest? LastRequest { get; private set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            foreach (var chunk in Chunks)
            {
                yield return chunk;
                if (chunk.Content == "First ") AfterChunk?.Invoke();
            }

            Started.TrySetResult();
            if (Wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (Fail) throw new InvalidOperationException("not safe");
        }
    }

    private sealed class MemoryRepository : IConversationRepository
    {
        public List<Conversation> Conversations { get; } = [];
        public List<StoredChatMessage> Messages { get; } = [];
        public bool FailNextUpdate { get; set; }
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Conversation>>(Conversations);
        public Task<Conversation?> GetConversationAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Conversations.FirstOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<StoredChatMessage>>(Messages.Where(item => item.ConversationId == id).ToArray());
        public Task<Conversation> CreateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default) { Conversations.Add(conversation); return Task.FromResult(conversation); }
        public Task UpdateConversationAsync(Conversation conversation, CancellationToken cancellationToken = default)
        {
            if (FailNextUpdate)
            {
                FailNextUpdate = false;
                throw new IOException("update failed");
            }

            Conversations[Conversations.FindIndex(item => item.Id == conversation.Id)] = conversation;
            return Task.CompletedTask;
        }
        public Task<StoredChatMessage> AddMessageAsync(StoredChatMessage message, CancellationToken cancellationToken = default) { Messages.Add(message); return Task.FromResult(message); }
    }

    private sealed class TestSettings : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; } = ApplicationSettings.CreateDefault();
        public string SettingsPath => string.Empty;
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
