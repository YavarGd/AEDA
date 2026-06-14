using System.Globalization;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Persistence;

namespace PersonalAI.Infrastructure.Workspaces;

public sealed class SqliteWorkspaceRepository : IWorkspaceRepository
{
    private readonly string _databasePath;

    public SqliteWorkspaceRepository(string databasePath)
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
                CREATE TABLE IF NOT EXISTS persisted_workspaces (
                    id TEXT NOT NULL PRIMARY KEY,
                    display_name TEXT NOT NULL,
                    canonical_root_path TEXT NOT NULL,
                    canonical_root_key TEXT NOT NULL,
                    source TEXT NOT NULL,
                    added_at_utc TEXT NOT NULL,
                    last_validated_at_utc TEXT NULL,
                    status TEXT NOT NULL,
                    safe_status_code TEXT NULL,
                    is_read_only INTEGER NOT NULL,
                    removed_at_utc TEXT NULL
                );
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE UNIQUE INDEX IF NOT EXISTS ux_persisted_workspaces_active_root
                ON persisted_workspaces (canonical_root_key)
                WHERE removed_at_utc IS NULL;
                """,
                cancellationToken);

            await ExecuteNonQueryAsync(
                connection,
                transaction,
                """
                CREATE INDEX IF NOT EXISTS ix_persisted_workspaces_status
                ON persisted_workspaces (status, removed_at_utc);
                """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
        bool includeRemoved = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,
                       display_name,
                       canonical_root_path,
                       source,
                       added_at_utc,
                       last_validated_at_utc,
                       status,
                       safe_status_code,
                       is_read_only,
                       removed_at_utc
                FROM persisted_workspaces
                WHERE $include_removed = 1 OR removed_at_utc IS NULL
                ORDER BY display_name COLLATE NOCASE ASC;
                """;
            command.Parameters.AddWithValue(
                "$include_removed",
                includeRemoved ? 1 : 0);

            return await ReadWorkspacesAsync(command, cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<PersistedWorkspace?> GetAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,
                       display_name,
                       canonical_root_path,
                       source,
                       added_at_utc,
                       last_validated_at_utc,
                       status,
                       safe_status_code,
                       is_read_only,
                       removed_at_utc
                FROM persisted_workspaces
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", workspaceId.ToString());

            var workspaces = await ReadWorkspacesAsync(command, cancellationToken);
            return workspaces.FirstOrDefault();
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<PersistedWorkspace?> FindActiveByCanonicalRootAsync(
        string canonicalRootPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id,
                       display_name,
                       canonical_root_path,
                       source,
                       added_at_utc,
                       last_validated_at_utc,
                       status,
                       safe_status_code,
                       is_read_only,
                       removed_at_utc
                FROM persisted_workspaces
                WHERE canonical_root_key = $canonical_root_key
                  AND removed_at_utc IS NULL
                LIMIT 1;
                """;
            command.Parameters.AddWithValue(
                "$canonical_root_key",
                CreateCanonicalRootKey(canonicalRootPath));

            var workspaces = await ReadWorkspacesAsync(command, cancellationToken);
            return workspaces.FirstOrDefault();
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task UpsertAsync(
        PersistedWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO persisted_workspaces (
                    id,
                    display_name,
                    canonical_root_path,
                    canonical_root_key,
                    source,
                    added_at_utc,
                    last_validated_at_utc,
                    status,
                    safe_status_code,
                    is_read_only,
                    removed_at_utc
                )
                VALUES (
                    $id,
                    $display_name,
                    $canonical_root_path,
                    $canonical_root_key,
                    $source,
                    $added_at_utc,
                    $last_validated_at_utc,
                    $status,
                    $safe_status_code,
                    $is_read_only,
                    $removed_at_utc
                )
                ON CONFLICT(id) DO UPDATE SET
                    display_name = excluded.display_name,
                    canonical_root_path = excluded.canonical_root_path,
                    canonical_root_key = excluded.canonical_root_key,
                    source = excluded.source,
                    added_at_utc = excluded.added_at_utc,
                    last_validated_at_utc = excluded.last_validated_at_utc,
                    status = excluded.status,
                    safe_status_code = excluded.safe_status_code,
                    is_read_only = excluded.is_read_only,
                    removed_at_utc = excluded.removed_at_utc;
                """;
            AddWorkspaceParameters(command, workspace);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            throw new WorkspaceAccessException(
                "workspace_duplicate",
                "Workspace root was already registered.");
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task RemoveAsync(
        WorkspaceId workspaceId,
        DateTimeOffset removedAtUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE persisted_workspaces
                SET status = $status,
                    removed_at_utc = $removed_at_utc,
                    safe_status_code = NULL
                WHERE id = $id;
                """;
            command.Parameters.AddWithValue("$id", workspaceId.ToString());
            command.Parameters.AddWithValue(
                "$status",
                WorkspaceRegistrationStatus.Removed.ToString());
            command.Parameters.AddWithValue(
                "$removed_at_utc",
                FormatUtc(removedAtUtc));

            // Removal is intentionally idempotent: an unknown id leaves no row changed.
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task<IReadOnlyList<PersistedWorkspace>> ReadWorkspacesAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var workspaces = new List<PersistedWorkspace>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            workspaces.Add(ReadWorkspace(reader));
        }

        return workspaces;
    }

    private static PersistedWorkspace ReadWorkspace(SqliteDataReader reader)
    {
        var status = Enum.TryParse<WorkspaceRegistrationStatus>(
            reader.GetString(6),
            out var parsedStatus)
            ? parsedStatus
            : WorkspaceRegistrationStatus.ValidationFailed;

        return new PersistedWorkspace(
            new WorkspaceId(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            ParseUtc(reader.GetString(4)),
            reader.IsDBNull(5) ? null : ParseUtc(reader.GetString(5)),
            status,
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt32(8) != 0,
            reader.IsDBNull(9) ? null : ParseUtc(reader.GetString(9)));
    }

    private static void AddWorkspaceParameters(
        SqliteCommand command,
        PersistedWorkspace workspace)
    {
        command.Parameters.AddWithValue("$id", workspace.Id.ToString());
        command.Parameters.AddWithValue("$display_name", workspace.DisplayName);
        command.Parameters.AddWithValue(
            "$canonical_root_path",
            workspace.CanonicalRootPath);
        command.Parameters.AddWithValue(
            "$canonical_root_key",
            CreateCanonicalRootKey(workspace.CanonicalRootPath));
        command.Parameters.AddWithValue("$source", workspace.Source);
        command.Parameters.AddWithValue(
            "$added_at_utc",
            FormatUtc(workspace.AddedAtUtc));
        command.Parameters.AddWithValue(
            "$last_validated_at_utc",
            workspace.LastValidatedAtUtc is null
                ? DBNull.Value
                : FormatUtc(workspace.LastValidatedAtUtc.Value));
        command.Parameters.AddWithValue("$status", workspace.Status.ToString());
        command.Parameters.AddWithValue(
            "$safe_status_code",
            workspace.SafeStatusCode is null
                ? DBNull.Value
                : workspace.SafeStatusCode);
        command.Parameters.AddWithValue("$is_read_only", workspace.IsReadOnly ? 1 : 0);
        command.Parameters.AddWithValue(
            "$removed_at_utc",
            workspace.RemovedAtUtc is null
                ? DBNull.Value
                : FormatUtc(workspace.RemovedAtUtc.Value));
    }

    private static string CreateCanonicalRootKey(string canonicalRootPath)
    {
        var normalized = WorkspaceRegistry.NormalizeCanonicalRoot(canonicalRootPath);
        return OperatingSystem.IsWindows()
            ? normalized.ToUpperInvariant()
            : normalized;
    }

    private static string FormatUtc(DateTimeOffset value)
    {
        return value.ToUniversalTime().ToString("O");
    }

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

        throw new WorkspaceRepositoryDataFormatException();
    }

    private static bool IsExpectedPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            WorkspaceRepositoryDataFormatException;

    private static WorkspaceAccessException PersistenceFailure() =>
        new(
            "workspace_persistence_failed",
            "Workspace registrations could not be saved.");

    private sealed class WorkspaceRepositoryDataFormatException : Exception
    {
    }
}
