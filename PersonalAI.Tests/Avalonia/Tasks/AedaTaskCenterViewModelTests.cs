using PersonalAI.Core.Approvals;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Tasks;

public sealed class AedaTaskCenterViewModelTests
{
    [Fact]
    public async Task SelectionClearsPreviousTimelineBeforeNewRequestCompletes()
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        var aRequest = service.QueueTimeline(a.Id);
        var bRequest = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        aRequest.Complete([Group("A event")]);
        await loadA;
        var loadB = viewModel.SelectTaskAsync(b);

        Assert.Equal(b.Id, viewModel.SelectedTask?.Id);
        Assert.Empty(viewModel.TimelineGroups);
        bRequest.Complete([Group("B event")]);
        await loadB;
        AssertTimeline(viewModel, "B event");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestSelectionWinsRegardlessOfCompletionOrder(bool newerCompletesFirst)
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        var aRequest = service.QueueTimeline(a.Id);
        var bRequest = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        var loadB = viewModel.SelectTaskAsync(b);
        if (newerCompletesFirst)
        {
            bRequest.Complete([Group("B event")]);
            await loadB;
            aRequest.Complete([Group("A event")]);
            await loadA;
        }
        else
        {
            aRequest.Complete([Group("A event")]);
            await loadA;
            Assert.Empty(viewModel.TimelineGroups);
            bRequest.Complete([Group("B event")]);
            await loadB;
        }

        Assert.Equal(b.Id, viewModel.SelectedTask?.Id);
        AssertTimeline(viewModel, "B event");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LatestSameTaskRequestWinsRegardlessOfCompletionOrder(bool newerCompletesFirst)
    {
        var service = new FakeService();
        var b = Summary("B");
        var older = service.QueueTimeline(b.Id);
        var newer = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var olderLoad = viewModel.SelectTaskAsync(b);
        var newerLoad = viewModel.SelectTaskAsync(b);
        if (newerCompletesFirst)
        {
            newer.Complete([Group("newer")]);
            await newerLoad;
            older.Complete([Group("older")]);
            await olderLoad;
        }
        else
        {
            older.Complete([Group("older")]);
            await olderLoad;
            Assert.Empty(viewModel.TimelineGroups);
            newer.Complete([Group("newer")]);
            await newerLoad;
        }

        AssertTimeline(viewModel, "newer");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RefreshAndSelectionOverlapUsesLatestTimelineRequest(bool refreshCompletesFirst)
    {
        var service = new FakeService();
        var b = Summary("B");
        var dashboard = service.QueueDashboard();
        var selectionRequest = service.QueueTimeline(b.Id);
        var refreshRequest = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var refresh = viewModel.RefreshAsync();
        var selection = viewModel.SelectTaskAsync(b);
        dashboard.Complete(MakeDashboard([b], "dashboard"));
        await refreshRequest.Started.Task;

        if (refreshCompletesFirst)
        {
            refreshRequest.Complete([Group("newer")]);
            await refresh;
            selectionRequest.Complete([Group("older")]);
            await selection;
        }
        else
        {
            selectionRequest.Complete([Group("older")]);
            await selection;
            Assert.Empty(viewModel.TimelineGroups);
            refreshRequest.Complete([Group("newer")]);
            await refresh;
        }

        Assert.Equal(b.Id, viewModel.SelectedTask?.Id);
        AssertTimeline(viewModel, "newer");
    }

    [Fact]
    public async Task RapidSelectionsOnlyAllowLastTaskToPublish()
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        var c = Summary("C");
        var aRequest = service.QueueTimeline(a.Id);
        var bRequest = service.QueueTimeline(b.Id);
        var cRequest = service.QueueTimeline(c.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        var loadB = viewModel.SelectTaskAsync(b);
        var loadC = viewModel.SelectTaskAsync(c);
        bRequest.Complete([Group("B event")]);
        aRequest.Complete([Group("A event")]);
        await Task.WhenAll(loadA, loadB);
        Assert.Empty(viewModel.TimelineGroups);
        cRequest.Complete([Group("C event")]);
        await loadC;

        Assert.Equal(c.Id, viewModel.SelectedTask?.Id);
        AssertTimeline(viewModel, "C event");
    }

    [Fact]
    public async Task StaleFailureAndCancellationCannotReplaceCurrentStatus()
    {
        var service = new FakeService();
        var a = Summary("A");
        var b = Summary("B");
        var staleFailure = service.QueueTimeline(a.Id);
        var current = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var loadA = viewModel.SelectTaskAsync(a);
        var loadB = viewModel.SelectTaskAsync(b);
        current.Complete([Group("B event")]);
        await loadB;
        staleFailure.Fail(new InvalidOperationException());
        await loadA;
        Assert.Equal("Timeline loaded.", viewModel.SafeStatusMessage);

        var staleCancellation = service.QueueTimeline(a.Id);
        var loadCancelledA = viewModel.SelectTaskAsync(a);
        var currentAgain = service.QueueTimeline(b.Id);
        var loadCurrentB = viewModel.SelectTaskAsync(b);
        currentAgain.Complete([Group("B newer")]);
        await loadCurrentB;
        staleCancellation.Cancel();
        await loadCancelledA;
        Assert.Equal("Timeline loaded.", viewModel.SafeStatusMessage);
        AssertTimeline(viewModel, "B newer");
    }

    [Fact]
    public async Task CurrentFailureAndCancellationUseSafeStatusMessages()
    {
        var service = new FakeService();
        var task = Summary("task");
        var failure = service.QueueTimeline(task.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var failedLoad = viewModel.SelectTaskAsync(task);
        failure.Fail(new InvalidOperationException());
        await failedLoad;
        Assert.Equal("Task Center is temporarily unavailable.", viewModel.SafeStatusMessage);

        var cancellation = service.QueueTimeline(task.Id);
        var cancelledLoad = viewModel.SelectTaskAsync(task);
        cancellation.Cancel();
        await cancelledLoad;
        Assert.Equal("Task Center timeline load cancelled.", viewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task ClearingSelectionInvalidatesPendingTimeline()
    {
        var service = new FakeService();
        var task = Summary("task");
        var pending = service.QueueTimeline(task.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var oldLoad = viewModel.SelectTaskAsync(task);
        await viewModel.SelectTaskAsync(null);
        pending.Complete([Group("stale")]);
        await oldLoad;

        Assert.Null(viewModel.SelectedTask);
        Assert.Empty(viewModel.TimelineGroups);
        Assert.Equal("Select a task to view its timeline.", viewModel.SafeStatusMessage);
    }

    [Fact]
    public async Task StaleRefreshFailureCannotReplaceNewerSelectionStatus()
    {
        var service = new FakeService();
        var b = Summary("B");
        var dashboard = service.QueueDashboard();
        var timeline = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var refresh = viewModel.RefreshAsync();
        var selection = viewModel.SelectTaskAsync(b);
        timeline.Complete([Group("B event")]);
        await selection;
        dashboard.Fail(new InvalidOperationException());
        await refresh;

        Assert.Equal("Timeline loaded.", viewModel.SafeStatusMessage);
        AssertTimeline(viewModel, "B event");
    }

    [Fact]
    public async Task StaleRefreshSuccessStatusDoesNotReplaceNewerSelectionStatus()
    {
        var service = new FakeService();
        var b = Summary("B");
        var dashboard = service.QueueDashboard();
        var selectionRequest = service.QueueTimeline(b.Id);
        var refreshRequest = service.QueueTimeline(b.Id);
        var viewModel = new AedaTaskCenterViewModel(service);

        var refresh = viewModel.RefreshAsync();
        var selection = viewModel.SelectTaskAsync(b);
        selectionRequest.Complete([Group("selected")]);
        await selection;
        dashboard.Complete(MakeDashboard([b], "stale dashboard status"));
        await refreshRequest.Started.Task;

        Assert.Equal("Timeline loaded.", viewModel.SafeStatusMessage);
        refreshRequest.Complete([Group("refreshed")]);
        await refresh;
        AssertTimeline(viewModel, "refreshed");
    }

    [Fact]
    public async Task RefreshReconcilesSelectedSummaryByIdAndClearsRemovedSelection()
    {
        var service = new FakeService();
        var original = Summary("old");
        var refreshed = original with { Title = "new" };
        service.Dashboard = MakeDashboard([refreshed]);
        var viewModel = new AedaTaskCenterViewModel(service) { SelectedTask = original };

        await viewModel.RefreshAsync();
        Assert.Same(refreshed, viewModel.SelectedTask);

        service.Dashboard = MakeDashboard([]);
        await viewModel.RefreshAsync();
        Assert.Null(viewModel.SelectedTask);
        Assert.Empty(viewModel.TimelineGroups);
    }

    private static AedaTaskSummary Summary(string title) => new(
        TaskId.NewId(), title, new(AedaTaskCenterStatus.Running, "Running", "", false, false),
        new(AedaTaskCenterModule.Chat, "Chat", AedaModuleId.Chat, "chat"),
        DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, title, []);

    private static AedaTaskActivityGroup Group(string title) => new(title, title, []);

    private static AedaTaskCenterDashboard MakeDashboard(
        IReadOnlyList<AedaTaskSummary> tasks,
        string status = "ready") =>
        new(tasks, [], [], [], new Dictionary<AedaTaskCenterStatus, int>(),
            new Dictionary<AedaTaskCenterModule, int>(), DateTimeOffset.UtcNow, status);

    private static void AssertTimeline(AedaTaskCenterViewModel viewModel, string title) =>
        Assert.Equal(title, viewModel.TimelineGroups.Single().Title);

    private sealed class FakeService : IAedaTaskCenterService
    {
        private readonly Queue<Pending<AedaTaskCenterDashboard>> _dashboards = [];
        private readonly Dictionary<TaskId, Queue<Pending<IReadOnlyList<AedaTaskActivityGroup>>>> _timelines = [];

        public AedaTaskCenterDashboard Dashboard { get; set; } = MakeDashboard([]);

        public Pending<AedaTaskCenterDashboard> QueueDashboard()
        {
            var pending = new Pending<AedaTaskCenterDashboard>();
            _dashboards.Enqueue(pending);
            return pending;
        }

        public Pending<IReadOnlyList<AedaTaskActivityGroup>> QueueTimeline(TaskId taskId)
        {
            var pending = new Pending<IReadOnlyList<AedaTaskActivityGroup>>();
            if (!_timelines.TryGetValue(taskId, out var requests))
            {
                requests = [];
                _timelines.Add(taskId, requests);
            }

            requests.Enqueue(pending);
            return pending;
        }

        public ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(
            AedaTaskFilter? filter = null,
            CancellationToken cancellationToken = default) =>
            _dashboards.Count == 0
                ? ValueTask.FromResult(Dashboard)
                : _dashboards.Dequeue().Start();

        public ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(
            TaskId taskId,
            int limit = 100,
            CancellationToken cancellationToken = default) =>
            _timelines.TryGetValue(taskId, out var requests) && requests.Count > 0
                ? requests.Dequeue().Start()
                : ValueTask.FromResult<IReadOnlyList<AedaTaskActivityGroup>>([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(AedaTaskCenterModule module, int limit, CancellationToken cancellationToken = default) => new([]);
        public ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(TaskId taskId, Guid eventId, CancellationToken cancellationToken = default) => new((AedaTaskTimelineItem?)null);
        public ValueTask CancelTaskAsync(TaskId taskId, TaskCancellationReason reason, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class Pending<T>
    {
        private readonly TaskCompletionSource<T> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<T> Start()
        {
            Started.TrySetResult();
            return new(_completion.Task);
        }

        public void Complete(T result) => _completion.SetResult(result);

        public void Fail(Exception exception) => _completion.SetException(exception);

        public void Cancel() => _completion.SetCanceled();
    }
}
