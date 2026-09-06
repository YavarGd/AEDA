using PersonalAI.Core.Approvals;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Tasks;

public sealed class AedaTaskCenterViewModelTests
{
    [Fact]
    public async Task StaleTimelineCannotPublishAfterSelectionChanges()
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        service.Timelines[a.Id] = Pending();
        service.Timelines[b.Id] = Pending();
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        var loadB = viewModel.SelectTaskAsync(b);
        service.Complete(b.Id, Group(b, "B event"));
        await loadB;
        service.Complete(a.Id, Group(a, "A event"));
        await loadA;

        Assert.Equal(b.Id, viewModel.SelectedTask?.Id);
        Assert.Equal("B event", viewModel.TimelineGroups.Single().Title);
    }

    [Fact]
    public async Task RefreshReconcilesSelectedSummaryByIdAndClearsRemovedSelection()
    {
        var service = new FakeService();
        var original = Summary("old");
        var refreshed = original with { Title = "new" };
        service.Dashboard = Dashboard([refreshed]);
        var viewModel = new AedaTaskCenterViewModel(service);
        viewModel.SelectedTask = original;

        await viewModel.RefreshAsync();

        Assert.Same(refreshed, viewModel.SelectedTask);
        service.Dashboard = Dashboard([]);
        await viewModel.RefreshAsync();
        Assert.Null(viewModel.SelectedTask);
        Assert.Empty(viewModel.TimelineGroups);
    }

    [Fact]
    public async Task StaleFailureCannotReplaceCurrentStatusAndCurrentFailureIsSafe()
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        service.Errors[a.Id] = new InvalidOperationException();
        service.Timelines[b.Id] = Pending();
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        var loadB = viewModel.SelectTaskAsync(b);
        service.Complete(b.Id, Group(b, "B event"));
        await loadB;
        await loadA;
        Assert.Equal("Timeline loaded.", viewModel.SafeStatusMessage);

        service.Errors[b.Id] = new InvalidOperationException();
        await viewModel.SelectTaskAsync(b);
        Assert.Equal("Task Center is temporarily unavailable.", viewModel.SafeStatusMessage);
    }

    private static TaskCompletionSource<IReadOnlyList<AedaTaskActivityGroup>> Pending() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static AedaTaskSummary Summary(string title) => new(
        TaskId.NewId(), title, new(AedaTaskCenterStatus.Running, "Running", "", false, false),
        new(AedaTaskCenterModule.Chat, "Chat", AedaModuleId.Chat, "chat"),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, title, []);

    private static AedaTaskActivityGroup Group(AedaTaskSummary task, string title) =>
        new(title, title, []);

    private static AedaTaskCenterDashboard Dashboard(IReadOnlyList<AedaTaskSummary> tasks) =>
        new(tasks, [], [], [], new Dictionary<AedaTaskCenterStatus, int>(),
            new Dictionary<AedaTaskCenterModule, int>(), DateTimeOffset.UtcNow, "ready");

    private sealed class FakeService : IAedaTaskCenterService
    {
        public AedaTaskCenterDashboard Dashboard { get; set; } = Dashboard([]);
        public Dictionary<TaskId, TaskCompletionSource<IReadOnlyList<AedaTaskActivityGroup>>> Timelines { get; } = [];
        public Dictionary<TaskId, Exception> Errors { get; } = [];

        public void Complete(TaskId id, AedaTaskActivityGroup group) =>
            Timelines[id].SetResult([group]);

        public ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(AedaTaskFilter? filter = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Dashboard);

        public ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(TaskId taskId, int limit = 100, CancellationToken cancellationToken = default) =>
            Errors.TryGetValue(taskId, out var error)
                ? ValueTask.FromException<IReadOnlyList<AedaTaskActivityGroup>>(error)
                : Timelines.TryGetValue(taskId, out var timeline)
                ? new(timeline.Task)
                : new(Array.Empty<AedaTaskActivityGroup>());

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(AedaTaskCenterModule module, int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(TaskId taskId, Guid eventId, CancellationToken cancellationToken = default) => new((AedaTaskTimelineItem?)null);
        public ValueTask CancelTaskAsync(TaskId taskId, TaskCancellationReason reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
