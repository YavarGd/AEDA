using PersonalAI.Core.Memory;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Infrastructure.Memory;

public sealed class WorkspaceIndexingService(
    IWorkspaceReader reader,
    IEmbeddingProvider? embeddingProvider = null,
    IVectorIndex? vectorIndex = null,
    IKnowledgeRepository? knowledgeRepository = null,
    ITaskRuntime? taskRuntime = null,
    int maxFileSizeBytes = 256 * 1024,
    int maxChunksPerRun = 200,
    int maxFilesPerRun = 500,
    int embeddingBatchSize = 8) : IWorkspaceIndexingService
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
        _ = reader.GetWorkspace(workspaceId);
        var task = taskRuntime is null
            ? null
            : await taskRuntime.StartTaskAsync(
                "Workspace indexing",
                "workspace_indexing",
                cancellationToken: cancellationToken);
        SetStatus(workspaceId, WorkspaceIndexingState.Queued);
        cancellationToken.ThrowIfCancellationRequested();
        SetStatus(workspaceId, WorkspaceIndexingState.Running);
        await AppendAsync(task?.Id, TaskEventKind.WorkspaceIndexingStarted, "Workspace indexing started.", cancellationToken);

        var indexed = 0;
        var skipped = 0;
        var unchanged = 0;
        var queued = 0;
        try
        {
            foreach (var entry in reader.ListDirectory(
                         workspaceId,
                         ".",
                         maxFilesPerRun,
                         includeHidden: false,
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_cancelled.Remove(workspaceId))
                {
                    SetStatus(workspaceId, WorkspaceIndexingState.Cancelled, indexed, skipped, unchanged, queued);
                    await AppendAsync(task?.Id, TaskEventKind.WorkspaceIndexingCancelled, "Workspace indexing cancelled.", CancellationToken.None);
                    if (task is not null)
                    {
                        await taskRuntime!.CancelTaskAsync(task.Id, TaskCancellationReason.UserRequested, CancellationToken.None);
                    }
                    return;
                }

                if (entry.Type == WorkspaceEntryType.Directory)
                {
                    if (ExcludedDirectoryNames.Contains(entry.Name))
                    {
                        skipped++;
                        await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileSkipped, "Workspace file skipped.", cancellationToken);
                    }

                    continue;
                }

                queued++;
                await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileQueued, "Workspace file queued.", cancellationToken);
                if (entry.SizeBytes > maxFileSizeBytes)
                {
                    skipped++;
                    await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileSkipped, "Workspace file skipped.", cancellationToken);
                    continue;
                }

                var result = await IndexFileAsync(
                    workspaceId,
                    entry.RelativePath,
                    task?.Id,
                    cancellationToken);
                if (result == FileIndexResult.Indexed)
                {
                    indexed++;
                    await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileIndexed, "Workspace file indexed.", cancellationToken);
                }
                else if (result == FileIndexResult.Unchanged)
                {
                    unchanged++;
                    await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileSkipped, "Workspace file unchanged.", cancellationToken);
                }
                else
                {
                    skipped++;
                    await AppendAsync(task?.Id, TaskEventKind.WorkspaceFileSkipped, "Workspace file skipped.", cancellationToken);
                }

                if (PendingChunkCount(workspaceId) >= maxChunksPerRun)
                {
                    break;
                }
            }

            SetStatus(workspaceId, WorkspaceIndexingState.Completed, indexed, skipped, unchanged, queued);
            await AppendAsync(task?.Id, TaskEventKind.WorkspaceIndexingCompleted, "Workspace indexing completed.", cancellationToken);
            if (task is not null)
            {
                await taskRuntime!.CompleteTaskAsync(task.Id, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(workspaceId, WorkspaceIndexingState.Cancelled, indexed, skipped, unchanged, queued);
            await AppendAsync(task?.Id, TaskEventKind.WorkspaceIndexingCancelled, "Workspace indexing cancelled.", CancellationToken.None);
            throw;
        }
        catch (WorkspaceAccessException)
        {
            SetStatus(workspaceId, WorkspaceIndexingState.Failed, indexed, skipped, unchanged, queued, "workspace_indexing_failed");
            await AppendAsync(task?.Id, TaskEventKind.WorkspaceIndexingFailed, "Workspace indexing failed.", CancellationToken.None);
            if (task is not null)
            {
                await taskRuntime!.FailTaskAsync(task.Id, "workspace_indexing_failed", CancellationToken.None);
            }
        }
    }

    public async Task EnqueueFileReindexAsync(
        WorkspaceId workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        _ = reader.GetWorkspace(workspaceId);
        SetStatus(workspaceId, WorkspaceIndexingState.Running);
        var result = await IndexFileAsync(workspaceId, relativePath, taskId: null, cancellationToken);
        SetStatus(
            workspaceId,
            WorkspaceIndexingState.Completed,
            result == FileIndexResult.Indexed ? 1 : 0,
            result == FileIndexResult.Skipped ? 1 : 0,
            result == FileIndexResult.Unchanged ? 1 : 0,
            FilesQueued: 1);
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
            : new WorkspaceIndexingStatus(workspaceId, WorkspaceIndexingState.Idle, 0, 0, 0, 0, 0));
    }

    public Task<IReadOnlyList<KnowledgeDocument>> ListIndexedDocumentsAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (knowledgeRepository is not null)
        {
            return knowledgeRepository.ListDocumentsAsync(
                workspaceId.ToString(),
                cancellationToken: cancellationToken);
        }

        return Task.FromResult<IReadOnlyList<KnowledgeDocument>>(
            _documents.TryGetValue(workspaceId, out var documents)
                ? documents.ToArray()
                : []);
    }

    public async Task ClearWorkspaceIndexAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _documents.Remove(workspaceId);
        _pendingChunks.Remove(workspaceId);
        if (knowledgeRepository is not null)
        {
            await knowledgeRepository.ClearWorkspaceAsync(workspaceId.ToString(), cancellationToken);
        }

        if (vectorIndex is SqliteVectorIndex sqliteVectorIndex)
        {
            await sqliteVectorIndex.DeleteBySourcePrefixAsync(
                $"{workspaceId}:",
                cancellationToken);
        }

        SetStatus(workspaceId, WorkspaceIndexingState.Idle);
    }

    private async Task<FileIndexResult> IndexFileAsync(
        WorkspaceId workspaceId,
        string relativePath,
        TaskId? taskId,
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
            return FileIndexResult.Skipped;
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

        if (result.IsSkipped)
        {
            AddDocument(workspaceId, result.Document);
            if (knowledgeRepository is not null)
            {
                await knowledgeRepository.UpsertDocumentAsync(result.Document, [], cancellationToken);
            }

            return FileIndexResult.Skipped;
        }

        var existing = knowledgeRepository is null
            ? _documents.TryGetValue(workspaceId, out var documents)
                ? documents.FirstOrDefault(document => document.Id == result.Document.Id)
                : null
            : await knowledgeRepository.GetDocumentAsync(result.Document.Id, cancellationToken);
        if (existing is not null &&
            string.Equals(existing.ContentHash, result.Document.ContentHash, StringComparison.Ordinal))
        {
            return FileIndexResult.Unchanged;
        }

        AddDocument(workspaceId, result.Document with { State = DocumentIndexState.Indexed });
        if (knowledgeRepository is not null)
        {
            await knowledgeRepository.UpsertDocumentAsync(
                result.Document with { State = DocumentIndexState.Indexed },
                result.Chunks,
                cancellationToken);
        }

        if (vectorIndex is SqliteVectorIndex sqliteVectorIndex)
        {
            await sqliteVectorIndex.DeleteBySourcePrefixAsync(result.Document.Id + ":", cancellationToken);
        }

        if (embeddingProvider is null || vectorIndex is null ||
            embeddingProvider.GetStatus().Status != EmbeddingProviderStatus.Available)
        {
            AddPendingChunks(workspaceId, result.Chunks);
            return FileIndexResult.Indexed;
        }

        foreach (var batch in result.Chunks.Chunk(Math.Clamp(embeddingBatchSize, 1, embeddingProvider.ModelInfo.MaxBatchSize)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var embedding = await embeddingProvider.EmbedAsync(
                new EmbeddingRequest(batch.Select(chunk => chunk.Text).ToArray()),
                cancellationToken);
            for (var index = 0; index < batch.Length; index++)
            {
                var chunk = batch[index];
                await vectorIndex.UpsertAsync(
                    new VectorDocument(
                        chunk.Id,
                        embedding.Vectors[index],
                        chunk.Text,
                        MemoryScope.Workspace,
                        WorkspaceId: workspaceId.ToString(),
                        SourceKind: KnowledgeSourceType.WorkspaceFile.ToString(),
                        SourceId: chunk.Id,
                        Metadata: new Dictionary<string, string>
                        {
                            ["document_id"] = chunk.DocumentId,
                            ["relative_path"] = chunk.Source.RelativePath ?? string.Empty,
                            ["content_hash"] = chunk.ContentHash,
                            ["ordinal"] = chunk.Ordinal.ToString()
                        }),
                    cancellationToken);
            }
        }

        return FileIndexResult.Indexed;
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
        int DocumentsUnchanged = 0,
        int FilesQueued = 0,
        string? safeReasonCode = null)
    {
        _statuses[workspaceId] = new WorkspaceIndexingStatus(
            workspaceId,
            state,
            documentsIndexed,
            documentsSkipped,
            DocumentsUnchanged,
            FilesQueued,
            PendingChunkCount(workspaceId),
            safeReasonCode);
    }

    private async ValueTask AppendAsync(
        TaskId? taskId,
        TaskEventKind kind,
        string summary,
        CancellationToken cancellationToken)
    {
        if (taskId is null || taskRuntime is null)
        {
            return;
        }

        await taskRuntime.AppendEventAsync(taskId.Value, kind, summary, cancellationToken);
    }

    private enum FileIndexResult
    {
        Indexed,
        Skipped,
        Unchanged
    }
}
