using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;
using PersonalAI.Infrastructure.Memory;

namespace PersonalAI.Tests.Memory;

public sealed class WorkspaceIndexingRetrievalTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "PersonalAI.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WorkspaceIndexing_EnqueueScanSkipsExcludedAndLargeFiles()
    {
        var workspaceId = WorkspaceId.NewId();
        var reader = new FakeWorkspaceReader(workspaceId);
        reader.AddDirectory(
            ".",
            [
                new WorkspaceDirectoryEntry(".git", ".git", WorkspaceEntryType.Directory, null, DateTimeOffset.UtcNow, false, string.Empty),
                new WorkspaceDirectoryEntry("large.txt", "large.txt", WorkspaceEntryType.File, 999_999, DateTimeOffset.UtcNow, false, ".txt"),
                new WorkspaceDirectoryEntry("notes.txt", "notes.txt", WorkspaceEntryType.File, 20, DateTimeOffset.UtcNow, false, ".txt")
            ]);
        reader.AddFile("notes.txt", "alpha project notes");
        var service = new WorkspaceIndexingService(reader, maxFileSizeBytes: 100, maxChunksPerRun: 10);

        await service.EnqueueWorkspaceScanAsync(workspaceId);
        var status = await service.GetStatusAsync(workspaceId);
        var documents = await service.ListIndexedDocumentsAsync(workspaceId);

        Assert.Equal(WorkspaceIndexingState.Completed, status.State);
        Assert.Equal(1, status.DocumentsIndexed);
        Assert.Equal(2, status.DocumentsSkipped);
        Assert.True(status.PendingChunks > 0);
        Assert.Single(documents);
        Assert.Equal(0, reader.WriteCount);
    }

    [Fact]
    public async Task WorkspaceIndexing_IndexesWithFakeEmbeddingProviderAndClears()
    {
        var workspaceId = WorkspaceId.NewId();
        var reader = new FakeWorkspaceReader(workspaceId);
        reader.AddFile("notes.txt", "alpha project notes");
        var vectorIndex = new InMemoryVectorIndex(4);
        var service = new WorkspaceIndexingService(
            reader,
            new FakeEmbeddingProvider(),
            vectorIndex,
            maxFileSizeBytes: 1000,
            maxChunksPerRun: 10);

        await service.EnqueueFileReindexAsync(workspaceId, "notes.txt");
        var vectorStatus = await vectorIndex.GetStatusAsync();
        var documents = await service.ListIndexedDocumentsAsync(workspaceId);

        Assert.Equal(1, vectorStatus.DocumentCount);
        Assert.Single(documents);

        await service.ClearWorkspaceIndexAsync(workspaceId);
        Assert.Empty(await service.ListIndexedDocumentsAsync(workspaceId));
    }

    [Fact]
    public async Task WorkspaceIndexing_CancellationIsHonored()
    {
        var workspaceId = WorkspaceId.NewId();
        var reader = new FakeWorkspaceReader(workspaceId);
        var service = new WorkspaceIndexingService(reader);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.EnqueueWorkspaceScanAsync(workspaceId, cts.Token));
    }

    [Fact]
    public async Task Retrieval_ReturnsBoundedTextSearchFallbackWithSources()
    {
        var repository = new SqliteMemoryRepository(Path.Combine(_directory, "retrieval.db"));
        await repository.InitializeAsync();
        await repository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            "Workspace memory about SQLite retrieval.",
            MemoryKind.ProjectFact,
            MemoryScope.Workspace,
            WorkspaceId.NewId()));
        await repository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            "Another normal SQLite retrieval note.",
            MemoryKind.ProjectFact,
            MemoryScope.Global));
        await repository.CreateAsync(MemoryRepositoryTests.CreateMemory(
            "Sensitive finance note.",
            MemoryKind.ProjectFact,
            MemoryScope.Global) with
        {
            Sensitivity = MemorySensitivity.Sensitive
        });
        var service = new RetrievalService(repository);

        var pack = await service.RetrieveAsync(new RetrievalQuery(
            "SQLite retrieval",
            MaxItems: 1));

        var item = Assert.Single(pack.Items);
        Assert.False(pack.UsedEmbeddingSearch);
        Assert.True(pack.IsTruncated);
        Assert.Equal(RetrievalContextItemKind.Memory, item.Kind);
        Assert.Contains("SQLite", item.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("explicit_user_save", item.Source.SourceType);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeWorkspaceReader(WorkspaceId workspaceId) : IWorkspaceReader
    {
        private readonly Dictionary<string, IReadOnlyList<WorkspaceDirectoryEntry>> _directories =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _files =
            new(StringComparer.OrdinalIgnoreCase);

        public int WriteCount { get; private set; }

        public WorkspaceDescriptor GetWorkspace(WorkspaceId id) =>
            new(workspaceId, "Test", "C:\\safe", DateTimeOffset.UtcNow, WorkspaceAccessPolicy.ReadOnly);

        public IReadOnlyList<WorkspaceDirectoryEntry> ListDirectory(
            WorkspaceId id,
            string relativePath,
            int maxEntries,
            bool includeHidden,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _directories.TryGetValue(relativePath, out var entries)
                ? entries
                : _files.Keys
                    .Select(path => new WorkspaceDirectoryEntry(
                        Path.GetFileName(path),
                        path,
                        WorkspaceEntryType.File,
                        _files[path].Length,
                        DateTimeOffset.UtcNow,
                        false,
                        Path.GetExtension(path)))
                    .ToArray();
        }

        public WorkspaceTextFile ReadTextFile(
            WorkspaceId id,
            string relativePath,
            int maxCharacters,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = _files[relativePath];
            return new WorkspaceTextFile(
                relativePath,
                content.Length > maxCharacters ? content[..maxCharacters] : content,
                "utf-8",
                content.Length,
                content.Length > maxCharacters,
                false);
        }

        public WorkspaceSearchResult SearchText(
            WorkspaceId id,
            string query,
            string relativeDirectory,
            string? filePattern,
            bool matchCase,
            int maxResults,
            CancellationToken cancellationToken = default) =>
            new(query, relativeDirectory, [], false, 0, 0);

        public void AddDirectory(
            string relativePath,
            IReadOnlyList<WorkspaceDirectoryEntry> entries) =>
            _directories[relativePath] = entries;

        public void AddFile(string relativePath, string content) =>
            _files[relativePath] = content;
    }
}
