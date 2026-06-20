using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Coding;

public sealed class SqlitePatchProposalRepository(string databasePath)
    : IPatchProposalRepository
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
            CREATE TABLE IF NOT EXISTS patch_proposals (
                id TEXT NOT NULL PRIMARY KEY,
                workspace_id TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                status TEXT NOT NULL,
                risk TEXT NOT NULL,
                risk_reasons_json TEXT NOT NULL,
                files_json TEXT NOT NULL,
                sources_json TEXT NOT NULL,
                validation_plan_json TEXT NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL
            );
            """, cancellationToken);
        await ExecuteAsync(connection, """
            CREATE INDEX IF NOT EXISTS ix_patch_proposals_updated
            ON patch_proposals (updated_at_utc DESC);
            """, cancellationToken);
    }

    public async Task CreateAsync(
        PatchProposal proposal,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO patch_proposals (
                id, workspace_id, title, summary, status, risk, risk_reasons_json,
                files_json, sources_json, validation_plan_json,
                created_at_utc, updated_at_utc)
            VALUES (
                $id, $workspace_id, $title, $summary, $status, $risk, $risk_reasons_json,
                $files_json, $sources_json, $validation_plan_json,
                $created_at_utc, $updated_at_utc);
            """;
        Add(command, "$id", proposal.Id.ToString());
        Add(command, "$workspace_id", proposal.WorkspaceId.ToString());
        Add(command, "$title", proposal.Title);
        Add(command, "$summary", proposal.Summary);
        Add(command, "$status", proposal.Status.ToString());
        Add(command, "$risk", proposal.Risk.ToString());
        Add(command, "$risk_reasons_json", JsonSerializer.Serialize(proposal.RiskReasons, JsonOptions));
        Add(command, "$files_json", JsonSerializer.Serialize(proposal.Files, JsonOptions));
        Add(command, "$sources_json", JsonSerializer.Serialize(proposal.Sources, JsonOptions));
        Add(command, "$validation_plan_json", JsonSerializer.Serialize(proposal.ValidationPlan, JsonOptions));
        Add(command, "$created_at_utc", FormatUtc(proposal.CreatedAtUtc));
        Add(command, "$updated_at_utc", FormatUtc(proposal.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PatchProposal?> GetAsync(
        PatchProposalId proposalId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM patch_proposals WHERE id = $id LIMIT 1;";
        Add(command, "$id", proposalId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadProposal(reader)
            : null;
    }

    public async Task<IReadOnlyList<PatchProposal>> ListRecentAsync(
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT * FROM patch_proposals
            ORDER BY updated_at_utc DESC
            LIMIT $limit;
            """;
        Add(command, "$limit", Math.Clamp(limit, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var proposals = new List<PatchProposal>();
        while (await reader.ReadAsync(cancellationToken))
        {
            proposals.Add(ReadProposal(reader));
        }

        return proposals;
    }

    public async Task UpdateStatusAsync(
        PatchProposalId proposalId,
        PatchProposalStatus status,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE patch_proposals
            SET status = $status, updated_at_utc = $updated_at_utc
            WHERE id = $id;
            """;
        Add(command, "$id", proposalId.ToString());
        Add(command, "$status", status.ToString());
        Add(command, "$updated_at_utc", FormatUtc(DateTimeOffset.UtcNow));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqliteConnection CreateConnection() =>
        new($"Data Source={databasePath}");

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static PatchProposal ReadProposal(SqliteDataReader reader)
    {
        var created = ParseUtc(reader["created_at_utc"]?.ToString());
        var updated = ParseUtc(reader["updated_at_utc"]?.ToString());
        return new PatchProposal(
            new PatchProposalId(Guid.Parse(reader["id"].ToString()!)),
            new WorkspaceId(reader["workspace_id"].ToString()!),
            reader["title"].ToString() ?? "Patch proposal",
            reader["summary"].ToString() ?? string.Empty,
            Enum.TryParse<PatchProposalStatus>(reader["status"].ToString(), out var status)
                ? status
                : PatchProposalStatus.Failed,
            Enum.TryParse<PatchProposalRisk>(reader["risk"].ToString(), out var risk)
                ? risk
                : PatchProposalRisk.Blocked,
            Deserialize<IReadOnlyList<string>>(reader["risk_reasons_json"].ToString(), []),
            Deserialize<IReadOnlyList<PatchProposalFile>>(reader["files_json"].ToString(), []),
            Deserialize<IReadOnlyList<PatchProposalSource>>(reader["sources_json"].ToString(), []),
            Deserialize(reader["validation_plan_json"].ToString(), new PatchProposalValidationPlan([], [])),
            created,
            updated);
    }

    private static T Deserialize<T>(string? json, T fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(json)
                ? fallback
                : JsonSerializer.Deserialize<T>(json, JsonOptions) ?? fallback;
        }
        catch (JsonException)
        {
            return fallback;
        }
    }

    private static DateTimeOffset ParseUtc(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTimeOffset.UnixEpoch;

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void Add(SqliteCommand command, string name, object? value) =>
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
}
