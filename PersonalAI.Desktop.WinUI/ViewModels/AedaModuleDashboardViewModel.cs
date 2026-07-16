using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Core.Workspaces;

namespace PersonalAI.Desktop.WinUI.ViewModels;

public sealed partial class AedaModuleDashboardViewModel : ObservableObject
{
    public const int ActiveTaskLimit = 5;
    public const int RecentTaskLimit = 8;

    private readonly IAedaModuleRegistry _registry;
    private readonly ITaskQueryService _taskQueryService;
    private readonly IWorkspaceRegistry _workspaceRegistry;
    private readonly Action<AedaModuleDescriptor> _openModule;

    public AedaModuleDashboardViewModel(
        IAedaModuleRegistry registry,
        ITaskQueryService taskQueryService,
        IWorkspaceRegistry workspaceRegistry,
        Action<AedaModuleDescriptor> openModule)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _taskQueryService = taskQueryService ??
            throw new ArgumentNullException(nameof(taskQueryService));
        _workspaceRegistry = workspaceRegistry ??
            throw new ArgumentNullException(nameof(workspaceRegistry));
        _openModule = openModule ?? throw new ArgumentNullException(nameof(openModule));
        LoadTiles();
        RefreshWorkspaceSummary();
    }

    public ObservableCollection<ModuleTileViewModel> ModuleTiles { get; } = [];

    public ModuleTileViewModel CodeTile => Tile(AedaModuleKind.Code);
    public ModuleTileViewModel MemoryTile => Tile(AedaModuleKind.Memory);
    public ModuleTileViewModel ResearchTile => Tile(AedaModuleKind.Research);
    public ModuleTileViewModel TaskCenterTile => Tile(AedaModuleKind.TaskCenter);
    public IEnumerable<ModuleTileViewModel> DeferredTiles =>
        ModuleTiles.Where(tile => !tile.IsEnabled);

    public ObservableCollection<TaskSummaryItemViewModel> ActiveTasks { get; } = [];

    public ObservableCollection<TaskSummaryItemViewModel> RecentTasks { get; } = [];

    public bool HasActiveTasks => ActiveTasks.Count > 0;

    public bool HasRecentTasks => RecentTasks.Count > 0;

    public bool HasNoActiveTasks => !HasActiveTasks;

    public bool HasNoRecentTasks => !HasRecentTasks;

    [ObservableProperty]
    private string _currentWorkspaceSummary = "No workspace selected.";

    [ObservableProperty]
    private string _safeStatusMessage = "Dashboard ready.";

    public async Task RefreshTaskSummariesAsync(
        CancellationToken cancellationToken = default)
    {
        var active = await _taskQueryService.GetCurrentlyRunningTasksAsync(
            ActiveTaskLimit,
            cancellationToken).ConfigureAwait(false);
        var recent = await _taskQueryService.ListRecentTaskRunsAsync(
            RecentTaskLimit,
            cancellationToken).ConfigureAwait(false);

        ActiveTasks.Clear();
        foreach (var task in active
                     .OrderByDescending(task => task.UpdatedAtUtc)
                     .ThenByDescending(task => task.CreatedAtUtc)
                     .Take(ActiveTaskLimit))
        {
            ActiveTasks.Add(new TaskSummaryItemViewModel(task));
        }

        RecentTasks.Clear();
        foreach (var task in recent
                     .OrderByDescending(task => task.UpdatedAtUtc)
                     .ThenByDescending(task => task.CreatedAtUtc)
                     .Take(RecentTaskLimit))
        {
            RecentTasks.Add(new TaskSummaryItemViewModel(task));
        }

        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(HasRecentTasks));
        OnPropertyChanged(nameof(HasNoActiveTasks));
        OnPropertyChanged(nameof(HasNoRecentTasks));
        SafeStatusMessage = "Task summaries refreshed.";
    }

    public void RefreshWorkspaceSummary()
    {
        var workspaces = _workspaceRegistry.List();
        CurrentWorkspaceSummary = workspaces.Count == 0
            ? "No workspace registered."
            : $"{workspaces.Count} workspace(s) registered.";
    }

    public void RefreshTheme()
    {
        foreach (var tile in ModuleTiles)
        {
            tile.RefreshTheme();
        }
    }

    private void LoadTiles()
    {
        ModuleTiles.Clear();
        foreach (var module in _registry.ListModules())
        {
            ModuleTiles.Add(new ModuleTileViewModel(module, _openModule));
        }
    }

    private ModuleTileViewModel Tile(AedaModuleKind kind) =>
        ModuleTiles.Single(tile => tile.Kind == kind);
}
