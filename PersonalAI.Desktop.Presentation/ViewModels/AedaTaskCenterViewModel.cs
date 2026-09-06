using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalAI.Core.Tasks;

namespace PersonalAI.Desktop.Presentation.ViewModels;

public sealed partial class AedaTaskCenterViewModel : ObservableObject
{
    private readonly IAedaTaskCenterService _taskCenterService;
    private long _timelineGeneration;

    public AedaTaskCenterViewModel(IAedaTaskCenterService taskCenterService)
    {
        _taskCenterService = taskCenterService ??
            throw new ArgumentNullException(nameof(taskCenterService));
    }

    public ObservableCollection<AedaTaskSummary> ActiveTasks { get; } = [];

    public ObservableCollection<AedaTaskApprovalSummary> WaitingApprovals { get; } = [];

    public ObservableCollection<AedaTaskSummary> RecentTasks { get; } = [];

    public ObservableCollection<AedaTaskSummary> FailedOrCancelledTasks { get; } = [];

    public ObservableCollection<AedaTaskActivityGroup> TimelineGroups { get; } = [];

    public ObservableCollection<string> StatusCounts { get; } = [];

    public ObservableCollection<string> ModuleCounts { get; } = [];

    [ObservableProperty]
    private AedaTaskSummary? _selectedTask;

    [ObservableProperty]
    private string _safeStatusMessage = "Task Center ready.";

    [ObservableProperty]
    private bool _isRefreshing;

    public bool HasActiveTasks => ActiveTasks.Count > 0;

    public bool HasWaitingApprovals => WaitingApprovals.Count > 0;

    public bool HasRecentTasks => RecentTasks.Count > 0;

    public bool HasFailedOrCancelledTasks => FailedOrCancelledTasks.Count > 0;

    public bool HasTimeline => TimelineGroups.Count > 0;

    public bool HasNoActiveTasks => !HasActiveTasks;

    public bool HasNoWaitingApprovals => !HasWaitingApprovals;

    public bool HasNoRecentTasks => !HasRecentTasks;

    public bool HasNoFailedOrCancelledTasks => !HasFailedOrCancelledTasks;

    public bool HasNoTimeline => !HasTimeline;

    public bool HasSelectedTask => SelectedTask is not null;

    public string ActiveTaskCountText => $"{ActiveTasks.Count} active";

    public string WaitingApprovalCountText => $"{WaitingApprovals.Count} approval(s)";

    public string RecentFailureCountText => $"{FailedOrCancelledTasks.Count} recent issue(s)";

    [RelayCommand]
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsRefreshing)
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            ++_timelineGeneration;
            var selectedTaskId = SelectedTask?.Id;
            var dashboard = await _taskCenterService.GetDashboardAsync(
                cancellationToken: cancellationToken);
            Replace(ActiveTasks, dashboard.ActiveTasks);
            Replace(WaitingApprovals, dashboard.WaitingApprovals);
            Replace(RecentTasks, dashboard.RecentTasks);
            Replace(FailedOrCancelledTasks, dashboard.FailedOrCancelledTasks);
            Replace(
                StatusCounts,
                dashboard.CountsByStatus
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}: {pair.Value}")
                    .ToArray());
            Replace(
                ModuleCounts,
                dashboard.CountsByModule
                    .OrderBy(pair => pair.Key)
                    .Select(pair => $"{pair.Key}: {pair.Value}")
                    .ToArray());
            SafeStatusMessage = dashboard.SafeStatusMessage;
            var refreshedSelectedTask = selectedTaskId is null
                ? null
                : ActiveTasks.Concat(RecentTasks).Concat(FailedOrCancelledTasks)
                    .FirstOrDefault(task => task.Id == selectedTaskId);
            if (SelectedTask?.Id == selectedTaskId)
            {
                SelectedTask = refreshedSelectedTask
                    ?? (selectedTaskId is null
                        ? ActiveTasks.FirstOrDefault()
                            ?? RecentTasks.FirstOrDefault()
                            ?? FailedOrCancelledTasks.FirstOrDefault()
                        : null);
            }

            await LoadSelectedTimelineAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Task Center refresh cancelled.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is IOException ||
            exception is ArgumentException)
        {
            SafeStatusMessage = "Task Center is temporarily unavailable.";
        }
        finally
        {
            IsRefreshing = false;
            NotifyCountsChanged();
        }
    }

    [RelayCommand]
    public async Task SelectTaskAsync(
        AedaTaskSummary? task,
        CancellationToken cancellationToken = default)
    {
        var sameTask = SelectedTask?.Id == task?.Id;
        SelectedTask = task;
        if (sameTask)
        {
            _timelineGeneration++;
            TimelineGroups.Clear();
        }
        await LoadSelectedTimelineAsync(cancellationToken);
    }

    partial void OnSelectedTaskChanged(AedaTaskSummary? value)
    {
        _timelineGeneration++;
        OnPropertyChanged(nameof(HasSelectedTask));
    }

    private async Task LoadSelectedTimelineAsync(CancellationToken cancellationToken)
    {
        var task = SelectedTask;
        var generation = _timelineGeneration;
        if (task is null)
        {
            TimelineGroups.Clear();
            SafeStatusMessage = "Select a task to view its timeline.";
            NotifyCountsChanged();
            return;
        }

        try
        {
            var groups = await _taskCenterService.GetTimelineAsync(
                task.Id,
                cancellationToken: cancellationToken);
            if (generation != _timelineGeneration || SelectedTask?.Id != task.Id)
            {
                return;
            }

            TimelineGroups.Clear();
            foreach (var group in groups)
            {
                TimelineGroups.Add(group);
            }

            SafeStatusMessage = groups.Count == 0
                ? "No timeline events are available for this task."
                : "Timeline loaded.";
            NotifyCountsChanged();
        }
        catch (OperationCanceledException) when (
            generation != _timelineGeneration || SelectedTask?.Id != task.Id)
        {
        }
        catch (OperationCanceledException)
        {
            SafeStatusMessage = "Task Center timeline load cancelled.";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException ||
            exception is IOException ||
            exception is ArgumentException)
        {
            if (generation == _timelineGeneration && SelectedTask?.Id == task.Id)
            {
                SafeStatusMessage = "Task Center is temporarily unavailable.";
            }
        }
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(HasWaitingApprovals));
        OnPropertyChanged(nameof(HasRecentTasks));
        OnPropertyChanged(nameof(HasFailedOrCancelledTasks));
        OnPropertyChanged(nameof(HasTimeline));
        OnPropertyChanged(nameof(HasNoActiveTasks));
        OnPropertyChanged(nameof(HasNoWaitingApprovals));
        OnPropertyChanged(nameof(HasNoRecentTasks));
        OnPropertyChanged(nameof(HasNoFailedOrCancelledTasks));
        OnPropertyChanged(nameof(HasNoTimeline));
        OnPropertyChanged(nameof(ActiveTaskCountText));
        OnPropertyChanged(nameof(WaitingApprovalCountText));
        OnPropertyChanged(nameof(RecentFailureCountText));
    }

    private static void Replace<T>(
        ObservableCollection<T> collection,
        IReadOnlyList<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
        }
    }
}
