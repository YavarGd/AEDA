namespace PersonalAI.Core.Approvals;

public sealed record ApprovalCheckpoint(
    ApprovalRequest Request,
    ApprovalDecision? Decision = null);
