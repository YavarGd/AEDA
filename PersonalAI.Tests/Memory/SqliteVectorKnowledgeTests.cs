using Microsoft.Data.Sqlite;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class SqliteVectorKnowledgeTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SqliteVectorIndex_InitUpsertSearchUpdateDelete()
    {
        var path = Path.Combine(_directory, "vectors.db");
        var index = new SqliteVectorIndex(path, dimension: 3);
        await index.InitializeAsync();
        await index.InitializeAsync();

        await index.UpsertAsync(Vector("b", [1, 0, 0], workspaceId: "ws1"));
        await index.UpsertAsync(Vector("a", [1, 0, 0], workspaceId: "ws1"));
        await index.UpsertAsync(Vector("c", [0, 1, 0], scope: MemoryScope.Project, workspaceId: "ws2"));

        var results = await index.SearchAsync(new VectorSearchQuery(
            new EmbeddingVector([1, 0, 0]),
            TopK: 10,
            Scope: MemoryScope.Workspace,
            WorkspaceId: "ws1"));

        Assert.Collection(
            results,
            result => Assert.Equal("a", result.Document.Id),
            result => Assert.Equal("b", result.Document.Id));

        await index.UpsertAsync(Vector("a", [0, 1, 0], workspaceId: "ws1"));
        results = await index.SearchAsync(new VectorSearchQuery(
            new EmbeddingVector([0, 1, 0]),
            TopK: 1,
            WorkspaceId: "ws1"));
        Assert.Equal("a", Assert.Single(results).Document.Id);

        await index.DeleteAsync("a");
        Assert.DoesNotContain(
            await index.SearchAsync(new VectorSearchQuery(new EmbeddingVector([0, 1, 0]), TopK: 10)),
            result => result.Document.Id == "a");
    }

    [Fact]
    public async Task SqliteVectorIndex_RejectsDimensionMismatchAndMalformedDataSafely()
    {
        var path = Path.Combine(_directory, "bad-vectors.db");
        var index = new SqliteVectorIndex(path, dimension: 3);
        await index.InitializeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () =>
            {
                await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Pooling = false
                }.ToString());
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    INSERT INTO vector_documents (
                        id, dimension, vector_json, text, scope, updated_at_utc
                    )
                    VALUES ('bad', 3, '[1,2]', 'bad', 'Global', '2026-01-01T00:00:00.0000000+00:00');
                    """;
                await command.ExecuteNonQueryAsync();
                await index.SearchAsync(new VectorSearchQuery(
                    new EmbeddingVector([1, 0, 0]),
                    TopK: 10));
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => index.UpsertAsync(
            Vector("wrong", [1, 2])));
    }

    [Fact]
    public async Task SqliteVectorIndex_BoundsTopKAndHonorsCancellation()
    {
        var index = new SqliteVectorIndex(Path.Combine(_directory, "cancel-vectors.db"), dimension: 3);
        await index.InitializeAsync();
        await index.UpsertAsync(Vector("a", [1, 0, 0]));
        await index.UpsertAsync(Vector("b", [0, 1, 0]));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var results = await index.SearchAsync(new VectorSearchQuery(
            new EmbeddingVector([1, 0, 0]),
            TopK: 1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => index.SearchAsync(new VectorSearchQuery(new EmbeddingVector([1, 0, 0]), TopK: 1), cts.Token));

        Assert.Single(results);
    }

    [Fact]
    public async Task KnowledgeRepository_PersistsChunksAndClearsWorkspace()
    {
        var path = Path.Combine(_directory, "knowledge.db");
        var repository = new SqliteKnowledgeRepository(path);
        await repository.InitializeAsync();
        await repository.InitializeAsync();
        var workspaceId = WorkspaceId.NewId();
        var source = new KnowledgeSource(
            KnowledgeSourceType.WorkspaceFile,
            DateTimeOffset.UtcNow,
            workspaceId,
            "src/App.cs");
        var chunking = KnowledgeChunker.ChunkText(
            $"{workspaceId}:src/App.cs",
            "src/App.cs",
            "public class App { }",
            source);

        await repository.UpsertDocumentAsync(
            chunking.Document with { State = DocumentIndexState.Indexed },
            chunking.Chunks);

        var reloaded = new SqliteKnowledgeRepository(path);
        await reloaded.InitializeAsync();
        var documents = await reloaded.ListDocumentsAsync(workspaceId.ToString());
        var document = Assert.Single(documents);
        var chunks = await reloaded.ListChunksAsync(document.Id);

        Assert.Equal("src/App.cs", document.Source.RelativePath);
        Assert.Contains("public class", Assert.Single(chunks).Text);

        var changed = KnowledgeChunker.ChunkText(
            document.Id,
            "src/App.cs",
            "public class App { void Run() {} }",
            source);
        await repository.UpsertDocumentAsync(
            changed.Document with { State = DocumentIndexState.Indexed },
            changed.Chunks);
        Assert.NotEqual(document.ContentHash, (await repository.GetDocumentAsync(document.Id))?.ContentHash);

        await repository.ClearWorkspaceAsync(workspaceId.ToString());
        Assert.Empty(await repository.ListDocumentsAsync(workspaceId.ToString()));
    }

    [Fact]
    public async Task KnowledgeRepository_MalformedTimestampFailsSafely()
    {
        var path = Path.Combine(_directory, "bad-knowledge.db");
        var repository = new SqliteKnowledgeRepository(path);
        await repository.InitializeAsync();
        await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder
                     {
                         DataSource = path,
                         Pooling = false
                     }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO knowledge_documents (
                    id, source_type, source_timestamp_utc, title, content_hash,
                    updated_at_utc, state
                )
                VALUES ('bad', 'ManualNote', 'not-date', 'bad', 'hash', 'not-date', 'Indexed');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.ListDocumentsAsync());

        Assert.Equal("Knowledge records could not be loaded or saved.", exception.Message);
    }

    private static VectorDocument Vector(
        string id,
        float[] values,
        MemoryScope scope = MemoryScope.Workspace,
        string? workspaceId = null) =>
        new(
            id,
            new EmbeddingVector(values),
            id,
            scope,
            WorkspaceId: workspaceId,
            SourceKind: KnowledgeSourceType.WorkspaceFile.ToString(),
            SourceId: id);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
