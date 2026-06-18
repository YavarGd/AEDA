using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Memory;

public enum WorkspaceIndexingState
{
    Idle,
    Queued,
    Running,
    Completed,
    Cancelled,
    Failed
}

public sealed record WorkspaceIndexingStatus(
    WorkspaceId WorkspaceId,
    WorkspaceIndexingState State,
    int DocumentsIndexed,
    int DocumentsSkipped,
    int PendingChunks,
    string? SafeReasonCode = null);

public interface IWorkspaceIndexingService
{
    Task EnqueueWorkspaceScanAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task EnqueueFileReindexAsync(
        WorkspaceId workspaceId,
        string relativePath,
        CancellationToken cancellationToken = default);

    Task CancelAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<WorkspaceIndexingStatus> GetStatusAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeDocument>> ListIndexedDocumentsAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);

    Task ClearWorkspaceIndexAsync(
        WorkspaceId workspaceId,
        CancellationToken cancellationToken = default);
}
