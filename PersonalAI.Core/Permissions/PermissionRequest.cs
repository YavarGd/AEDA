using PersonalAI.Core.Tasks;
using PersonalAI.Core.Tools;

namespace PersonalAI.Core.Permissions;

public sealed record PermissionRequest(
    Guid RequestId,
    TaskId TaskId,
    ToolId ToolId,
    string ToolDisplayName,
    IReadOnlyList<ToolPermission> Permissions,
    PermissionRiskLevel RiskLevel,
    string Explanation,
    string? ResourceScope = null,
    bool LeavesMachine = false,
    bool ChangesState = false,
    bool IsReadOnly = true,
    DateTimeOffset? RequestedAtUtc = null)
{
    public DateTimeOffset RequestedAt => RequestedAtUtc ?? DateTimeOffset.UtcNow;

    public static PermissionRequest Create(
        TaskId taskId,
        ToolDescriptor descriptor,
        string explanation,
        string? resourceScope = null)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);

        return new PermissionRequest(
            Guid.NewGuid(),
            taskId,
            descriptor.Id,
            descriptor.DisplayName,
            descriptor.RequiredPermissions,
            descriptor.RiskLevel,
            explanation,
            resourceScope,
            descriptor.LeavesMachine,
            descriptor.ChangesState,
            descriptor.IsReadOnly,
            DateTimeOffset.UtcNow);
    }
}
