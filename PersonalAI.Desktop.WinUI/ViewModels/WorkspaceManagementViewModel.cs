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
    private readonly HashSet<WorkspaceId> _busyWorkspaceIds = [];

    public ObservableCollection<WorkspaceItemViewModel> Workspaces { get; } = [];

    public Func<WorkspaceItemViewModel, Task<bool>> ConfirmRemoveWorkspaceAsync { get; set; } =
        _ => Task.FromResult(false);

    public Func<WorkspaceItemViewModel, Task<string?>> RequestRenameWorkspaceAsync { get; set; } =
        workspace => Task.FromResult<string?>(workspace.DisplayName);

    [ObservableProperty]
    private string _pendingFolderPath = string.Empty;

    [ObservableProperty]
    private string _pendingDisplayName = string.Empty;

    [ObservableProperty]
    private bool _hasPendingWorkspace;

    [ObservableProperty]
    private string _statusMessage = "Workspace management ready.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyMessage = string.Empty;

    public bool HasWorkspaces => Workspaces.Count > 0;

    public bool HasNoWorkspaces => Workspaces.Count == 0;

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool CanStartOperation => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task RefreshAsync()
    {
        await RunBusyAsync("Loading workspaces...", async () =>
        {
            await LoadAsync();
            StatusMessage = HasWorkspaces
                ? "Workspaces loaded."
                : "No workspaces registered.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task PickWorkspaceFolderAsync()
    {
        await RunBusyAsync("Opening folder picker...", async () =>
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
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public void CancelPendingWorkspace()
    {
        PendingFolderPath = string.Empty;
        PendingDisplayName = string.Empty;
        HasPendingWorkspace = false;
        StatusMessage = "Workspace review cancelled.";
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task AddPendingWorkspaceAsync()
    {
        if (!HasPendingWorkspace ||
            string.IsNullOrWhiteSpace(PendingFolderPath))
        {
            return;
        }

        await RunBusyAsync("Adding workspace...", async () =>
        {
            var workspace = await registrationService.RegisterAsync(
                PendingFolderPath,
                PendingDisplayName,
                "WinUI workspace picker");
            PendingFolderPath = string.Empty;
            PendingDisplayName = string.Empty;
            HasPendingWorkspace = false;
            await LoadAsync();
            var presentation = WorkspaceStatusPresentationMapper.Map(workspace);
            StatusMessage = workspace.Status == WorkspaceRegistrationStatus.Available
                ? "Workspace added."
                : presentation.Message;
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task RenameWorkspaceAsync(WorkspaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunWorkspaceOperationAsync(item, "Renaming workspace...", async () =>
        {
            var requestedName = await RequestRenameWorkspaceAsync(item);
            if (requestedName is null)
            {
                StatusMessage = "Workspace rename cancelled.";
                return;
            }

            var displayName = requestedName.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                StatusMessage = "Workspace name cannot be empty.";
                return;
            }

            var updated = await registrationService.UpdateDisplayNameAsync(
                item.Id,
                displayName);
            if (updated is not null)
            {
                item.Update(updated);
                StatusMessage = "Workspace name updated.";
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task RevalidateWorkspaceAsync(WorkspaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunWorkspaceOperationAsync(item, "Revalidating workspace...", async () =>
        {
            var updated = await registrationService.RevalidateAsync(item.Id);
            if (updated is not null)
            {
                item.Update(updated);
                StatusMessage = WorkspaceStatusPresentationMapper.Map(updated).Message;
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task RevalidateAllAsync()
    {
        await RunBusyAsync("Revalidating workspaces...", async () =>
        {
            await registrationService.RevalidateAllAsync();
            await LoadAsync();
            var available = Workspaces.Count(workspace => workspace.CanUseWorkspace);
            var attention = Workspaces.Count - available;
            StatusMessage = attention == 0
                ? $"{Workspaces.Count} workspaces revalidated. {available} available."
                : $"{Workspaces.Count} workspaces revalidated. {available} available, {attention} requires attention.";
        });
    }

    [RelayCommand(CanExecute = nameof(CanStartOperation))]
    public async Task RemoveWorkspaceAsync(WorkspaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        await RunWorkspaceOperationAsync(item, "Removing workspace...", async () =>
        {
            if (!await ConfirmRemoveWorkspaceAsync(item))
            {
                StatusMessage = "Workspace removal cancelled.";
                return;
            }

            await registrationService.RemoveAsync(item.Id);
            Workspaces.Remove(item);
            OnPropertyChanged(nameof(HasWorkspaces));
            OnPropertyChanged(nameof(HasNoWorkspaces));
            StatusMessage = "Workspace removed. Files were not deleted.";
        });
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

    private async Task RunWorkspaceOperationAsync(
        WorkspaceItemViewModel item,
        string busyMessage,
        Func<Task> operation)
    {
        if (!_busyWorkspaceIds.Add(item.Id))
        {
            return;
        }

        try
        {
            await RunBusyAsync(busyMessage, operation);
        }
        finally
        {
            _busyWorkspaceIds.Remove(item.Id);
        }
    }

    private async Task RunBusyAsync(string busyMessage, Func<Task> operation)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        BusyMessage = busyMessage;
        NotifyCommandStates();
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WorkspaceAccessException exception)
        {
            StatusMessage = exception.SafeErrorMessage;
        }
        catch
        {
            StatusMessage = "Workspace operation failed.";
        }
        finally
        {
            IsBusy = false;
            BusyMessage = string.Empty;
            NotifyCommandStates();
        }
    }

    private void NotifyCommandStates()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        PickWorkspaceFolderCommand.NotifyCanExecuteChanged();
        CancelPendingWorkspaceCommand.NotifyCanExecuteChanged();
        AddPendingWorkspaceCommand.NotifyCanExecuteChanged();
        RenameWorkspaceCommand.NotifyCanExecuteChanged();
        RevalidateWorkspaceCommand.NotifyCanExecuteChanged();
        RevalidateAllCommand.NotifyCanExecuteChanged();
        RemoveWorkspaceCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanStartOperation));
    }

    partial void OnStatusMessageChanged(string value)
    {
        OnPropertyChanged(nameof(HasStatusMessage));
    }
}
