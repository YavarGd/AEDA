namespace PersonalAI.Core.Approvals;

public interface IApprovalCheckpointQueryStore
{
    ValueTask<IReadOnlyList<ApprovalCheckpoint>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
