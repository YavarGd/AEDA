namespace PersonalAI.Core.Approvals;

public enum ApprovalKind
{
    WorkspacePermission,
    FileWrite,
    CommandExecution,
    BrowserSubmission,
    OfficeDocumentEdit,
    MemoryWrite,
    ApprovePatchProposal,
    RejectPatchProposal,
    ApproveFutureApply,
    Generic
}
