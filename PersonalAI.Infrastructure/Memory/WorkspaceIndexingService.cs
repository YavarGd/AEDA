using PersonalAI.Core.Memory;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Memory;

public sealed class WorkspaceIndexingService(
    IWorkspaceReader reader,
    IEmbeddingProvider? embeddingProvider = null,
    IVectorIndex? vectorIndex = null,
    int maxFileSizeBytes = 256 * 1024,
    int maxChunksPerRun = 200) : IWorkspaceIndexingService
{
    private static readonly HashSet<string> ExcludedDirectoryNames = new(
        [".git", "bin", "obj", "node_modules", ".vs", ".idea"],
        StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<WorkspaceId, WorkspaceIndexingStatus> _statuses = [];
    private readonly Dictionary<WorkspaceId, List<KnowledgeDocument>> _documents = [];
    private readonly Dictionary<WorkspaceId, List<KnowledgeChunk>> _pendingChunks = [];
    private readonly HashSet<WorkspaceId> _cancelled = [];

    public async Task EnqueueWorkspaceScanAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        SetStatus(workspaceId, WorkspaceIndexingState.Queued);
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(workspaceId, WorkspaceIndexingState.Running);

        var indexed = 0;
        var skipped = 0;
        try
        {
            foreach (var entry in reader.ListDirectory(
                         workspaceId,
                         ".",
                         500,
                         includeHidden: false,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_cancelled.Remove(workspaceId))
                {
                    SetStatus(workspaceId, WorkspaceIndexingState.Cancelled, indexed, skipped);
                    return;
                }

                if (entry.Type == WorkspaceEntryType.Directory)
                {
                    if (ExcludedDirectoryNames.Contains(entry.Name))
                    {
                        skipped++;
                    }

                    continue;
                }

                if (entry.SizeBytes > maxFileSizeBytes)
                {
                    skipped++;
                    continue;
                }

                var fileIndexed = await IndexFileAsync(
                    workspaceId,
                    entry.RelativePath,
                    cancellationToken);
                if (fileIndexed)
                {
                    indexed++;
                }
                else
                {
                    skipped++;
                }

                if (PendingChunkCount(workspaceId) >= maxChunksPerRun)
                {
                    break;
                }
            }

            SetStatus(workspaceId, WorkspaceIndexingState.Completed, indexed, skipped);
        }
        catch (OperationCanceledException)
        {
            SetStatus(workspaceId, WorkspaceIndexingState.Cancelled, indexed, skipped);
            throw;
        }
        catch (WorkspaceAccessException)
        {
            SetStatus(workspaceId, WorkspaceIndexingState.Failed, indexed, skipped, "workspace_indexing_failed");
        }
    }

    public async Task EnqueueFileReindexAsync(
        WorkspaceId workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        SetStatus(workspaceId, WorkspaceIndexingState.Running);
        var indexed = await IndexFileAsync(workspaceId, relativePath, cancellationToken);
        SetStatus(
            workspaceId,
            WorkspaceIndexingState.Completed,
            indexed ? 1 : 0,
            indexed ? 0 : 1);
    }

    public Task CancelAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cancelled.Add(workspaceId);
        SetStatus(workspaceId, WorkspaceIndexingState.Cancelled);
        return Task.CompletedTask;
    }

    public Task<WorkspaceIndexingStatus> GetStatusAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_statuses.TryGetValue(workspaceId, out var status)
            ? status
            : new WorkspaceIndexingStatus(workspaceId, WorkspaceIndexingState.Idle, 0, 0, 0));
    }

    public Task<IReadOnlyList<KnowledgeDocument>> ListIndexedDocumentsAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<KnowledgeDocument>>(
            _documents.TryGetValue(workspaceId, out var documents)
                ? documents.ToArray()
                : []);
    }

    public Task ClearWorkspaceIndexAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _documents.Remove(workspaceId);
        _pendingChunks.Remove(workspaceId);
        SetStatus(workspaceId, WorkspaceIndexingState.Idle);
        return Task.CompletedTask;
    }

    private async Task<bool> IndexFileAsync(
        WorkspaceId workspaceId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        WorkspaceTextFile file;
        try
        {
            file = reader.ReadTextFile(
                workspaceId,
                relativePath,
                maxFileSizeBytes,
                cancellationToken);
        }
        catch (WorkspaceAccessException)
        {
            return false;
        }

        var source = new KnowledgeSource(
            KnowledgeSourceType.WorkspaceFile,
            DateTimeOffset.UtcNow,
            workspaceId,
            file.RelativePath);
        var documentId = $"{workspaceId}:{file.RelativePath}".ToLowerInvariant();
        var result = KnowledgeChunker.ChunkText(
            documentId,
            file.RelativePath,
            file.Content,
            source,
            ChunkingOptions.Default with { MaxChunks = maxChunksPerRun });

        AddDocument(workspaceId, result.Document);
        if (result.IsSkipped)
        {
            return false;
        }

        if (embeddingProvider is null || vectorIndex is null ||
            embeddingProvider.GetStatus().Status != EmbeddingProviderStatus.Available)
        {
            AddPendingChunks(workspaceId, result.Chunks);
            return true;
        }

        foreach (var chunk in result.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await embeddingProvider.EmbedAsync(
                new EmbeddingRequest([chunk.Text]),
                cancellationToken);
            await vectorIndex.UpsertAsync(
                new VectorDocument(
                    chunk.Id,
                    embedding.Vectors[0],
                    chunk.Text,
                    MemoryScope.Workspace,
                    WorkspaceId: workspaceId.ToString(),
                    SourceId: chunk.Id),
                cancellationToken);
        }

        return true;
    }

    private void AddDocument(WorkspaceId workspaceId, KnowledgeDocument document)
    {
        if (!_documents.TryGetValue(workspaceId, out var documents))
        {
            documents = [];
            _documents[workspaceId] = documents;
        }

        documents.RemoveAll(item => item.Id == document.Id);
        documents.Add(document);
    }

    private void AddPendingChunks(
        WorkspaceId workspaceId,
        IReadOnlyList<KnowledgeChunk> chunks)
    {
        if (!_pendingChunks.TryGetValue(workspaceId, out var pendingChunks))
        {
            pendingChunks = [];
            _pendingChunks[workspaceId] = pendingChunks;
        }

        pendingChunks.AddRange(chunks);
    }

    private int PendingChunkCount(WorkspaceId workspaceId) =>
        _pendingChunks.TryGetValue(workspaceId, out var chunks) ? chunks.Count : 0;

    private void SetStatus(
        WorkspaceId workspaceId,
        WorkspaceIndexingState state,
        int documentsIndexed = 0,
        int documentsSkipped = 0,
        string? safeReasonCode = null)
    {
        _statuses[workspaceId] = new WorkspaceIndexingStatus(
            workspaceId,
            state,
            documentsIndexed,
            documentsSkipped,
            PendingChunkCount(workspaceId),
            safeReasonCode);
    }
}
