using PersonalAI.Core.Approvals;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Approvals;

public sealed class ApprovalCheckpointTests
{
    [Fact]
    public async Task Decisions_CoverAllowDenyAndCancel()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var request = await store.RequestAsync(CreateRequest());

        var allowOnce = await store.DecideAsync(
            request,
            ApprovalDecisionKind.AllowOnce,
            "approved");
        var deny = await store.DecideAsync(
            request,
            ApprovalDecisionKind.Deny,
            "denied");
        var cancel = await store.DecideAsync(
            request,
            ApprovalDecisionKind.Cancel,
            "cancelled");

        Assert.True(allowOnce.IsAllowed);
        Assert.False(deny.IsAllowed);
        Assert.False(cancel.IsAllowed);
        Assert.True(cancel.CancelsTask);
    }

    [Fact]
    public async Task AllowForTask_IsScopedToSameTaskAndResource()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var taskId = TaskId.NewId();
        var request = await store.RequestAsync(CreateRequest(taskId, "workspace:1"));

        await store.DecideAsync(request, ApprovalDecisionKind.AllowForTask);

        var sameScope = await store.FindReusableDecisionAsync(
            new ApprovalScope(taskId, ApprovalKind.WorkspacePermission, "WORKSPACE:1"));
        var differentTask = await store.FindReusableDecisionAsync(
            new ApprovalScope(TaskId.NewId(), ApprovalKind.WorkspacePermission, "workspace:1"));
        var differentResource = await store.FindReusableDecisionAsync(
            new ApprovalScope(taskId, ApprovalKind.WorkspacePermission, "workspace:2"));

        Assert.NotNull(sameScope);
        Assert.Null(differentTask);
        Assert.Null(differentResource);
    }

    [Fact]
    public void Request_SanitizesTokenLikeText()
    {
        var request = ApprovalRequest.Create(
            new ApprovalScope(TaskId.NewId(), ApprovalKind.Generic, "resource"),
            "token=abc",
            "access_token=abc");

        Assert.Equal("[redacted]", request.Title);
        Assert.Equal("[redacted]", request.Body);
    }

    private static ApprovalRequest CreateRequest(
        TaskId? taskId = null,
        string resource = "workspace:root") =>
        ApprovalRequest.Create(
            new ApprovalScope(
                taskId ?? TaskId.NewId(),
                ApprovalKind.WorkspacePermission,
                resource),
            "Allow read?",
            "Read from registered workspace.");
}
