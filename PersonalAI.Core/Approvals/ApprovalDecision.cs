namespace PersonalAI.Core.Approvals;

public sealed record ApprovalDecision(
    Guid RequestId,
    ApprovalDecisionKind Kind,
    DateTimeOffset DecidedAtUtc,
    string? Summary = null)
{
    public bool IsAllowed =>
        Kind is ApprovalDecisionKind.AllowOnce or ApprovalDecisionKind.AllowForTask;

    public bool CancelsTask => Kind == ApprovalDecisionKind.Cancel;

    public static ApprovalDecision AllowOnce(ApprovalRequest request, string? summary = null) =>
        Create(request, ApprovalDecisionKind.AllowOnce, summary);

    public static ApprovalDecision AllowForTask(ApprovalRequest request, string? summary = null) =>
        Create(request, ApprovalDecisionKind.AllowForTask, summary);

    public static ApprovalDecision Deny(ApprovalRequest request, string? summary = null) =>
        Create(request, ApprovalDecisionKind.Deny, summary);

    public static ApprovalDecision Cancel(ApprovalRequest request, string? summary = null) =>
        Create(request, ApprovalDecisionKind.Cancel, summary);

    private static ApprovalDecision Create(
        ApprovalRequest request,
        ApprovalDecisionKind kind,
        string? summary)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ApprovalDecision(
            request.RequestId,
            kind,
            DateTimeOffset.UtcNow,
            summary is null
                ? null
                : Tasks.TaskEventMetadata.SanitizeSummary(summary));
    }
}
