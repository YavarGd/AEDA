using PersonalAI.Core.Permissions;

namespace PersonalAI.Core.Tools;

public sealed record ToolDescriptor(
    ToolId Id,
    string DisplayName,
    string Description,
    Type InputType,
    Type OutputType,
    IReadOnlyList<ToolPermission> RequiredPermissions,
    PermissionRiskLevel RiskLevel,
    bool RequiresApproval,
    bool SupportsCancellation,
    TimeSpan? RecommendedTimeout = null,
    string? Category = null,
    bool IsReadOnly = true,
    bool ChangesState = false,
    bool LeavesMachine = false,
    bool OutputMayContainSensitiveData = false)
{
    public static ToolDescriptor Create<TInput, TOutput>(
        ToolId id,
        string displayName,
        string description,
        IReadOnlyList<ToolPermission>? requiredPermissions = null,
        PermissionRiskLevel riskLevel = PermissionRiskLevel.Low,
        bool requiresApproval = false,
        bool supportsCancellation = true,
        TimeSpan? recommendedTimeout = null,
        string? category = null,
        bool isReadOnly = true,
        bool changesState = false,
        bool leavesMachine = false,
        bool outputMayContainSensitiveData = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        return new ToolDescriptor(
            id,
            displayName,
            description,
            typeof(TInput),
            typeof(TOutput),
            requiredPermissions ?? [],
            riskLevel,
            requiresApproval,
            supportsCancellation,
            recommendedTimeout,
            category,
            isReadOnly,
            changesState,
            leavesMachine,
            outputMayContainSensitiveData);
    }
}
