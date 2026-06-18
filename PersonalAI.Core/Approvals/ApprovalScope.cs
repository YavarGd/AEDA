using PersonalAI.Core.Tasks;

namespace PersonalAI.Core.Approvals;

public sealed record ApprovalScope(
    TaskId TaskId,
    ApprovalKind Kind,
    string ResourceScope)
{
    public string NormalizedResourceScope =>
        ResourceScope.Trim().ToUpperInvariant();
}
