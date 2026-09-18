using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using PersonalAI.Core.Approvals;
using PersonalAI.Core.Modules;
using PersonalAI.Core.Tasks;
using PersonalAI.Desktop.Avalonia.Views.Tasks;
using PersonalAI.Desktop.Presentation.ViewModels;

namespace PersonalAI.Tests.Avalonia.Tasks;

public sealed class AvaloniaTaskCenterNavigationTests
{
    private const string ProbeVariable = "AEDA_D08_NAVIGATION_PROBE";

    [Fact]
    public Task D08_ActiveTaskBackReturnsToActive() => RunScenarioAsync("active");

    [Fact]
    public Task D08_DirectSelectedTaskBackReturnsToOverview() => RunScenarioAsync("direct");

    [Fact]
    public Task D08_StaleQueueOriginDoesNotLeakIntoDirectEntry() => RunScenarioAsync("stale");

    [Fact]
    public Task D08_QueueOriginCanReplaceDirectOrigin() => RunScenarioAsync("replace");

    [Fact]
    public Task D08_RecentTaskBackReturnsToRecent() => RunScenarioAsync("recent");

    [Fact]
    public Task D08_RepeatedDirectVisitsReturnToOverview() => RunScenarioAsync("repeat");

    [Fact]
    public Task D08_EntryAndBackKeepExistingFocusTargets() => RunScenarioAsync("focus");

    private static async Task RunScenarioAsync(
        string scenario,
        [CallerMemberName] string testName = "")
    {
        if (Environment.GetEnvironmentVariable(ProbeVariable) == scenario)
        {
            RunScenario(scenario);
            return;
        }

        await RunProbeInIsolatedProcessAsync(testName, scenario);
    }

    private static void RunScenario(string scenario)
    {
        StartApplication();
        var active = Summary("Active task", AedaTaskCenterStatus.Running);
        var recent = Summary("Recent task", AedaTaskCenterStatus.Completed);
        var failed = Summary("Failed task", AedaTaskCenterStatus.Failed);
        var dashboard = new AedaTaskCenterDashboard(
            [active],
            [],
            [recent],
            [failed],
            new Dictionary<AedaTaskCenterStatus, int>(),
            new Dictionary<AedaTaskCenterModule, int>(),
            DateTimeOffset.UtcNow,
            "Task Center loaded.");
        var viewModel = new AedaTaskCenterViewModel(new FakeService(dashboard));
        if (scenario is "direct" or "replace" or "repeat")
        {
            viewModel.SelectedTask = active;
        }

        var view = new TaskCenterView { DataContext = viewModel };
        var window = Show(view);
        try
        {
            view.ApplyResponsiveMode(compact: true, medium: false);
            Dispatcher.UIThread.RunJobs();

            switch (scenario)
            {
                case "active":
                    AssertQueueRoundTrip(view, "Active", active);
                    break;
                case "direct":
                    AssertDirectRoundTrip(view);
                    break;
                case "stale":
                    OpenQueue(view, "Active");
                    OpenTask(view, "Active", active);
                    Back(view);
                    Back(view);
                    AssertDirectRoundTrip(view);
                    break;
                case "replace":
                    AssertDirectRoundTrip(view);
                    AssertQueueRoundTrip(view, "Active", active);
                    break;
                case "recent":
                    AssertQueueRoundTrip(view, "Recent", recent);
                    break;
                case "repeat":
                    for (var visit = 0; visit < 3; visit++)
                    {
                        AssertDirectRoundTrip(view);
                    }

                    break;
                case "focus":
                    AssertFocusRoundTrips(view, active);
                    break;
                default:
                    throw new InvalidOperationException(scenario);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertQueueRoundTrip(
        TaskCenterView view,
        string queue,
        AedaTaskSummary task)
    {
        OpenQueue(view, queue);
        var taskButton = OpenTask(view, queue, task);

        AssertPane(view, "SelectedTask");
        Assert.Equal($"Back to {QueueLabel(queue)}", BackButton(view).Content);
        Back(view);
        AssertPane(view, queue);
        Assert.Same(taskButton, Focused(view));
    }

    private static void AssertDirectRoundTrip(TaskCenterView view)
    {
        AssertPane(view, "Overview");
        Click(OverviewButton(view, "SelectedTask"));
        AssertPane(view, "SelectedTask");
        Assert.Equal("Back to Task Center overview", BackButton(view).Content);
        Back(view);
        AssertPane(view, "Overview");
    }

    private static void AssertFocusRoundTrips(TaskCenterView view, AedaTaskSummary task)
    {
        OpenQueue(view, "Active");
        var taskButton = OpenTask(view, "Active", task);
        Assert.Same(view.FindControl<Control>("SelectedTaskHeading"), Focused(view));
        Back(view);
        Assert.Same(taskButton, Focused(view));
        Back(view);

        Click(OverviewButton(view, "SelectedTask"));
        Assert.Same(view.FindControl<Control>("SelectedTaskHeading"), Focused(view));
        Back(view);
        Assert.Same(view.FindControl<Control>("CompactOverview"), Focused(view));
    }

    private static void OpenQueue(TaskCenterView view, string queue)
    {
        AssertPane(view, "Overview");
        Click(OverviewButton(view, queue));
        AssertPane(view, queue);
    }

    private static Button OpenTask(
        TaskCenterView view,
        string queue,
        AedaTaskSummary task)
    {
        var button = Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            candidate => candidate.Tag as string == queue &&
                candidate.DataContext is AedaTaskSummary summary &&
                summary.Id == task.Id);
        Click(button);
        return button;
    }

    private static Button OverviewButton(TaskCenterView view, string pane) =>
        Assert.Single(
            view.GetVisualDescendants().OfType<Button>(),
            candidate => candidate.Tag as string == pane &&
                candidate.DataContext is not AedaTaskSummary);

    private static Button BackButton(TaskCenterView view) =>
        view.FindControl<Button>("CompactBackButton")!;

    private static void Back(TaskCenterView view) => Click(BackButton(view));

    private static void Click(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertPane(TaskCenterView view, string expected) =>
        Assert.Equal(expected, GetPrivateEnum(view, "_compactPane"));

    private static object? Focused(TaskCenterView view) =>
        TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement();

    private static string GetPrivateEnum(object target, string fieldName) =>
        target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!.ToString()!;

    private static string QueueLabel(string queue) => queue switch
    {
        "Active" => "Active tasks",
        "Recent" => "Recent tasks",
        _ => "Failed or cancelled"
    };

    private static Window Show(Control view)
    {
        var window = new Window
        {
            Width = 720,
            Height = 900,
            ShowInTaskbar = false,
            Content = view
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static void StartApplication() =>
        AppBuilder.Configure<PersonalAI.Desktop.Avalonia.App>()
            .UsePlatformDetect()
            .SetupWithoutStarting();

    private static AedaTaskSummary Summary(
        string title,
        AedaTaskCenterStatus status) => new(
        TaskId.NewId(),
        title,
        new AedaTaskStatusBadge(status, status.ToString(), string.Empty, false, false),
        new AedaTaskModuleBadge(AedaTaskCenterModule.Chat, "Chat", AedaModuleId.Chat, "chat"),
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        title,
        []);

    private static async Task RunProbeInIsolatedProcessAsync(
        string testName,
        string scenario)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(typeof(AvaloniaTaskCenterNavigationTests).Assembly.Location);
        startInfo.ArgumentList.Add(
            "--TestCaseFilter:FullyQualifiedName=" +
            typeof(AvaloniaTaskCenterNavigationTests).FullName + "." + testName);
        startInfo.Environment[ProbeVariable] = scenario;

        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var output = await standardOutput + await standardError;

        Assert.True(process.ExitCode == 0, output);
    }

    private sealed class FakeService(AedaTaskCenterDashboard dashboard) : IAedaTaskCenterService
    {
        public ValueTask<AedaTaskCenterDashboard> GetDashboardAsync(
            AedaTaskFilter? filter = null,
            CancellationToken cancellationToken = default) => new(dashboard);

        public ValueTask<IReadOnlyList<AedaTaskActivityGroup>> GetTimelineAsync(
            TaskId taskId,
            int limit = 100,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListActiveTasksAsync(
            int limit,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<IReadOnlyList<AedaTaskApprovalSummary>> ListWaitingApprovalsAsync(
            int limit,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListRecentTasksAsync(
            int limit,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListFailedOrCancelledTasksAsync(
            int limit,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<IReadOnlyList<AedaTaskSummary>> ListTasksByModuleAsync(
            AedaTaskCenterModule module,
            int limit,
            CancellationToken cancellationToken = default) => new([]);

        public ValueTask<AedaTaskTimelineItem?> GetSafeEventDetailsAsync(
            TaskId taskId,
            Guid eventId,
            CancellationToken cancellationToken = default) => new((AedaTaskTimelineItem?)null);

        public ValueTask CancelTaskAsync(
            TaskId taskId,
            TaskCancellationReason reason,
            CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }
}
