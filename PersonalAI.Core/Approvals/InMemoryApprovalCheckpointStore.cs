using System.Collections.Concurrent;

namespace PersonalAI.Core.Approvals;

public sealed class InMemoryApprovalCheckpointStore : IApprovalCheckpointStore
{
    private readonly ConcurrentDictionary<Guid, ApprovalRequest> _requests = [];
    private readonly ConcurrentDictionary<Guid, ApprovalDecision> _decisions = [];
    private readonly ConcurrentDictionary<ScopeKey, ApprovalDecision> _taskScopedAllows = [];

    public ValueTask<ApprovalRequest> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        _requests[request.RequestId] = request;
        return ValueTask.FromResult(request);
    }

    public ValueTask<ApprovalDecision> DecideAsync(
        ApprovalRequest request,
        ApprovalDecisionKind decision,
        string? summary = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var approvalDecision = decision switch
        {
            ApprovalDecisionKind.AllowOnce => ApprovalDecision.AllowOnce(request, summary),
            ApprovalDecisionKind.AllowForTask => ApprovalDecision.AllowForTask(request, summary),
            ApprovalDecisionKind.Deny => ApprovalDecision.Deny(request, summary),
            ApprovalDecisionKind.Cancel => ApprovalDecision.Cancel(request, summary),
            _ => ApprovalDecision.Deny(request, "Unsupported approval decision.")
        };

        _decisions[request.RequestId] = approvalDecision;

        if (approvalDecision.Kind == ApprovalDecisionKind.AllowForTask)
        {
            _taskScopedAllows[ScopeKey.From(request.Scope)] = approvalDecision;
        }

        return ValueTask.FromResult(approvalDecision);
    }

    public ValueTask<ApprovalDecision?> FindReusableDecisionAsync(
        ApprovalScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _taskScopedAllows.TryGetValue(ScopeKey.From(scope), out var decision);
        return ValueTask.FromResult(decision);
    }

    private sealed record ScopeKey(
        Tasks.TaskId TaskId,
        ApprovalKind Kind,
        string ResourceScope)
    {
        public static ScopeKey From(ApprovalScope scope) =>
            new(scope.TaskId, scope.Kind, scope.NormalizedResourceScope);
    }
}
