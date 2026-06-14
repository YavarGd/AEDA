using Microsoft.Data.Sqlite;
using PersonalAI.Core.Tools;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Workspaces;

namespace PersonalAI.Tests.Workspaces;

public sealed class WorkspaceRegistrationPersistenceTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.WorkspacePersistence.Tests", Guid.NewGuid().ToString());
    private readonly string _databasePath;

    public WorkspaceRegistrationPersistenceTests()
    {
        Directory.CreateDirectory(_directory);
        _databasePath = Path.Combine(_directory, "personalai-test.db");
    }

    [Fact]
    public async Task Repository_RoundTripsStableWorkspaceId()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var workspace = CreatePersistedWorkspace(Path.Combine(_directory, "repo"));

        await repository.UpsertAsync(workspace);
        var stored = await repository.GetAsync(workspace.Id);
        var listed = await repository.ListAsync();

        Assert.NotNull(stored);
        Assert.Equal(workspace.Id, stored.Id);
        Assert.Equal(workspace.CanonicalRootPath, stored.CanonicalRootPath);
        Assert.Equal(
            workspace.AddedAtUtc.ToUniversalTime(),
            stored.AddedAtUtc.ToUniversalTime());
        Assert.NotEqual(DateTimeOffset.UnixEpoch, stored.AddedAtUtc);
        Assert.Single(listed);
    }

    [Fact]
    public async Task Repository_InitializeAsync_IsIdempotentAndCreatesSchema()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);

        await repository.InitializeAsync();
        await repository.InitializeAsync();

        await using var connection = new SqliteConnection(CreateConnectionString());
        await connection.OpenAsync();
        Assert.True(await ExistsAsync(connection, "table", "persisted_workspaces"));
        Assert.True(await ExistsAsync(connection, "index", "ux_persisted_workspaces_active_root"));
        Assert.True(await ExistsAsync(connection, "index", "ix_persisted_workspaces_status"));
    }

    [Fact]
    public async Task Repository_UpdatesAndRemovesWorkspace()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var workspace = CreatePersistedWorkspace(Path.Combine(_directory, "repo"));

        await repository.UpsertAsync(workspace);
        await repository.UpsertAsync(workspace with { DisplayName = "Updated" });
        await repository.RemoveAsync(workspace.Id, DateTimeOffset.UtcNow);

        Assert.Empty(await repository.ListAsync());
        var removed = await repository.GetAsync(workspace.Id);
        Assert.NotNull(removed);
        Assert.Equal(WorkspaceRegistrationStatus.Removed, removed.Status);
        Assert.Single(await repository.ListAsync(includeRemoved: true));
    }

    [Fact]
    public async Task Repository_DuplicateActiveCanonicalRootIsRejected()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var root = Path.Combine(_directory, "repo");
        var first = CreatePersistedWorkspace(root);
        var second = CreatePersistedWorkspace(root) with { Id = WorkspaceId.NewId() };

        await repository.UpsertAsync(first);
        var exception = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => repository.UpsertAsync(second));

        Assert.Equal("workspace_duplicate", exception.SafeErrorCode);
    }

    [Fact]
    public async Task Repository_RemovedDuplicateRootCanBeReadded()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var root = Path.Combine(_directory, "repo");
        var first = CreatePersistedWorkspace(root);
        var second = CreatePersistedWorkspace(root) with { Id = WorkspaceId.NewId() };

        await repository.UpsertAsync(first);
        await repository.RemoveAsync(first.Id, DateTimeOffset.UtcNow);
        await repository.UpsertAsync(second);

        var active = Assert.Single(await repository.ListAsync());
        Assert.Equal(second.Id, active.Id);
        Assert.Equal(2, (await repository.ListAsync(includeRemoved: true)).Count);
    }

    [Fact]
    public async Task Repository_InvalidStoredTimestampFailsSafely()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        await using var connection = new SqliteConnection(CreateConnectionString());
        await connection.OpenAsync();
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
                'Bad timestamp',
                $root,
                $root,
                'test',
                'not-a-timestamp',
                NULL,
                'Available',
                NULL,
                1,
                NULL
            );
            """;
        command.Parameters.AddWithValue("$id", WorkspaceId.NewId().ToString());
        command.Parameters.AddWithValue("$root", Path.Combine(_directory, "bad"));
        await command.ExecuteNonQueryAsync();

        var exception = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => repository.ListAsync());

        Assert.Equal("workspace_persistence_failed", exception.SafeErrorCode);
        Assert.Equal("Workspace registrations could not be saved.", exception.SafeErrorMessage);
        Assert.DoesNotContain("not-a-timestamp", exception.SafeErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_MigrationDoesNotRemoveConversationTables()
    {
        var conversationRepository =
            new PersonalAI.Infrastructure.Persistence.SqliteConversationRepository(_databasePath);
        await conversationRepository.InitializeAsync();
        var workspaceRepository = new SqliteWorkspaceRepository(_databasePath);

        await workspaceRepository.InitializeAsync();

        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task RegistrationService_ExplicitRegistrationPersistsAndRegistersRuntime()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);
        await service.InitializeAsync();

        var workspace = await service.RegisterAsync(root, "My workspace", "test");

        Assert.True(registry.TryGet(workspace.Id, out var descriptor));
        Assert.Equal(workspace.Id, descriptor.Id);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task RegistrationService_MissingFolderRejectedWithoutPersistence()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var service = new WorkspaceRegistrationService(repository, new WorkspaceRegistry());
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => service.RegisterAsync(Path.Combine(_directory, "missing"), "Missing", "test"));

        Assert.Equal("workspace_not_found", exception.SafeErrorCode);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task RegistrationService_FilePathRejectedWithoutPersistence()
    {
        var file = Path.Combine(_directory, "file.txt");
        await File.WriteAllTextAsync(file, "content");
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var service = new WorkspaceRegistrationService(repository, new WorkspaceRegistry());
        await service.InitializeAsync();

        var exception = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => service.RegisterAsync(file, "File", "test"));

        Assert.Equal("workspace_root_is_file", exception.SafeErrorCode);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task RegistrationService_DuplicateRootReturnsExistingWorkspace()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var service = new WorkspaceRegistrationService(repository, new WorkspaceRegistry());
        await service.InitializeAsync();

        var first = await service.RegisterAsync(root, "First", "test");
        var second = await service.RegisterAsync(root, "Second", "test");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await repository.ListAsync());
    }

    [Fact]
    public async Task RegistrationService_DuplicateInvalidRootIsRevalidated()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var existing = CreatePersistedWorkspace(root) with
        {
            Status = WorkspaceRegistrationStatus.Missing,
            SafeStatusCode = "workspace_not_found"
        };
        await repository.UpsertAsync(existing);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);

        var registered = await service.RegisterAsync(root, "Updated", "test");

        Assert.Equal(existing.Id, registered.Id);
        Assert.Equal(WorkspaceRegistrationStatus.Available, registered.Status);
        Assert.True(registry.TryGet(existing.Id, out _));
        var stored = await repository.GetAsync(existing.Id);
        Assert.NotNull(stored);
        Assert.Equal(WorkspaceRegistrationStatus.Available, stored.Status);
    }

    [Fact]
    public async Task RegistrationService_RuntimeFailureDuringRegisterDoesNotPersistAvailable()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new FailingWorkspaceRegistry { FailAllRegisters = true };
        var invalidator = new RecordingInvalidator();
        var service = new WorkspaceRegistrationService(repository, registry, invalidator);
        await service.InitializeAsync();

        var workspace = await service.RegisterAsync(root, "Workspace", "test");

        Assert.Equal(WorkspaceRegistrationStatus.ValidationFailed, workspace.Status);
        Assert.Equal("workspace_runtime_registration_failed", workspace.SafeStatusCode);
        Assert.False(registry.TryGet(workspace.Id, out _));
        Assert.Contains(workspace.Id, invalidator.Invalidated);
        var stored = await repository.GetAsync(workspace.Id);
        Assert.NotNull(stored);
        Assert.NotEqual(WorkspaceRegistrationStatus.Available, stored.Status);
    }

    [Fact]
    public async Task RegistrationService_RemoveUpdatesPersistenceRuntimeAndInvalidatesPermissions()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new WorkspaceRegistry();
        var invalidator = new RecordingInvalidator();
        var service = new WorkspaceRegistrationService(repository, registry, invalidator);
        await service.InitializeAsync();
        var workspace = await service.RegisterAsync(root, "Workspace", "test");

        await service.RemoveAsync(workspace.Id);

        Assert.False(registry.TryGet(workspace.Id, out _));
        Assert.Contains(workspace.Id, invalidator.Invalidated);
        Assert.Empty(await repository.ListAsync());
    }

    [Fact]
    public async Task RegistrationService_RevalidationMarksMissingWorkspaceInvalid()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);
        await service.InitializeAsync();
        var workspace = await service.RegisterAsync(root, "Workspace", "test");
        Directory.Delete(root);

        var updated = await service.RevalidateAsync(workspace.Id);

        Assert.NotNull(updated);
        Assert.Equal(WorkspaceRegistrationStatus.Missing, updated.Status);
        Assert.False(registry.TryGet(workspace.Id, out _));
    }

    [Fact]
    public async Task RegistrationService_RuntimeFailureDuringRevalidateMarksInvalid()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var workspace = CreatePersistedWorkspace(root);
        await repository.UpsertAsync(workspace);
        var registry = new FailingWorkspaceRegistry();
        registry.FailRegisterIds.Add(workspace.Id);
        var invalidator = new RecordingInvalidator();
        var service = new WorkspaceRegistrationService(repository, registry, invalidator);

        var updated = await service.RevalidateAsync(workspace.Id);

        Assert.NotNull(updated);
        Assert.Equal(WorkspaceRegistrationStatus.ValidationFailed, updated.Status);
        Assert.Equal("workspace_runtime_registration_failed", updated.SafeStatusCode);
        Assert.False(registry.TryGet(workspace.Id, out _));
        Assert.Contains(workspace.Id, invalidator.Invalidated);
    }

    [Fact]
    public async Task RegistrationService_StartupLoadsValidAndKeepsInvalidVisible()
    {
        var validRoot = Path.Combine(_directory, "valid");
        var missingRoot = Path.Combine(_directory, "missing");
        Directory.CreateDirectory(validRoot);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var valid = CreatePersistedWorkspace(validRoot);
        var missing = CreatePersistedWorkspace(missingRoot, createDirectory: false);
        await repository.UpsertAsync(valid);
        await repository.UpsertAsync(missing);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);

        await service.InitializeAsync();

        Assert.True(registry.TryGet(valid.Id, out _));
        Assert.False(registry.TryGet(missing.Id, out _));
        var records = await repository.ListAsync();
        Assert.Contains(records, item => item.Id == missing.Id &&
            item.Status == WorkspaceRegistrationStatus.Missing);
    }

    [Fact]
    public async Task RegistrationService_RevalidateAll_RuntimeFailureDoesNotBlockLaterWorkspace()
    {
        var badRoot = Path.Combine(_directory, "a-bad");
        var goodRoot = Path.Combine(_directory, "b-good");
        Directory.CreateDirectory(badRoot);
        Directory.CreateDirectory(goodRoot);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var bad = CreatePersistedWorkspace(badRoot) with { DisplayName = "A bad" };
        var good = CreatePersistedWorkspace(goodRoot) with { DisplayName = "B good" };
        await repository.UpsertAsync(bad);
        await repository.UpsertAsync(good);
        var registry = new FailingWorkspaceRegistry();
        registry.FailRegisterIds.Add(bad.Id);
        var service = new WorkspaceRegistrationService(repository, registry);

        await service.RevalidateAllAsync();

        var storedBad = await repository.GetAsync(bad.Id);
        Assert.NotNull(storedBad);
        Assert.Equal(WorkspaceRegistrationStatus.ValidationFailed, storedBad.Status);
        Assert.True(registry.TryGet(good.Id, out _));
    }

    [Fact]
    public async Task RegistrationService_OneInvalidWorkspaceDoesNotBlockLaterValidWorkspace()
    {
        var validRoot = Path.Combine(_directory, "valid");
        var missingRoot = Path.Combine(_directory, "missing");
        Directory.CreateDirectory(validRoot);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();
        var missing = CreatePersistedWorkspace(missingRoot, createDirectory: false) with
        {
            DisplayName = "A missing"
        };
        var valid = CreatePersistedWorkspace(validRoot) with
        {
            DisplayName = "B valid"
        };
        await repository.UpsertAsync(missing);
        await repository.UpsertAsync(valid);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);

        await service.RevalidateAllAsync();

        Assert.False(registry.TryGet(missing.Id, out _));
        Assert.True(registry.TryGet(valid.Id, out _));
    }

    [Fact]
    public async Task RegistrationService_UpdateDisplayNameKeepsAvailableWorkspaceRegistered()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new WorkspaceRegistry();
        var service = new WorkspaceRegistrationService(repository, registry);
        await service.InitializeAsync();
        var workspace = await service.RegisterAsync(root, "Workspace", "test");

        var updated = await service.UpdateDisplayNameAsync(workspace.Id, "Renamed");

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.DisplayName);
        Assert.True(registry.TryGet(workspace.Id, out _));
    }

    [Fact]
    public async Task RegistrationService_RemoveInvalidatesOnlyRemovedWorkspace()
    {
        var firstRoot = Path.Combine(_directory, "first");
        var secondRoot = Path.Combine(_directory, "second");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new WorkspaceRegistry();
        var invalidator = new RecordingInvalidator();
        var service = new WorkspaceRegistrationService(repository, registry, invalidator);
        await service.InitializeAsync();
        var first = await service.RegisterAsync(firstRoot, "First", "test");
        var second = await service.RegisterAsync(secondRoot, "Second", "test");

        await service.RemoveAsync(first.Id);

        Assert.Contains(first.Id, invalidator.Invalidated);
        Assert.DoesNotContain(second.Id, invalidator.Invalidated);
    }

    [Fact]
    public async Task RegistrationService_CancellationPropagates()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var service = new WorkspaceRegistrationService(repository, new WorkspaceRegistry());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.InitializeAsync(cancellation.Token));
    }

    [Fact]
    public async Task RegistrationService_RuntimeFailureStatusIsSafe()
    {
        var root = Path.Combine(_directory, "workspace");
        Directory.CreateDirectory(root);
        var repository = new SqliteWorkspaceRepository(_databasePath);
        var registry = new FailingWorkspaceRegistry
        {
            FailAllRegisters = true,
            FailureMessage = $"sensitive {root}"
        };
        var service = new WorkspaceRegistrationService(repository, registry);
        await service.InitializeAsync();

        var workspace = await service.RegisterAsync(root, "Workspace", "test");

        Assert.Equal("workspace_runtime_registration_failed", workspace.SafeStatusCode);
        Assert.DoesNotContain("sensitive", workspace.SafeStatusCode, StringComparison.Ordinal);
        Assert.DoesNotContain(root, workspace.SafeStatusCode, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_CancellationPropagates()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => repository.InitializeAsync(cancellation.Token));
    }

    [Fact]
    public async Task Repository_InvalidDatabasePathMapsSafely()
    {
        var repository = new SqliteWorkspaceRepository("bad\0path.db");

        var exception = await Assert.ThrowsAsync<WorkspaceAccessException>(
            () => repository.InitializeAsync());

        Assert.Equal("workspace_persistence_failed", exception.SafeErrorCode);
        Assert.Equal("Workspace registrations could not be saved.", exception.SafeErrorMessage);
        Assert.DoesNotContain("bad", exception.SafeErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repository_RemoveUnknownIdIsIdempotent()
    {
        var repository = new SqliteWorkspaceRepository(_databasePath);
        await repository.InitializeAsync();

        await repository.RemoveAsync(WorkspaceId.NewId(), DateTimeOffset.UtcNow);

        Assert.Empty(await repository.ListAsync(includeRemoved: true));
    }

    private PersistedWorkspace CreatePersistedWorkspace(
        string rootPath,
        bool createDirectory = true)
    {
        if (createDirectory)
        {
            Directory.CreateDirectory(rootPath);
        }

        var id = WorkspaceId.NewId();
        var now = DateTimeOffset.UtcNow;
        var canonicalRoot = createDirectory
            ? WorkspaceRegistry.CreateDescriptor(
                id,
                rootPath,
                "Workspace",
                "test").CanonicalRootPath
            : Path.GetFullPath(rootPath);

        return new PersistedWorkspace(
            id,
            "Workspace",
            canonicalRoot,
            "test",
            now,
            now,
            WorkspaceRegistrationStatus.Available,
            null,
            true);
    }

    private string CreateConnectionString() =>
        new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        string type,
        string name)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*)
            FROM sqlite_master
            WHERE type = $type
              AND name = $name;
            """;
        command.Parameters.AddWithValue("$type", type);
        command.Parameters.AddWithValue("$name", name);
        var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
        return count > 0;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingInvalidator : IWorkspacePermissionInvalidator
    {
        public List<WorkspaceId> Invalidated { get; } = [];

        public void InvalidateWorkspacePermissions(WorkspaceId workspaceId)
        {
            Invalidated.Add(workspaceId);
        }
    }

    private sealed class FailingWorkspaceRegistry : IWorkspaceRegistry
    {
        private readonly WorkspaceRegistry _inner = new();

        public bool FailAllRegisters { get; set; }

        public string FailureMessage { get; set; } = "runtime failed";

        public HashSet<WorkspaceId> FailRegisterIds { get; } = [];

        public WorkspaceDescriptor Register(
            string rootPath,
            string? displayName = null,
            string? source = null)
        {
            if (FailAllRegisters)
            {
                throw new InvalidOperationException(FailureMessage);
            }

            return _inner.Register(rootPath, displayName, source);
        }

        public WorkspaceDescriptor Register(
            WorkspaceId workspaceId,
            string rootPath,
            string? displayName = null,
            string? source = null)
        {
            if (FailAllRegisters || FailRegisterIds.Contains(workspaceId))
            {
                throw new InvalidOperationException(FailureMessage);
            }

            return _inner.Register(workspaceId, rootPath, displayName, source);
        }

        public bool TryGet(WorkspaceId workspaceId, out WorkspaceDescriptor workspace) =>
            _inner.TryGet(workspaceId, out workspace);

        public IReadOnlyList<WorkspaceDescriptor> List() => _inner.List();

        public bool Remove(WorkspaceId workspaceId) => _inner.Remove(workspaceId);
    }
}
