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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
