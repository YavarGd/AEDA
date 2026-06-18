using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;

namespace PersonalAI.Infrastructure.Tasks;

public sealed class SqliteTaskEventStore : ITaskEventStore
{
    private const int DefaultRecentLimit = 50;
    private readonly string _databasePath;

    public SqliteTaskEventStore(string databasePath)
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
        try
        {
            var directory = Path.GetDirectoryName(_databasePath);
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
                CREATE TABLE IF NOT EXISTS task_runs (
                    id TEXT NOT NULL PRIMARY KEY,
                    title TEXT NOT NULL,
                    status TEXT NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    safe_error_code TEXT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS task_events (
                    insertion_order INTEGER PRIMARY KEY AUTOINCREMENT,
                    id TEXT NOT NULL UNIQUE,
                    task_id TEXT NOT NULL,
                    timestamp_utc TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    summary TEXT NOT NULL,
                    state TEXT NULL,
                    tool_id TEXT NULL,
                    progress_percent INTEGER NULL,
                    progress_label TEXT NULL,
                    safe_metadata_json TEXT NULL,
                    safe_error_code TEXT NULL,
                    safe_error_message TEXT NULL,
                    FOREIGN KEY (task_id)
                        REFERENCES task_runs (id)
                        ON DELETE CASCADE
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE TABLE IF NOT EXISTS task_artifacts (
                    insertion_order INTEGER PRIMARY KEY AUTOINCREMENT,
                    id TEXT NOT NULL UNIQUE,
                    task_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    kind TEXT NOT NULL,
                    safe_uri TEXT NULL,
                    created_at_utc TEXT NOT NULL,
                    safe_metadata_json TEXT NULL,
                    FOREIGN KEY (task_id)
                        REFERENCES task_runs (id)
                        ON DELETE CASCADE
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_task_events_task_timestamp
                ON task_events (task_id, timestamp_utc, insertion_order);
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_task_runs_updated
                ON task_runs (updated_at_utc DESC);
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask CreateTaskRunAsync(
        TaskRun taskRun,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskRun);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO task_runs (
                    id,
                    title,
                    status,
                    created_at_utc,
                    updated_at_utc,
                    safe_error_code
                )
                VALUES (
                    $id,
                    $title,
                    $status,
                    $created_at_utc,
                    $updated_at_utc,
                    $safe_error_code
                );
                """;
            AddTaskRunParameters(command, taskRun);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask UpdateTaskRunStatusAsync(
        TaskId taskId,
        TaskRunStatus status,
        DateTimeOffset updatedAtUtc,
        string? safeErrorCode = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE task_runs
                SET status = $status,
                    updated_at_utc = $updated_at_utc,
                    safe_error_code = $safe_error_code
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", taskId.ToString());
            command.Parameters.AddWithValue("$status", status.ToString());
            command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(updatedAtUtc));
            command.Parameters.AddWithValue(
                "$safe_error_code",
                safeErrorCode is null
                    ? DBNull.Value
                    : TaskEventMetadata.SanitizeErrorCode(safeErrorCode));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask AppendAsync(
        TaskEvent taskEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO task_events (
                    id,
                    task_id,
                    timestamp_utc,
                    kind,
                    summary,
                    state,
                    tool_id,
                    progress_percent,
                    progress_label,
                    safe_metadata_json,
                    safe_error_code,
                    safe_error_message
                )
                VALUES (
                    $id,
                    $task_id,
                    $timestamp_utc,
                    $kind,
                    $summary,
                    $state,
                    $tool_id,
                    $progress_percent,
                    $progress_label,
                    $safe_metadata_json,
                    $safe_error_code,
                    $safe_error_message
                );
                """;
            AddTaskEventParameters(command, taskEvent);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask AppendArtifactAsync(
        TaskId taskId,
        TaskArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT OR IGNORE INTO task_artifacts (
                    id,
                    task_id,
                    name,
                    kind,
                    safe_uri,
                    created_at_utc,
                    safe_metadata_json
                )
                VALUES (
                    $id,
                    $task_id,
                    $name,
                    $kind,
                    $safe_uri,
                    $created_at_utc,
                    $safe_metadata_json
                );
                """;
            command.Parameters.AddWithValue("$id", artifact.Id.ToString());
            command.Parameters.AddWithValue("$task_id", taskId.ToString());
            command.Parameters.AddWithValue("$name", artifact.Name);
            command.Parameters.AddWithValue("$kind", artifact.Kind);
            command.Parameters.AddWithValue(
                "$safe_uri",
                artifact.SafeUri is null ? DBNull.Value : artifact.SafeUri);
            command.Parameters.AddWithValue(
                "$created_at_utc",
                FormatUtc(artifact.CreatedAtUtc));
            command.Parameters.AddWithValue(
                "$safe_metadata_json",
                artifact.SafeMetadata is null
                    ? DBNull.Value
                    : JsonSerializer.Serialize(artifact.SafeMetadata));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask<IReadOnlyList<TaskRun>> ListRecentTaskRunsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            limit = DefaultRecentLimit;
        }

        limit = Math.Min(limit, 200);

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, title, status, created_at_utc, updated_at_utc, safe_error_code
                FROM task_runs
                ORDER BY updated_at_utc DESC, rowid DESC
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            return await ReadTaskRunsAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async ValueTask<TaskRunRecord?> GetTaskRunAsync(
        TaskId taskId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var runCommand = connection.CreateCommand();
            runCommand.CommandText =
                """
                SELECT id, title, status, created_at_utc, updated_at_utc, safe_error_code
                FROM task_runs
                WHERE id = $id;
                """;
            runCommand.Parameters.AddWithValue("$id", taskId.ToString());
            var runs = await ReadTaskRunsAsync(runCommand, cancellationToken);
            var run = runs.FirstOrDefault();
            if (run is null)
            {
                return null;
            }

            var events = await ReadEventsAsync(connection, taskId, cancellationToken);
            var artifacts = await ReadArtifactsAsync(connection, taskId, cancellationToken);
            return new TaskRunRecord(run, events, artifacts);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public ValueTask MarkCancelledAsync(
        TaskId taskId,
        TaskCancellationReason reason,
        DateTimeOffset cancelledAtUtc,
        CancellationToken cancellationToken = default) =>
        UpdateTaskRunStatusAsync(
            taskId,
            TaskRunStatus.Cancelled,
            cancelledAtUtc,
            reason.ToString().ToLowerInvariant(),
            cancellationToken);

    public ValueTask MarkFailedAsync(
        TaskId taskId,
        string safeErrorCode,
        DateTimeOffset failedAtUtc,
        CancellationToken cancellationToken = default) =>
        UpdateTaskRunStatusAsync(
            taskId,
            TaskRunStatus.Failed,
            failedAtUtc,
            safeErrorCode,
            cancellationToken);

    public async IAsyncEnumerable<TaskEvent> ReadAsync(
        TaskId taskId,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<TaskEvent> events;

        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            events = await ReadEventsAsync(connection, taskId, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }

        foreach (var taskEvent in events)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return taskEvent;
        }
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
        SqliteTransaction transaction,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<TaskRun>> ReadTaskRunsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var runs = new List<TaskRun>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(ReadTaskRun(reader));
        }

        return runs;
    }

    private static async Task<IReadOnlyList<TaskEvent>> ReadEventsAsync(
        SqliteConnection connection,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   task_id,
                   timestamp_utc,
                   kind,
                   summary,
                   state,
                   tool_id,
                   progress_percent,
                   progress_label,
                   safe_metadata_json,
                   safe_error_code,
                   safe_error_message
            FROM task_events
            WHERE task_id = $task_id
            ORDER BY timestamp_utc ASC, insertion_order ASC
            LIMIT 1000;
            """;
        command.Parameters.AddWithValue("$task_id", taskId.ToString());

        var events = new List<TaskEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(ReadTaskEvent(reader));
        }

        return events;
    }

    private static async Task<IReadOnlyList<TaskArtifact>> ReadArtifactsAsync(
        SqliteConnection connection,
        TaskId taskId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id,
                   name,
                   kind,
                   safe_uri,
                   created_at_utc,
                   safe_metadata_json
            FROM task_artifacts
            WHERE task_id = $task_id
            ORDER BY created_at_utc ASC, insertion_order ASC
            LIMIT 1000;
            """;
        command.Parameters.AddWithValue("$task_id", taskId.ToString());

        var artifacts = new List<TaskArtifact>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            artifacts.Add(new TaskArtifact(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                ParseUtc(reader.GetString(4)),
                reader.IsDBNull(5) ? null : ReadMetadata(reader.GetString(5))));
        }

        return artifacts;
    }

    private static TaskRun ReadTaskRun(SqliteDataReader reader)
    {
        if (!Enum.TryParse<TaskRunStatus>(reader.GetString(2), out var status))
        {
            status = TaskRunStatus.Failed;
        }

        return new TaskRun(
            TaskId.Parse(reader.GetString(0)),
            reader.GetString(1),
            status,
            ParseUtc(reader.GetString(3)),
            ParseUtc(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    private static TaskEvent ReadTaskEvent(SqliteDataReader reader)
    {
        if (!Enum.TryParse<TaskEventKind>(reader.GetString(3), out var kind))
        {
            kind = TaskEventKind.TaskFailed;
        }

        TaskExecutionState? state = null;
        if (!reader.IsDBNull(5) &&
            Enum.TryParse<TaskExecutionState>(reader.GetString(5), out var parsedState))
        {
            state = parsedState;
        }

        return TaskEvent.Rehydrate(
            Guid.Parse(reader.GetString(0)),
            TaskId.Parse(reader.GetString(1)),
            ParseUtc(reader.GetString(2)),
            kind,
            reader.GetString(4),
            state,
            reader.IsDBNull(6) ? null : new ToolId(reader.GetString(6)),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : ReadMetadata(reader.GetString(9)),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private static void AddTaskRunParameters(
        SqliteCommand command,
        TaskRun taskRun)
    {
        command.Parameters.AddWithValue("$id", taskRun.Id.ToString());
        command.Parameters.AddWithValue("$title", taskRun.Title);
        command.Parameters.AddWithValue("$status", taskRun.Status.ToString());
        command.Parameters.AddWithValue("$created_at_utc", FormatUtc(taskRun.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated_at_utc", FormatUtc(taskRun.UpdatedAtUtc));
        command.Parameters.AddWithValue(
            "$safe_error_code",
            taskRun.SafeErrorCode is null ? DBNull.Value : taskRun.SafeErrorCode);
    }

    private static void AddTaskEventParameters(
        SqliteCommand command,
        TaskEvent taskEvent)
    {
        command.Parameters.AddWithValue("$id", taskEvent.EventId.ToString());
        command.Parameters.AddWithValue("$task_id", taskEvent.TaskId.ToString());
        command.Parameters.AddWithValue("$timestamp_utc", FormatUtc(taskEvent.TimestampUtc));
        command.Parameters.AddWithValue("$kind", taskEvent.Kind.ToString());
        command.Parameters.AddWithValue("$summary", taskEvent.Summary);
        command.Parameters.AddWithValue(
            "$state",
            taskEvent.State is null ? DBNull.Value : taskEvent.State.ToString());
        command.Parameters.AddWithValue(
            "$tool_id",
            taskEvent.ToolId is null ? DBNull.Value : taskEvent.ToolId.ToString());
        command.Parameters.AddWithValue(
            "$progress_percent",
            taskEvent.ProgressPercent is null
                ? DBNull.Value
                : taskEvent.ProgressPercent.Value);
        command.Parameters.AddWithValue(
            "$progress_label",
            taskEvent.ProgressLabel is null ? DBNull.Value : taskEvent.ProgressLabel);
        command.Parameters.AddWithValue(
            "$safe_metadata_json",
            taskEvent.SafeMetadata is null
                ? DBNull.Value
                : JsonSerializer.Serialize(taskEvent.SafeMetadata));
        command.Parameters.AddWithValue(
            "$safe_error_code",
            taskEvent.SafeErrorCode is null ? DBNull.Value : taskEvent.SafeErrorCode);
        command.Parameters.AddWithValue(
            "$safe_error_message",
            taskEvent.SafeErrorMessage is null
                ? DBNull.Value
                : taskEvent.SafeErrorMessage);
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(string json)
    {
        var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ??
            throw new TaskRepositoryDataFormatException();
        return TaskEventMetadata.Normalize(metadata) ??
            throw new TaskRepositoryDataFormatException();
    }

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

        throw new TaskRepositoryDataFormatException();
    }

    private static bool IsExpectedPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            JsonException or
            TaskRepositoryDataFormatException;

    private static InvalidOperationException PersistenceFailure() =>
        new("Task runtime records could not be loaded or saved.");

    private sealed class TaskRepositoryDataFormatException : Exception
    {
    }
}
