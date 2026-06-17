using PersonalAI.Core.Workspaces;
using PersonalAI.Desktop.WinUI.Services;
using PersonalAI.Desktop.WinUI.ViewModels;

namespace PersonalAI.Tests.Workspaces;

public sealed class WorkspaceManagementViewModelTests
{
    [Fact]
    public async Task RefreshAsync_LoadingActiveWorkspacesPopulatesList()
    {
        var service = new FakeWorkspaceRegistrationService();
        var workspace = service.AddWorkspace("One");
        var viewModel = CreateViewModel(service);

        await viewModel.RefreshAsync();

        var item = Assert.Single(viewModel.Workspaces);
        Assert.Equal(workspace.Id, item.Id);
        Assert.False(viewModel.HasNoWorkspaces);
        Assert.True(viewModel.HasWorkspaces);
    }

    [Fact]
    public async Task RefreshAsync_NoRecordsShowsEmptyState()
    {
        var viewModel = CreateViewModel(new FakeWorkspaceRegistrationService());

        await viewModel.RefreshAsync();

        Assert.Empty(viewModel.Workspaces);
        Assert.True(viewModel.HasNoWorkspaces);
        Assert.Equal("No workspaces registered.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddPendingWorkspaceAsync_SuccessRefreshesPersistedRecord()
    {
        var service = new FakeWorkspaceRegistrationService();
        var viewModel = CreateViewModel(service, @"C:\repo");
        await viewModel.PickWorkspaceFolderAsync();

        await viewModel.AddPendingWorkspaceAsync();

        var item = Assert.Single(viewModel.Workspaces);
        Assert.Equal("repo", item.DisplayName);
        Assert.Equal(1, service.RegisterCount);
        Assert.False(viewModel.HasPendingWorkspace);
    }

    [Fact]
    public async Task PickWorkspaceFolderAsync_SelectedFolderCreatesPendingReview()
    {
        var viewModel = CreateViewModel(
            new FakeWorkspaceRegistrationService(),
            @"C:\Workspaces\ToolUxTest");

        await viewModel.PickWorkspaceFolderAsync();

        Assert.True(viewModel.HasPendingWorkspace);
        Assert.Equal(@"C:\Workspaces\ToolUxTest", viewModel.PendingFolderPath);
        Assert.Equal("ToolUxTest", viewModel.PendingDisplayName);
        Assert.Equal("Review the selected folder before adding it.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PickWorkspaceFolderAsync_UserCancellationIsNoOp()
    {
        var viewModel = CreateViewModel(
            new FakeWorkspaceRegistrationService(),
            pickedPath: null);

        await viewModel.PickWorkspaceFolderAsync();

        Assert.False(viewModel.HasPendingWorkspace);
        Assert.Equal(string.Empty, viewModel.PendingFolderPath);
        Assert.Equal("Workspace selection cancelled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task PickWorkspaceFolderAsync_PickerFailureShowsSafeMessage()
    {
        var pickerException = new WorkspaceAccessException(
            "folder_picker_failed",
            "Could not open the folder picker.");
        var viewModel = CreateViewModel(
            new FakeWorkspaceRegistrationService(),
            pickerException: pickerException);

        await viewModel.PickWorkspaceFolderAsync();

        Assert.False(viewModel.HasPendingWorkspace);
        Assert.Equal("Could not open the folder picker.", viewModel.StatusMessage);
        Assert.DoesNotContain("HRESULT", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("COM", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddPendingWorkspaceAsync_DuplicateRootUsesFreshReturnedStatus()
    {
        var service = new FakeWorkspaceRegistrationService();
        var existing = service.AddWorkspace("Existing");
        service.RegisterOverride = (_, _, _) =>
        {
            var updated = existing with
            {
                DisplayName = "Existing",
                Status = WorkspaceRegistrationStatus.ValidationFailed,
                SafeStatusCode = "workspace_runtime_registration_failed"
            };
            service.Replace(updated);
            return Task.FromResult(updated);
        };
        var viewModel = CreateViewModel(service, existing.CanonicalRootPath);
        await viewModel.RefreshAsync();
        await viewModel.PickWorkspaceFolderAsync();

        await viewModel.AddPendingWorkspaceAsync();

        var item = Assert.Single(viewModel.Workspaces);
        Assert.Equal("Runtime registration failed", item.Status);
        Assert.Equal(
            "Runtime access could not be registered. Try revalidating.",
            viewModel.StatusMessage);
    }

    [Fact]
    public async Task AddPendingWorkspaceAsync_FailureShowsOnlySafeError()
    {
        var service = new FakeWorkspaceRegistrationService
        {
            RegisterException = new WorkspaceAccessException(
                "workspace_persistence_failed",
                "Workspace registrations could not be saved.")
        };
        var viewModel = CreateViewModel(service, @"C:\secret");
        await viewModel.PickWorkspaceFolderAsync();

        await viewModel.AddPendingWorkspaceAsync();

        Assert.Equal(
            "Workspace registrations could not be saved.",
            viewModel.StatusMessage);
        Assert.DoesNotContain("secret", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenameWorkspaceAsync_SuccessUpdatesDisplayedName()
    {
        var service = new FakeWorkspaceRegistrationService();
        service.AddWorkspace("Old");
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.RequestRenameWorkspaceAsync = _ => Task.FromResult<string?>("  New  ");

        await viewModel.RenameWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Equal("New", viewModel.Workspaces[0].DisplayName);
        Assert.Equal("Workspace name updated.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RenameWorkspaceAsync_ValidationFailurePreservesOriginalItem()
    {
        var service = new FakeWorkspaceRegistrationService
        {
            UpdateException = new WorkspaceAccessException(
                "invalid_workspace_name",
                "Workspace name was invalid.")
        };
        service.AddWorkspace("Original");
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.RequestRenameWorkspaceAsync = _ => Task.FromResult<string?>("Bad");

        await viewModel.RenameWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Equal("Original", viewModel.Workspaces[0].DisplayName);
        Assert.Equal("Workspace name was invalid.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RevalidateWorkspaceAsync_RefreshesItem()
    {
        var service = new FakeWorkspaceRegistrationService();
        var workspace = service.AddWorkspace(
            "Workspace",
            WorkspaceRegistrationStatus.Missing,
            "workspace_not_found");
        service.RevalidateOverride = id =>
        {
            var updated = workspace with
            {
                Status = WorkspaceRegistrationStatus.Available,
                SafeStatusCode = null
            };
            service.Replace(updated);
            return Task.FromResult<PersistedWorkspace?>(updated);
        };
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();

        await viewModel.RevalidateWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Equal("Available", viewModel.Workspaces[0].Status);
        Assert.Equal("Workspace is available.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RevalidateAllAsync_MixedResultsProducesSummary()
    {
        var service = new FakeWorkspaceRegistrationService();
        service.AddWorkspace("Available");
        service.AddWorkspace(
            "Missing",
            WorkspaceRegistrationStatus.Missing,
            "workspace_not_found");
        var viewModel = CreateViewModel(service);

        await viewModel.RevalidateAllAsync();

        Assert.Equal(
            "2 workspaces revalidated. 1 available, 1 requires attention.",
            viewModel.StatusMessage);
        Assert.Equal(2, viewModel.Workspaces.Count);
    }

    [Fact]
    public async Task RevalidateAllAsync_ContinuesAfterPerWorkspaceFailureHandledByService()
    {
        var service = new FakeWorkspaceRegistrationService();
        var first = service.AddWorkspace("First");
        var second = service.AddWorkspace("Second");
        service.RevalidateAllOverride = () =>
        {
            service.Replace(first with
            {
                Status = WorkspaceRegistrationStatus.ValidationFailed,
                SafeStatusCode = "workspace_revalidation_failed"
            });
            service.Replace(second with
            {
                Status = WorkspaceRegistrationStatus.Available,
                SafeStatusCode = null
            });
            return Task.CompletedTask;
        };
        var viewModel = CreateViewModel(service);

        await viewModel.RevalidateAllAsync();

        Assert.Equal(2, viewModel.Workspaces.Count);
        Assert.Contains(viewModel.Workspaces, item => item.DisplayName == "Second" && item.CanUseWorkspace);
    }

    [Fact]
    public async Task CancellationPropagatesAndDoesNotDisplayFailureNotification()
    {
        var service = new FakeWorkspaceRegistrationService
        {
            ListException = new OperationCanceledException()
        };
        var viewModel = CreateViewModel(service);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => viewModel.RefreshAsync());

        Assert.Equal("Workspace management ready.", viewModel.StatusMessage);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_ConfirmationAcceptedRemovesItem()
    {
        var service = new FakeWorkspaceRegistrationService();
        var workspace = service.AddWorkspace("Workspace");
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.ConfirmRemoveWorkspaceAsync = _ => Task.FromResult(true);

        await viewModel.RemoveWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Empty(viewModel.Workspaces);
        Assert.Equal(workspace.Id, service.RemovedIds.Single());
    }

    [Fact]
    public async Task RemoveWorkspaceAsync_ConfirmationCancelledDoesNotMutateService()
    {
        var service = new FakeWorkspaceRegistrationService();
        service.AddWorkspace("Workspace");
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.ConfirmRemoveWorkspaceAsync = _ => Task.FromResult(false);

        await viewModel.RemoveWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Single(viewModel.Workspaces);
        Assert.Empty(service.RemovedIds);
        Assert.Equal("Workspace removal cancelled.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RepeatedInvocationOfSameMutationIsDeduplicated()
    {
        var service = new FakeWorkspaceRegistrationService();
        service.AddWorkspace("Workspace");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.UpdateOverride = async (id, name) =>
        {
            await release.Task;
            return service.UpdateName(id, name);
        };
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.RequestRenameWorkspaceAsync = _ => Task.FromResult<string?>("Renamed");

        var first = viewModel.RenameWorkspaceAsync(viewModel.Workspaces[0]);
        var second = viewModel.RenameWorkspaceAsync(viewModel.Workspaces[0]);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, service.UpdateCount);
    }

    [Fact]
    public async Task CommandsAreDisabledWhileBusy()
    {
        var service = new FakeWorkspaceRegistrationService();
        var release = new TaskCompletionSource<IReadOnlyList<PersistedWorkspace>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.ListOverride = () => release.Task;
        var viewModel = CreateViewModel(service);

        var refresh = viewModel.RefreshAsync();
        await service.ListStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.RefreshCommand.CanExecute(null));
        Assert.False(viewModel.AddPendingWorkspaceCommand.CanExecute(null));

        release.SetResult([]);
        await refresh;
    }

    [Fact]
    public async Task UnexpectedServiceExceptionIsNormalized()
    {
        var service = new FakeWorkspaceRegistrationService
        {
            ListException = new InvalidOperationException(@"raw C:\secret")
        };
        var viewModel = CreateViewModel(service);

        await viewModel.RefreshAsync();

        Assert.Equal("Workspace operation failed.", viewModel.StatusMessage);
        Assert.DoesNotContain("secret", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailureForOneWorkspaceDoesNotMutateAnotherItem()
    {
        var service = new FakeWorkspaceRegistrationService
        {
            UpdateException = new WorkspaceAccessException(
                "invalid_workspace_name",
                "Workspace name was invalid.")
        };
        service.AddWorkspace("First");
        service.AddWorkspace("Second");
        var viewModel = CreateViewModel(service);
        await viewModel.RefreshAsync();
        viewModel.RequestRenameWorkspaceAsync = _ => Task.FromResult<string?>("Bad");

        await viewModel.RenameWorkspaceAsync(viewModel.Workspaces[0]);

        Assert.Equal("First", viewModel.Workspaces[0].DisplayName);
        Assert.Equal("Second", viewModel.Workspaces[1].DisplayName);
    }

    private static WorkspaceManagementViewModel CreateViewModel(
        FakeWorkspaceRegistrationService service,
        string? pickedPath = null,
        Exception? pickerException = null) =>
        new(service, new FakeFolderPicker(pickedPath, pickerException));

    private sealed class FakeFolderPicker(string? path, Exception? exception) : IFolderPickerService
    {
        public Task<string?> PickSingleFolderAsync(
            CancellationToken cancellationToken = default)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return Task.FromResult(path);
        }
    }

    private sealed class FakeWorkspaceRegistrationService : IWorkspaceRegistrationService
    {
        private readonly List<PersistedWorkspace> _records = [];

        public TaskCompletionSource ListStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Exception? ListException { get; set; }

        public Exception? RegisterException { get; set; }

        public Exception? UpdateException { get; set; }

        public Func<Task<IReadOnlyList<PersistedWorkspace>>>? ListOverride { get; set; }

        public Func<string, string, string, Task<PersistedWorkspace>>? RegisterOverride { get; set; }

        public Func<WorkspaceId, string, Task<PersistedWorkspace?>>? UpdateOverride { get; set; }

        public Func<WorkspaceId, Task<PersistedWorkspace?>>? RevalidateOverride { get; set; }

        public Func<Task>? RevalidateAllOverride { get; set; }

        public int RegisterCount { get; private set; }

        public int UpdateCount { get; private set; }

        public List<WorkspaceId> RemovedIds { get; } = [];

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public async Task<IReadOnlyList<PersistedWorkspace>> ListAsync(
            CancellationToken cancellationToken = default)
        {
            ListStarted.TrySetResult();
            if (ListException is not null)
            {
                throw ListException;
            }

            if (ListOverride is not null)
            {
                return await ListOverride();
            }

            return _records.ToArray();
        }

        public async Task<PersistedWorkspace> RegisterAsync(
            string rootPath,
            string displayName,
            string source,
            CancellationToken cancellationToken = default)
        {
            RegisterCount++;
            if (RegisterException is not null)
            {
                throw RegisterException;
            }

            if (RegisterOverride is not null)
            {
                return await RegisterOverride(rootPath, displayName, source);
            }

            var workspace = CreateWorkspace(
                displayName,
                WorkspaceRegistrationStatus.Available,
                null,
                rootPath);
            _records.RemoveAll(item => item.CanonicalRootPath == workspace.CanonicalRootPath);
            _records.Add(workspace);
            return workspace;
        }

        public Task RemoveAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default)
        {
            RemovedIds.Add(workspaceId);
            _records.RemoveAll(item => item.Id == workspaceId);
            return Task.CompletedTask;
        }

        public async Task<PersistedWorkspace?> UpdateDisplayNameAsync(
            WorkspaceId workspaceId,
            string displayName,
            CancellationToken cancellationToken = default)
        {
            UpdateCount++;
            if (UpdateException is not null)
            {
                throw UpdateException;
            }

            if (UpdateOverride is not null)
            {
                return await UpdateOverride(workspaceId, displayName);
            }

            return UpdateName(workspaceId, displayName);
        }

        public Task<PersistedWorkspace?> RevalidateAsync(
            WorkspaceId workspaceId,
            CancellationToken cancellationToken = default)
        {
            if (RevalidateOverride is not null)
            {
                return RevalidateOverride(workspaceId);
            }

            return Task.FromResult(_records.FirstOrDefault(item => item.Id == workspaceId));
        }

        public Task RevalidateAllAsync(CancellationToken cancellationToken = default)
        {
            if (RevalidateAllOverride is not null)
            {
                return RevalidateAllOverride();
            }

            return Task.CompletedTask;
        }

        public PersistedWorkspace AddWorkspace(
            string displayName,
            WorkspaceRegistrationStatus status = WorkspaceRegistrationStatus.Available,
            string? safeStatusCode = null)
        {
            var workspace = CreateWorkspace(displayName, status, safeStatusCode);
            _records.Add(workspace);
            return workspace;
        }

        public void Replace(PersistedWorkspace workspace)
        {
            var index = _records.FindIndex(item => item.Id == workspace.Id);
            if (index >= 0)
            {
                _records[index] = workspace;
                return;
            }

            _records.Add(workspace);
        }

        public PersistedWorkspace? UpdateName(WorkspaceId workspaceId, string displayName)
        {
            var index = _records.FindIndex(item => item.Id == workspaceId);
            if (index < 0)
            {
                return null;
            }

            _records[index] = _records[index] with
            {
                DisplayName = displayName
            };
            return _records[index];
        }

        private static PersistedWorkspace CreateWorkspace(
            string displayName,
            WorkspaceRegistrationStatus status,
            string? safeStatusCode,
            string? rootPath = null)
        {
            var now = DateTimeOffset.UtcNow;
            return new PersistedWorkspace(
                WorkspaceId.NewId(),
                displayName,
                rootPath ?? $@"C:\Workspaces\{displayName}",
                "test",
                now,
                now,
                status,
                safeStatusCode,
                true);
        }
    }
}
