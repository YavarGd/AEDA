using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PersonalAI.Core.Memory;

namespace PersonalAI.Infrastructure.Memory;

public sealed class SqliteVectorIndex(string databasePath, int dimension) : IVectorIndex
{
    private const int MaxTopK = 100;
    private bool _rebuildRequired;

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
            await ExecuteNonQueryAsync(
                connection,
                """
                CREATE TABLE IF NOT EXISTS vector_documents (
                    id TEXT NOT NULL PRIMARY KEY,
                    dimension INTEGER NOT NULL,
                    vector_json TEXT NOT NULL,
                    text TEXT NOT NULL,
                    scope TEXT NOT NULL,
                    project_id TEXT NULL,
                    workspace_id TEXT NULL,
                    source_kind TEXT NULL,
                    source_id TEXT NULL,
                    metadata_json TEXT NULL,
                    updated_at_utc TEXT NOT NULL
                );
                """,
                cancellationToken);
            await ExecuteNonQueryAsync(
                connection,
                "CREATE INDEX IF NOT EXISTS ix_vector_documents_scope_workspace ON vector_documents (scope, workspace_id, project_id, source_kind);",
                cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task UpsertAsync(
        VectorDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateDimension(document.Vector);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO vector_documents (
                    id, dimension, vector_json, text, scope, project_id, workspace_id,
                    source_kind, source_id, metadata_json, updated_at_utc
                )
                VALUES (
                    $id, $dimension, $vector_json, $text, $scope, $project_id, $workspace_id,
                    $source_kind, $source_id, $metadata_json, $updated_at_utc
                )
                ON CONFLICT(id) DO UPDATE SET
                    dimension = excluded.dimension,
                    vector_json = excluded.vector_json,
                    text = excluded.text,
                    scope = excluded.scope,
                    project_id = excluded.project_id,
                    workspace_id = excluded.workspace_id,
                    source_kind = excluded.source_kind,
                    source_id = excluded.source_id,
                    metadata_json = excluded.metadata_json,
                    updated_at_utc = excluded.updated_at_utc;
                """;
            AddParameters(command, document);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task DeleteAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM vector_documents WHERE id = $id;";
            command.Parameters.AddWithValue("$id", documentId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        VectorSearchQuery query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateDimension(query.Vector);
            var topK = Math.Clamp(query.TopK, 1, MaxTopK);
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            var clauses = new List<string>();
            if (query.Scope is not null)
            {
                clauses.Add("scope = $scope");
                command.Parameters.AddWithValue("$scope", query.Scope.Value.ToString());
            }

            if (!string.IsNullOrWhiteSpace(query.ProjectId))
            {
                clauses.Add("project_id = $project_id");
                command.Parameters.AddWithValue("$project_id", query.ProjectId);
            }

            if (!string.IsNullOrWhiteSpace(query.WorkspaceId))
            {
                clauses.Add("workspace_id = $workspace_id");
                command.Parameters.AddWithValue("$workspace_id", query.WorkspaceId);
            }

            if (!string.IsNullOrWhiteSpace(query.SourceKind))
            {
                clauses.Add("source_kind = $source_kind");
                command.Parameters.AddWithValue("$source_kind", query.SourceKind);
            }

            command.CommandText =
                """
                SELECT id, dimension, vector_json, text, scope, project_id, workspace_id,
                       source_kind, source_id, metadata_json
                FROM vector_documents
                """ +
                (clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses)) +
                " ORDER BY id ASC LIMIT 2000;";

            var results = new List<VectorSearchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var document = ReadDocument(reader);
                results.Add(new VectorSearchResult(
                    document,
                    CosineSimilarity(query.Vector.Values, document.Vector.Values)));
            }

            return results
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Document.Id, StringComparer.Ordinal)
                .Take(topK)
                .ToArray();
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public async Task<VectorIndexStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM vector_documents;";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            return new VectorIndexStatus(true, count, dimension, _rebuildRequired);
        }
        catch (Exception exception) when (IsExpectedPersistenceException(exception))
        {
            throw PersistenceFailure();
        }
    }

    public Task MarkRebuildRequiredAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _rebuildRequired = true;
        return Task.CompletedTask;
    }

    public async Task DeleteBySourcePrefixAsync(
        string sourceIdPrefix,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM vector_documents WHERE source_id LIKE $prefix OR id LIKE $prefix;";
            command.Parameters.AddWithValue("$prefix", sourceIdPrefix + "%");
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

    private static async Task ExecuteNonQueryAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void AddParameters(SqliteCommand command, VectorDocument document)
    {
        command.Parameters.AddWithValue("$id", document.Id);
        command.Parameters.AddWithValue("$dimension", document.Vector.Dimension);
        command.Parameters.AddWithValue("$vector_json", JsonSerializer.Serialize(document.Vector.Values));
        command.Parameters.AddWithValue("$text", document.Text);
        command.Parameters.AddWithValue("$scope", document.Scope.ToString());
        command.Parameters.AddWithValue("$project_id", document.ProjectId is null ? DBNull.Value : document.ProjectId);
        command.Parameters.AddWithValue("$workspace_id", document.WorkspaceId is null ? DBNull.Value : document.WorkspaceId);
        command.Parameters.AddWithValue("$source_kind", document.SourceKind is null ? DBNull.Value : document.SourceKind);
        command.Parameters.AddWithValue("$source_id", document.SourceId is null ? DBNull.Value : document.SourceId);
        command.Parameters.AddWithValue("$metadata_json", document.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(document.Metadata));
        command.Parameters.AddWithValue("$updated_at_utc", DateTimeOffset.UtcNow.ToString("O"));
    }

    private VectorDocument ReadDocument(SqliteDataReader reader)
    {
        var storedDimension = reader.GetInt32(1);
        if (storedDimension != dimension)
        {
            throw new VectorIndexDataFormatException();
        }

        var values = JsonSerializer.Deserialize<float[]>(reader.GetString(2)) ??
            throw new VectorIndexDataFormatException();
        if (values.Length != dimension)
        {
            throw new VectorIndexDataFormatException();
        }

        return new VectorDocument(
            reader.GetString(0),
            new EmbeddingVector(values),
            reader.GetString(3),
            ParseEnum<MemoryScope>(reader.GetString(4)),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(reader.GetString(9)));
    }

    private void ValidateDimension(EmbeddingVector vector)
    {
        if (vector.Dimension != dimension)
        {
            throw new ArgumentException(
                "Vector dimension did not match the index.",
                nameof(vector));
        }
    }

    private static double CosineSimilarity(
        IReadOnlyList<float> left,
        IReadOnlyList<float> right)
    {
        double dot = 0;
        double leftMagnitude = 0;
        double rightMagnitude = 0;
        for (var index = 0; index < left.Count; index++)
        {
            dot += left[index] * right[index];
            leftMagnitude += left[index] * left[index];
            rightMagnitude += right[index] * right[index];
        }

        return leftMagnitude == 0 || rightMagnitude == 0
            ? 0
            : dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }

    private static T ParseEnum<T>(string value)
        where T : struct
    {
        if (Enum.TryParse<T>(value, out var parsed))
        {
            return parsed;
        }

        throw new VectorIndexDataFormatException();
    }

    private static bool IsExpectedPersistenceException(Exception exception) =>
        exception is SqliteException or
            IOException or
            UnauthorizedAccessException or
            ArgumentException or
            NotSupportedException or
            PathTooLongException or
            JsonException or
            VectorIndexDataFormatException;

    private static InvalidOperationException PersistenceFailure() =>
        new("Vector index records could not be loaded or saved.");

    private sealed class VectorIndexDataFormatException : Exception
    {
    }
}
