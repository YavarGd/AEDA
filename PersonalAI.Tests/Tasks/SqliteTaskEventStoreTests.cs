using Microsoft.Data.Sqlite;
using PersonalAI.Core.Tasks;
using PersonalAI.Infrastructure.Tasks;

namespace PersonalAI.Tests.Tasks;

public sealed class SqliteTaskEventStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString());
    private readonly string _databasePath;

    public SqliteTaskEventStoreTests()
    {
        _databasePath = Path.Combine(_directory, "tasks.db");
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        var store = CreateStore();

        await store.InitializeAsync();
        await store.InitializeAsync();

        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task CreateAppendAndLoadTaskRun_ReturnsOrderedEventsAndArtifacts()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var run = new TaskRun(
            TaskId.NewId(),
            "Summarize document",
            TaskRunStatus.Created,
            now,
            now);
        await store.CreateTaskRunAsync(run);
        await store.UpdateTaskRunStatusAsync(run.Id, TaskRunStatus.Running, now.AddSeconds(1));

        var first = TaskEvent.Rehydrate(
            Guid.NewGuid(),
            run.Id,
            now,
            TaskEventKind.TaskStarted,
            "started");
        var second = TaskEvent.Rehydrate(
            Guid.NewGuid(),
            run.Id,
            now,
            TaskEventKind.ToolCompleted,
            "completed");
        await store.AppendAsync(first);
        await store.AppendAsync(second);
        await store.AppendArtifactAsync(
            run.Id,
            TaskArtifact.Create(
                "summary",
                "text",
                safeMetadata: TaskEventMetadata.CreateSafe(("kind", "preview"))));

        var loaded = await store.GetTaskRunAsync(run.Id);
        var recent = await store.ListRecentTaskRunsAsync(10);

        Assert.NotNull(loaded);
        Assert.Equal(TaskRunStatus.Running, loaded.TaskRun.Status);
        Assert.Collection(
            loaded.Events,
            item => Assert.Equal(first.EventId, item.EventId),
            item => Assert.Equal(second.EventId, item.EventId));
        var artifact = Assert.Single(loaded.Artifacts);
        Assert.Equal("summary", artifact.Name);
        Assert.Equal(run.Id, Assert.Single(recent).Id);
    }

    [Fact]
    public async Task MalformedTimestamp_FailsSafely()
    {
        var store = CreateStore();
        await store.InitializeAsync();

        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO task_runs (
                id, title, status, created_at_utc, updated_at_utc, safe_error_code
            )
            VALUES (
                $id, 'bad', 'Created', 'not-a-date', 'not-a-date', NULL
            );
            """;
        command.Parameters.AddWithValue("$id", TaskId.NewId().ToString());
        await command.ExecuteNonQueryAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await store.ListRecentTaskRunsAsync(10));
        Assert.Equal("Task runtime records could not be loaded or saved.", exception.Message);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.ListRecentTaskRunsAsync(10, cancellation.Token));
    }

    [Fact]
    public async Task RawExceptionText_IsNotPersistedThroughSupportedApi()
    {
        var store = CreateStore();
        await store.InitializeAsync();
        var run = TaskRun.Create("raw exception test");
        await store.CreateTaskRunAsync(run);
        await store.AppendAsync(TaskEvent.Create(
            run.Id,
            TaskEventKind.TaskFailed,
            "System.Exception: token=abc",
            safeErrorCode: "task_failed",
            safeErrorMessage: "System.Exception: token=abc at Secret.Frame()"));

        var databaseText = await File.ReadAllTextAsync(_databasePath);

        Assert.DoesNotContain("token=abc", databaseText, StringComparison.Ordinal);
        Assert.DoesNotContain("Secret.Frame", databaseText, StringComparison.Ordinal);
    }

    private SqliteTaskEventStore CreateStore() => new(_databasePath);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
