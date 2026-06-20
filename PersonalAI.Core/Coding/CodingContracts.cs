using PersonalAI.Core.Approvals;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Core.Coding;

public readonly record struct PatchProposalId(Guid Value)
{
    public static PatchProposalId NewId() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}

public enum PatchProposalStatus
{
    Draft,
    ReadyForReview,
    ApprovalRequested,
    ApprovedForApply,
    Rejected,
    Superseded,
    Failed,
    Cancelled
}

public enum PatchProposalRisk
{
    Low,
    Medium,
    High,
    Blocked
}

public enum PatchProposalFileChangeKind
{
    Add,
    Modify,
    Delete,
    NoOp
}

public sealed record CodeChangeRequest(
    WorkspaceId WorkspaceId,
    string UserRequest,
    IReadOnlyList<string> RelativePaths,
    DateTimeOffset RequestedAtUtc,
    string Source = "user")
{
    public static CodeChangeRequest Create(
        WorkspaceId workspaceId,
        string userRequest,
        IReadOnlyList<string>? relativePaths = null,
        string source = "user") =>
        new(
            workspaceId,
            userRequest.Trim(),
            relativePaths ?? [],
            DateTimeOffset.UtcNow,
            source.Trim());
}

public sealed record CodeContextFile(
    WorkspaceId WorkspaceId,
    string RelativePath,
    string Content,
    string ContentHash,
    string EncodingName,
    long FileSizeBytes,
    bool IsTruncated,
    bool HadDecodingErrors);

public sealed record CodeContextSearchRequest(
    WorkspaceId WorkspaceId,
    string Query,
    string RelativeDirectory = ".",
    string? FilePattern = null,
    int MaxResults = 50);

public sealed record CodeContextSearchMatch(
    string RelativePath,
    int LineNumber,
    string Preview);

public sealed record CodeContextPack(
    WorkspaceId WorkspaceId,
    IReadOnlyList<CodeContextFile> Files,
    IReadOnlyList<CodeContextSearchMatch> SearchMatches,
    IReadOnlyList<string> SkippedSafeReasons,
    bool IsTruncated);

public sealed record CodeChangeStep(
    int Order,
    string Title,
    string Summary,
    IReadOnlyList<string> AffectedRelativePaths);

public sealed record CodeChangePlan(
    string Title,
    string Summary,
    IReadOnlyList<string> AffectedRelativePaths,
    IReadOnlyList<CodeChangeStep> Steps,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> Risks,
    PatchProposalValidationPlan ValidationPlan,
    IReadOnlyList<PatchProposalSource> ContextSources);

public sealed record PatchProposalSource(
    WorkspaceId WorkspaceId,
    string RelativePath,
    string ContentHash,
    string SourceKind);

public sealed record PatchProposalHunk(
    int OldStart,
    int OldLineCount,
    int NewStart,
    int NewLineCount,
    IReadOnlyList<string> Lines);

public sealed record PatchProposalFile(
    string RelativePath,
    PatchProposalFileChangeKind ChangeKind,
    string? OriginalContent,
    string? ProposedContent,
    string OriginalContentHash,
    string ProposedContentHash,
    string UnifiedDiff,
    IReadOnlyList<PatchProposalHunk> Hunks);

public sealed record PatchProposalValidationCommand(
    string Command,
    string Rationale);

public sealed record PatchProposalValidationPlan(
    IReadOnlyList<PatchProposalValidationCommand> SuggestedCommands,
    IReadOnlyList<string> ManualChecks);

public sealed record PatchProposalReviewFinding(
    PatchProposalRisk Severity,
    string SafeReasonCode,
    string Summary,
    string? RelativePath = null);

public sealed record PatchProposal(
    PatchProposalId Id,
    WorkspaceId WorkspaceId,
    string Title,
    string Summary,
    PatchProposalStatus Status,
    PatchProposalRisk Risk,
    IReadOnlyList<string> RiskReasons,
    IReadOnlyList<PatchProposalFile> Files,
    IReadOnlyList<PatchProposalSource> Sources,
    PatchProposalValidationPlan ValidationPlan,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    ApprovalRequest? ApprovalRequest = null);

public sealed record PatchProposalCreateRequest(
    WorkspaceId WorkspaceId,
    string Title,
    string Summary,
    IReadOnlyList<PatchProposalFileEdit> FileEdits,
    IReadOnlyList<PatchProposalSource> Sources,
    PatchProposalValidationPlan? ValidationPlan = null);

public sealed record PatchProposalFileEdit(
    string RelativePath,
    string? OriginalContent,
    string? ProposedContent,
    PatchProposalFileChangeKind ChangeKind = PatchProposalFileChangeKind.Modify);
