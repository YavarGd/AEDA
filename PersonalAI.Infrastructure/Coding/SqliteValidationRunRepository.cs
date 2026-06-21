using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class SqliteValidationRunRepository(string databasePath)
    : IValidationRunRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS validation_runs (
                id TEXT NOT NULL PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                template_id TEXT NOT NULL,
                working_directory_label TEXT NOT NULL,
                status TEXT NOT NULL,
                proposal_id TEXT NULL,
                apply_result_id TEXT NULL,
                command_result_json TEXT NULL,
                failures_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_validation_runs_workspace_updated
            ON validation_runs (workspace_id, updated_at_utc DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task CreateAsync(
        ValidationRun run,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO validation_runs (
                id, workspace_id, template_id, working_directory_label, status,
                proposal_id, apply_result_id, command_result_json, failures_json,
                created_at_utc, updated_at_utc)
            VALUES (
                $id, $workspace_id, $template_id, $working_directory_label, $status,
                $proposal_id, $apply_result_id, $command_result_json, $failures_json,
                $created_at_utc, $updated_at_utc);
            """;
        AddRunParameters(command, run);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        ValidationRun run,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE validation_runs
            SET status = $status,
                command_result_json = $command_result_json,
                failures_json = $failures_json,
                updated_at_utc = $updated_at_utc
            WHERE id = $id;
            """;
        Add(command, "$id", run.Id.ToString());
        Add(command, "$status", run.Status.ToString());
        Add(command, "$command_result_json", run.CommandResult is null ? null : JsonSerializer.Serialize(run.CommandResult, JsonOptions));
        Add(command, "$failures_json", JsonSerializer.Serialize(run.FailureReasons, JsonOptions));
        Add(command, "$updated_at_utc", FormatUtc(run.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ValidationRun?> GetAsync(
        ValidationRunId runId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM validation_runs WHERE id = $id LIMIT 1;";
        Add(command, "$id", runId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadRun(reader) : null;
    }

    public async Task<IReadOnlyList<ValidationRun>> ListRecentAsync(
        WorkspaceId workspaceId,
        PatchProposalId? proposalId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = proposalId is null
            ? "SELECT * FROM validation_runs WHERE workspace_id = $workspace_id ORDER BY updated_at_utc DESC LIMIT $limit;"
            : "SELECT * FROM validation_runs WHERE workspace_id = $workspace_id AND proposal_id = $proposal_id ORDER BY updated_at_utc DESC LIMIT $limit;";
        Add(command, "$workspace_id", workspaceId.ToString());
        Add(command, "$proposal_id", proposalId?.ToString());
        Add(command, "$limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var runs = new List<ValidationRun>();
        while (await reader.ReadAsync(cancellationToken))
        {
            runs.Add(ReadRun(reader));
        }

        return runs;
    }

    private SqliteConnection CreateConnection() => new($"Data Source={databasePath}");

    private static ValidationRun ReadRun(SqliteDataReader reader)
    {
        PatchProposalId? proposalId = Guid.TryParse(reader["proposal_id"]?.ToString(), out var proposalGuid)
            ? new PatchProposalId(proposalGuid)
            : null;
        PatchApplyResultId? applyId = Guid.TryParse(reader["apply_result_id"]?.ToString(), out var applyGuid)
            ? new PatchApplyResultId(applyGuid)
            : null;
        return new ValidationRun(
            new ValidationRunId(Guid.Parse(reader["id"].ToString()!)),
            new WorkspaceId(reader["workspace_id"].ToString()!),
            reader["template_id"].ToString() ?? string.Empty,
            reader["working_directory_label"].ToString() ?? ".",
            Enum.TryParse<ValidationRunStatus>(reader["status"].ToString(), out var status) ? status : ValidationRunStatus.Blocked,
            proposalId,
            applyId,
            Deserialize<ValidationCommandResult?>(reader["command_result_json"].ToString(), null),
            Deserialize<IReadOnlyList<ValidationFailureReason>>(reader["failures_json"].ToString(), []),
            ParseUtc(reader["created_at_utc"].ToString()),
            ParseUtc(reader["updated_at_utc"].ToString()));
    }

    private static void AddRunParameters(SqliteCommand command, ValidationRun run)
    {
        Add(command, "$id", run.Id.ToString());
        Add(command, "$workspace_id", run.WorkspaceId.ToString());
        Add(command, "$template_id", run.TemplateId);
        Add(command, "$working_directory_label", run.SafeWorkingDirectoryLabel);
        Add(command, "$status", run.Status.ToString());
        Add(command, "$proposal_id", run.ProposalId?.ToString());
        Add(command, "$apply_result_id", run.ApplyResultId?.ToString());
        Add(command, "$command_result_json", run.CommandResult is null ? null : JsonSerializer.Serialize(run.CommandResult, JsonOptions));
        Add(command, "$failures_json", JsonSerializer.Serialize(run.FailureReasons, JsonOptions));
        Add(command, "$created_at_utc", FormatUtc(run.CreatedAtUtc));
        Add(command, "$updated_at_utc", FormatUtc(run.UpdatedAtUtc));
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
