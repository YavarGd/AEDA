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
    public async Task Retry_OnlyAvailable_WhenFailedOrCancelled()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("partial", true));
        provider.FailNext = true;
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";

        await viewModel.SendAsync();

        var assistant = viewModel.Messages.Single(message => message.Role == ChatRole.Assistant);
        Assert.Equal(ChatMessageStatus.Failed, assistant.Status);
        Assert.True(assistant.CanRetry);
        Assert.True(viewModel.RetryCommand.CanExecute(assistant));

        provider.Enqueue(new ChatChunk("recovered", true));
        await viewModel.RetryCommand.ExecuteAsync(assistant);

        Assert.Equal(ChatMessageStatus.Completed, assistant.Status);
        Assert.False(assistant.CanRetry);
    }

    [Fact]
    public async Task Retry_ResendsOriginalPrompt_ReplacesAssistantMessageInPlace_WithoutDuplicatingUserTurn()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("partial", true));
        provider.FailNext = true;
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Original question";

        await viewModel.SendAsync();

        var assistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);
        Assert.Equal(1, viewModel.Messages.Count(message => message.Role == ChatRole.Assistant));

        provider.Enqueue(new ChatChunk("Answered", true));
        await viewModel.RetryCommand.ExecuteAsync(assistant);

        // Same VM item updated in place - not a second assistant bubble.
        Assert.Equal(1, viewModel.Messages.Count(message => message.Role == ChatRole.Assistant));
        Assert.Same(assistant, viewModel.Messages.Single(message => message.Role == ChatRole.Assistant));
        Assert.Equal("Answered", assistant.Content);

        // The resent request carried the original prompt as the latest user turn.
        var lastRequest = provider.Requests[^1];
        Assert.Equal("Original question", lastRequest.Messages[^1].Content);
        Assert.Equal(ChatRole.User, lastRequest.Messages[^1].Role);

        // No duplicate user turn, either in the live timeline or in persisted storage.
        Assert.Equal(1, viewModel.Messages.Count(message => message.Role == ChatRole.User));
        Assert.Equal(1, repository.Messages.Count(message => message.Role == ChatRole.User));
    }

    [Fact]
    public async Task Regenerate_OnlyAvailable_OnLatestCompletedAssistantMessage()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";

        await viewModel.SendAsync();

        var firstAssistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);
        Assert.True(firstAssistant.CanRegenerate);
        Assert.True(viewModel.RegenerateCommand.CanExecute(firstAssistant));
        Assert.False(firstAssistant.CanRetry);
    }

    [Fact]
    public async Task Regenerate_AppendsNewAssistantTurn_WithoutDuplicatingUserHistory()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();

        var firstAssistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);

        provider.Enqueue(new ChatChunk("Second answer", true));
        await viewModel.RegenerateCommand.ExecuteAsync(firstAssistant);

        var assistantMessages = viewModel.Messages.Where(message => message.Role == ChatRole.Assistant).ToArray();
        Assert.Equal(2, assistantMessages.Length);
        Assert.Same(firstAssistant, assistantMessages[0]);
        Assert.Equal("First answer", assistantMessages[0].Content);
        Assert.Equal("Second answer", assistantMessages[1].Content);
        Assert.Equal(ChatMessageStatus.Regenerated, assistantMessages[1].Status);

        // The old message is no longer the latest, so it can no longer be regenerated, and the
        // new Regenerated message is not itself a Completed message so it cannot either.
        Assert.False(assistantMessages[0].CanRegenerate);
        Assert.False(assistantMessages[1].CanRegenerate);

        // Only the single original user turn is on record - no duplicate for the regenerate.
        Assert.Equal(1, viewModel.Messages.Count(message => message.Role == ChatRole.User));
        Assert.Equal(1, repository.Messages.Count(message => message.Role == ChatRole.User));

        var lastRequest = provider.Requests[^1];
        Assert.Equal("Question", lastRequest.Messages[^1].Content);
    }

    [Fact]
    public async Task Retry_And_Regenerate_KeepTheSameConversationSelected()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();

        var conversationId = viewModel.ActiveConversation!.Id;
        var assistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);

        provider.Enqueue(new ChatChunk("Regenerated answer", true));
        await viewModel.RegenerateCommand.ExecuteAsync(assistant);

        Assert.Equal(conversationId, viewModel.ActiveConversation!.Id);
        Assert.Single(repository.Conversations);
    }

    [Fact]
    public async Task RetryAndRegenerate_AreDisabled_WhileAGenerationIsInFlight()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();
        var assistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);
        Assert.True(viewModel.RegenerateCommand.CanExecute(assistant));

        provider.Enqueue(new ChatChunk("still going", false));
        provider.WaitNext = true;
        var regenerate = viewModel.RegenerateCommand.ExecuteAsync(assistant);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(viewModel.IsGenerating);
        Assert.False(viewModel.RegenerateCommand.CanExecute(assistant));
        Assert.False(viewModel.RetryCommand.CanExecute(assistant));
        Assert.False(viewModel.SendCommand.CanExecute(null));

        viewModel.Cancel();
        await regenerate;
    }

    [Fact]
    public async Task Cancel_DuringRetry_PersistsPartialResponse_AndReenablesRetry()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("original failure", true));
        provider.FailNext = true;
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();
        var assistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);
        Assert.True(assistant.CanRetry);

        provider.Enqueue(new ChatChunk("partial retry", false));
        provider.WaitNext = true;
        var retry = viewModel.RetryCommand.ExecuteAsync(assistant);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Cancel();
        await retry;

        Assert.Equal(ChatMessageStatus.Cancelled, assistant.Status);
        Assert.True(assistant.CanRetry);
        Assert.Equal(ConversationStatus.Cancelled, Assert.Single(repository.Conversations).Status);
        Assert.False(viewModel.IsGenerating);
    }

    [Fact]
    public async Task Cancel_DuringRegenerate_PersistsPartialResponse_OnTheNewMessageOnly()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();
        var firstAssistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);

        provider.Enqueue(new ChatChunk("partial regen", false));
        provider.WaitNext = true;
        var regenerate = viewModel.RegenerateCommand.ExecuteAsync(firstAssistant);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        viewModel.Cancel();
        await regenerate;

        Assert.Equal(ChatMessageStatus.Completed, firstAssistant.Status);
        Assert.Equal("First answer", firstAssistant.Content);
        var newAssistant = viewModel.Messages.Last();
        Assert.Equal(ChatRole.Assistant, newAssistant.Role);
        Assert.Equal(ChatMessageStatus.Cancelled, newAssistant.Status);
        Assert.Equal("partial regen", newAssistant.Content);
        Assert.Equal(ConversationStatus.Cancelled, Assert.Single(repository.Conversations).Status);
    }

    [Fact]
    public async Task NavigatingAwayDuringGeneration_IsIgnored_SoOutputAppliesToTheOriginalConversation()
    {
        var repository = new MemoryRepository();
        var provider = new MultiTurnProvider();
        provider.Enqueue(new ChatChunk("First answer", true));
        var viewModel = Create(repository, provider);
        viewModel.Draft = "Question";
        await viewModel.SendAsync();
        var conversationId = viewModel.ActiveConversation!.Id;
        var assistant = Assert.Single(viewModel.Messages, message => message.Role == ChatRole.Assistant);

        provider.Enqueue(new ChatChunk("regenerated", false));
        provider.WaitNext = true;
        var regenerate = viewModel.RegenerateCommand.ExecuteAsync(assistant);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

        // Both navigation actions are gated on IsGenerating, so neither should apply while the
        // regenerate is in flight.
        viewModel.NewChat();
        await viewModel.OpenConversationAsync(conversationId);
        Assert.Equal(conversationId, viewModel.ActiveConversation!.Id);
        Assert.Equal(2, viewModel.Messages.Count(message => message.Role == ChatRole.Assistant));

        viewModel.Cancel();
        await regenerate;

        Assert.Equal(conversationId, viewModel.ActiveConversation!.Id);
    }

    [Fact]
    public async Task ReopenedConversation_RestoresRetryAndRegenerateEligibility()
    {
        var failedRepository = new MemoryRepository();
        var failedConversation = await failedRepository.CreateConversationAsync(new Conversation(
            Guid.NewGuid(), "Failed turn", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ConversationStatus.Error));
        await failedRepository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), failedConversation.Id, ChatRole.User, "Question", DateTimeOffset.UtcNow));
        await failedRepository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), failedConversation.Id, ChatRole.Assistant, "partial", DateTimeOffset.UtcNow));
        var reopenedFailed = Create(failedRepository, []);
        await reopenedFailed.OpenConversationAsync(failedConversation.Id);
        var reopenedFailedAssistant = Assert.Single(reopenedFailed.Messages, message => message.Role == ChatRole.Assistant);
        Assert.True(reopenedFailedAssistant.CanRetry);
        Assert.False(reopenedFailedAssistant.CanRegenerate);

        var completedRepository = new MemoryRepository();
        var completedConversation = await completedRepository.CreateConversationAsync(new Conversation(
            Guid.NewGuid(), "Completed turn", "test", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ConversationStatus.Completed));
        await completedRepository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), completedConversation.Id, ChatRole.User, "Question", DateTimeOffset.UtcNow));
        await completedRepository.AddMessageAsync(new StoredChatMessage(Guid.NewGuid(), completedConversation.Id, ChatRole.Assistant, "Answer", DateTimeOffset.UtcNow));
        var reopenedCompleted = Create(completedRepository, []);
        await reopenedCompleted.OpenConversationAsync(completedConversation.Id);
        var reopenedCompletedAssistant = Assert.Single(reopenedCompleted.Messages, message => message.Role == ChatRole.Assistant);
        Assert.False(reopenedCompletedAssistant.CanRetry);
        Assert.True(reopenedCompletedAssistant.CanRegenerate);
    }

    [Fact]
    public void ChatView_ExposesRetryAndRegenerateButtons_WithAccessibleNames()
    {
        var source = File.ReadAllText(Find("PersonalAI.Desktop.Avalonia", "Views", "Chat", "ChatView.axaml"));

        Assert.Contains("AutomationProperties.Name=\"Retry message\"", source);
        Assert.Contains("AutomationProperties.Name=\"Regenerate response\"", source);
        Assert.Contains("Command=\"{Binding DataContext.RetryCommand, ElementName=MessagesItemsControl}\"", source);
        Assert.Contains("Command=\"{Binding DataContext.RegenerateCommand, ElementName=MessagesItemsControl}\"", source);
        Assert.Contains("IsVisible=\"{Binding CanRetry}\"", source);
        Assert.Contains("IsVisible=\"{Binding CanRegenerate}\"", source);
    }

    [Fact]
    public void ComposerKeyboardBehavior_IsUnchangedByRetryRegenerate()
    {
        Assert.Equal(
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyAction.Send,
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyboardPolicy.ResolveComposerKey(
                global::Avalonia.Input.Key.Enter, global::Avalonia.Input.KeyModifiers.None, isGenerating: false));

        Assert.Equal(
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyAction.InsertNewline,
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyboardPolicy.ResolveComposerKey(
                global::Avalonia.Input.Key.Enter, global::Avalonia.Input.KeyModifiers.Shift, isGenerating: false));

        Assert.Equal(
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyAction.Cancel,
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyboardPolicy.ResolveComposerKey(
                global::Avalonia.Input.Key.Escape, global::Avalonia.Input.KeyModifiers.None, isGenerating: true));

        Assert.Equal(
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyAction.None,
            PersonalAI.Desktop.Avalonia.Views.Chat.ChatKeyboardPolicy.ResolveComposerKey(
                global::Avalonia.Input.Key.Escape, global::Avalonia.Input.KeyModifiers.None, isGenerating: false));
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

    private static AvaloniaChatViewModel Create(MemoryRepository repository, MultiTurnProvider provider) =>
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

    /// <summary>
    /// A chat provider whose response can be reconfigured per StreamAsync call, so a single
    /// test can drive an initial Send followed by a Retry or Regenerate with a different
    /// scripted response. Each call's request is recorded for assertions on the resent prompt
    /// and history shape.
    /// </summary>
    private sealed class MultiTurnProvider : IChatProvider
    {
        public string ProviderName => "test";
        public List<ChatRequest> Requests { get; } = [];
        public bool FailNext { get; set; }
        public bool WaitNext { get; set; }
        public TaskCompletionSource Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly Queue<IReadOnlyList<ChatChunk>> _responses = new();

        public void Enqueue(params ChatChunk[] chunks) => _responses.Enqueue(chunks);

        public async IAsyncEnumerable<ChatChunk> StreamAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var chunks = _responses.Count > 0 ? _responses.Dequeue() : [];
            var wait = WaitNext;
            var fail = FailNext;
            WaitNext = false;
            FailNext = false;

            foreach (var chunk in chunks)
            {
                yield return chunk;
            }

            Started.TrySetResult();
            if (wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            if (fail) throw new InvalidOperationException("not safe");
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
