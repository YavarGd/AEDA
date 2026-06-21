using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Coding;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AedaCodeModuleViewModel(
    IAedaCodeModuleService moduleService) : ObservableObject
{
    [ObservableProperty]
    private AedaCodeSession? _session;

    [ObservableProperty]
    private AedaCodeDashboardModel? _dashboard;

    [ObservableProperty]
    private string? _safeStatusMessage;

    public async Task StartSessionAsync(
        WorkspaceId workspaceId,
        string? safeSummary = null,
        CancellationToken cancellationToken = default)
    {
        Session = await moduleService.StartSessionAsync(
            workspaceId,
            safeSummary,
            cancellationToken).ConfigureAwait(false);
        Dashboard = await moduleService.GetDashboardAsync(
            Session.Id,
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "AEDA Code session ready";
    }

    public async Task RefreshDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        if (Session is null)
        {
            SafeStatusMessage = "No AEDA Code session";
            return;
        }

        Dashboard = await moduleService.GetDashboardAsync(
            Session.Id,
            cancellationToken).ConfigureAwait(false);
        SafeStatusMessage = "AEDA Code dashboard refreshed";
    }
}
