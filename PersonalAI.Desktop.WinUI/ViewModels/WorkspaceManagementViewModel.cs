using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.Services;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class WorkspaceManagementViewModel(
    IWorkspaceRegistrationService registrationService,
    IFolderPickerService folderPickerService)
    : ObservableObject
{
    public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; } = [];

    public Func<WorkspaceItemViewModel, Task<bool>> ConfirmRemoveWorkspaceAsync { get; set; } =
        _ => Task.FromResult(false);

    [ObservableProperty]
    private string _pendingFolderPath = string.Empty;

    [ObservableProperty]
    private string _pendingDisplayName = string.Empty;

    [ObservableProperty]
    private bool _hasPendingWorkspace;

    [ObservableProperty]
    private string _statusMessage = "Workspace management ready.";

    public bool HasWorkspaces => Workspaces.Count > 0;

    public bool HasNoWorkspaces => Workspaces.Count == 0;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadAsync();
    }

    [RelayCommand]
    public async Task PickWorkspaceFolderAsync()
    {
        try
        {
            var folderPath = await folderPickerService.PickSingleFolderAsync();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                StatusMessage = "Workspace selection cancelled.";
                return;
            }

            PendingFolderPath = folderPath;
            PendingDisplayName = Path.GetFileName(
                folderPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(PendingDisplayName))
            {
                PendingDisplayName = "Workspace";
            }

            HasPendingWorkspace = true;
            StatusMessage = "Review the selected folder before adding it.";
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
    }

    [RelayCommand]
    public void CancelPendingWorkspace()
    {
        PendingFolderPath = string.Empty;
        PendingDisplayName = string.Empty;
        HasPendingWorkspace = false;
        StatusMessage = "Workspace review cancelled.";
    }

    [RelayCommand]
    public async Task AddPendingWorkspaceAsync()
    {
        if (!HasPendingWorkspace ||
            string.IsNullOrWhiteSpace(PendingFolderPath))
        {
            return;
        }

        try
        {
            await registrationService.RegisterAsync(
                PendingFolderPath,
                PendingDisplayName,
                "WinUI workspace picker");
            PendingFolderPath = string.Empty;
            PendingDisplayName = string.Empty;
            HasPendingWorkspace = false;
            await LoadAsync();
            StatusMessage = "Workspace added.";
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
    }

    [RelayCommand]
    public async Task SaveDisplayNameAsync(WorkspaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var updated = await registrationService.UpdateDisplayNameAsync(
                item.Id,
                item.EditableDisplayName);
            if (updated is not null)
            {
                item.Update(updated);
                StatusMessage = "Workspace name updated.";
            }
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
    }

    [RelayCommand]
    public async Task RevalidateWorkspaceAsync(WorkspaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            var updated = await registrationService.RevalidateAsync(item.Id);
            if (updated is not null)
            {
                item.Update(updated);
                StatusMessage = "Workspace revalidated.";
            }
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
    }

    [RelayCommand]
    public async Task RemoveWorkspaceAsync(WorkspaceItemViewModel? item)
    {
        if (item is null ||
            !await ConfirmRemoveWorkspaceAsync(item))
        {
            return;
        }

        try
        {
            await registrationService.RemoveAsync(item.Id);
            Workspaces.Remove(item);
            OnPropertyChanged(nameof(HasWorkspaces));
            OnPropertyChanged(nameof(HasNoWorkspaces));
            StatusMessage = "Workspace removed. Files were not deleted.";
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
    }

    private async Task LoadAsync()
    {
        var records = await registrationService.ListAsync();
        Workspaces.Clear();
        foreach (var workspace in records)
        {
            Workspaces.Add(new WorkspaceItemViewModel(workspace));
        }

        OnPropertyChanged(nameof(HasWorkspaces));
        OnPropertyChanged(nameof(HasNoWorkspaces));
    }
}
