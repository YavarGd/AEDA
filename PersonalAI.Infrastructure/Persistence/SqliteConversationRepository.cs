using Microsoft.Data.Sqlite;
using PersonalAI.Core.Chat;

namespace PersonalAI.Infrastructure.Persistence;

public sealed class SqliteConversationRepository : IConversationRepository
{
    private readonly string _databasePath;

    public SqliteConversationRepository(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(
                "A database path is required.",
                nameof(databasePath));
        }

        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(_databasePath);

        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS conversations (
                id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                model TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                status TEXT NOT NULL
            );
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS messages (
                id TEXT NOT NULL PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                role TEXT NOT NULL,
                content TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                FOREIGN KEY (conversation_id)
                    REFERENCES conversations (id)
                    ON DELETE CASCADE
            );
            """,
            cancellationToken);

        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE INDEX IF NOT EXISTS ix_messages_conversation_created
            ON messages (conversation_id, created_at_utc);
            """,
            cancellationToken);
    }

    public async Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, model, created_at_utc, updated_at_utc, status
            FROM conversations
            ORDER BY updated_at_utc DESC;
            """;

        return await ReadConversationsAsync(command, cancellationToken);
    }

    public async Task<Conversation?> GetConversationAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, title, model, created_at_utc, updated_at_utc, status
            FROM conversations
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", conversationId.ToString());

        var conversations = await ReadConversationsAsync(command, cancellationToken);
        return conversations.FirstOrDefault();
    }

    public async Task<IReadOnlyList<StoredChatMessage>> ListMessagesAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, conversation_id, role, content, created_at_utc
            FROM messages
            WHERE conversation_id = $conversation_id
            ORDER BY created_at_utc ASC, rowid ASC;
            """;
        command.Parameters.AddWithValue("$conversation_id", conversationId.ToString());

        var messages = new List<StoredChatMessage>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            messages.Add(ReadMessage(reader));
        }

        return messages;
    }

    public async Task<Conversation> CreateConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO conversations (
                id,
                title,
                model,
                created_at_utc,
                updated_at_utc,
                status
            )
            VALUES (
                $id,
                $title,
                $model,
                $created_at_utc,
                $updated_at_utc,
                $status
            );
            """;
        AddConversationParameters(command, conversation);

        await command.ExecuteNonQueryAsync(cancellationToken);
        return conversation;
    }

    public async Task UpdateConversationAsync(
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE conversations
            SET title = $title,
                model = $model,
                created_at_utc = $created_at_utc,
                updated_at_utc = $updated_at_utc,
                status = $status
            WHERE id = $id;
            """;
        AddConversationParameters(command, conversation);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<StoredChatMessage> AddMessageAsync(
        StoredChatMessage message,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO messages (
                id,
                conversation_id,
                role,
                content,
                created_at_utc
            )
            VALUES (
                $id,
                $conversation_id,
                $role,
                $content,
                $created_at_utc
            );
            """;
        command.Parameters.AddWithValue("$id", message.Id.ToString());
        command.Parameters.AddWithValue(
            "$conversation_id",
            message.ConversationId.ToString());
        command.Parameters.AddWithValue("$role", message.Role.ToString());
        command.Parameters.AddWithValue("$content", message.Content);
        command.Parameters.AddWithValue(
            "$created_at_utc",
            FormatUtc(message.CreatedAtUtc));

        await command.ExecuteNonQueryAsync(cancellationToken);
        return message;
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();

        return new SqliteConnection(connectionString);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<Conversation>> ReadConversationsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var conversations = new List<Conversation>();

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            conversations.Add(ReadConversation(reader));
        }

        return conversations;
    }

    private static Conversation ReadConversation(SqliteDataReader reader)
    {
        return new Conversation(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            ParseUtc(reader.GetString(3)),
            ParseUtc(reader.GetString(4)),
            Enum.Parse<ConversationStatus>(reader.GetString(5)));
    }

    private static StoredChatMessage ReadMessage(SqliteDataReader reader)
    {
        return new StoredChatMessage(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            Enum.Parse<ChatRole>(reader.GetString(2)),
            reader.GetString(3),
            ParseUtc(reader.GetString(4)));
    }

    private static void AddConversationParameters(
        SqliteCommand command,
        Conversation conversation)
    {
        command.Parameters.AddWithValue("$id", conversation.Id.ToString());
        command.Parameters.AddWithValue("$title", conversation.Title);
        command.Parameters.AddWithValue("$model", conversation.Model);
        command.Parameters.AddWithValue(
            "$created_at_utc",
            FormatUtc(conversation.CreatedAtUtc));
        command.Parameters.AddWithValue(
            "$updated_at_utc",
            FormatUtc(conversation.UpdatedAtUtc));
        command.Parameters.AddWithValue("$status", conversation.Status.ToString());
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value).ToUniversalTime();
    }
}
