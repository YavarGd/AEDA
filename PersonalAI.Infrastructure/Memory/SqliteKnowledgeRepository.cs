using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Memory;

public sealed class SqliteKnowledgeRepository(string databasePath) : IKnowledgeRepository
{
    private const int MaxLimit = 500;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var directory = Path.GetDirectoryName(databasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS knowledge_documents (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_type TEXT NOT NULL,
                    source_timestamp_utc TEXT NOT NULL,
                    workspace_id TEXT NULL,
                    relative_path TEXT NULL,
                    conversation_id TEXT NULL,
                    task_run_id TEXT NULL,
                    artifact_id TEXT NULL,
                    title TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    state TEXT NOT NULL,
                    safe_status_code TEXT NULL
                );
                """,
                cancellationToken);
            await AddColumnIfMissingAsync(connection, transaction, "knowledge_documents", "source_timestamp_utc", "TEXT NOT NULL DEFAULT '1970-01-01T00:00:00.0000000+00:00'", cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS knowledge_chunks (
                    id TEXT NOT NULL PRIMARY KEY,
                    document_id TEXT NOT NULL,
                    ordinal INTEGER NOT NULL,
                    text TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    FOREIGN KEY (document_id)
                        REFERENCES knowledge_documents (id)
                        ON DELETE CASCADE
                );
                """,
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "CREATE INDEX IF NOT EXISTS ix_knowledge_documents_workspace ON knowledge_documents (workspace_id, relative_path);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "CREATE INDEX IF NOT EXISTS ix_knowledge_chunks_document ON knowledge_chunks (document_id, ordinal);",
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task UpsertDocumentAsync(
        KnowledgeDocument document,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO knowledge_documents (
                        id, source_type, source_timestamp_utc, workspace_id, relative_path,
                        conversation_id, task_run_id, artifact_id, title, content_hash,
                        updated_at_utc, state, safe_status_code
                    )
                    VALUES (
                        $id, $source_type, $source_timestamp_utc, $workspace_id, $relative_path,
                        $conversation_id, $task_run_id, $artifact_id, $title, $content_hash,
                        $updated_at_utc, $state, $safe_status_code
                    )
                    ON CONFLICT(id) DO UPDATE SET
                        source_type = excluded.source_type,
                        source_timestamp_utc = excluded.source_timestamp_utc,
                        workspace_id = excluded.workspace_id,
                        relative_path = excluded.relative_path,
                        conversation_id = excluded.conversation_id,
                        task_run_id = excluded.task_run_id,
                        artifact_id = excluded.artifact_id,
                        title = excluded.title,
                        content_hash = excluded.content_hash,
                        updated_at_utc = excluded.updated_at_utc,
                        state = excluded.state,
                        safe_status_code = excluded.safe_status_code;
                    """;
                AddDocumentParameters(command, document);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM knowledge_chunks WHERE document_id = $document_id;";
                delete.Parameters.AddWithValue("$document_id", document.Id);
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var chunk in chunks.OrderBy(chunk => chunk.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO knowledge_chunks (
                        id, document_id, ordinal, text, content_hash, updated_at_utc
                    )
                    VALUES (
                        $id, $document_id, $ordinal, $text, $content_hash, $updated_at_utc
                    );
                    """;
                command.Parameters.AddWithValue("$id", chunk.Id);
                command.Parameters.AddWithValue("$document_id", chunk.DocumentId);
                command.Parameters.AddWithValue("$ordinal", chunk.Ordinal);
                command.Parameters.AddWithValue("$text", chunk.Text);
                command.Parameters.AddWithValue("$content_hash", chunk.ContentHash);
                command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(chunk.UpdatedAtUtc));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<KnowledgeDocument?> GetDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SelectDocumentsSql + " WHERE id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", documentId);
            return (await ReadDocumentsAsync(command, cancellationToken)).FirstOrDefault();
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> ListDocumentsAsync(
        string? workspaceId = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SelectDocumentsSql +
                (string.IsNullOrWhiteSpace(workspaceId) ? string.Empty : " WHERE workspace_id = $workspace_id") +
                " ORDER BY updated_at_utc DESC, id ASC LIMIT $limit;";
            command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                command.Parameters.AddWithValue("$workspace_id", workspaceId);
            }

            return await ReadDocumentsAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> ListChunksAsync(
        string documentId,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SelectChunksSql +
                " WHERE c.document_id = $document_id ORDER BY c.ordinal ASC LIMIT $limit;";
            command.Parameters.AddWithValue("$document_id", documentId);
            command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));
            return await ReadChunksAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<KnowledgeChunk>> SearchChunksAsync(
        string text,
        string? workspaceId = null,
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SelectChunksSql +
                " WHERE c.text LIKE $text" +
                (string.IsNullOrWhiteSpace(workspaceId) ? string.Empty : " AND d.workspace_id = $workspace_id") +
                " ORDER BY d.updated_at_utc DESC, c.ordinal ASC LIMIT $limit;";
            command.Parameters.AddWithValue("$text", $"%{text.Trim()}%");
            command.Parameters.AddWithValue("$limit", NormalizeLimit(limit));
            if (!string.IsNullOrWhiteSpace(workspaceId))
            {
                command.Parameters.AddWithValue("$workspace_id", workspaceId);
            }

            return await ReadChunksAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task DeleteDocumentAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM knowledge_documents WHERE id = $id;";
            command.Parameters.AddWithValue("$id", documentId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task ClearWorkspaceAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM knowledge_documents WHERE workspace_id = $workspace_id;";
            command.Parameters.AddWithValue("$workspace_id", workspaceId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    private const string SelectDocumentsSql =
        """
        SELECT id, source_type, source_timestamp_utc, workspace_id, relative_path,
               conversation_id, task_run_id, artifact_id, title, content_hash,
               updated_at_utc, state, safe_status_code
        FROM knowledge_documents
        """;

    private const string SelectChunksSql =
        """
        SELECT c.id, c.document_id, c.ordinal, c.text, c.content_hash, c.updated_at_utc,
               d.source_type, d.source_timestamp_utc, d.workspace_id, d.relative_path,
               d.conversation_id, d.task_run_id, d.artifact_id
        FROM knowledge_chunks c
        INNER JOIN knowledge_documents d ON d.id = c.document_id
        """;

    private static async Task<IReadOnlyList<KnowledgeDocument>> ReadDocumentsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var documents = new List<KnowledgeDocument>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(ReadDocument(reader));
        }

        return documents;
    }

    private static async Task<IReadOnlyList<KnowledgeChunk>> ReadChunksAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var chunks = new List<KnowledgeChunk>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var source = ReadSource(reader, offset: 6);
            chunks.Add(new KnowledgeChunk(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                source,
                ParseUtc(reader.GetString(5))));
        }

        return chunks;
    }

    private static KnowledgeDocument ReadDocument(SqliteDataReader reader)
    {
        var source = ReadSource(reader, offset: 1);
        return new KnowledgeDocument(
            reader.GetString(0),
            source,
            reader.GetString(8),
            reader.GetString(9),
            ParseUtc(reader.GetString(10)),
            ParseEnum<DocumentIndexState>(reader.GetString(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12));
    }

    private static KnowledgeSource ReadSource(SqliteDataReader reader, int offset)
    {
        return new KnowledgeSource(
            ParseEnum<KnowledgeSourceType>(reader.GetString(offset)),
            ParseUtc(reader.GetString(offset + 1)),
            reader.IsDBNull(offset + 2) ? null : new WorkspaceId(reader.GetString(offset + 2)),
            reader.IsDBNull(offset + 3) ? null : reader.GetString(offset + 3),
            reader.IsDBNull(offset + 4) ? null : Guid.Parse(reader.GetString(offset + 4)),
            reader.IsDBNull(offset + 5) ? null : TaskId.Parse(reader.GetString(offset + 5)),
            reader.IsDBNull(offset + 6) ? null : reader.GetString(offset + 6));
    }

    private static void AddDocumentParameters(SqliteCommand command, KnowledgeDocument document)
    {
        command.Parameters.AddWithValue("$id", document.Id);
        command.Parameters.AddWithValue("$source_type", document.Source.Type.ToString());
        command.Parameters.AddWithValue("$source_timestamp_utc", FormatUtc(document.Source.TimestampUtc));
        command.Parameters.AddWithValue("$workspace_id", document.Source.WorkspaceId is null ? DBNull.Value : document.Source.WorkspaceId.Value.ToString());
        command.Parameters.AddWithValue("$relative_path", document.Source.RelativePath is null ? DBNull.Value : document.Source.RelativePath);
        command.Parameters.AddWithValue("$conversation_id", document.Source.ConversationId is null ? DBNull.Value : document.Source.ConversationId.Value.ToString());
        command.Parameters.AddWithValue("$task_run_id", document.Source.TaskRunId is null ? DBNull.Value : document.Source.TaskRunId.Value.ToString());
        command.Parameters.AddWithValue("$artifact_id", document.Source.ArtifactId is null ? DBNull.Value : document.Source.ArtifactId);
        command.Parameters.AddWithValue("$title", document.Title);
        command.Parameters.AddWithValue("$content_hash", document.ContentHash);
        command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(document.UpdatedAtUtc));
        command.Parameters.AddWithValue("$state", document.State.ToString());
        command.Parameters.AddWithValue("$safe_status_code", document.SafeStatusCode is null ? DBNull.Value : document.SafeStatusCode);
    }

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AddColumnIfMissingAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        string columnName,
        string columnDefinition,
        CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.Transaction = transaction;
        pragma.CommandText = $"PRAGMA table_info({tableName});";
        var exists = false;
        await using (var reader = await pragma.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (!exists)
        {
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition};",
                cancellationToken);
        }
    }

    private static int NormalizeLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private static DateTimeOffset ParseUtc(string value)
    {
        if (DateTimeOffset.TryParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        throw new KnowledgeRepositoryDataFormatException();
    }

    private static T ParseEnum<T>(string value)
        where T : struct
    {
        if (Enum.TryParse<T>(value, out var parsed))
        {
            return parsed;
        }

        throw new KnowledgeRepositoryDataFormatException();
    }

    private static bool IsExpectedPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            KnowledgeRepositoryDataFormatException;

    private static InvalidOperationException PersistenceFailure() =>
        new("Knowledge records could not be loaded or saved.");

    private sealed class KnowledgeRepositoryDataFormatException : Exception
    {
    }
}
