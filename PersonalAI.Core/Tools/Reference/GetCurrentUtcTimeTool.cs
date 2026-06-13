using PersonalAI.Core.Permissions;

namespace PersonalAI.Core.Tools.Reference;

public sealed record GetCurrentUtcTimeInput;

public sealed record GetCurrentUtcTimeOutput(
    DateTimeOffset UtcNow,
    string Iso8601);

public sealed class GetCurrentUtcTimeTool
    : TypedToolBase<GetCurrentUtcTimeInput, GetCurrentUtcTimeOutput>
{
    public static ToolId Id { get; } = new("core.time.get_current_utc");

    public override ToolDescriptor Descriptor { get; } =
        ToolDescriptor.Create<GetCurrentUtcTimeInput, GetCurrentUtcTimeOutput>(
            Id,
            "Get current UTC time",
            "Returns the current UTC timestamp.",
            requiredPermissions: [ToolPermission.ReadSystemTime],
            riskLevel: PermissionRiskLevel.Low,
            requiresApproval: false,
            supportsCancellation: false,
            recommendedTimeout: TimeSpan.FromSeconds(2),
            category: "Reference",
            isReadOnly: true,
            changesState: false,
            leavesMachine: false,
            outputMayContainSensitiveData: false);

    public override ValueTask<GetCurrentUtcTimeOutput> ExecuteAsync(
        GetCurrentUtcTimeInput input,
        TaskExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        return ValueTask.FromResult(new GetCurrentUtcTimeOutput(
            now,
            now.ToString("O")));
    }
}
