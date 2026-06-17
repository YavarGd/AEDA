using PersonalAI.Core.Permissions;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed class PermissionRequestViewModel(PermissionRequest request)
{
    private readonly ToolPermissionPresentation _presentation =
        ToolPresentationMapper.ForPermission(request);

    public string Title => _presentation.Title;

    public string Action => _presentation.Action;

    public string Explanation => _presentation.Explanation;

    public string Scope => _presentation.Scope;

    public string TechnicalDetails => _presentation.TechnicalDetails;

    public string Permissions => request.Permissions.Count == 0
        ? "No declared permissions"
        : string.Join(", ", request.Permissions);

    public string Impact => _presentation.ReadOnlyExplanation;
}
