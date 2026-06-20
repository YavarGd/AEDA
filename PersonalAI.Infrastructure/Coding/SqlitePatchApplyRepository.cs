using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class SqlitePatchApplyRepository(string databasePath)
    : IPatchApplyRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS patch_apply_results (
                id TEXT NOT NULL PRIMARY KEY,
                proposal_id TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                status TEXT NOT NULL,
                files_json TEXT NOT NULL,
                failures_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS patch_apply_backups (
                apply_result_id TEXT NOT NULL,
                proposal_id TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                original_content TEXT NOT NULL,
                original_hash TEXT NOT NULL,
                applied_hash TEXT NOT NULL,
                operation_kind TEXT NOT NULL,
                encoding_name TEXT NOT NULL,
                created_at_utc TEXT NOT NULL
            );
            """, cancellationToken);
        await ExecuteAsync(connection, """
            CREATE TABLE IF NOT EXISTS patch_rollback_results (
                id TEXT NOT NULL PRIMARY KEY,
                apply_result_id TEXT NOT NULL,
                workspace_id TEXT NOT NULL,
                status TEXT NOT NULL,
                files_json TEXT NOT NULL,
                failures_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken);
    }

    public async Task CreateApplyResultAsync(
        PatchApplyResult result,
        IReadOnlyList<PatchApplyBackup> backups,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await InsertApplyResultAsync(connection, transaction, result, cancellationToken);
        foreach (var backup in backups)
        {
            await InsertBackupAsync(connection, transaction, backup, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PatchApplyResult?> GetApplyResultAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM patch_apply_results WHERE id = $id LIMIT 1;";
        Add(command, "$id", resultId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadApplyResult(reader)
            : null;
    }

    public async Task<IReadOnlyList<PatchApplyResult>> ListRecentApplyResultsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM patch_apply_results ORDER BY updated_at_utc DESC LIMIT $limit;";
        Add(command, "$limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var results = new List<PatchApplyResult>();
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(ReadApplyResult(reader));
        }

        return results;
    }

    public async Task<IReadOnlyList<PatchApplyBackup>> ListBackupsAsync(
        PatchApplyResultId resultId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM patch_apply_backups WHERE apply_result_id = $id;";
        Add(command, "$id", resultId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var backups = new List<PatchApplyBackup>();
        while (await reader.ReadAsync(cancellationToken))
        {
            backups.Add(ReadBackup(reader));
        }

        return backups;
    }

    public async Task CreateRollbackResultAsync(
        PatchRollbackResult result,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO patch_rollback_results (
                id, apply_result_id, workspace_id, status, files_json, failures_json,
                created_at_utc, updated_at_utc)
            VALUES (
                $id, $apply_result_id, $workspace_id, $status, $files_json, $failures_json,
                $created_at_utc, $updated_at_utc);
            """;
        Add(command, "$id", result.Id.ToString());
        Add(command, "$apply_result_id", result.ApplyResultId.ToString());
        Add(command, "$workspace_id", result.WorkspaceId.ToString());
        Add(command, "$status", result.Status.ToString());
        Add(command, "$files_json", JsonSerializer.Serialize(result.Files, JsonOptions));
        Add(command, "$failures_json", JsonSerializer.Serialize(result.FailureReasons, JsonOptions));
        Add(command, "$created_at_utc", FormatUtc(result.CreatedAtUtc));
        Add(command, "$updated_at_utc", FormatUtc(result.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PatchRollbackResult?> GetRollbackResultAsync(
        PatchRollbackResultId resultId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM patch_rollback_results WHERE id = $id LIMIT 1;";
        Add(command, "$id", resultId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadRollbackResult(reader)
            : null;
    }

    private static async Task InsertApplyResultAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PatchApplyResult result,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO patch_apply_results (
                id, proposal_id, workspace_id, status, files_json, failures_json,
                created_at_utc, updated_at_utc)
            VALUES (
                $id, $proposal_id, $workspace_id, $status, $files_json, $failures_json,
                $created_at_utc, $updated_at_utc);
            """;
        Add(command, "$id", result.Id.ToString());
        Add(command, "$proposal_id", result.ProposalId.ToString());
        Add(command, "$workspace_id", result.WorkspaceId.ToString());
        Add(command, "$status", result.Status.ToString());
        Add(command, "$files_json", JsonSerializer.Serialize(result.Files, JsonOptions));
        Add(command, "$failures_json", JsonSerializer.Serialize(result.FailureReasons, JsonOptions));
        Add(command, "$created_at_utc", FormatUtc(result.CreatedAtUtc));
        Add(command, "$updated_at_utc", FormatUtc(result.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertBackupAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PatchApplyBackup backup,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO patch_apply_backups (
                apply_result_id, proposal_id, workspace_id, relative_path, original_content,
                original_hash, applied_hash, operation_kind, encoding_name, created_at_utc)
            VALUES (
                $apply_result_id, $proposal_id, $workspace_id, $relative_path, $original_content,
                $original_hash, $applied_hash, $operation_kind, $encoding_name, $created_at_utc);
            """;
        Add(command, "$apply_result_id", backup.ApplyResultId.ToString());
        Add(command, "$proposal_id", backup.ProposalId.ToString());
        Add(command, "$workspace_id", backup.WorkspaceId.ToString());
        Add(command, "$relative_path", backup.RelativePath);
        Add(command, "$original_content", backup.OriginalContent);
        Add(command, "$original_hash", backup.OriginalContentHash);
        Add(command, "$applied_hash", backup.AppliedContentHash);
        Add(command, "$operation_kind", backup.OperationKind.ToString());
        Add(command, "$encoding_name", backup.EncodingName);
        Add(command, "$created_at_utc", FormatUtc(backup.CreatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() => new($"Data Source={databasePath}");

    private static PatchApplyResult ReadApplyResult(SqliteDataReader reader) =>
        new(
            new PatchApplyResultId(Guid.Parse(reader["id"].ToString()!)),
            new PatchProposalId(Guid.Parse(reader["proposal_id"].ToString()!)),
            new WorkspaceId(reader["workspace_id"].ToString()!),
            Enum.TryParse<PatchApplyStatus>(reader["status"].ToString(), out var status) ? status : PatchApplyStatus.Failed,
            Deserialize<IReadOnlyList<PatchApplyFileResult>>(reader["files_json"].ToString(), []),
            Deserialize<IReadOnlyList<PatchApplyFailureReason>>(reader["failures_json"].ToString(), []),
            ParseUtc(reader["created_at_utc"].ToString()),
            ParseUtc(reader["updated_at_utc"].ToString()));

    private static PatchApplyBackup ReadBackup(SqliteDataReader reader) =>
        new(
            new PatchApplyResultId(Guid.Parse(reader["apply_result_id"].ToString()!)),
            new PatchProposalId(Guid.Parse(reader["proposal_id"].ToString()!)),
            new WorkspaceId(reader["workspace_id"].ToString()!),
            reader["relative_path"].ToString() ?? string.Empty,
            reader["original_content"].ToString() ?? string.Empty,
            reader["original_hash"].ToString() ?? string.Empty,
            reader["applied_hash"].ToString() ?? string.Empty,
            ParseUtc(reader["created_at_utc"].ToString()),
            Enum.TryParse<PatchProposalFileChangeKind>(reader["operation_kind"].ToString(), out var kind) ? kind : PatchProposalFileChangeKind.Modify,
            reader["encoding_name"].ToString() ?? "utf-8");

    private static PatchRollbackResult ReadRollbackResult(SqliteDataReader reader) =>
        new(
            new PatchRollbackResultId(Guid.Parse(reader["id"].ToString()!)),
            new PatchApplyResultId(Guid.Parse(reader["apply_result_id"].ToString()!)),
            new WorkspaceId(reader["workspace_id"].ToString()!),
            Enum.TryParse<PatchApplyStatus>(reader["status"].ToString(), out var status) ? status : PatchApplyStatus.RollbackFailed,
            Deserialize<IReadOnlyList<PatchApplyFileResult>>(reader["files_json"].ToString(), []),
            Deserialize<IReadOnlyList<PatchApplyFailureReason>>(reader["failures_json"].ToString(), []),
            ParseUtc(reader["created_at_utc"].ToString()),
            ParseUtc(reader["updated_at_utc"].ToString()));

    private static async Task ExecuteAsync(SqliteConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static T Deserialize<T>(string? json, T fallback)
    {
        try { return string.IsNullOrWhiteSpace(json) ? fallback : JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback; }
        catch (JsonException) { return fallback; }
    }

    private static DateTimeOffset ParseUtc(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.UnixEpoch;

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
