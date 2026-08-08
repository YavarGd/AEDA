namespace PersonalAI.Core.Approvals;

public sealed class InMemoryApprovalCheckpointStore :
    IApprovalCheckpointStore,
    IApprovalCheckpointQueryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, ApprovalRequest> _requests = [];
    private readonly Dictionary<Guid, ApprovalDecision> _decisions = [];
    private readonly Dictionary<ScopeKey, Guid> _latestRequests = [];
    private readonly Dictionary<ScopeKey, ApprovalDecision> _taskScopedAllows = [];
    private readonly HashSet<Guid> _consumedDecisions = [];

    public ValueTask<ApprovalRequest> RequestAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            if (_requests.TryGetValue(request.RequestId, out var existing) && existing != request)
            {
                throw new InvalidOperationException("approval_request_id_conflict");
            }

            var key = ScopeKey.From(request.Scope);
            if (_latestRequests.TryGetValue(key, out var previousRequestId) &&
                previousRequestId != request.RequestId)
            {
                _requests.Remove(previousRequestId);
                _decisions.Remove(previousRequestId);
            }

            _requests[request.RequestId] = request;
            _latestRequests[key] = request.RequestId;
        }

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
        lock (_gate)
        {
            var key = ScopeKey.From(request.Scope);
            if (!_requests.TryGetValue(request.RequestId, out var issuedRequest) ||
                issuedRequest != request ||
                !_latestRequests.TryGetValue(key, out var latestRequestId) ||
                latestRequestId != request.RequestId)
            {
                throw new InvalidOperationException("approval_request_not_issued");
            }

            if (_decisions.TryGetValue(request.RequestId, out var existingDecision))
            {
                if (existingDecision.Kind == decision)
                {
                    return ValueTask.FromResult(existingDecision);
                }

                throw new InvalidOperationException("approval_request_already_decided");
            }

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
                _taskScopedAllows[key] = approvalDecision;
            }

            return ValueTask.FromResult(approvalDecision);
        }
    }

    public ValueTask<ApprovalDecision?> FindReusableDecisionAsync(
        ApprovalScope scope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var key = ScopeKey.From(scope);
            if (!_taskScopedAllows.TryGetValue(key, out var decision) ||
                !_latestRequests.TryGetValue(key, out var requestId) ||
                requestId != decision.RequestId ||
                !_decisions.TryGetValue(requestId, out var currentDecision) ||
                currentDecision != decision)
            {
                return ValueTask.FromResult<ApprovalDecision?>(null);
            }

            return ValueTask.FromResult<ApprovalDecision?>(decision);
        }
    }

    public ValueTask<bool> TryConsumeAsync(
        ApprovalRequest request,
        ApprovalDecision decision,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(decision);
        lock (_gate)
        {
            var key = ScopeKey.From(request.Scope);
            var valid = decision.Kind == ApprovalDecisionKind.AllowOnce &&
                decision.RequestId == request.RequestId &&
                _requests.TryGetValue(request.RequestId, out var issuedRequest) &&
                issuedRequest == request &&
                _latestRequests.TryGetValue(key, out var latestRequestId) &&
                latestRequestId == request.RequestId &&
                _decisions.TryGetValue(request.RequestId, out var issuedDecision) &&
                issuedDecision == decision;
            return ValueTask.FromResult(valid && _consumedDecisions.Add(decision.DecisionId));
        }
    }

    public ValueTask<IReadOnlyList<ApprovalCheckpoint>> ListPendingAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var selected = _requests.Values
                .Where(request => !_decisions.ContainsKey(request.RequestId))
                .OrderByDescending(request => request.RequestedAtUtc)
                .ThenBy(request => request.RequestId)
                .Take(Math.Clamp(limit, 1, 100))
                .Select(request => new ApprovalCheckpoint(request))
                .ToArray();
            return ValueTask.FromResult<IReadOnlyList<ApprovalCheckpoint>>(selected);
        }
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
