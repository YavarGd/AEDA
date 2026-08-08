namespace PersonalAI.Core.Approvals;

public interface IApprovalCheckpointStore
{
    ValueTask<ApprovalRequest> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default);

    ValueTask<ApprovalDecision> DecideAsync(
        ApprovalRequest request,
        ApprovalDecisionKind decision,
        string? summary = null,
        CancellationToken cancellationToken = default);

    ValueTask<ApprovalDecision?> FindReusableDecisionAsync(
        ApprovalScope scope,
        CancellationToken cancellationToken = default);

    ValueTask<bool> TryConsumeAsync(
        ApprovalRequest request,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default);
}
