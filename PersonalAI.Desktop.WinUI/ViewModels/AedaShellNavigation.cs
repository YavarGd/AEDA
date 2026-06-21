using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Modules;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public enum AedaShellSection
{
    Chat,
    Dashboard,
    TaskCenter,
    Code,
    Memory,
    Research,
    Settings
}

public sealed record AedaShellRoute(
    AedaShellSection Section,
    AedaModuleId? ModuleId = null,
    string? RouteId = null);

public sealed partial class AedaShellNavigationState : ObservableObject
{
    [ObservableProperty]
    private AedaShellRoute _currentRoute = new(AedaShellSection.Chat);

    public AedaShellSection CurrentSection => CurrentRoute.Section;

    public bool IsChatVisible => CurrentSection == AedaShellSection.Chat;

    public bool IsDashboardVisible => CurrentSection == AedaShellSection.Dashboard;

    public bool IsTaskCenterVisible => CurrentSection == AedaShellSection.TaskCenter;

    public bool IsCodeVisible => CurrentSection == AedaShellSection.Code;

    public bool IsMemoryVisible => CurrentSection == AedaShellSection.Memory;

    public bool IsResearchVisible => CurrentSection == AedaShellSection.Research;

    public bool IsSettingsVisible => CurrentSection == AedaShellSection.Settings;

    public void Navigate(AedaShellRoute route)
    {
        CurrentRoute = route;
    }

    public void OpenChat() => Navigate(new AedaShellRoute(AedaShellSection.Chat));

    public void OpenDashboard() =>
        Navigate(new AedaShellRoute(AedaShellSection.Dashboard));

    public void OpenTaskCenter() =>
        Navigate(new AedaShellRoute(AedaShellSection.TaskCenter));

    public void OpenSettings() =>
        Navigate(new AedaShellRoute(AedaShellSection.Settings));

    public bool TryOpenModule(AedaModuleDescriptor descriptor)
    {
        if (descriptor.Status == AedaModuleStatus.Unavailable)
        {
            return false;
        }

        Navigate(new AedaShellRoute(
            descriptor.Kind == AedaModuleKind.Code
                ? AedaShellSection.Code
                : descriptor.Kind == AedaModuleKind.Memory
                    ? AedaShellSection.Memory
                : descriptor.Kind == AedaModuleKind.Research
                    ? AedaShellSection.Research
                : descriptor.Kind == AedaModuleKind.TaskCenter
                    ? AedaShellSection.TaskCenter
                : AedaShellSection.Dashboard,
            descriptor.Id,
            descriptor.Route.RouteId));
        return true;
    }

    partial void OnCurrentRouteChanged(AedaShellRoute value)
    {
        OnPropertyChanged(nameof(CurrentSection));
        OnPropertyChanged(nameof(IsChatVisible));
        OnPropertyChanged(nameof(IsDashboardVisible));
        OnPropertyChanged(nameof(IsTaskCenterVisible));
        OnPropertyChanged(nameof(IsCodeVisible));
        OnPropertyChanged(nameof(IsMemoryVisible));
        OnPropertyChanged(nameof(IsResearchVisible));
        OnPropertyChanged(nameof(IsSettingsVisible));
    }
}
