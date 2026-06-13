using PersonalAI.Core.Permissions;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools.Reference;

namespace PersonalAI.Tests.Permissions;

public sealed class PermissionDialogDecisionMapperTests
{
    [Theory]
    [InlineData(PermissionDialogOutcome.Dismissed)]
    [InlineData(PermissionDialogOutcome.Unknown)]
    [InlineData(PermissionDialogOutcome.Error)]
    [InlineData(PermissionDialogOutcome.Unavailable)]
    [InlineData(PermissionDialogOutcome.Deny)]
    public void NonExplicitApproval_MapsToDeny(PermissionDialogOutcome outcome)
    {
        var response = PermissionDialogDecisionMapper.Map(CreateRequest(), outcome);

        Assert.Equal(PermissionDecision.Deny, response.Decision);
    }

    [Fact]
    public void ExplicitAllow_MapsToAllowOnce()
    {
        var response = PermissionDialogDecisionMapper.Map(
            CreateRequest(),
            PermissionDialogOutcome.AllowOnce);

        Assert.Equal(PermissionDecision.AllowOnce, response.Decision);
    }

    [Fact]
    public void ExplicitAllowForTask_MapsToAllowForTask()
    {
        var response = PermissionDialogDecisionMapper.Map(
            CreateRequest(),
            PermissionDialogOutcome.AllowForTask);

        Assert.Equal(PermissionDecision.AllowForTask, response.Decision);
    }

    [Fact]
    public void ExplicitCancelTask_MapsToCancelTask()
    {
        var response = PermissionDialogDecisionMapper.Map(
            CreateRequest(),
            PermissionDialogOutcome.CancelTask);

        Assert.Equal(PermissionDecision.CancelTask, response.Decision);
    }

    private static PermissionRequest CreateRequest() =>
        new(
            Guid.NewGuid(),
            TaskId.NewId(),
            GetCurrentUtcTimeTool.Id,
            "Tool",
            [ToolPermission.ReadSystemTime],
            PermissionRiskLevel.Low,
            "Approve?",
            "clock",
            PermissionAccessMode.Read);
}
