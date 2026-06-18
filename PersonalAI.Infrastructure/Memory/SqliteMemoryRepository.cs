using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Memory;

public sealed class SqliteMemoryRepository(string databasePath) : IMemoryRepository
{
    private const int MaxQueryLimit = 200;

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
                CREATE TABLE IF NOT EXISTS memories (
                    id TEXT NOT NULL PRIMARY KEY,
                    kind TEXT NOT NULL,
                    scope TEXT NOT NULL,
                    text TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    confidence TEXT NOT NULL,
                    visibility TEXT NOT NULL,
                    sensitivity TEXT NOT NULL,
                    retention_days INTEGER NULL,
                    retention_archive_on_expiry INTEGER NOT NULL,
                    project_id TEXT NULL,
                    workspace_id TEXT NULL,
                    conversation_id TEXT NULL,
                    task_run_id TEXT NULL,
                    metadata_json TEXT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS memory_sources (
                    memory_id TEXT NOT NULL PRIMARY KEY,
                    source_type TEXT NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    conversation_id TEXT NULL,
                    task_run_id TEXT NULL,
                    workspace_id TEXT NULL,
                    project_id TEXT NULL,
                    relative_file_path TEXT NULL,
                    document_id TEXT NULL,
                    chunk_id TEXT NULL,
                    excerpt TEXT NULL,
                    confidence TEXT NOT NULL,
                    FOREIGN KEY (memory_id)
                        REFERENCES memories (id)
                        ON DELETE CASCADE
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS knowledge_documents (
                    id TEXT NOT NULL PRIMARY KEY,
                    source_type TEXT NOT NULL,
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
                "CREATE INDEX IF NOT EXISTS ix_memories_scope_kind ON memories (scope, kind, visibility);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "CREATE INDEX IF NOT EXISTS ix_memories_workspace ON memories (workspace_id, updated_at_utc DESC);",
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                transaction,
                "CREATE INDEX IF NOT EXISTS ix_memory_text ON memories (text);",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<MemoryRecord> CreateAsync(
        MemoryRecord memory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateMemory(memory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    INSERT INTO memories (
                        id, kind, scope, text, created_at_utc, updated_at_utc,
                        confidence, visibility, sensitivity, retention_days,
                        retention_archive_on_expiry, project_id, workspace_id,
                        conversation_id, task_run_id, metadata_json
                    )
                    VALUES (
                        $id, $kind, $scope, $text, $created_at_utc, $updated_at_utc,
                        $confidence, $visibility, $sensitivity, $retention_days,
                        $retention_archive_on_expiry, $project_id, $workspace_id,
                        $conversation_id, $task_run_id, $metadata_json
                    );
                    """;
                AddMemoryParameters(command, memory);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpsertSourceAsync(connection, transaction, memory, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return memory;
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task UpdateAsync(
        MemoryRecord memory,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateMemory(memory);

            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var transaction =
                (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText =
                    """
                    UPDATE memories
                    SET kind = $kind,
                        scope = $scope,
                        text = $text,
                        created_at_utc = $created_at_utc,
                        updated_at_utc = $updated_at_utc,
                        confidence = $confidence,
                        visibility = $visibility,
                        sensitivity = $sensitivity,
                        retention_days = $retention_days,
                        retention_archive_on_expiry = $retention_archive_on_expiry,
                        project_id = $project_id,
                        workspace_id = $workspace_id,
                        conversation_id = $conversation_id,
                        task_run_id = $task_run_id,
                        metadata_json = $metadata_json
                    WHERE id = $id;
                    """;
                AddMemoryParameters(command, memory);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await UpsertSourceAsync(connection, transaction, memory, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task ArchiveAsync(
        MemoryId memoryId,
        DateTimeOffset archivedAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE memories
                SET visibility = $visibility,
                    updated_at_utc = $updated_at_utc
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", memoryId.ToString());
            command.Parameters.AddWithValue("$visibility", MemoryVisibility.Archived.ToString());
            command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(archivedAtUtc));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task DeleteAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM memories WHERE id = $id;";
            command.Parameters.AddWithValue("$id", memoryId.ToString());
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<MemoryRecord?> GetAsync(
        MemoryId memoryId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = SelectSql + " WHERE m.id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", memoryId.ToString());
            return (await ReadMemoriesAsync(command, cancellationToken)).FirstOrDefault();
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<MemoryRecord>> ListAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = BuildQuery(connection, query, includeTextSearch: false);
            return await ReadMemoriesAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<MemorySearchResult>> SearchAsync(
        MemorySearchQuery query,
        CancellationToken cancellationToken = default)
    {
        var memories = await ListAsync(query with { Limit = MaxQueryLimit }, cancellationToken);
        var terms = (query.Text ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return memories
            .Select(memory => new MemorySearchResult(
                memory,
                ScoreMemory(memory, terms),
                memory.Source))
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.Memory.UpdatedAtUtc)
            .Take(NormalizeLimit(query.Limit))
            .ToArray();
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

    private static readonly string SelectSql =
        """
        SELECT m.id, m.kind, m.scope, m.text, m.created_at_utc, m.updated_at_utc,
               m.confidence, m.visibility, m.sensitivity, m.retention_days,
               m.retention_archive_on_expiry, m.project_id, m.workspace_id,
               m.conversation_id, m.task_run_id, m.metadata_json,
               s.source_type, s.timestamp_utc, s.conversation_id, s.task_run_id,
               s.workspace_id, s.project_id, s.relative_file_path, s.document_id,
               s.chunk_id, s.excerpt, s.confidence
        FROM memories m
        INNER JOIN memory_sources s ON s.memory_id = m.id
        """;

    private static SqliteCommand BuildQuery(
        SqliteConnection connection,
        MemorySearchQuery query,
        bool includeTextSearch)
    {
        var command = connection.CreateCommand();
        var clauses = new List<string>();
        if (!query.IncludeArchived)
        {
            clauses.Add("m.visibility = $active");
            command.Parameters.AddWithValue("$active", MemoryVisibility.Active.ToString());
        }

        if (!query.IncludeSensitive)
        {
            clauses.Add("m.sensitivity = $normal");
            command.Parameters.AddWithValue("$normal", MemorySensitivity.Normal.ToString());
        }

        if (query.Scope is not null)
        {
            clauses.Add("m.scope = $scope");
            command.Parameters.AddWithValue("$scope", query.Scope.Value.ToString());
        }

        if (query.Kind is not null)
        {
            clauses.Add("m.kind = $kind");
            command.Parameters.AddWithValue("$kind", query.Kind.Value.ToString());
        }

        if (!string.IsNullOrWhiteSpace(query.ProjectId))
        {
            clauses.Add("m.project_id = $project_id");
            command.Parameters.AddWithValue("$project_id", query.ProjectId);
        }

        if (query.WorkspaceId is not null)
        {
            clauses.Add("m.workspace_id = $workspace_id");
            command.Parameters.AddWithValue("$workspace_id", query.WorkspaceId.Value.ToString());
        }

        if (query.ConversationId is not null)
        {
            clauses.Add("m.conversation_id = $conversation_id");
            command.Parameters.AddWithValue("$conversation_id", query.ConversationId.Value.ToString());
        }

        if (query.TaskRunId is not null)
        {
            clauses.Add("m.task_run_id = $task_run_id");
            command.Parameters.AddWithValue("$task_run_id", query.TaskRunId.Value.ToString());
        }

        if (includeTextSearch && !string.IsNullOrWhiteSpace(query.Text))
        {
            clauses.Add("(m.text LIKE $text OR s.excerpt LIKE $text)");
            command.Parameters.AddWithValue("$text", $"%{query.Text.Trim()}%");
        }

        command.CommandText = SelectSql +
            (clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses)) +
            " ORDER BY m.updated_at_utc DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", NormalizeLimit(query.Limit));
        return command;
    }

    private static async Task<IReadOnlyList<MemoryRecord>> ReadMemoriesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var memories = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            memories.Add(ReadMemory(reader));
        }

        return memories;
    }

    private static MemoryRecord ReadMemory(SqliteDataReader reader)
    {
        var source = new MemorySource(
            reader.GetString(16),
            ParseUtc(reader.GetString(17)),
            reader.IsDBNull(18) ? null : Guid.Parse(reader.GetString(18)),
            reader.IsDBNull(19) ? null : TaskId.Parse(reader.GetString(19)),
            reader.IsDBNull(20) ? null : new WorkspaceId(reader.GetString(20)),
            reader.IsDBNull(21) ? null : reader.GetString(21),
            reader.IsDBNull(22) ? null : reader.GetString(22),
            reader.IsDBNull(23) ? null : reader.GetString(23),
            reader.IsDBNull(24) ? null : reader.GetString(24),
            reader.IsDBNull(25) ? null : reader.GetString(25),
            ParseEnum<MemoryConfidence>(reader.GetString(26)));

        return new MemoryRecord(
            new MemoryId(reader.GetString(0)),
            ParseEnum<MemoryKind>(reader.GetString(1)),
            ParseEnum<MemoryScope>(reader.GetString(2)),
            reader.GetString(3),
            source,
            ParseUtc(reader.GetString(4)),
            ParseUtc(reader.GetString(5)),
            ParseEnum<MemoryConfidence>(reader.GetString(6)),
            ParseEnum<MemoryVisibility>(reader.GetString(7)),
            ParseEnum<MemorySensitivity>(reader.GetString(8)),
            new MemoryRetentionPolicy(
                reader.IsDBNull(9) ? null : reader.GetInt32(9),
                reader.GetInt32(10) != 0),
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.IsDBNull(12) ? null : new WorkspaceId(reader.GetString(12)),
            reader.IsDBNull(13) ? null : Guid.Parse(reader.GetString(13)),
            reader.IsDBNull(14) ? null : TaskId.Parse(reader.GetString(14)),
            reader.IsDBNull(15)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(15)));
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

    private static async Task UpsertSourceAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MemoryRecord memory,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO memory_sources (
                memory_id, source_type, timestamp_utc, conversation_id, task_run_id,
                workspace_id, project_id, relative_file_path, document_id, chunk_id,
                excerpt, confidence
            )
            VALUES (
                $memory_id, $source_type, $timestamp_utc, $source_conversation_id,
                $source_task_run_id, $source_workspace_id, $source_project_id,
                $relative_file_path, $document_id, $chunk_id, $excerpt, $source_confidence
            )
            ON CONFLICT(memory_id) DO UPDATE SET
                source_type = excluded.source_type,
                timestamp_utc = excluded.timestamp_utc,
                conversation_id = excluded.conversation_id,
                task_run_id = excluded.task_run_id,
                workspace_id = excluded.workspace_id,
                project_id = excluded.project_id,
                relative_file_path = excluded.relative_file_path,
                document_id = excluded.document_id,
                chunk_id = excluded.chunk_id,
                excerpt = excluded.excerpt,
                confidence = excluded.confidence;
            """;
        AddSourceParameters(command, memory);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddMemoryParameters(SqliteCommand command, MemoryRecord memory)
    {
        command.Parameters.AddWithValue("$id", memory.Id.ToString());
        command.Parameters.AddWithValue("$kind", memory.Kind.ToString());
        command.Parameters.AddWithValue("$scope", memory.Scope.ToString());
        command.Parameters.AddWithValue("$text", memory.Text);
        command.Parameters.AddWithValue("$created_at_utc", FormatUtc(memory.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(memory.UpdatedAtUtc));
        command.Parameters.AddWithValue("$confidence", memory.Confidence.ToString());
        command.Parameters.AddWithValue("$visibility", memory.Visibility.ToString());
        command.Parameters.AddWithValue("$sensitivity", memory.Sensitivity.ToString());
        command.Parameters.AddWithValue("$retention_days", memory.RetentionPolicy?.RetentionDays is null ? DBNull.Value : memory.RetentionPolicy.RetentionDays);
        command.Parameters.AddWithValue("$retention_archive_on_expiry", memory.RetentionPolicy?.ArchiveOnExpiry == true ? 1 : 0);
        command.Parameters.AddWithValue("$project_id", memory.ProjectId is null ? DBNull.Value : memory.ProjectId);
        command.Parameters.AddWithValue("$workspace_id", memory.WorkspaceId is null ? DBNull.Value : memory.WorkspaceId.Value.ToString());
        command.Parameters.AddWithValue("$conversation_id", memory.ConversationId is null ? DBNull.Value : memory.ConversationId.Value.ToString());
        command.Parameters.AddWithValue("$task_run_id", memory.TaskRunId is null ? DBNull.Value : memory.TaskRunId.Value.ToString());
        command.Parameters.AddWithValue("$metadata_json", memory.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(memory.Metadata));
    }

    private static void AddSourceParameters(SqliteCommand command, MemoryRecord memory)
    {
        var source = memory.Source;
        command.Parameters.AddWithValue("$memory_id", memory.Id.ToString());
        command.Parameters.AddWithValue("$source_type", source.SourceType);
        command.Parameters.AddWithValue("$timestamp_utc", FormatUtc(source.TimestampUtc));
        command.Parameters.AddWithValue("$source_conversation_id", source.ConversationId is null ? DBNull.Value : source.ConversationId.Value.ToString());
        command.Parameters.AddWithValue("$source_task_run_id", source.TaskRunId is null ? DBNull.Value : source.TaskRunId.Value.ToString());
        command.Parameters.AddWithValue("$source_workspace_id", source.WorkspaceId is null ? DBNull.Value : source.WorkspaceId.Value.ToString());
        command.Parameters.AddWithValue("$source_project_id", source.ProjectId is null ? DBNull.Value : source.ProjectId);
        command.Parameters.AddWithValue("$relative_file_path", source.RelativeFilePath is null ? DBNull.Value : source.RelativeFilePath);
        command.Parameters.AddWithValue("$document_id", source.DocumentId is null ? DBNull.Value : source.DocumentId);
        command.Parameters.AddWithValue("$chunk_id", source.ChunkId is null ? DBNull.Value : source.ChunkId);
        command.Parameters.AddWithValue("$excerpt", source.Excerpt is null ? DBNull.Value : Bound(source.Excerpt, MemorySource.MaxExcerptCharacters));
        command.Parameters.AddWithValue("$source_confidence", source.Confidence.ToString());
    }

    private static void ValidateMemory(MemoryRecord memory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(memory.Id.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(memory.Text);
        var sourceError = MemoryService.ValidateSource(memory.Source);
        if (sourceError is not null)
        {
            throw new MemoryRepositoryDataFormatException();
        }
    }

    private static double ScoreMemory(MemoryRecord memory, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return 1;
        }

        var score = 0d;
        foreach (var term in terms)
        {
            if (memory.Text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }

            if (memory.Source.Excerpt?.Contains(term, StringComparison.OrdinalIgnoreCase) == true)
            {
                score += 1;
            }
        }

        return score;
    }

    private static int NormalizeLimit(int limit) =>
        Math.Clamp(limit, 1, MaxQueryLimit);

    private static string Bound(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

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

        throw new MemoryRepositoryDataFormatException();
    }

    private static T ParseEnum<T>(string value)
        where T : struct
    {
        if (Enum.TryParse<T>(value, out var parsed))
        {
            return parsed;
        }

        throw new MemoryRepositoryDataFormatException();
    }

    private static bool IsExpectedPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            JsonException or
            MemoryRepositoryDataFormatException;

    private static InvalidOperationException PersistenceFailure() =>
        new("Memory records could not be loaded or saved.");

    private sealed class MemoryRepositoryDataFormatException : Exception
    {
    }
}
