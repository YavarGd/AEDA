using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Approvals;

public sealed record ApprovalRequest(
    Guid RequestId,
    ApprovalScope Scope,
    string Title,
    string Body,
    DateTimeOffset RequestedAtUtc)
{
    public static ApprovalRequest Create(
        ApprovalScope scope,
        string title,
        string body) =>
        new(
            Guid.NewGuid(),
            scope,
            TaskEventMetadata.SanitizeSummary(title),
            TaskEventMetadata.SanitizeSummary(body),
            DateTimeOffset.UtcNow);
}
