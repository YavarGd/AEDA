using PersonalAI.Core.Chat;
using PersonalAI.Infrastructure.Persistence;

namespace PersonalAI.Tests.Persistence;

public sealed class SqliteConversationRepositoryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString());
    private readonly string _databasePath;

    public SqliteConversationRepositoryTests()
    {
        _databasePath = Path.Combine(_directory, "personalai-test.db");
    }

    [Fact]
    public async Task InitializeAsync_CreatesDatabaseAndStoresConversationMessages()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(),
            "SQLite test",
            "gemma4",
            now,
            now,
            ConversationStatus.Active);

        await repository.CreateConversationAsync(conversation);
        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.User,
            "Hello",
            now.AddSeconds(1)));
        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Assistant,
            "Hi there",
            now.AddSeconds(2)));

        var conversations = await repository.ListConversationsAsync();
        var messages = await repository.ListMessagesAsync(conversation.Id);

        Assert.True(File.Exists(_databasePath));
        var storedConversation = Assert.Single(conversations);
        Assert.Equal(conversation.Id, storedConversation.Id);
        Assert.Equal("SQLite test", storedConversation.Title);

        Assert.Collection(
            messages,
            message =>
            {
                Assert.Equal(ChatRole.User, message.Role);
                Assert.Equal("Hello", message.Content);
            },
            message =>
            {
                Assert.Equal(ChatRole.Assistant, message.Role);
                Assert.Equal("Hi there", message.Content);
            });
    }

    [Fact]
    public async Task UpdateConversationAsync_UpdatesStatusModelAndTimestamp()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(
            Guid.NewGuid(),
            "Update test",
            "gemma4",
            now,
            now,
            ConversationStatus.Active);

        await repository.CreateConversationAsync(conversation);

        var updated = conversation with
        {
            Model = "llama3",
            UpdatedAtUtc = now.AddMinutes(1),
            Status = ConversationStatus.Cancelled
        };

        await repository.UpdateConversationAsync(updated);

        var stored = await repository.GetConversationAsync(conversation.Id);

        Assert.NotNull(stored);
        Assert.Equal("llama3", stored.Model);
        Assert.Equal(ConversationStatus.Cancelled, stored.Status);
        Assert.Equal(
            updated.UpdatedAtUtc.ToUniversalTime(),
            stored.UpdatedAtUtc.ToUniversalTime());
    }

    [Fact]
    public async Task ListConversationsAsync_OrdersByMostRecentlyUpdatedFirst()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var older = new Conversation(
            Guid.NewGuid(),
            "Older",
            "gemma4",
            now.AddMinutes(-10),
            now.AddMinutes(-5),
            ConversationStatus.Completed);
        var newer = new Conversation(
            Guid.NewGuid(),
            "Newer",
            "gemma4",
            now.AddMinutes(-10),
            now,
            ConversationStatus.Completed);

        await repository.CreateConversationAsync(older);
        await repository.CreateConversationAsync(newer);

        var conversations = await repository.ListConversationsAsync();

        Assert.Collection(
            conversations,
            conversation => Assert.Equal(newer.Id, conversation.Id),
            conversation => Assert.Equal(older.Id, conversation.Id));
    }

    [Fact]
    public async Task ListMessagesAsync_ReturnsOnlyMessagesForSelectedConversation()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();

        var now = DateTimeOffset.UtcNow;
        var firstConversation = new Conversation(
            Guid.NewGuid(),
            "First",
            "gemma4",
            now,
            now,
            ConversationStatus.Completed);
        var secondConversation = new Conversation(
            Guid.NewGuid(),
            "Second",
            "gemma4",
            now,
            now,
            ConversationStatus.Completed);

        await repository.CreateConversationAsync(firstConversation);
        await repository.CreateConversationAsync(secondConversation);
        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            firstConversation.Id,
            ChatRole.User,
            "First message",
            now.AddSeconds(1)));
        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            secondConversation.Id,
            ChatRole.User,
            "Second message",
            now.AddSeconds(1)));

        var messages = await repository.ListMessagesAsync(secondConversation.Id);

        var message = Assert.Single(messages);
        Assert.Equal(secondConversation.Id, message.ConversationId);
        Assert.Equal("Second message", message.Content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
