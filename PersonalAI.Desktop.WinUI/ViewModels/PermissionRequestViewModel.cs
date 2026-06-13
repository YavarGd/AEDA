using PersonalAI.Core.Permissions;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed class PermissionRequestViewModel(PermissionRequest request)
{
    public string Title => request.ToolDisplayName;

    public string Explanation => request.Explanation;

    public string Risk => request.RiskLevel.ToString();

    public string Scope => string.IsNullOrWhiteSpace(request.ResourceScope)
        ? "No specific resource scope"
        : request.ResourceScope;

    public string Permissions => request.Permissions.Count == 0
        ? "No declared permissions"
        : string.Join(", ", request.Permissions);

    public string Impact =>
        request.ChangesState
            ? "May change local state"
            : request.LeavesMachine
                ? "May send data outside this device"
                : "Read-only local action";
}
