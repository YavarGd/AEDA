using Microsoft.Data.Sqlite;
using PersonalAI.Core.Memory;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class MemoryRepositoryTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));
    private readonly string _databasePath;

    public MemoryRepositoryTests()
    {
        _databasePath = Path.Combine(_directory, "memory.db");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var repository = new SqliteMemoryRepository(_databasePath);

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task CreateGetUpdateArchiveDelete_RoundTripsMemory()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var memory = CreateMemory("User prefers concise answers.");

        await repository.CreateAsync(memory);
        var stored = await repository.GetAsync(memory.Id);
        Assert.NotNull(stored);
        Assert.Equal("User prefers concise answers.", stored.Text);
        Assert.Equal(MemoryVisibility.Active, stored.Visibility);
        Assert.Equal("save this", stored.Source.Excerpt);

        var updated = stored with
        {
            Text = "User prefers concise backend reports.",
            UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(1)
        };
        await repository.UpdateAsync(updated);
        stored = await repository.GetAsync(memory.Id);
        Assert.Equal("User prefers concise backend reports.", stored?.Text);

        await repository.ArchiveAsync(memory.Id, DateTimeOffset.UtcNow.AddMinutes(2));
        stored = await repository.GetAsync(memory.Id);
        Assert.Equal(MemoryVisibility.Archived, stored?.Visibility);

        await repository.DeleteAsync(memory.Id);
        Assert.Null(await repository.GetAsync(memory.Id));
    }

    [Fact]
    public async Task ListAndSearch_FilterByScopeKindAndBoundsLimit()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var workspaceId = WorkspaceId.NewId();
        await repository.CreateAsync(CreateMemory(
            "Project uses SQLite memory storage.",
            MemoryKind.ProjectFact,
            MemoryScope.Workspace,
            workspaceId: workspaceId));
        await repository.CreateAsync(CreateMemory(
            "Task completed durable event store work.",
            MemoryKind.TaskOutcome,
            MemoryScope.Task,
            taskRunId: TaskId.NewId()));

        var listed = await repository.ListAsync(new MemorySearchQuery(
            Scope: MemoryScope.Workspace,
            Kind: MemoryKind.ProjectFact,
            WorkspaceId: workspaceId,
            Limit: 999));
        var search = await repository.SearchAsync(new MemorySearchQuery(
            Text: "SQLite storage",
            Limit: 1));

        var memory = Assert.Single(listed);
        Assert.Equal(MemoryKind.ProjectFact, memory.Kind);
        var result = Assert.Single(search);
        Assert.True(result.Score > 0);
        Assert.Equal("explicit_user_save", result.Source.SourceType);
    }

    [Fact]
    public async Task MissingSourceAttribution_IsRejectedSafely()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var invalid = CreateMemory("No attribution") with
        {
            Source = new MemorySource("unknown", DateTimeOffset.UtcNow)
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.CreateAsync(invalid));

        Assert.Equal("Memory records could not be loaded or saved.", exception.Message);
    }

    [Fact]
    public async Task MalformedUtc_FailsSafelyWithoutRawException()
    {
        var repository = await CreateInitializedRepositoryAsync();
        var memory = CreateMemory("Malformed test");
        await repository.CreateAsync(memory);

        await using (var connection = new SqliteConnection(
                         new SqliteConnectionStringBuilder
                         {
                             DataSource = _databasePath,
                             Pooling = false
                         }.ToString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE memories SET created_at_utc = 'not-a-date' WHERE id = $id;";
            command.Parameters.AddWithValue("$id", memory.Id.ToString());
            await command.ExecuteNonQueryAsync();
        }

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => repository.GetAsync(memory.Id));

        Assert.Equal("Memory records could not be loaded or saved.", exception.Message);
        Assert.DoesNotContain("not-a-date", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_IsHonored()
    {
        var repository = await CreateInitializedRepositoryAsync();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.ListAsync(new MemorySearchQuery(), cts.Token));
    }

    private async Task<SqliteMemoryRepository> CreateInitializedRepositoryAsync()
    {
        var repository = new SqliteMemoryRepository(_databasePath);
        await repository.InitializeAsync();
        return repository;
    }

    internal static MemoryRecord CreateMemory(
        string text,
        MemoryKind kind = MemoryKind.ExplicitUserPreference,
        MemoryScope scope = MemoryScope.Global,
        WorkspaceId? workspaceId = null,
        TaskId? taskRunId = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new MemoryRecord(
            MemoryId.NewId(),
            kind,
            scope,
            text,
            new MemorySource(
                "explicit_user_save",
                now,
                WorkspaceId: workspaceId,
                TaskRunId: taskRunId,
                Excerpt: "save this",
                Confidence: MemoryConfidence.High),
            now,
            now,
            MemoryConfidence.High,
            WorkspaceId: workspaceId,
            TaskRunId: taskRunId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Directory.Delete(_directory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 4)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(100);
                }
                catch (IOException)
                {
                    return;
                }
            }
        }
    }
}
