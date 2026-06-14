using PersonalAI.Core.Chat;
using PersonalAI.Infrastructure.Persistence;
using System.Text.Json;

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

    [Fact]
    public async Task ToolMessages_RoundTripProviderNeutralJson()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var conversation = await CreateConversationAsync(repository, now);
        var toolCallJson =
            """
            {"kind":"tool_call","toolCallId":"call-123","toolName":"workspace.directory.list","arguments":{"workspaceId":"ws1","relativePath":"."}}
            """;
        var toolResultJson =
            """
            {"toolCallId":"call-123","toolName":"workspace.directory.list","isSuccess":true,"status":"Succeeded","summary":"Listed directory.","safeErrorCode":null,"safeErrorMessage":null,"output":{"relativePath":".","entries":[]},"isTruncated":false}
            """;

        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Tool,
            toolCallJson,
            now.AddSeconds(1)));
        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Tool,
            toolResultJson,
            now.AddSeconds(2)));

        var messages = await repository.ListMessagesAsync(conversation.Id);

        Assert.Collection(
            messages,
            request =>
            {
                Assert.Equal(ChatRole.Tool, request.Role);
                using var document = JsonDocument.Parse(request.Content);
                Assert.Equal("call-123", document.RootElement.GetProperty("toolCallId").GetString());
                Assert.Equal("ws1", document.RootElement.GetProperty("arguments").GetProperty("workspaceId").GetString());
            },
            result =>
            {
                Assert.Equal(ChatRole.Tool, result.Role);
                using var document = JsonDocument.Parse(result.Content);
                Assert.Equal("call-123", document.RootElement.GetProperty("toolCallId").GetString());
                Assert.False(document.RootElement.GetProperty("isTruncated").GetBoolean());
            });
    }

    [Fact]
    public async Task ToolMessages_TruncationAndFutureFieldsRoundTrip()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var conversation = await CreateConversationAsync(repository, now);
        var content =
            """
            {"toolCallId":"call-456","toolName":"workspace.file.read_text","isSuccess":true,"status":"Succeeded","summary":"Read file.","safeErrorCode":null,"safeErrorMessage":null,"output":{"relativePath":"README.md","content":"preview"},"isTruncated":true,"futureField":{"ignored":true}}
            """;

        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Tool,
            content,
            now.AddSeconds(1)));

        var message = Assert.Single(await repository.ListMessagesAsync(conversation.Id));

        using var document = JsonDocument.Parse(message.Content);
        Assert.Equal("call-456", document.RootElement.GetProperty("toolCallId").GetString());
        Assert.True(document.RootElement.GetProperty("isTruncated").GetBoolean());
        Assert.True(document.RootElement.TryGetProperty("futureField", out _));
    }

    [Fact]
    public async Task ToolMessages_MalformedJsonLoadsAsStoredDataForSafeUiHandling()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var conversation = await CreateConversationAsync(repository, now);

        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Tool,
            "{malformed",
            now.AddSeconds(1)));

        var message = Assert.Single(await repository.ListMessagesAsync(conversation.Id));

        Assert.Equal(ChatRole.Tool, message.Role);
        Assert.Equal("{malformed", message.Content);
    }

    [Fact]
    public async Task ToolMessages_DoNotPersistRawRuntimeDetails()
    {
        var repository = new SqliteConversationRepository(_databasePath);
        await repository.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var conversation = await CreateConversationAsync(repository, now);
        var safeResult =
            """
            {"toolCallId":"call-789","toolName":"workspace.directory.list","isSuccess":false,"status":"ValidationFailed","summary":"Workspace tool execution failed unexpectedly.","safeErrorCode":"tool_runtime_failed","safeErrorMessage":"Workspace tool execution failed unexpectedly.","output":null,"isTruncated":false}
            """;

        await repository.AddMessageAsync(new StoredChatMessage(
            Guid.NewGuid(),
            conversation.Id,
            ChatRole.Tool,
            safeResult,
            now.AddSeconds(1)));

        var message = Assert.Single(await repository.ListMessagesAsync(conversation.Id));

        Assert.DoesNotContain("InvalidOperationException", message.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("permission", message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\", message.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Conversation> CreateConversationAsync(
        IConversationRepository repository,
        DateTimeOffset now)
    {
        var conversation = new Conversation(
            Guid.NewGuid(),
            "Tool persistence",
            "qwen3",
            now,
            now,
            ConversationStatus.Completed);

        return await repository.CreateConversationAsync(conversation);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
