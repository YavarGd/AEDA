using PersonalAI.Core.Approvals;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Tests.Approvals;

public sealed class ApprovalCheckpointTests
{
    [Fact]
    public async Task Decisions_CoverAllowDenyAndCancel()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var allowRequest = await store.RequestAsync(CreateRequest());
        var denyRequest = await store.RequestAsync(CreateRequest(TaskId.NewId(), "workspace:deny"));
        var cancelRequest = await store.RequestAsync(CreateRequest(TaskId.NewId(), "workspace:cancel"));

        var allowOnce = await store.DecideAsync(
            allowRequest,
            ApprovalDecisionKind.AllowOnce,
            "approved");
        var deny = await store.DecideAsync(
            denyRequest,
            ApprovalDecisionKind.Deny,
            "denied");
        var cancel = await store.DecideAsync(
            cancelRequest,
            ApprovalDecisionKind.Cancel,
            "cancelled");

        Assert.True(allowOnce.IsAllowed);
        Assert.False(deny.IsAllowed);
        Assert.False(cancel.IsAllowed);
        Assert.True(cancel.CancelsTask);
    }

    [Fact]
    public async Task Decision_CannotMintAnotherGrantForSameRequest()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var request = await store.RequestAsync(CreateRequest());
        var first = await store.DecideAsync(request, ApprovalDecisionKind.AllowOnce);
        var repeated = await store.DecideAsync(request, ApprovalDecisionKind.AllowOnce);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.DecideAsync(request, ApprovalDecisionKind.Deny));

        Assert.Same(first, repeated);
        Assert.Equal("approval_request_already_decided", error.Message);
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
    public async Task Consumption_RequiresExactIssuedCurrentAllowOnceDecision()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var taskId = TaskId.NewId();
        var request = await store.RequestAsync(CreateRequest(taskId));
        var decision = await store.DecideAsync(request, ApprovalDecisionKind.AllowOnce);

        Assert.False(await store.TryConsumeAsync(
            request with { RequestId = Guid.NewGuid() },
            decision));
        Assert.False(await store.TryConsumeAsync(
            request,
            decision with { DecisionId = Guid.NewGuid() }));
        Assert.False(await store.TryConsumeAsync(
            request with { Scope = request.Scope with { TaskId = TaskId.NewId() } },
            decision));

        var replacement = await store.RequestAsync(CreateRequest(taskId));
        var replacementDecision = await store.DecideAsync(
            replacement,
            ApprovalDecisionKind.AllowOnce);

        Assert.False(await store.TryConsumeAsync(request, decision));
        Assert.True(await store.TryConsumeAsync(replacement, replacementDecision));
        Assert.False(await store.TryConsumeAsync(replacement, replacementDecision));

        var deniedRequest = await store.RequestAsync(CreateRequest(TaskId.NewId(), "workspace:denied"));
        var denied = await store.DecideAsync(deniedRequest, ApprovalDecisionKind.Deny);
        Assert.False(await store.TryConsumeAsync(deniedRequest, denied));
    }

    [Fact]
    public async Task AllowOnce_ConcurrentConsumptionSucceedsExactlyOnce()
    {
        var store = new InMemoryApprovalCheckpointStore();
        var request = await store.RequestAsync(CreateRequest());
        var decision = await store.DecideAsync(request, ApprovalDecisionKind.AllowOnce);

        var attempts = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ =>
            Task.Run(async () => await store.TryConsumeAsync(request, decision))));

        Assert.Single(attempts, allowed => allowed);
    }

    [Fact]
    public async Task Decision_RejectsUnissuedRequest()
    {
        var store = new InMemoryApprovalCheckpointStore();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await store.DecideAsync(CreateRequest(), ApprovalDecisionKind.AllowOnce));

        Assert.Equal("approval_request_not_issued", error.Message);
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
