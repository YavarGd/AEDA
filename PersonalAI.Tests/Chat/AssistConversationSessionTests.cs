using System.Runtime.CompilerServices;
using PersonalAI.Core.Chat;
using PersonalAI.Core.Settings;
using PersonalAI.Desktop.WinUI.Models;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Tests.Chat;

public sealed class AssistConversationSessionTests
{
    [Fact]
    public async Task ProductionHost_UsesSharedRoutingSessionAndOpensStoredConversation()
    {
        var repository = new FakeConversationRepository();
        var provider = new FakeProvider
        {
            Chunks = [new ChatChunk("Routed answer", true)]
        };
        var session = new ConversationSessionService(
            repository,
            new ChatSessionService(provider));
        Guid? openedConversation = null;
        var host = new AssistPillHost(
            session,
            new FakeSettingsService(),
            new DeterministicChatModelRouter(),
            _ => Task.FromResult(PersonalAI.Core.Providers.ProviderHealth.Available),
            _ => Task.FromResult<IReadOnlyList<string>>(["test-model"]),
            contextService: null!,
            getExplicitContext: () => null,
            new FakeClipboardWriter(),
            conversationId =>
            {
                openedConversation = conversationId;
                return Task.CompletedTask;
            });
        var streamed = string.Empty;

        var result = await host.GenerateAsync(
            "Route this",
            context: null,
            chunk => streamed += chunk,
            CancellationToken.None);
        await host.OpenInAedaAsync();

        Assert.Equal(ChatStatus.Completed, result.Status);
        Assert.Equal("test-model", provider.LastRequest?.Model);
        Assert.Equal("Routed answer", streamed);
        Assert.Equal(Assert.Single(repository.Conversations).Id, openedConversation);
    }

    [Fact]
    public async Task ProductionHost_ChecksHealthBeforeModelsAndReturnsOllamaRecovery()
    {
        var provider = new FakeProvider();
        var host = new AssistPillHost(
            new ConversationSessionService(
                new FakeConversationRepository(),
                new ChatSessionService(provider)),
            new FakeSettingsService(),
            new DeterministicChatModelRouter(),
            _ => Task.FromResult(new PersonalAI.Core.Providers.ProviderHealth(
                PersonalAI.Core.Providers.ProviderStatus.Unavailable)),
            _ => throw new InvalidOperationException("models must not be queried"),
            contextService: null!,
            getExplicitContext: () => null,
            clipboardWriter: new FakeClipboardWriter(),
            openConversationAsync: _ => Task.CompletedTask);

        var result = await host.GenerateAsync(
            "Explain this",
            context: null,
            _ => { },
            CancellationToken.None);

        Assert.Equal(ChatStatus.Failed, result.Status);
        Assert.Equal("Ollama is not running.", result.SafeErrorMessage);
        Assert.Null(provider.LastRequest);
    }

    [Fact]
    public async Task SharedSession_StreamsAndPersistsTheSameConversation()
    {
        var repository = new FakeConversationRepository();
        var provider = new FakeProvider
        {
            Chunks = [new ChatChunk("First ", false), new ChatChunk("answer", true)]
        };
        var service = new ConversationSessionService(
            repository,
            new ChatSessionService(provider));
        var streamed = new List<string>();

        var result = await service.GenerateNewConversationTurnAsync(
            "Explain this",
            "test-model",
            [new ChatMessage(ChatRole.User, "Explain this")],
            streamed.Add,
            CancellationToken.None);

        Assert.Equal(ChatStatus.Completed, result.Status);
        Assert.Equal("test-model", provider.LastRequest?.Model);
        Assert.Equal(["First ", "answer"], streamed);
        var conversation = Assert.Single(repository.Conversations);
        Assert.Equal(result.ConversationId, conversation.Id);
        Assert.Equal(ConversationStatus.Completed, conversation.Status);
        Assert.Collection(
            repository.Messages,
            message =>
            {
                Assert.Equal(ChatRole.User, message.Role);
                Assert.Equal("Explain this", message.Content);
            },
            message =>
            {
                Assert.Equal(ChatRole.Assistant, message.Role);
                Assert.Equal("First answer", message.Content);
            });
    }

    [Fact]
    public async Task AssistCancellation_DoesNotCancelConcurrentMainTurn()
    {
        var repository = new FakeConversationRepository();
        var provider = new FakeProvider { WaitForPrompt = "Assist wait" };
        var service = new ConversationSessionService(
            repository,
            new ChatSessionService(provider));
        using var assistCancellation = new CancellationTokenSource();

        var assist = service.GenerateNewConversationTurnAsync(
            "Assist wait",
            "test-model",
            [new ChatMessage(ChatRole.User, "Assist wait")],
            _ => { },
            assistCancellation.Token);
        await provider.WaitingStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        provider.Chunks = [new ChatChunk("Main answer", true)];
        var main = await service.GenerateNewConversationTurnAsync(
            "Main request",
            "test-model",
            [new ChatMessage(ChatRole.User, "Main request")],
            _ => { },
            CancellationToken.None);

        assistCancellation.Cancel();
        var cancelled = await assist;

        Assert.Equal(ChatStatus.Completed, main.Status);
        Assert.Equal(ChatStatus.Cancelled, cancelled.Status);
        Assert.Contains(repository.Conversations, conversation =>
            conversation.Id == main.ConversationId &&
            conversation.Status == ConversationStatus.Completed);
        Assert.Contains(repository.Conversations, conversation =>
            conversation.Id == cancelled.ConversationId &&
            conversation.Status == ConversationStatus.Cancelled);
    }

    [Fact]
    public async Task PersistenceFailureAfterVisibleChunk_ReturnsControlledFailure()
    {
        var repository = new FakeConversationRepository
        {
            FailAssistantPersistence = true
        };
        var provider = new FakeProvider
        {
            Chunks = [new ChatChunk("Visible partial", true)]
        };
        var service = new ConversationSessionService(
            repository,
            new ChatSessionService(provider));
        var streamed = string.Empty;

        var result = await service.GenerateNewConversationTurnAsync(
            "Request",
            "test-model",
            [new ChatMessage(ChatRole.User, "Request")],
            chunk => streamed += chunk,
            CancellationToken.None);

        Assert.Equal(ChatStatus.Failed, result.Status);
        Assert.Equal("Visible partial", streamed);
        Assert.Equal(
            ConversationStatus.Error,
            Assert.Single(repository.Conversations).Status);
    }

    private sealed class FakeProvider : IChatProvider
    {
        public string ProviderName => "fake";
        public IReadOnlyList<ChatChunk> Chunks { get; set; } = [];
        public string? WaitForPrompt { get; init; }
        public ChatRequest? LastRequest { get; private set; }
        public TaskCompletionSource WaitingStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async IAsyncEnumerable<ChatChunk> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            if (request.Messages.Any(message => message.Content == WaitForPrompt))
            {
                WaitingStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            foreach (var chunk in Chunks)
            {
                yield return chunk;
            }
        }
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        public List<Conversation> Conversations { get; } = [];
        public List<StoredChatMessage> Messages { get; } = [];
        public bool FailAssistantPersistence { get; init; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<Conversation>> ListConversationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Conversations);

        public Task<Conversation?> GetConversationAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Conversations.FirstOrDefault(item =>
                item.Id == conversationId));

        public Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(
            Guid conversationId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<StoredChatMessage>>(
                Messages.Where(message => message.ConversationId == conversationId).ToArray());

        public Task<Conversation> CreateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default)
        {
            Conversations.Add(conversation);
            return Task.FromResult(conversation);
        }

        public Task UpdateConversationAsync(
            Conversation conversation,
            CancellationToken cancellationToken = default)
        {
            var index = Conversations.FindIndex(item => item.Id == conversation.Id);
            Conversations[index] = conversation;
            return Task.CompletedTask;
        }

        public Task<StoredChatMessage> AddMessageAsync(
            StoredChatMessage message,
            CancellationToken cancellationToken = default)
        {
            if (FailAssistantPersistence && message.Role == ChatRole.Assistant)
            {
                throw new IOException("database unavailable");
            }

            Messages.Add(message);
            return Task.FromResult(message);
        }
    }

    private sealed class FakeSettingsService : IApplicationSettingsService
    {
        public ApplicationSettings Current { get; private set; } =
            ApplicationSettings.CreateDefault();

        public string SettingsPath => string.Empty;

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            ApplicationSettings settings,
            CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            Current = ApplicationSettings.CreateDefault();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClipboardWriter : IClipboardWriter
    {
        public Task CopyTextAsync(
            string text,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
